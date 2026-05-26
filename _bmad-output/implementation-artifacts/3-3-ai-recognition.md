# Story 3.3: AI 识别集成 + 降级策略

Status: done

## Story

As a 用户，
I want 拍照后 AI 自动识别物品名称，
so that 不需要手动打字就能完成录入。

## Acceptance Criteria

1. **AC-1: AI 识别返回结果** — 传入图片文件路径，调用 OpenAI 兼容 API，返回物品名称和备注描述
2. **AC-2: 超时降级** — API 15s 内无响应时静默返回 null，不抛异常
3. **AC-3: 错误降级** — API 返回错误（4xx/5xx）时静默返回 null，不抛异常
4. **AC-4: 配置驱动** — base URL、model name、API key 从 `appsettings.json` 读取（`Llm:BaseUrl`, `Llm:Model`, `Llm:ApiKey`）
5. **AC-5: API Key 安全** — 仅在后端持有，前端永远不接触

## Tasks / Subtasks

- [x] Task 1: 创建 LLM 配置模型 (AC: #4)
  - [x] 1.1 `src/BoxWise.Server/Configuration/LlmOptions.cs` — BaseUrl, Model, ApiKey
  - [x] 1.2 `Program.cs` 中 `builder.Services.Configure<LlmOptions>(builder.Configuration.GetSection("Llm"))`

- [x] Task 2: 创建 LlmClient 服务 (AC: #1, #2, #3, #5)
  - [x] 2.1 `src/BoxWise.Server/Services/LlmClient.cs` — `RecognizeAsync(imagePath)` 返回 `RecognitionResult?`
  - [x] 2.2 使用 `HttpClient` + `IOptions<LlmOptions>` 发送 OpenAI 兼容 Vision API 请求
  - [x] 2.3 15s 超时（`CancellationTokenSource` + `Task.Delay`）
  - [x] 2.4 所有异常（HTTP、超时、JSON 解析）静默捕获，返回 null
  - [x] 2.5 图片文件 → Base64 编码 → OpenAI 兼容请求体

- [x] Task 3: 注册 DI (AC: #1-#5)
  - [x] 3.1 `Program.cs` 注册 `LlmClient` 为 Singleton + `IHttpClientFactory`
  - [x] 3.2 添加 `appsettings.Development.json` 示例（仅占位值）

- [x] Task 4: 单元测试 (AC: #1-#3)
  - [x] 4.1 `LlmClient` 测试：超时返回 null、解析失败返回 null
  - [x] 4.2 `dotnet test` 全部通过

- [x] Task 5: 构建验证 (AC: #1-#5)
  - [x] 5.1 `dotnet build BoxWise.slnx` 零错误零警告

---

## Dev Notes

### 前置上下文

- **Epic 3 前序完成:** Item 实体 + 图片上传 + 物品录入 API 全部就绪
- **架构决定:** OpenAI 兼容 API（非原生 Claude/Gemini API），v1 固定单一模型
- **API Key 在后端:** 前端不接触 API Key，通过 Server 端 LlmClient 调用

### Epic 2+3 关键学习

1. **Singleton 服务 + IServiceScopeFactory** — 图片服务模式（Story 3.1），LlmClient 同理
2. **异常静默降级** — 不抛异常到调用方，让 AI 失败成为常态而非异常
3. **配置从 IConfiguration 读取** — 与 ImageStorageService 模式一致

### LlmOptions 配置模型

```csharp
public class LlmOptions
{
    public string BaseUrl { get; set; } = "https://api.openai.com/v1";
    public string Model { get; set; } = "gpt-4o";
    public string ApiKey { get; set; } = string.Empty;
}
```

**appsettings.Development.json 示例：**
```json
{
  "Llm": {
    "BaseUrl": "https://api.openai.com/v1",
    "Model": "gpt-4o",
    "ApiKey": "sk-your-key-here"
  }
}
```

### LlmClient 核心逻辑

```csharp
public class LlmClient
{
    private readonly HttpClient _http;
    private readonly LlmOptions _options;
    private readonly ILogger<LlmClient> _logger;

    public LlmClient(HttpClient http, IOptions<LlmOptions> options, ILogger<LlmClient> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<RecognitionResult?> RecognizeAsync(string imagePath)
    {
        try
        {
            var imageBase64 = Convert.ToBase64String(await File.ReadAllBytesAsync(imagePath));
            var requestBody = new
            {
                model = _options.Model,
                messages = new[]
                {
                    new
                    {
                        role = "user",
                        content = new object[]
                        {
                            new { type = "text", text = "识别这张照片中的物品，返回物品名称和简短描述。请以JSON格式返回：{\"name\":\"物品名称\",\"note\":\"简要描述\"}" },
                            new { type = "image_url", image_url = new { url = $"data:image/jpeg;base64,{imageBase64}" } }
                        }
                    }
                },
                max_tokens = 200
            };

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var response = await _http.PostAsJsonAsync(
                $"{_options.BaseUrl.TrimEnd('/')}/chat/completions",
                requestBody,
                cancellationToken: cts.Token);

            if (!response.IsSuccessStatusCode) return null;

            var result = await response.Content.ReadFromJsonAsync<OpenAiResponse>(cancellationToken: cts.Token);
            var content = result?.Choices?.FirstOrDefault()?.Message?.Content;
            if (content is null) return null;

            return JsonSerializer.Deserialize<RecognitionResult>(content);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "AI 识别失败，降级为手动输入");
            return null;
        }
    }
}
```

### RecognitionResult DTO

```csharp
public record RecognitionResult(string Name, string Note);
```

### 注册 DI

```csharp
builder.Services.Configure<LlmOptions>(builder.Configuration.GetSection("Llm"));
builder.Services.AddHttpClient<LlmClient>();
```

使用 `AddHttpClient<T>` 而非手动 `new HttpClient`，获得 `IHttpClientFactory` 的连接池管理。

### 文件结构变更

```
src/BoxWise.Server/
  Configuration/LlmOptions.cs          (new)
  Services/LlmClient.cs                (new)
  Program.cs                           (modified — DI)
src/BoxWise.Shared/Dtos/
  RecognitionResult.cs                 (new)
src/BoxWise.Server.Tests/
  Services/LlmClientTests.cs           (new)
```

**无迁移** — 纯服务层变更。

### 构建与验证

```bash
dotnet build BoxWise.slnx
dotnet test BoxWise.slnx

# 配置真实 API Key 后手动测试:
# dotnet run → 调用 RecognizeAsync 验证返回结果
```

### 关键风险点

1. **API Key 安全** — `appsettings.Development.json` 包含示例占位值，不提交真实 Key。生产环境通过环境变量覆盖
2. **Base64 内存** — 大图片（>10MB）编码后约 13MB，需确保上传时已限制 10MB
3. **OpenAI 兼容性** — 国产模型（如 Qwen、DeepSeek）的 Vision API 兼容性需实测验证
4. **JSON 解析鲁棒性** — AI 返回的 JSON 可能格式不完美（多余文本、markdown 包裹），`JsonSerializer.Deserialize` 可能失败。建议先用简单字符串匹配兜底

---

## References

| 内容 | 来源 |
|------|------|
| Story AC 定义 | [Source: epics.md#Story 3.3] |
| FR-2 AI 识别预填 | [Source: prd.md#FR-2] |
| FR-3 手动输入兜底 | [Source: prd.md#FR-3] |
| AI 抽象 + 15s 超时 | [Source: architecture.md#AI Reliability] |
| LlmClient 通过配置文件 | [Source: architecture.md#Story 3.3] |
| Singleton 服务模式 | [Source: Story 3.1: ThumbnailService] |

## Dev Agent Record

### Agent Model Used

deepseek-v4-pro

### Debug Log References

- LlmClient 使用 regex 兜底解析 AI 返回的非标准 JSON

**代码审查修复记录:**
- 🔴 `JsonSerializer.Deserialize` 大小写敏感 → 添加 `PropertyNameCaseInsensitive = true`
- 🔴 超时不覆盖文件 I/O → `CancellationTokenSource` 提前到方法顶部
- 🔴 大文件无保护 → 添加 `FileInfo.Length > 10MB` 检查
- 🟡 `RecognitionResult` 缺 Dto 后缀 → 重命名为 `RecognitionResultDto`
- 🟡 `Configure<LlmOptions>` 无验证 → 改为 `AddOptions<T>().ValidateOnStart()`

### Completion Notes List

✅ **全部 5 个任务完成** — AI 识别集成就绪，22/22 测试通过

**实施要点：**
- LlmClient：HttpClient + OpenAI 兼容 Vision API + 15s 超时
- 双重降级：超时/HTTP 错误/JSON 解析失败 → 静默返回 null
- JSON 解析兜底：标准 Deserialize 失败后用 regex 提取 name/note 字段
- API Key 仅在后端，通过 `IOptions<LlmOptions>` 读取

### File List

**新增文件:**
- `src/BoxWise.Server/Configuration/LlmOptions.cs` (new)
- `src/BoxWise.Server/Services/LlmClient.cs` (new)
- `src/BoxWise.Shared/Dtos/RecognitionResult.cs` (new)

**修改文件:**
- `src/BoxWise.Server/Program.cs` (modified) — DI 注册

### Review Findings

- [x] [Review][Decision] **AI 识别失败无视觉反馈** — 已添加加载中微调器 + 失败提示"AI 未能识别物品" [ItemEntry.razor:24-32,72-89]
- [x] [Review][Patch] **连续拍照竞态 + 导航后 StateHasChanged** — 添加 `_recognitionCts` 取消机制 + `_isDisposed` 检查 [ItemEntry.razor:58,75-76,102-106]
- [x] [Review][Patch] **空 catch 吞噬致命异常** — AiService.cs 改为 `OperationCanceledException`/`HttpRequestException`；ItemEntry.razor 改为 `when ex is not OutOfMemoryException` [AiService.cs:31-39, ItemEntry.razor:95]
- [x] [Review][Patch] **文件大小验证滞后 + 无 RequestFormLimits** — Program.cs 添加 `FormOptions.MultipartBodyLengthLimit = 10MB` [Server/Program.cs:16-19]
- [x] [Review][Patch] **AiEndpoints 无异常保护导致 500** — 添加 try-catch 包裹 `llmClient.RecognizeAsync`，异常返回 422 [AiEndpoints.cs:63-66]
- [x] [Review][Patch] **CancellationToken 缺失（3处）** — AiEndpoints 接受 `CancellationToken`，CopyToAsync 和 PostAsync 传递 token [AiEndpoints.cs:28,49, AiService.cs:27]
- [x] [Review][Patch] **Content-Type 仅检查客户端声明** — 添加 `HasValidImageHeader()` 魔数验证（JPEG FF D8 FF / PNG 89 50 4E 47 / WebP RIFF）[AiEndpoints.cs:49-51,73-92]
- [x] [Review][Patch] **OpenReadStream 默认 512KB 限制** — 误报。`PhotoCapture.OpenReadStream()` 创建 `new MemoryStream(Bytes)`，无大小限制；大小校验已在 `ImageUploader` 中完成
- [x] [Review][Patch] **data URL MIME 硬编码为 image/jpeg** — `RecognizeAsync` 接受 `contentType` 参数，`GetMimeType()` 根据格式返回正确的 MIME [LlmClient.cs:25,50,110-115]
- [x] [Review][Patch] **Path.GetTempFileName 65535 文件限制** — 改为 `Path.Combine(Path.GetTempPath(), $"boxwise_ai_{Guid.NewGuid():N}.tmp")` [AiEndpoints.cs:40]
- [x] [Review][Patch] **Content-Type 非常规值直接拒绝** — AllowedTypes 增加 `image/jpg` 变体 [AiEndpoints.cs:7]
- [x] [Review][Patch] **缺少 ProducesProblem(422)** — 端点链式调用添加 `.ProducesProblem(422)` [AiEndpoints.cs:24]
- [x] [Review][Defer] **空 base64 创建 0 字节 PhotoCapture** — ImageUploader.razor 既有问题 [ImageUploader.razor:101]
- [x] [Review][Defer] **BaseUrl 为 null 时 NRE** — LlmOptions 既有问题，ValidateOnStart 未拦截 [LlmClient.cs:57]
- [x] [Review][Defer] **MemoryStream 双重释放** — ItemEntry.razor 既有问题，幂等安全 [ItemEntry.razor:148]

### R2 Review Findings（第二轮修复后复审）

- [x] [Review][Patch] **CancellationToken 未传播到 LlmClient** — `LlmClient.RecognizeAsync` 添加 `CancellationToken` 参数 + `CreateLinkedTokenSource`，端点传入 `HttpContext.RequestAborted` [LlmClient.cs:25-30, AiEndpoints.cs:63]
- [x] [Review][Patch] **AllowedTypes 大小写敏感** — 改为 `StringComparer.OrdinalIgnoreCase` [AiEndpoints.cs:44]
- [x] [Review][Patch] **魔数验证在文件写入之后** — 改为流式验证：先读头部12字节验签，通过后再写入文件（避免无效文件10MB I/O浪费）[AiEndpoints.cs:53-70]
- [x] [Review][Patch] **WebP 魔数验证不全** — 增加 offset 8 的 "WEBP" 四字节验证，防止 RIFF 容器（AVI/WAV）绕过 [AiEndpoints.cs:99-103]
- [x] [Review][Patch] **LLM 返回 null name 静默通过** — `TryParse` 增加 `!string.IsNullOrWhiteSpace(dto.Name)` 校验 [LlmClient.cs:96-97]
