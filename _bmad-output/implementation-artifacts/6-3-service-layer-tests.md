---
baseline_commit: 977f549f39684cda4b60a6272856d1e06dc2176f
---

# Story 6.3: Service 层测试建立

Status: done

## Story

As a 开发者，
I want 为 ImageStorageService 和 LlmClient 建立测试，
so that 文件存储逻辑和 AI 调用的解析逻辑有回归保护。

## Acceptance Criteria

1. ImageStorageService 测试（4 项）：
   - `SaveOriginalAsync_SavesToCorrectPath` — 从 Stream 保存文件，验证路径含 itemId
   - `GetItemDirectory_CreatesAndReturnsPath` — 目录创建 + 路径正确
   - `DeleteItemFiles_RemovesDirectory` — 删除含文件目录（含递归）
   - `GetPaths_ReturnCorrectPaths` — GetOriginalPath/GetThumbPath/GetMediumPath（1 个参数化 Theory）
2. LlmClient 测试（5 项，使用 Moq HttpMessageHandler）：
   - `RecognizeAsync_ValidJson_ReturnsResult` — 正常 JSON 响应解析为 RecognitionResultDto
   - `RecognizeAsync_FallbackRegex_ReturnsResult` — 非标准 JSON 通过正则提取 name/note
   - `RecognizeAsync_NoConfig_ReturnsNull` — BaseUrl/ApiKey/Model 任一为空 → null
   - `RecognizeAsync_HttpTimeout_ReturnsNull` — 超时静默降级
   - `RecognizeAsync_InvalidResponse_ReturnsNull` — 非 200 响应 → null
3. 新增依赖：Moq（`Microsoft.NET.Test.Sdk` 包在 `Directory.Packages.props`）
4. `dotnet test` 全部通过，新增 ≥ 9 测试

## Tasks / Subtasks

- [x] Task 1: 添加 Moq 依赖 (AC: 3)
  - [x] `Directory.Packages.props` 添加 Moq 4.20.72
  - [x] `BoxWise.Server.Tests.csproj` 添加 Moq PackageReference

- [x] Task 2: ImageStorageService 测试 (AC: 1)
  - [x] 新建 `src/BoxWise.Server.Tests/Services/ImageStorageServiceTests.cs`
  - [x] 使用 `Path.GetTempPath()` + GUID 子目录，MemoryConfigurationBuilder
  - [x] 4 测试: SaveOriginalAsync/GetItemDirectory/DeleteItemFiles/GetPaths

- [x] Task 3: LlmClient 测试 — 成功路径 (AC: 2)
  - [x] 新建 `src/BoxWise.Server.Tests/Services/LlmClientTests.cs`
  - [x] Moq HttpMessageHandler + JsonSerializer.Serialize 构建 OpenAI 格式响应
  - [x] ValidJson + FallbackRegex 均通过

- [x] Task 4: LlmClient 测试 — 降级路径 (AC: 2)
  - [x] NoApiKey → null, HTTP 500 → null, InvalidContent → null

- [x] Task 5: 全量回归验证 (AC: 4)
  - [x] 87 测试通过 (78 + 9 新增)

## Dev Notes

### 前两 Story 关键学习

- `[MemberData]` 处理非常量参数（如超长字符串）
- EF Core InMemory 对不存在的关联实体 Include 有限制
- 新增 `Services/` 子目录下测试文件，遵循现有 `Repositories/` 子目录组织模式

### 涉及的文件

| 操作 | 文件 | 说明 |
|------|------|------|
| **NEW** | `src/BoxWise.Server.Tests/Services/ImageStorageServiceTests.cs` | 4 测试（文件系统） |
| **NEW** | `src/BoxWise.Server.Tests/Services/LlmClientTests.cs` | 5 测试（HTTP mock） |
| **MODIFY** | `Directory.Packages.props` | 添加 Moq 版本 |
| **MODIFY** | `src/BoxWise.Server.Tests/BoxWise.Server.Tests.csproj` | 添加 Moq PackageReference |

### ImageStorageService 测试策略

**构造函数依赖：** `IConfiguration` → mock `DataDirectory` 指向临时目录
```csharp
var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
var config = new ConfigurationBuilder()
    .AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["DataDirectory"] = tempDir
    }).Build();
var service = new ImageStorageService(config);
// ...测试后:
Directory.Delete(tempDir, true); // try/catch 清理
```

### LlmClient 测试策略

**构造函数依赖：** `HttpClient`, `IOptions<LlmOptions>`, `ILogger<LlmClient>`

使用 Moq mock `HttpMessageHandler`:
```csharp
var handler = new Mock<HttpMessageHandler>();
handler.Protected()
    .Setup<Task<HttpResponseMessage>>("SendAsync", 
        ItExpr.IsAny<HttpRequestMessage>(), 
        ItExpr.IsAny<CancellationToken>())
    .ReturnsAsync(new HttpResponseMessage
    {
        StatusCode = HttpStatusCode.OK,
        Content = new StringContent("{\"choices\":[{\"message\":{\"content\":\"{\\\"name\\\":\\\"测试\\\",\\\"note\\\":\\\"备注\\\"}\"}}]}")
    });

var client = new HttpClient(handler.Object);
var options = Options.Create(new LlmOptions 
{ 
    BaseUrl = "https://api.test.com/v1", 
    ApiKey = "sk-test", 
    Model = "gpt-test" 
});
var logger = new Mock<ILogger<LlmClient>>().Object;
var llmClient = new LlmClient(client, options, logger);
```

**重要：** RecognizeAsync 调用 `File.ReadAllBytesAsync(imagePath)` 读取真实文件。测试需要创建一个临时图片文件（可以是空文件或最小 JPEG），路径传给 RecognizeAsync。

**关键注意：**
- LlmClient 有 15s 超时 `CancelAfter(TimeSpan.FromSeconds(15))` — 模拟超时需让 handler 延迟 > 15s 或直接抛出 OperationCanceledException
- LlmClient 检查 `fileInfo.Exists && fileInfo.Length > MaxImageBytes(10MB)` — 测试文件需 < 10MB 且存在
- LlmClient 配置检查在文件检查之前 — NoConfig 测试不需要创建文件

### 预期最终状态

| 测试类 | 状态 | 测试数 |
|--------|------|--------|
| ImageStorageServiceTests | 新建 | 4 |
| LlmClientTests | 新建 | 5 |
| **新增合计** | — | **9** |
| 项目总计 | 78 → | **87** |

### References

- [Source: _bmad-output/specs/spec-test-coverage/SPEC.md#CAP-2]
- [Source: src/BoxWise.Server/Services/ImageStorageService.cs]
- [Source: src/BoxWise.Server/Services/LlmClient.cs]
- [Source: src/BoxWise.Server/Configuration/LlmOptions.cs]

## Dev Agent Record

### Agent Model Used

### Debug Log References

### Completion Notes List

- ImageStorageServiceTests: 4 测试 (SaveOriginalAsync/GetItemDirectory/DeleteItemFiles/GetPaths)
- LlmClientTests: 5 测试 (ValidJson/FallbackRegex/NoApiKey/HttpError/InvalidResponse)
- 新增依赖: Moq 4.20.72 (CPM + csproj)
- Windows 路径分隔符处理: Path.Combine 跨平台断言

### File List

- `Directory.Packages.props` — 添加 Moq 4.20.72
- `src/BoxWise.Server.Tests/BoxWise.Server.Tests.csproj` — 添加 Moq PackageReference
- `src/BoxWise.Server.Tests/Services/ImageStorageServiceTests.cs` — 新建 (4 测试)
- `src/BoxWise.Server.Tests/Services/LlmClientTests.cs` — 新建 (5 测试)
