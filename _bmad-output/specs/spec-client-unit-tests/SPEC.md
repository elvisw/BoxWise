---
id: SPEC-client-unit-tests
companions: []
sources: []
---

> **Canonical contract.** This SPEC and the files in `companions:` are the complete, preservation-validated contract for what to build, test, and validate. Source documents listed in frontmatter are for traceability only — consult them only if you need narrative rationale or prose color this contract intentionally omits.

# BoxWise Client 服务层单元测试

## Why

BoxWise.Client 包含 10 个 C# 文件（8 个 Service + 1 个 Handler + 1 个 Model），目前**零测试覆盖**。其中 AppState 是纯状态机、ItemService 包含 URL 查询串拼接逻辑、AiService 包含超时/异常降级分支、AuthService 协调多个依赖的状态同步——这些类包含实际业务逻辑，但每次修改只能通过手工点击 UI 验证。Server 端已有 85+ 测试的安全网，Client 端需要同等级别的保护。

## Capabilities

- id: CAP-1
  intent: 为 AppState 建立完整的状态转换测试，覆盖 SetUser、Clear、SetContinuousLocation、ClearContinuousLocation 四个方法及 StateChanged 事件触发。
  success: AppState 的每个 public 方法有 happy-path 测试，状态变更后 IsLoggedIn/IsAdmin/ContinuousLocationId 等属性断言正确，StateChanged 事件验证触发次数。

- id: CAP-2
  intent: 为 ItemService.GetFilteredAsync 建立 URL 查询串拼接测试，覆盖 locationId/tagIds/query 三参数的各种组合。
  success: 验证无参数时返回 `api/items`、单/多 tagId 时 `&` 分隔正确、query 经过 Uri.EscapeDataString 编码、null/空集合/空白字符串分支均有覆盖。

- id: CAP-3
  intent: 为 AiService.RecognizeAsync 建立异常降级测试，覆盖成功、OperationCanceledException（超时）、HttpRequestException（网络故障）三条路径。
  success: 成功路径返回 RecognitionResultDto、超时和网络异常均返回 null 不抛出、MultipartFormDataContent 构建正确（fileName + contentType 传递）。

- id: CAP-4
  intent: 为 AuthService 建立认证流程测试，覆盖登录成功/失败、登出、修改资料成功/失败及错误消息解析、修改密码成功/失败。
  success: LoginAsync 成功时 AppState.SetUser 和 NotifyAuthenticationStateChanged 被调用、失败时返回 LoginResult.Failure。UpdateProfileAsync/ChangePasswordAsync 错误时 TryGetErrorAsync 解析 ProblemDetails 的 Detail 和 Errors 字段。

- id: CAP-5
  intent: 新建 `src/BoxWise.Client.Tests/` 测试项目，使用与 Server.Tests 一致的技术栈（xUnit + Moq），通过 Mock HttpMessageHandler 模拟 HTTP 响应。
  success: `dotnet test BoxWise.Client.Tests` 可独立运行，零编译警告（WarningsAsErrors），所有测试在单次命令中通过。

## Constraints

- 测试框架：xUnit + Moq（与 Server.Tests 一致）
- HTTP Mock：通过 `Mock<HttpMessageHandler>` 注入 HttpClient，不发起真实网络请求
- 测试项目：新建 `src/BoxWise.Client.Tests/`，SDK 为 `Microsoft.NET.Sdk`，目标 `net10.0`
- 命名：遵循现有 `{方法名}_{场景}_{结果}` 约定
- 不修改被测试的 Client 源代码（纯测试补充）
- WarningsAsErrors 保持开启
- 不需要 bUnit — 不测试 Blazor 组件渲染

## Non-goals

- 不测试 LocationService / TagService / ItemEntryService — 纯 HTTP 委托，无分支逻辑
- 不测试 CookieHandler — 仅设置浏览器凭据标志，需浏览器运行时才有意义
- 不测试 PhotoCapture — 无行为的数据 record
- 不测试 Program.cs — DI 注册配置
- 不测试 CookieAuthenticationStateProvider — 其核心逻辑（Claims 构建 + 异常静默）依赖 `api/auth/me` 端点行为，与 AuthService 的集成测试重叠；单元测试 mock 价值低
- 不测试 Blazor UI 组件（Pages/Components）— 需要 bUnit，属于独立工作范畴
- 不追求特定代码覆盖率百分比

## Success signal

`dotnet test` 运行包括 `BoxWise.Client.Tests` 在内的全部测试项目，绿色通过，新增 35-40 个 Client 端测试。Client 服务层中每个包含分支逻辑的方法都有至少 1 个 happy-path + 关键异常路径测试。

## Assumptions

- Blazor WebAssembly 项目可被标准 `Microsoft.NET.Sdk` 测试项目引用（bUnit 已验证此模式可行）
- `Mock<HttpMessageHandler>` 的 `Protected()` 模式可用于模拟 HttpClient 响应（Moq 支持）
- Client 项目的 `net10.0` TFM 与测试项目的 `net10.0` TFM 兼容
- 现有 Server.Tests 的 `Directory.Packages.props` 中 xUnit/Moq 版本直接复用

## Open Questions

<!-- 全部已确认 — 无待解决问题 -->
