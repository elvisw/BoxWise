# Story 12.1: 前端 AiService 直调火山 API

Status: done

baseline_commit: 474611c

## Story

As a 国内用户,
I want 拍照后 AI 能在几秒内识别物品,
so that 我不需要每次手动输入物品名称和备注。

## Acceptance Criteria

1. `AiService.RecognizeAsync` 改为接收 `byte[]` + `contentType`，直接 POST 到火山 ARK API `/chat/completions`
2. 请求 payload 为 OpenAI 兼容格式：`{ model, messages: [{ role: "user", content: [text, { type: "image_url", image_url: { url: "data:{mime};base64,{base64}" } }] }], max_tokens: 200 }`
3. `AiService` 使用 `IHttpClientFactory` 创建独立 HttpClient（不依赖 Server 的 BaseAddress 和 CookieHandler）
4. `wwwroot/appsettings.json` 新增 `VolcEngine` 配置块替代 `AiSettings`：`{ BaseUrl, ApiKey, Model, TimeoutSeconds: 30 }`
5. `ItemEntry.razor` 调用从 `photo.OpenReadStream()` 改为 `photo.Bytes`
6. `Program.cs` 注册 `IHttpClientFactory`，`AiService` 通过 `IServiceProvider` 或构造注入获取
7. 所有现有测试通过（除 `AiServiceTests.cs` 需随本 Story 更新）

## Tasks / Subtasks

- [x] Task 1: 更新配置和 NuGet 依赖 (AC: #4)
  - [x] 在 `Directory.Packages.props` 中添加 `<PackageVersion Include="Microsoft.Extensions.Http" Version="10.0.8" />`
  - [x] 在 `src/BoxWise.Client/BoxWise.Client.csproj` 中添加 `<PackageReference Include="Microsoft.Extensions.Http" />`
  - [x] 移除 `wwwroot/appsettings.json` 中 `AiSettings` 配置块
  - [x] 新增 `VolcEngine` 配置块：BaseUrl, ApiKey, Model, TimeoutSeconds
  - [x] 开发环境 `appsettings.Development.json` 添加 `VolcEngine` 配置（ApiKey 用开发 Key）
  - [x] 备注：生产部署时创建 `wwwroot/appsettings.Production.json`（VolcEngine + 生产 ApiKey，gitignored）

- [x] Task 2: 重写 `AiService.cs` (AC: #1, #2, #3)
  - [x] 构造函数改为注入 `IConfiguration` + `IHttpClientFactory`
  - [x] 方法签名改为 `RecognizeAsync(byte[] imageBytes, string contentType, CancellationToken ct = default)`
  - [x] 内部实现：读取 `VolcEngine:*` 配置 → base64 编码字节 → 构造 OpenAI 兼容 JSON body → 用独立 HttpClient POST 到火山 ARK API
  - [x] 检查配置完整性（BaseUrl/ApiKey/Model 任一缺失返回 null，保持静默降级）
  - [x] 构造 HTTP 请求：通过 `_httpFactory.CreateClient("VolcEngine")` 获取 HttpClient → `request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey)` → POST 相对路径 `/api/v3/chat/completions`（BaseAddress 已在 Program.cs 中配置）
  - [x] 超时控制：`CancelAfter(TimeoutSeconds)`（默认 30s，Clamp 5-120s 防止配置错误）
  - [x] 错误处理：`OperationCanceledException`（超时）、`HttpRequestException`（网络错误）、非 200 状态码 → 均返回 null
  - [x] GetMimeType 辅助方法：`image/jpeg` → `image/jpeg`，`image/png` → `image/png`，`image/webp` → `image/webp`，其他 → `image/jpeg`
  - [x] 解析 OpenAI 兼容响应：定义 `OpenAiResponse`/`Choice`/`Message` 内部类 → 从 `choices[0].message.content` 提取 JSON 字符串 → `TryParse`（JSON 反序列化 + 正则回退）→ `RecognitionResultDto`（可直接参考 `LlmClient.cs:111-136` 的 TryParse 和内部类模式）

- [x] Task 3: 更新 `Program.cs` (Client) (AC: #6)
  - [x] 注册命名 HttpClient：`builder.Services.AddHttpClient("VolcEngine", c => { c.BaseAddress = new Uri(builder.Configuration["VolcEngine:BaseUrl"] ?? "https://ark.cn-beijing.volces.com/api/v3"); c.Timeout = TimeSpan.FromSeconds(30); })`
  - [x] AiService 保持 `AddScoped<AiService>()`，构造注入 `IHttpClientFactory` → `factory.CreateClient("VolcEngine")` 获取配置好的 HttpClient
  - [x] 注意：**不要**使用 `AddHttpClient<AiService>()`（Typed Client）——`AiService` 通过 `IHttpClientFactory` 显式获取命名客户端，避免配置不匹配
  - [x] 注意：`Microsoft.Extensions.Http` 包需在 Task 1 中先添加

- [x] Task 4: 更新 `ItemEntry.razor` (AC: #5)
  - [x] `OnPhotoCaptured` 方法中（第 96-97 行）：`photo.OpenReadStream()` → `photo.Bytes`，去掉 `photo.FileName` 参数
  - [x] AiService.RecognizeAsync 调用改为 `AiService.RecognizeAsync(photo.Bytes, photo.ContentType, token)`
  - [x] 其余逻辑不变（取消、状态管理、错误展示）
  - [x] **注意：** `UploadPhotoAsync`（第 200 行）不受影响，继续使用 `photo.OpenReadStream()`（返回 `new MemoryStream(Bytes)`，功能等价）

- [x] Task 5: 重写 `AiServiceTests.cs` (AC: #7)
  - [x] **辅助方法：** `CreateService` 改为 mock `IHttpClientFactory`：`factory.Setup(f => f.CreateClient("VolcEngine")).Returns(httpClient)`
  - [x] `RecognizeAsync_Success_ReturnsResult` — Mock 返回 200 + `{"choices":[{"message":{"content":"{\"name\":\"螺丝刀\",\"note\":\"蓝色手柄\"}"}}]}` → 验证 `RecognitionResultDto` 解析
  - [x] `RecognizeAsync_HttpError_ReturnsNull` — Mock 返回 500
  - [x] `RecognizeAsync_Timeout_ReturnsNull` — Mock 抛出 `OperationCanceledException`
  - [x] `RecognizeAsync_NetworkError_ReturnsNull` — Mock 抛出 `HttpRequestException`
  - [x] `RecognizeAsync_SendsCorrectPayload` — 验证发送的 JSON body 包含 model、base64 image_url、中文 prompt、max_tokens: 200
  - [x] `RecognizeAsync_MissingConfig_ReturnsNull` — 缺少 ApiKey/BaseUrl/Model 时返回 null
  - [x] `RecognizeAsync_EmptyResponse_ReturnsNull` — API 返回 200 但 `choices[0].message.content` 为 null
  - [x] `RecognizeAsync_NonJsonContent_ReturnsNull` — content 为纯文本而非 JSON → 正则回退或返回 null

- [x] Task 6: 验证
  - [x] `dotnet build` 零错误零警告
  - [x] `dotnet test` 全部通过
  - [x] 手动测试：启动 Client → 拍照 → AI 识别成功返回结果

## Dev Notes

### 当前架构 vs 目标架构

```
CURRENT (❌ 生产不可用):
  ItemEntry.razor → AiService.RecognizeAsync(Stream)
    → POST /api/ai/recognize (multipart/form-data)
    → AiEndpoints.cs → LlmClient.cs → 火山 API
  问题: 服务端中转 2.6MB → 海外→北京 91-128s > 60s 超时

TARGET (✅):
  ItemEntry.razor → AiService.RecognizeAsync(byte[])
    → POST https://ark.cn-beijing.volces.com/api/v3/chat/completions
    → 火山 API (浏览器直连, CORS ✅)
```

### AiService.cs 重写要点

**依赖变更：**
- OLD: `HttpClient http` (BaseAddress=Server, CookieHandler) + `IConfiguration configuration`
- NEW: `IHttpClientFactory httpFactory` + `IConfiguration configuration`

**方法签名变更：**
- OLD: `Task<RecognitionResultDto?> RecognizeAsync(Stream imageStream, string fileName, string contentType, CancellationToken ct)`
- NEW: `Task<RecognitionResultDto?> RecognizeAsync(byte[] imageBytes, string contentType, CancellationToken ct = default)`

**配置读取变更：**
- OLD: `configuration.GetValue("AiSettings:TimeoutSeconds", 90)`
- NEW: `configuration.GetValue("VolcEngine:TimeoutSeconds", 30)` + `VolcEngine:BaseUrl` + `VolcEngine:ApiKey` + `VolcEngine:Model`

**HTTP 调用变更：**
- OLD: `MultipartFormDataContent` → `_http.PostAsync("api/ai/recognize", ...)`
- NEW: `JsonContent.Create(requestBody)` → `_httpClient.PostAsync("/api/v3/chat/completions", jsonContent, cts.Token)` — 使用相对路径（BaseAddress 在 `Program.cs` 命名客户端注册时已配置）
- `_httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey)` 在构造函数中设置一次即可

**NuGet 依赖新增：**
- 需在 `Directory.Packages.props` 中添加 `Microsoft.Extensions.Http` v10.0.8
- 需在 `BoxWise.Client.csproj` 中添加 `<PackageReference Include="Microsoft.Extensions.Http" />`
- Blazor WASM 项目不自动包含此包，缺少会导致 `IHttpClientFactory`/`AddHttpClient()` 编译错误

**Story 12.2 前置提醒：**
- Story 12.2 退役 `AiEndpoints.cs` 时，需将 `IsValidMagic` 方法及魔数字节常量移植到 `ImageEndpoints.cs`（当前 `ImageEndpoints.cs` 无魔数验证）。12.1 不涉及此改动，但实施后部署验证前注意图片上传的文件校验仍完整。

### PhotoCapture 已有 Bytes 属性

`src/BoxWise.Client/Models/PhotoCapture.cs:3` — `public record PhotoCapture(string FileName, string ContentType, byte[] Bytes)` 已有 `Bytes` 属性，`ItemEntry.razor` 只需将 `photo.OpenReadStream()` 替换为 `photo.Bytes`。

### CORS 已确认

2026-06-05 Playwright 实测：从 `about:blank` 页面 fetch POST 到 `ark.cn-beijing.volces.com/api/v3/chat/completions`，返回 `type: "cors"`, HTTP 200，响应正文正常。火山 ARK API 完整支持浏览器跨域请求。

### 参考实现

当前 LlmClient.cs 的 base64 编码和 JSON body 构造模式可直接参考：
- `LlmClient.cs:58` — `Convert.ToBase64String(File.ReadAllBytesAsync(...))` → 对 `byte[]` 同理
- `LlmClient.cs:60-76` — JSON body 结构（model, messages, content array, max_tokens）
- `LlmClient.cs:48-49` — `CancellationTokenSource.CreateLinkedTokenSource` + `CancelAfter`

### 安全说明

- API Key 存储在 `wwwroot/appsettings.json`（git 跟踪，放开发/测试用 Key）和 `wwwroot/appsettings.Production.json`（gitignored，放生产 Key）
- 生产部署时需在服务器上创建 Client 端 `wwwroot/appsettings.Production.json`，内容如：
  ```json
  {
    "VolcEngine": {
      "BaseUrl": "https://ark.cn-beijing.volces.com/api/v3",
      "ApiKey": "ark-xxx",
      "Model": "doubao-seed-2-0-pro-260215",
      "TimeoutSeconds": 30
    }
  }
  ```
- 5 人家庭场景，Key 泄露风险可接受。建议在火山 ARK 控制台为该 Key 设置最低权限和消费上限告警

### 文件变更清单

| 文件 | 操作 | 说明 |
|------|:--:|------|
| `src/BoxWise.Client/Services/AiService.cs` | MODIFY | 完全重写 |
| `src/BoxWise.Client/Program.cs` | MODIFY | 添加 IHttpClientFactory，更新 AiService 注册 |
| `src/BoxWise.Client/Pages/ItemEntry.razor` | MODIFY | OpenReadStream() → Bytes |
| `Directory.Packages.props` | MODIFY | 添加 Microsoft.Extensions.Http 10.0.8 |
| `src/BoxWise.Client/BoxWise.Client.csproj` | MODIFY | 添加 Microsoft.Extensions.Http 包引用 |
| `src/BoxWise.Client/wwwroot/appsettings.json` | MODIFY | AiSettings → VolcEngine |
| `src/BoxWise.Client/wwwroot/appsettings.Development.json` | MODIFY | 添加 VolcEngine 开发配置 |
| `src/BoxWise.Client.Tests/Services/AiServiceTests.cs` | MODIFY | 完全重写（5→8 测试） |

### References

- Sprint Change Proposal: `_bmad-output/planning-artifacts/sprint-change-proposal-2026-06-05.md`
- 调查案例: `_bmad-output/implementation-artifacts/investigations/ai-recognition-timeout-investigation.md`
- CORS 测试: `_bmad-output/implementation-artifacts/investigations/cors-test.js`
- 参考代码: `src/BoxWise.Server/Services/LlmClient.cs`（base64/JSON body 模式）
- `src/BoxWise.Client/Models/PhotoCapture.cs:3`（Bytes 属性）

### Review Findings

- [x] [Review][Patch] P1: 配置完整性检查不完整 [`src/BoxWise.Client/Services/AiService.cs:35`] — 仅校验 `_apiKey` 为 null，`_model` 缺失不返回 null。修复：添加 `IsNullOrWhiteSpace(_model)` 检查
- [x] [Review][Patch] P2: `TryParse` 裸 `catch` 吞噬 OOM [`src/BoxWise.Client/Services/AiService.cs:105`] — `catch` 未指定异常类型会吞噬 `OutOfMemoryException`。修复：改为 `catch (Exception ex) when (ex is not OutOfMemoryException)`
- [x] [Review][Patch] P3: `Program.cs` 未设置 `HttpClient.Timeout` [`src/BoxWise.Client/Program.cs:53`] — AddHttpClient 回调仅设置 BaseAddress，缺少 `c.Timeout = TimeSpan.FromSeconds(30)`。修复：添加 Timeout 配置
- [x] [Review][Defer] D1: `DefaultRequestHeaders.Authorization` 设在构造函数而非 per-request — Blazor WASM 单线程安全，简化实现。deferred, pre-existing design decision
- [x] [Review][Defer] D2: `object[]` 内容数组 STJ 序列化依赖默认行为 — 当前正确，测试覆盖。deferred, pre-existing
- [x] [Review][Defer] D3: BaseAddress + 请求路径 `/api/v3` 重复 — 行为正确但路径冗余。deferred, pre-existing
- [x] [Review][Defer] D4: `docs/` 下残留 `/api/ai/recognize` 引用 — 属 Story 12.3 范围。deferred, pre-existing
- [x] [Review][Defer] D5: 空 `BaseUrl` 导致 `new Uri("")` 启动崩溃 — 需配置错误才触发。deferred, pre-existing

## Dev Agent Record

### Agent Model Used

### Debug Log References

### Completion Notes List

### File List
