# AGENTS.md

本文件适用于在 BoxWise 仓库中工作的 AI 代理。

## 子代理委派

- **重型任务和开发任务必须委派给 `deepseek-v4.1-flash` 子代理执行。** 重型任务包括需要多个阶段、跨模块或跨层调查的大范围变更与迁移；开发任务包括实现功能、修复缺陷、重构和编写或修改自动化测试。
- 此规则由主代理执行。主代理负责界定范围、处理审批、审查交付并独立复验；子代理执行已分配任务，无需再次委派。不得仅凭子代理自报结果宣称验证通过。
- 如果该子代理在当前工具中不可用或调用失败，说明原因并等待用户决定；不得静默改用其他模型或由主代理代做。

## 构建与运行

```bash
# 构建整个解决方案
dotnet build BoxWise.slnx

# Server：API + Admin Razor Pages + Blazor WASM 静态回退
cd src/BoxWise.Server && dotnet run

# Client：Blazor WASM 开发服务器（热重载）
cd src/BoxWise.Client && dotnet run

# EF Core 迁移（在 src/BoxWise.Server 目录下）
dotnet ef migrations add <MigrationName>
dotnet ef database update
```

- 启动 Server 会执行 EF 迁移并可能写入管理员/LLM 种子数据。**仅做文档或代码核对时，不要启动应用、连接业务数据库或执行部署。**
- 开发环境必须使用 HTTPS：`SameSite=None` Cookie 需要 `Secure`，纯 HTTP 下浏览器会拒绝登录 Cookie。
- 构建启用 `TreatWarningsAsErrors`（见 [Directory.Build.props](Directory.Build.props)）；不要通过抑制警告绕过编译失败。

### 开发入口与 ApiBaseUrl

| 入口 | 提供内容 | 热重载 | 场景 |
|------|---------|--------|------|
| `https://localhost:5001` | Client 开发服务器（Blazor WASM 页面） | 有 | 日常 UI 开发 |
| `https://localhost:5000` | Server：API + Admin Razor Pages + WASM 静态回退 | 无 | Admin / 集成测试 |

- 默认双端口开发时，Client（5001）使用 `wwwroot/appsettings.Development.json` 中的 `ApiBaseUrl` 跨源请求 Server（5000）。`src/BoxWise.Client/Program.cs` 在页面地址不是 loopback、未配置有效 API 地址或端口相同时，使用页面同源地址作为 `Http.BaseAddress`。
- Admin 后台是 Server 端 Razor Pages，本地开发直接访问 `https://localhost:5000/admin`。`src/BoxWise.Client/Pages/Settings.razor` 的入口仅对管理员显示，使用 `IConfiguration["ApiBaseUrl"]` 生成链接，不依赖 `Http.BaseAddress`。
- 开发跨端口请求依赖 `CookieHandler`（credentials include）和 Server 开发环境的 CORS `Dev` 策略（仅允许 `https://localhost:5001`）。
- 生产环境不配置 `ApiBaseUrl`，页面/API/Admin 全部同源，Admin 路径为 `/admin`。

## 测试

```bash
dotnet test BoxWise.slnx
dotnet test src/BoxWise.Server.Tests
dotnet test src/BoxWise.Client.Tests
```

- 测试项目只有两个：`src/BoxWise.Server.Tests/`（引用 Server）和 `src/BoxWise.Client.Tests/`（引用 Client）。
- Server.Tests 使用 xUnit + Moq + EF Core InMemory（`TestDbContextFactory.Create()` 每个测试独立建库），覆盖 Repository、Service、Endpoint；认证/2FA 流程另有 `WebApplicationFactory` + 临时 SQLite 的进程内 E2E。
- Client.Tests 使用 xUnit + Moq，覆盖客户端服务。
- 不要 mock DbContext，不要共享 DbContext 实例；测试方法使用 `async Task`，不用 `async void`。

## 部署

```bash
# 二进制发布
dotnet publish src/BoxWise.Server -c Release -o publish

# Docker（Caddy + boxwise）
docker compose up -d
```

- Docker 架构：Caddy `443/80` → `boxwise:5000`；应用数据挂载 `./data:/app/data`。
- `data/` 下需要持久化：SQLite 数据库、物品图片、Data Protection 密钥环（`keys/`）、SMTP 配置（`smtp-config.json`）。丢失 `keys/` 会导致已加密数据无法解密。
- 首次启动通过 `Admin__Password` 创建管理员；登录后到 `/admin` 创建家庭成员账户。Admin 页面由 `AdminOnly` 策略保护。
- AI 配置由 `LlmApi__*` 环境变量在首次启动时种子入库，之后可在 `/admin/llm-config` 修改；未配置时 AI 静默降级为手动输入。
- 详细步骤见 [README](README.md) 和 [部署指南](docs/deployment-guide.md)。

## 项目结构与依赖

```
BoxWise.slnx
├── src/
│   ├── BoxWise.Client/        # Blazor WASM PWA
│   ├── BoxWise.Server/        # ASP.NET Core Minimal API + Admin Razor Pages
│   ├── BoxWise.Shared/        # 共享 DTO
│   ├── BoxWise.Server.Tests/  # Server 测试
│   └── BoxWise.Client.Tests/  # Client 测试
├── docs/                      # 架构、部署、安全文档
└── data/                      # 运行时数据（SQLite、图片、密钥）
```

- Server 主要目录：`Endpoints/`、`Repositories/`、`Services/`、`Data/`、`Models/`、`Areas/Identity/`、`Pages/Admin/`、`Migrations/`。
- 引用关系：Client → Shared；Server → Shared + Client（用于 `.MapFallbackToFile("index.html")` SPA 回退）。
- 目标框架和包版本分别以 [Directory.Build.props](Directory.Build.props)、[Directory.Packages.props](Directory.Packages.props) 为准（CPM 集中管理）；本文件不重复抄版本号。

## 关键技术决策

- **API：** Minimal API + `RouteGroupBuilder` 静态扩展方法组织端点。
- **数据库：** SQLite + EF Core；迁移在 Server 项目下维护。
- **图片：** SkiaSharp 生成 300px 缩略图和 1200px 中图，后台异步处理。
- **AI：** 客户端从 `GET /api/llm/config` 取得配置（需登录，含 API Key）后，在浏览器直连 OpenAI 兼容端点（默认火山 ARK）；超时 5–120 秒、默认 30 秒，失败静默降级。服务端只负责配置存取，不代理推理请求。

## 认证与客户端 DI

1. 浏览器首次加载 → `CookieAuthenticationStateProvider.GetAuthenticationStateAsync()` 调用 `GET /api/auth/me`，成功后水合 `AppState`。
2. 密码登录由 Server 端 Identity Razor Pages 处理，成功后重定向到本地返回地址（默认 `/`）；客户端加载时重新读取会话。通行密钥登录走客户端 `/login` 和 WebAuthn，成功后更新 `AppState`。
3. 全局 `FallbackPolicy = RequireAuthenticatedUser()`；需要匿名的端点显式 `.AllowAnonymous()`。
4. `"Admin"` 角色通过 `userManager.IsInRoleAsync(user, "Admin")` 判断，结果经 `AuthUserDto.IsAdmin` 传到客户端。
5. 认证、2FA、速率限制和 Data Protection 的细节见 [认证与安全](docs/auth-security.md)。

**客户端 DI：**

- 沿用 `src/BoxWise.Client/Program.cs` 的注册顺序：先注册使用 `CookieHandler` 的应用 `HttpClient`，再注册依赖它的服务。
- `CookieAuthenticationStateProvider` 需要两种注册：具体类型（`AuthService` 注入）和 `AuthenticationStateProvider` 抽象转发（`AuthorizeRouteView` 使用）。

## 端点开发规范

- Repository 返回 Entity，端点负责 Entity → DTO 映射；Scoped DI。
- 返回 `TypedResults` + `ProblemDetails`；错误用 `TypedResults.Problem(...)` 直接返回，不要嵌套在 `TypedResults.BadRequest()` 里。
- 按现有端点约定将参数校验失败映射为 `400`，目标不存在映射为 `404`。
- 所有端点加 `.ProducesProblem(401)` 注解。
- DTO 用 positional record，放在 `BoxWise.Shared.Dtos`。
- 名称统一 `Trim()` + 长度校验；并发写入捕获 `DbUpdateException` 兜底。
- 端点按资源组织为 `MapXxxEndpoints` 静态扩展方法。

## Identity 脚手架与退役检查

- 修改 `src/BoxWise.Server/Areas/Identity/` 前先查阅 [Identity 脚手架修改记录](docs/identity-scaffold-modifications.md)；任何改动都必须继续记录在该文件。
- 涉及退役（删除文件/方法/端点/组件）的 Story，close 前必须：
  - 用全文搜索检查 `docs/`、`AGENTS.md`、`_bmad-output/`，确认无已退役标识符残留（或仅剩退役说明本身）。
  - 同步 PR 描述，使其反映最新完成状态，不等到 Epic 结束。
  - 涉及 `Areas/Identity/` 的，更新上述脚手架修改记录。

## 版本管理

- `Directory.Build.targets` 的 `SetVersionFromGit` 在构建前执行 `git describe --tags --first-parent --match v* --abbrev=7 --always`，写入 `Version` / `InformationalVersion`；关于页读取并显示。
- CI 见 [.github/workflows/release.yml](.github/workflows/release.yml)：`main`/PR 执行构建 + 测试，`v*` 标签发布 linux-x64/win-x64 包并创建 Release。
- CI/Docker 构建前确保标签可见（`git fetch --tags`）。

## MudBlazor 组件约定

- `MudTreeView<T>`：使用 `SelectedValue` / `SelectedValueChanged`；`Items` 为 `TreeItemData<T>` 集合；自定义内容放 `BodyContent`。
- `MudChipSet<T>`：多选用 `SelectionMode="SelectionMode.MultiSelection"`；`SelectedValues` 类型为 `IReadOnlyCollection<T>`；不使用旧版的 `Filter` / `MultiSelection` 参数。
- 不要禁用 `MUD0002` 分析器；参数名不符合 9.x 约定时应修正代码。官方文档与编译结果冲突时，以安装版本的源码/分析器为准。

## BMad 工作流

- 本地安装：官方 BMad v7 Preview Alpha（模块版本 **6.13.0-next**，`core-tools` + `method`）。`_bmad/` 被 gitignore，新克隆需要另行安装，不能假设仓库自带完整安装。
- 入口：使用 `bmad` / `bmad-agent-po` 原生技能；票据解析用 `uv run _bmad/method/scripts/tickets.py --project-root . status`。
- 工件：活动 initiative 在 `_bmad-output/initiative-boxwise/`，历史独立票据在 `_bmad-output/backlog/`，v6 原始档在 `initiative-boxwise/archive-v6/`。
- 进度：以 `tickets.py status` 的票据树为准，不在本文件复制 Epic/Story 完成数。v7 Preview 已迁移；v6 路径仅保留历史归档。

## 文档入口

- [开发指南](docs/development-guide.md)
- [API 合约](docs/api-contracts-server.md)
