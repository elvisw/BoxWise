# Story 12.2: 退役服务端 AI 识别代码

Status: done

baseline_commit: 0982e14

## Story

As a 开发者,
I want 移除服务端 AI 识别相关的已退役代码,
so that 代码库保持清洁、无死代码残留，且图片上传魔数验证不丢失。

## Acceptance Criteria

1. `LlmClient.cs` (Server) 文件已删除
2. `LlmOptions.cs` (Server) 文件已删除
3. `AiEndpoints.cs` (Server) 整个文件已删除（`/api/ai/recognize` 端点 → 404）
4. `IsValidMagic` 方法 + JPEG/PNG/WebP 魔数字节常量已从 `AiEndpoints.cs` 移植到 `ImageEndpoints.cs`，图片上传保留文件字节校验不丢失
5. `Program.cs` (Server) 中 `AddOptions<LlmOptions>()`、`AddHttpClient<LlmClient>()`、`MapAiEndpoints()` 三处调用已移除
6. `LlmClientTests.cs` (Server.Tests) 文件已删除（6 个测试）
7. Server 端 `appsettings.Production.json`（gitignored 文件）中 Llm 配置块已手动移除
8. `dotnet build` 零错误，`dotnet test` 全部通过（测试总数减少 6）

## Tasks / Subtasks

**⚠️ 操作顺序约束：必须按以下顺序执行，否则中间状态编译失败！**
先修改 `Program.cs`（移除对 AI 类型的引用）→ 再删除 AI 文件。`ImageEndpoints.cs` 移植可随时进行（无依赖关系）。

- [ ] Task 1: 移植 `IsValidMagic` 到 `ImageEndpoints.cs`（独立任务，可最先执行）
  - [ ] 从 `AiEndpoints.cs` 复制魔数字节常量：`JpegMagic` / `PngMagic` / `RiffMagic` / `WebpMagic`
  - [ ] 复制 `IsValidMagic(byte[] header, int length)` 方法（保持原始签名）
  - [ ] 在 `UploadAsync` 中 `file.OpenReadStream()` 之后插入魔数验证（见 Dev Notes 推荐方案）
  - [ ] **同步修复：** `AllowedTypes.Contains(file.ContentType)` → 添加 `StringComparer.OrdinalIgnoreCase`（防止 `image/JPEG` 大写被拒绝）

- [ ] Task 2: 修改 `Program.cs` (Server) — **必须在删除 AI 文件之前执行**
  - [ ] 移除 `using BoxWise.Server.Configuration;`（该命名空间仅含 `LlmOptions`，删除后为空）
  - [ ] 移除 `builder.Services.AddOptions<LlmOptions>()` 注册（第 151-154 行，共 4 行）
  - [ ] 移除 `builder.Services.AddHttpClient<LlmClient>()`（第 155 行）
  - [ ] 移除 `app.MapAiEndpoints()` 调用（第 414 行）

- [ ] Task 3: 删除服务端 AI 代码文件 — **Task 2 完成后方可执行**
  - [ ] 删除 `src/BoxWise.Server/Endpoints/AiEndpoints.cs`（109 行）
  - [ ] 删除 `src/BoxWise.Server/Services/LlmClient.cs`（153 行）
  - [ ] 删除 `src/BoxWise.Server/Configuration/LlmOptions.cs`（21 行）
  - [ ] 删除 `src/BoxWise.Server.Tests/Services/LlmClientTests.cs`（176 行，6 个测试）

- [ ] Task 4: 手动清理 `appsettings.Production.json`（gitignored 文件，本地操作）
  - [ ] 编辑 `src/BoxWise.Server/appsettings.Production.json`（如果存在）
  - [ ] 移除整个 `"Llm": { "BaseUrl": "...", "ApiKey": "...", "Model": "...", "TimeoutSeconds": ... }` 配置块
  - [ ] 注意：即使保留也不会导致启动失败（未绑定的配置被静默忽略），但应清理避免运维混淆

- [ ] Task 5: 验证
  - [ ] `dotnet build` 零错误
  - [ ] `dotnet test` 全部通过
  - [ ] 验证图片上传仍正常（魔数校验 + Content-Type 校验 + 文件大小校验均生效）

## Dev Notes

### IsValidMagic 移植 + stream 位置处理

**目标位置：** `ImageEndpoints.UploadAsync` 中 `await using var stream = file.OpenReadStream()` 之后、`await storage.SaveOriginalAsync()` 之前。

**✅ 推荐方案：读头部 → 验证 → `stream.Position = 0` 回退**

`IFormFile.OpenReadStream()` 在 ASP.NET Core 中返回 `BufferedReadStream`，**是可查找的**（`CanSeek = true`），因为 `ReadFormAsync()` 已将整个表单缓冲。直接回退流位置，零额外内存分配：

```csharp
await using var stream = file.OpenReadStream();
// 魔数验证：读取头部字节
var header = new byte[12];
var headerLen = await stream.ReadAsync(header.AsMemory(0, 12));
if (!IsValidMagic(header, headerLen))
    return TypedResults.Problem("文件格式不支持，请上传有效的图片", statusCode: 400);
stream.Position = 0;  // 回退 → SaveOriginalAsync 写入完整文件
await storage.SaveOriginalAsync(itemId, stream);
```

> **为什么不推荐方案 A（`CopyToAsync` + `ToArray` + `new MemoryStream(bytes)`）？**
> 10MB 文件会产生 ~20MB 瞬时 LOH 分配（MemoryStream 内部缓冲区 + ToArray 副本），高并发时可能导致大对象堆碎片。`Position = 0` 完全避免了此开销，代码也更简洁。

### 移植时同步修复：AllowedTypes 大小写不敏感

`ImageEndpoints.cs` 当前第 47 行缺少 `StringComparer`：
```csharp
// BEFORE (有缺陷):
if (!AllowedTypes.Contains(file.ContentType))

// AFTER (修复):
if (!AllowedTypes.Contains(file.ContentType, StringComparer.OrdinalIgnoreCase))
```
如果浏览器发送 `image/JPEG`（大写），原代码会拒绝。`AiEndpoints.cs` 已经使用了 `OrdinalIgnoreCase`，移植验证时一并修复。

### SaveOriginalAsync 签名

实际签名为 `Task<string> SaveOriginalAsync(int itemId, Stream stream)`（返回文件路径），但 `ImageEndpoints.cs` 当前调用未使用返回值（`await storage.SaveOriginalAsync(itemId, stream);`），无需修改调用代码。

### 文件变更清单

| 文件 | 操作 | 说明 |
|------|:--:|------|
| `src/BoxWise.Server/Services/LlmClient.cs` | DELETE | 153 行退役 |
| `src/BoxWise.Server/Configuration/LlmOptions.cs` | DELETE | 21 行退役，`BoxWise.Server.Configuration` 命名空间变空 |
| `src/BoxWise.Server/Endpoints/AiEndpoints.cs` | DELETE | 109 行退役（IsValidMagic + 魔数常量已移植） |
| `src/BoxWise.Server.Tests/Services/LlmClientTests.cs` | DELETE | 176 行退役（6 个测试） |
| `src/BoxWise.Server/Program.cs` | MODIFY | 移除 4 处 AI 相关代码 |
| `src/BoxWise.Server/Endpoints/ImageEndpoints.cs` | MODIFY | 添加 IsValidMagic + 魔数验证 + AllowedTypes 大小写修复 |
| `src/BoxWise.Server/appsettings.Production.json` | MANUAL | 移除 Llm 配置块（gitignored，需手动处理本地副本） |

### Program.cs 变更详情

当前代码（需移除）：

```csharp
// Line 16: using 声明
using BoxWise.Server.Configuration;  // ← 移除（命名空间仅含 LlmOptions，删除后为空）

// Line 151-155: DI 注册
builder.Services.AddOptions<LlmOptions>()       // ← 移除（4 行）
    .Bind(builder.Configuration.GetSection(LlmOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddHttpClient<LlmClient>();     // ← 移除

// Line 414: 端点映射
app.MapAiEndpoints();                            // ← 移除
```

### 测试影响

| 测试项目 | 变更 | 数量 |
|---------|------|:--:|
| `LlmClientTests.cs` | 删除 | -6 |
| Server.Tests | 232 → 226 | -6 |
| 全部测试 | 261 → 255 | -6 |

- 现有 `ImageEndpointsTests.cs` 使用 `SkiaSharp` 生成真实 JPEG 字节（FF D8 FF 魔数 → IsValidMagic 通过），无需修改
- `WebApplicationFactory<Program>` 在移除注册后可正常启动
- 无其他测试引用 `LlmClient` 或 AI 端点代码

### RecognitionResultDto 保留说明

`RecognitionResultDto` (Shared DTO) 在 Server 端已无引用（`LlmClient.cs` + `AiEndpoints.cs` 均删除），但 **Client 端 `AiService.cs` 仍使用**，不能删除。本 Story 不涉及此文件。

### 检查清单

- [ ] `grep -rn "LlmClient\|LlmOptions\|MapAiEndpoints" src/` 仅返回本文档自身（零残留）
- [ ] `grep -rn "BoxWise.Server.Configuration" src/` 无结果（命名空间已空）
- [ ] 图片上传功能完整：魔数验证（新）+ Content-Type 校验（已有）+ 文件大小校验（已有）
- [ ] `stream.Position = 0` 回退已验证可用（BufferedReadStream.CanSeek = true）

### References

- Sprint Change Proposal: `_bmad-output/planning-artifacts/sprint-change-proposal-2026-06-05.md`
- Story 12.1: `_bmad-output/implementation-artifacts/12-1-frontend-direct-api.md`
- 移植源: `src/BoxWise.Server/Endpoints/AiEndpoints.cs:10-14` (魔数常量), `:89-108` (IsValidMagic)
- 移植目标: `src/BoxWise.Server/Endpoints/ImageEndpoints.cs`
- 待修改: `src/BoxWise.Server/Program.cs:16,151-155,414`

### Review Findings

- [x] [Review][Patch] P1: `stream.ReadAsync` 未传递 CancellationToken [`src/BoxWise.Server/Endpoints/ImageEndpoints.cs:70`] — 新增魔数读取调用未传递方法参数中的 `cancellationToken`，原 `AiEndpoints.cs` 使用带 token 的三参数重载，当前实现导致用户取消上传时读取不被中断。修复：`await stream.ReadAsync(header.AsMemory(0, 12), CancellationToken.None);`
- [x] [Review][Patch] P2: `image/jpg` Content-Type 被静默拒绝 [`src/BoxWise.Server/Endpoints/ImageEndpoints.cs:12`] — 原 `AiEndpoints.cs` AllowedTypes 含 `"image/jpg"`，移植后丢失。部分 Windows 系统可能发送此 MIME，导致合法 JPEG 文件被 Content-Type 检查拒绝（在魔数检查之前）。修复：在 `AllowedTypes` 数组中添加 `"image/jpg"`
- [x] [Review][Patch] P3: 缺少魔数验证失败路径的单元测试 [`src/BoxWise.Server.Tests/Endpoints/ImageEndpointsTests.cs`] — 新加入的 `IsValidMagic` 拒绝路径（合法 Content-Type + 无效魔数字节 → 400）无测试覆盖。修复：新增 `UploadAsync_InvalidMagic_Returns400` 测试用例
- [x] [Review][Patch] P4 (第二轮评审): 测试仅断言 400 未验证响应体 [`src/BoxWise.Server.Tests/Endpoints/ImageEndpointsTests.cs:227`] — 若其他校验（如 itemId 无效）也返回 400 则产生误报。修复：新增 `Assert.Contains("文件格式不支持", body)` 响应体验证
- [x] [Review][Defer] D1: `stream.Position = 0` 无 CanSeek 守卫 [`src/BoxWise.Server/Endpoints/ImageEndpoints.cs:72`] — ASP.NET Core BufferedReadStream 始终可 Seek，但 IFormFile 接口不保证 seekability。Spec 已明确记录此设计决策并拒绝了 MemoryStream 回退方案（避免 10MB LOH 分配）。deferred, pre-existing design decision
- [x] [Review][Defer] D2: appsettings 中残留 `"Llm"` 配置节变为静默死配置 — `AddOptions<LlmOptions>()` 移除后，现有 `appsettings.Development.json` 中的 `"Llm"` 节将静默忽略。不造成功能问题，后续清理即可。deferred, pre-existing

## Dev Agent Record

### Agent Model Used

### Debug Log References

### Completion Notes List

### File List
