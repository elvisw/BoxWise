---
id: SPEC-test-coverage
companions:
  - test-inventory.md
sources: []
---

> **Canonical contract.** This SPEC and the files in `companions:` are the complete, preservation-validated contract for what to build, test, and validate. Source documents listed in frontmatter are for traceability only — consult them only if you need narrative rationale or prose color this contract intentionally omits.

# BoxWise 单元测试补完

## Why

BoxWise 项目当前有 52 个有意义的单元测试（另 1 个是脚手架占位死代码），覆盖了 3 个 Repository 的核心路径，但整个 Service 层（3 个类）、整个 Endpoint 层（6 个静态类）、1 个 Admin PageModel 和 2 个 Repository 方法完全没有测试。这导致每次重构或升级依赖（如 MudBlazor 9.x、.NET 10）时只能靠手工验证，Epic 5 回顾中 CR 发现的关键 bug（NormalizedUserName、SecurityStamp）如果在开发阶段有测试覆盖就能提前捕获。目标是系统性地将测试覆盖扩展到所有包含业务逻辑的 Server 端代码，建立可回归的安全网。

## Capabilities

- id: CAP-1
  intent: 补齐 Repository 层缺失的测试方法和边界条件，使 ItemRepository、TagRepository、LocationRepository 的所有公开方法均有覆盖。
  success: 每个 Repository 的每个 public 方法至少有 1 个 happy-path 测试 + 关键异常路径测试。新增测试数 ≥ 12。

- id: CAP-2
  intent: 为 Service 层（ImageStorageService、ThumbnailService、LlmClient）建立测试，使用真实文件系统（临时目录）+ Mock HttpClient（针对 LlmClient）。
  success: 3 个 Service 类中的 2 个（ImageStorageService、LlmClient）核心方法有测试。ThumbnailService 因依赖 SkiaSharp 位图操作且逻辑与 ImageEndpoints 紧密耦合，降级为手动验证。

- id: CAP-3
  intent: 为 Endpoint 层建立测试，使用 TestIdentityFactory 提供的 UserManager/SignInManager 直接调用静态 handler 方法，验证请求-响应完整路径。
  success: 6 个 Endpoint 文件中至少 4 个（AuthEndpoints、ItemEndpoints、TagEndpoints、LocationEndpoints）有覆盖核心 happy-path 和常见错误路径的测试。ImageEndpoints 和 AiEndpoints 因依赖文件 I/O 和外部 HTTP 调用，优先级降低。

- id: CAP-4
  intent: 补齐 Admin PageModel 测试，覆盖 CreateAccountModel.OnPostAsync 以及其余 PageModel 中未测试的 OnGetAsync handler。
  success: CreateAccountModel 有 happy-path + 验证失败测试。EditAccountModel.OnGetAsync、IndexModel.OnGetAsync、ChangeUserPasswordModel.OnGetAsync 各有测试。

- id: CAP-5
  intent: 清理测试质量问题 — 删除死代码 UnitTest1.cs、将重复性的边界验证从 Fact 重构为 Theory、统一测试命名。
  success: UnitTest1.cs 已删除。至少 3 个参数化 [Theory] 测试替代现有的多个相似 Fact。所有新增测试遵循 Arrange-Act-Assert 三段式结构。

- id: CAP-6
  intent: 确保现有 52 个测试持续通过，新增测试不破坏已有行为。
  success: `dotnet test` 零失败。所有测试可在单次命令中运行完成。

## Constraints

- 测试框架：xUnit + EF Core InMemory Database + Moq（如需 Mock）
- 测试项目：`src/BoxWise.Server.Tests/`，不新建测试项目
- 数据库隔离：每个测试独立创建 GUID 命名的 InMemory DbContext，遵循 `TestDbContextFactory.Create()` 和 `TestIdentityFactory.CreateAsync()` 现有模式
- 文件系统：使用 `Path.GetTempPath()` + GUID 子目录，测试后清理
- 命名：遵循现有 `{方法名}_{场景}_{结果}` 约定
- 无需集成测试、无需 Selenium/Playwright UI 测试、无需性能基准测试
- 不修改被测试的源代码（纯测试补充）
- dotnet test 执行时间 ≤ 30 秒

## Non-goals

- 不测试 Blazor WASM 客户端组件（Client 项目）— 需要 bUnit，属于独立的 UI 测试范畴
- 不测试纯 CRUD 无逻辑的代码路径（如单行 `_db.Set<T>().Add(entity)` 委托）
- 不测试 Program.cs / 启动配置 / DI 注册
- 不达到特定代码覆盖率百分比 — 追求有效测试而非数字指标
- 不引入集成测试（不启动 WebApplicationFactory/TestServer）
- 不修改现有测试的方法签名或断言逻辑（除非测试本身有 bug）

## Success signal

`dotnet test` 运行全部测试，绿色通过，总数从 52 增长到 ≥ 85，覆盖所有 Repository 方法、主要 Service 方法、核心 Endpoint handler 和剩余 Admin PageModel handler。新开发者 checkout 代码后运行测试即可获得系统行为的基线信心。

## Assumptions

- TestIdentityFactory 提供的 UserManager/SignInManager 实例可直接用于 Endpoint handler 测试
- ImageStorageService 的文件操作可以完全用临时目录测试，无需 mock IConfiguration
- LlmClient 可以使用 Moq 的 HttpMessageHandler 模式模拟 OpenAI API 响应
- 现有 52 个测试的断言逻辑是正确的，无需修改
- 项目使用 .NET 10 的 InMemoryDatabase 行为与 9/8 兼容，无已知 bug

## Open Questions

<!-- 全部已确认 — 无待解决问题 -->
