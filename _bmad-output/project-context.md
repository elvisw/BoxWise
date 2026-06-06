---
project_name: 'BoxWise'
user_name: 'Elvis'
date: '2026-06-02'
sections_completed:
  ['technology_stack', 'language_rules', 'framework_rules', 'testing_rules', 'quality_rules', 'workflow_rules', 'anti_patterns']
status: 'complete'
rule_count: 78
optimized_for_llm: true
---

# Project Context for AI Agents

_This file contains critical rules and patterns that AI agents must follow when implementing code in this project. Focus on unobvious details that agents might otherwise miss._

---

## Technology Stack & Versions

### 运行时与框架
- **.NET SDK:** 10.0.300+ | **Target:** net10.0
- **Web 框架:** ASP.NET Core Minimal API (Server) + Blazor WASM Standalone (Client)
- **UI 框架:** MudBlazor 9.5.0

### 数据与存储
- **数据库:** SQLite (EF Core 10.0.8 + Microsoft.EntityFrameworkCore.Sqlite)
- **迁移工具:** EF Core Design 10.0.8
- **文件存储:** 本地文件系统 (`{DataDirectory}/images/`)

### 认证
- ASP.NET Core Identity 10.0.8（脚手架 Razor Pages + Cookie 认证）
- 登录/2FA/账户管理由 Identity 脚手架页面处理（`Areas/Identity/Pages/Account/`）
- 开发环境 SameSite=None + Secure（跨端口 5000↔5001），生产环境 Lax + Always
- 通行密钥（WebAuthn/FIDO2）登录保留在 Blazor WASM（`Login.razor`）

### 关键依赖
| 包 | 版本 | 用途 |
|---|---|---|
| MudBlazor | 9.5.0 | UI 组件库（API 与 v8 有显著差异） |
| SkiaSharp | 3.119.4 | 缩略图生成（MIT 许可，跨平台） |
| Identity UI | 10.0.8 | Identity 脚手架 Razor Pages |
| CodeGeneration.Design | 10.0.8 | Identity 脚手架代码生成 |
| xUnit | 2.9.3 | 测试框架 |
| EF Core InMemory | 10.0.8 | 测试数据库 |
| Moq | 4.20.72 | Mock 框架 |

### 构建基础设施
- Directory.Build.props — Nullable enable / ImplicitUsings / WarningsAsErrors
- Directory.Packages.props — CPM 集中包版本管理 (ManagePackageVersionsCentrally)
- .slnx 新格式解决方案文件

### 部署
- Docker 多阶段构建 (mcr.microsoft.com/dotnet/aspnet:10.0)
- Caddy 反向代理 (自动 Let's Encrypt TLS)

## Critical Implementation Rules

### C# 语言规则

**Null 安全:**
- Nullable 已全局启用，所有引用类型默认 non-null
- 禁止使用 `!` (null-forgiving) 压制警告——除非有明确验证逻辑保证非 null

**类型与命名:**
- DTO 使用 positional record，放在 `BoxWise.Shared.Dtos/`
- Entity 放在 `BoxWise.Server.Models/`，不直接暴露给客户端
- 所有公共 API 使用 PascalCase，参数/局部变量 camelCase，私有字段 `_camelCase`

**异步:**
- 始终使用 `async Task` / `async Task<T>`，禁止 `async void`
- 端点方法使用 `CancellationToken` 参数（Repository 层已支持）
- EF Core 异步方法：`ToListAsync()`、`FirstOrDefaultAsync()`、`SaveChangesAsync()`

**错误处理:**
- Repository 层抛出 `ArgumentException`（验证失败）和 `KeyNotFoundException`（不存在）
- 端点层捕获异常映射为 `TypedResults.Problem()` / `TypedResults.NotFound()`
- `DbUpdateException` 用于并发冲突兜底

**DI 与生命周期:**
- Scoped: Repository、DbContext
- Singleton: ImageStorageService、ThumbnailService（无状态）
- `HttpClient` 通过 `AddHttpClient<T>()` 注册（自动管理生命周期）
- 禁止在 Singleton 服务中注入 Scoped 服务——使用 `IServiceScopeFactory`

### ASP.NET Core Minimal API

**端点组织:**
- 每个资源组一个文件：`Endpoints/AuthEndpoints.cs`、`ItemEndpoints.cs`、`WebAuthnEndpoints.cs` 等
- 退役端点（已删除）：TwoFactorEndpoints.cs、TwoFactorModifyEndpoints.cs、EmailVerificationEndpoints.cs
- 新增：`Areas/Identity/Pages/Account/` — Identity 脚手架 Razor Pages 处理登录/登出/2FA/账户管理
- 使用 `RouteGroupBuilder` 静态扩展方法组织路由
- 路由命名：小写、复数资源名 — `/api/items`、`/api/locations`
- `MapRazorPages()` 必须在 `MapFallbackToFile()` 之前 — 否则 Identity 页面被 Blazor WASM SPA 回退拦截

**返回类型:**
- 始终使用 `TypedResults.*` 静态方法 — `TypedResults.Ok()`、`TypedResults.Created()`、`TypedResults.NoContent()`
- 错误使用 `TypedResults.Problem(detail, statusCode: 400)` — 不要套在 `BadRequest()` 里
- 列表直接返回数组 `[item1, item2]`，不要包装对象 `{ data: [...] }`
- 搜索/筛选结果附加 `X-Total-Count` 响应头
- 每个端点必须声明 `.ProducesProblem(401)` + 对应的成功 `.Produces<T>()`

**授权:**
- 全局 `FallbackPolicy` = `RequireAuthenticatedUser()`，所有端点默认需认证
- 匿名端点显式标记 `.AllowAnonymous()`
- Admin 端点使用 `AdminOnly` 策略：`policy.RequireRole("Admin")`
- Identity 脚手架页面自带 `[Authorize]` 保护，Login/Register 页面匿名

### EF Core

**Entity 配置:**
- **必须** 使用 `IEntityTypeConfiguration<T>`，**禁止** 使用 Data Annotation 属性
- 配置类放在 `Data/Configurations/` 下
- 表名 PascalCase 复数 — `Items`、`Locations`、`Tags`

**Materialized Path（位置树）:**
- Location 表 `Path TEXT NOT NULL` 列，格式 `/1/3/7/`
- 子树查询：`.Where(l => l.Path.StartsWith(parentPath) && l.Path != parentPath)`
- 子树移动：`ExecuteUpdateAsync(s => s.SetProperty(l => l.Path, ...))`
- **禁止**写 raw SQL 做位置查询——必须通过 `LocationRepository`

**级联删除:**
- 应用层处理：先删物理文件（图片），再删 DB 记录
- 不要依赖数据库级联——SQLite 对级联支持有限

### MudBlazor 9.x 关键 API

| 禁止（v8/旧文档） | 正确（v9.x） | 组件 |
|---|---|---|
| `@bind-ActivatedValue` | `SelectedValue` + `SelectedValueChanged` | MudTreeView |
| `Filter` / `Filter="true"` | `SelectionMode="SelectionMode.MultiSelection"` | MudChipSet |
| `MultiSelection="true"` | `SelectionMode="SelectionMode.MultiSelection"` | MudChipSet |
| `<Text>` 子元素 | `<BodyContent>` | MudTreeViewItem |

- TreeView Items 必须为 `IReadOnlyCollection<TreeItemData<T>>`
- SelectedValues 类型为 `IReadOnlyCollection<T>`
- MUD0002 分析器报错是正确行为——遵守它，不要禁用

### Blazor WASM

**DI 注册顺序（Client Program.cs）:**
1. `HttpClient` — **必须最先注册**（所有 Service 依赖它）
2. `CookieAuthenticationStateProvider` — 同时注册具体类型和抽象类型
3. 其余 Service 按依赖顺序注册

**认证状态:**
- `CookieAuthenticationStateProvider` 启动时调用 `GET /api/auth/me`
- 登录通过 Identity `Login.cshtml`（Server 端 Razor Page）→ Cookie 签发 → 302 重定向到 `/`
- 通行密钥登录保留在 Blazor WASM `Login.razor`（仅 WebAuthn 按钮，无用户名/密码表单）
- 客户端 `AuthService.cs` 仅保留 WebAuthn 方法 + `UpdateProfileAsync`（其余认证方法已退役）
- `Settings.razor` "管理账户设置"按钮跳转到 Identity Manage 页面（新标签页打开）
- 开发环境跨端口：`CookieHandler` 处理 cookie 跨源（SameSite=None + Secure）

### 测试

**框架与配置:**
- xUnit + EF Core InMemory + Moq
- 每测试独立创建 DbContext（GUID 命名数据库），保证测试隔离
- 使用 `TestDbContextFactory.Create()` 创建隔离的 InMemory 上下文

**测试组织:**
- 测试项目 `src/BoxWise.Server.Tests/` 镜像 Server 项目结构
- Repository 测试 → `Repositories/` 文件夹
- Service 测试 → `Services/` 文件夹
- 命名：`{ClassName}Tests.cs`

**测试范围:**
- Repository 层：覆盖正常路径 + 边界（空值、超长、不存在 ID、重复创建、业务规则违反）
- Service 层：mock 外部依赖，验证业务逻辑
- 每个 Repository 的 CRUD 测试至少包含：create、get、update、delete、not-found 五个场景

**禁止:**
- 不要 mock 数据库——使用 InMemory 测试真实 EF Core 行为
- 不要共享 DbContext 实例——每个测试独立创建
- 不要在测试中使用 `async void`——始终 `async Task`

### 代码质量与风格

**格式与编译:**
- `TreatWarningsAsErrors` = true — 所有警告都是错误，构建必须零警告
- 使用 .NET 内置代码分析 + MudBlazor MUD0002 分析器

**文件组织:**
- Server：`Endpoints/` (API) → `Services/` (业务逻辑) → `Repositories/` (数据访问) → `Models/` (实体)
- Client：`Pages/` (路由页面) → `Components/` (可复用组件) → `Services/` (客户端逻辑)
- Shared：仅 `Dtos/`，不放业务逻辑

**注释策略:**
- 默认不写注释——代码应该自说明
- 仅在 WHY 不明显时加注释：隐藏约束、微小不变量、特定 bug 修复
- 不要写解释代码做什么的注释（标识符已经说明）
- 不要写多行 docstring 或注释块

**名称处理:**
- 所有用户输入的名称统一 `Trim()` + `Length > N` 校验
- API JSON 字段 camelCase（System.Text.Json 默认）
- 数据库列名 PascalCase

### 开发工作流

**端口与入口:**
| 地址 | 用途 | 热重载 |
|------|------|--------|
| `https://localhost:5001` | Blazor WASM UI 开发（推荐，热重载） | 有 |
| `https://localhost:5000` | API + Admin + Identity 页面 + 完整集成测试 | 无 |

- 开发环境 Client 跨端口请求到 Server：`CookieHandler` + CORS `Dev` 策略
- Admin 后台是 Server 端 Razor Pages，仅在 5000 端口可用
- 生产环境同源部署，`ApiBaseUrl` 为空 → 所有请求走同源

**构建与运行:**
```bash
dotnet build                          # 构建全方案
dotnet test BoxWise.slnx               # 运行所有测试
cd src/BoxWise.Server && dotnet run   # 启动 Server
cd src/BoxWise.Client && dotnet run   # 启动 Client（热重载）
```

**EF Core 迁移:**
```bash
cd src/BoxWise.Server
dotnet ef migrations add <Name>
dotnet ef database update
```

**Docker 部署:**
- 持久化: `./data:/app/data` (SQLite + 图片)
- 环境变量：`Admin__Password` 创建管理员 | Client 端 `wwwroot/appsettings.Production.json` 配置 AI (VolcEngine)

### 关键避坑规则

**反模式（绝对不能做）:**
- ~~Minimal API 与 Controller 混用~~ — 只使用 Minimal API
- ~~raw SQL 做位置查询~~ — 必须通过 LocationRepository 的 materialized path 模式
- ~~`async void`~~ — 始终 `async Task` / `async Task<T>`
- ~~端点直接使用 DbContext~~ — 必须经过 Service/Repository 层
- ~~`File.Exists()` + `File.Delete()` 无异常处理~~ — 文件 I/O 必须 try/catch
- ~~硬编码路径~~ — 始终从 `IConfiguration` / `IOptions<T>` 读取
- ~~`TypedResults.BadRequest(TypedResults.Problem(...))`~~ — 直接 `TypedResults.Problem(...)`
- ~~MudBlazor v8 API~~ — 使用 v9.x API（SelectedValue、SelectionMode、BodyContent）
- ~~在 Singleton 中注入 Scoped~~ — 使用 `IServiceScopeFactory` 创建 scope
- ~~重新实现登录/2FA/账户管理端点~~ — 已由 Identity 脚手架页面替代，自定义端点已退役
- ~~`MapRazorPages()` 放在 `MapFallbackToFile()` 之后~~ — Identity 页面路由会被 Blazor WASM SPA 拦截

**安全规则:**
- Cookie: HttpOnly=true, Secure 策略按环境切换
- 三处 Cookie 配置（主/TwoFactorUserId/Session）**必须同步**更新 SameSite/SecurePolicy
- 开发环境：SameSite=None + SecurePolicy.SameAsRequest（跨端口 5000↔5001）
- 生产环境：SameSite=Lax + SecurePolicy.Always（Caddy 反向代理）
- 所有端点默认需认证（FallbackPolicy），匿名端点显式 `.AllowAnonymous()`
- Admin 页面：`[Authorize(Roles = "Admin")]` 双重保护（Razor Page + API 层）
- 密码不记录日志，API key 不序列化到客户端

**数据完整性:**
- 删除物品时先删文件再删记录，文件删除失败不阻断 DB 删除
- 位置删除检查子节点：`WHERE Path LIKE '/parent/%'` 确认无子节点
- 并发：`DbUpdateException` 捕获兜底，返回 409 Conflict

**AI 降级:**
- AI 识别由客户端直调火山 ARK API，30s 超时静默降级——不抛异常
- 前端检测 AI 返回 null 时静默降级为手动输入，不阻断录入流程

---

## Usage Guidelines

**For AI Agents:**
- Read this file before implementing any code in this project
- Follow ALL rules exactly as documented — this is the single source of truth
- When in doubt, prefer the more restrictive option
- If you discover a new pattern not documented here, propose adding it

**For Humans:**
- Keep this file lean and focused on agent needs — not a replacement for developer docs
- Update when technology stack or architectural decisions change
- Review quarterly for outdated rules
- Remove rules that become obvious or are superseded by newer patterns

Last Updated: 2026-06-02
