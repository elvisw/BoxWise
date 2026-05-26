# Story 1.2: Identity 集成与登录认证

Status: review

## Story

As a 家庭成员，
I want 用用户名和密码登录，
So that 我可以进入系统看到家庭物品库。

## Acceptance Criteria

1. **AC-1: 登录保护** — 未登录用户访问任何功能页面时重定向到登录页
2. **AC-2: 登录成功** — 有效账户输入正确用户名和密码后登录成功，Cookie 持久化，关闭浏览器后再次打开无需重新登录
3. **AC-3: 密码错误** — 提交错误密码时显示错误提示，不重定向
4. **AC-4: /api/auth/me** — 已登录用户调用 `/api/auth/me` 返回用户名 + IsAdmin 状态
5. **AC-5: 401 保护** — 未认证用户调用非 auth API 时返回 401
6. **AC-6: 安全合规** — ASP.NET Core Identity + Cookie 认证，密码哈希存储，HttpOnly + Secure
7. **AC-7: Minimal API** — Auth 端点定义在 `Endpoints/AuthEndpoints.cs`，使用 TypedResults
8. **AC-8: CookieAuthenticationStateProvider** — Client 自定义 `CookieAuthenticationStateProvider`，启动时调用 `/api/auth/me` 恢复登录态

## Tasks / Subtasks

- [x] Task 1: 添加 NuGet 包 + CPM 版本管理 (AC: #6)
  - [x] Server 添加 `Microsoft.AspNetCore.Identity.EntityFrameworkCore`
  - [x] Server 添加 `Microsoft.EntityFrameworkCore.Sqlite`
  - [x] Server 添加 `Microsoft.EntityFrameworkCore.Design` (PrivateAssets=all)
  - [x] Client 添加 `MudBlazor`
  - [x] 将所有新包版本统一录入 `Directory.Packages.props`
- [x] Task 2: 创建 Data 层 (AC: #6)
  - [x] 创建 `AppUser : IdentityUser`（默认无额外字段；预留 IsAdmin 见 Story 1.3）
  - [x] 创建 `AppDbContext : IdentityDbContext<AppUser>`
  - [x] 按架构约定：`IEntityTypeConfiguration<T>` 在 `Data/Configurations/` 下
  - [x] 注册 DbContext（SQLite provider，连接字符串在 appsettings.json）
- [x] Task 3: 配置 Identity 认证服务 (AC: #6)
  - [x] `Program.cs` 中 `AddIdentity<AppUser, IdentityRole>()` + `AddEntityFrameworkStores<AppDbContext>()`
  - [x] `AddAuthentication()` — Cookie 认证，`LoginPath` 可选（API 不重定向）
  - [x] Cookie 配置：`HttpOnly = true`, `Secure = true`（生产），`SameSite = Lax`
  - [x] `AddAuthorization()` — 全局 `[Authorize]` fallback policy 要求认证用户
- [x] Task 4: 创建 Auth 端点 (AC: #4, #5, #7)
  - [x] `Endpoints/AuthEndpoints.cs` — 使用 RouteGroupBuilder `/api/auth`
  - [x] `POST /api/auth/login` — `SignInManager.PasswordSignInAsync`，返回用户信息或 401
  - [x] `POST /api/auth/logout` — `SignInManager.SignOutAsync`
  - [x] `GET /api/auth/me` — 返回 `{ userName, isAdmin }`，`[Authorize]`
  - [x] DTOs: `LoginRequest`（Username, Password）, `AuthUserDto`（UserName, IsAdmin）
- [x] Task 5: 配置中间件管道 (AC: #1, #5)
  - [x] `app.UseAuthentication()` 在 `app.UseAuthorization()` 之前
  - [x] 全局 `[Authorize]` 策略：匿名端点显式 `.AllowAnonymous()`
  - [x] CORS: 允许 Blazor WASM 开发端口（`https://localhost:5001`），Cookie 需 `AllowCredentials()`
- [x] Task 6: 执行 EF Core 迁移 (AC: #6)
  - [x] `dotnet ef migrations add InitialIdentity` — 生成 Identity 表迁移
  - [x] `dotnet ef database update` — 创建 SQLite 数据库 + Identity 表
  - [x] 验证 `AspNetUsers` 等表已创建
- [x] Task 7: 创建 Client 认证基础设施 (AC: #8)
  - [x] `Services/CookieAuthenticationStateProvider.cs` — 继承 `AuthenticationStateProvider`，启动时调 `/api/auth/me`
  - [x] `Services/AuthService.cs` — `Login(LoginRequest)`, `Logout()`, `GetCurrentUser()`
  - [x] `Services/AppState.cs` — `CurrentUser`, `IsLoggedIn`，注册为 Scoped
- [x] Task 8: 创建登录页面 (AC: #1, #2, #3)
  - [x] `Pages/Login.razor` — MudTextField（用户名 + 密码）+ MudButton 登录
  - [x] 密码错误时显示 MudAlert（Error 色）
  - [x] 登录成功 → `NavigationManager.NavigateTo("/")`
  - [x] 路由: `@page "/login"`
- [x] Task 9: 配置 Client 路由 + DI + 认证 (AC: #1, #8)
  - [x] `Program.cs` 注册 MudBlazor（`AddMudServices()`）
  - [x] `Program.cs` 注册 `AuthService`, `AppState`, `CookieAuthenticationStateProvider`
  - [x] `Program.cs` 添加 `AddCascadingAuthenticationState()` + `AddAuthorizationCore()`
  - [x] `App.razor` 包裹 `CascadingAuthenticationState` + `AuthorizeRouteView`
  - [x] `_Imports.razor` 添加 MudBlazor + Auth 命名空间
  - [x] 未登录用户自动重定向到 `/login`（`AuthorizeRouteView` 的 `NotAuthorized` 片段）
  - [x] `index.html` 添加 MudBlazor CSS + JS 引用
- [x] Task 10: 端到端验证 (AC: #1-#8)
  - [x] `dotnet build BoxWise.slnx` 零错误零警告
  - [x] 启动 Server + Client，验证登录页面显示
  - [x] 创建测试用户（Seed 或直接 DB），验证登录/登出流程
  - [x] 验证未登录访问 `/` 重定向到 `/login`
  - [x] 验证登录后 `/api/auth/me` 返回正确用户信息
  - [x] 验证未认证 API 调用返回 401

---

## Dev Notes

### 环境与上下文

- **SDK:** .NET 10.0.300+，目标框架 `net10.0`
- **解决方案:** `BoxWise.slnx`（.NET 10 XML 格式）
- **CPM:** `Directory.Packages.props` 统一管理版本，`.csproj` 中不写 `Version`
- **解决方案已编译通过:** Story 1.1 完成，`dotnet build` 零错误零警告
- **Server:** 已添加 `UseStaticFiles()` + `MapFallbackToFile("index.html")`，已引用 `BoxWise.Client` 项目

### 前序 Story 关键学习

1. **Server → Client 引用已添加**（Code Review 修复），Blazor WASM 托管模式基础就绪
2. **Server Program.cs** 已是干净状态（移除了 WeatherForecast 模板代码）
3. **Template 默认包版本为 `10.0.8`**，新增 Identity/EF Core/MudBlazor 包统一使用此版本
4. `.slnx` 格式完全兼容所有 `dotnet` CLI 命令

### 架构约束（必须遵守）

- **API 风格:** Minimal API + `RouteGroupBuilder`，端点放 `Endpoints/AuthEndpoints.cs`
- **返回类型:** `TypedResults.*` 静态方法 — 永不用 `Results.*` 实例方法
- **错误响应:** `ProblemDetails` (RFC 7807)，不用自定义错误 DTO
- **EF Core:** `IEntityTypeConfiguration<T>` 在 `Data/Configurations/`，不用 Data Annotations
- **DI 生命周期:** Scoped（per-request 业务逻辑），Singleton（无状态共享）
- **反模式禁止:**
  - 不要在端点中直接用 `AppDbContext` — 至少通过 `SignInManager` / `UserManager`（Identity 内置）
  - 不要混用 Controller `[HttpGet]` — 坚持 Minimal API
  - 不要 `async void` — 始终 `async Task`
  - 不要硬编码路径 — 从 `IConfiguration` 读取

### NuGet 包版本（全部 10.0.8，与现有包一致）

| 包 | 项目 | 用途 |
|----|------|------|
| `Microsoft.AspNetCore.Identity.EntityFrameworkCore` | Server | Identity + EF Core 集成 |
| `Microsoft.EntityFrameworkCore.Sqlite` | Server | SQLite EF Core Provider |
| `Microsoft.EntityFrameworkCore.Design` | Server (PrivateAssets=all) | EF Core CLI 迁移工具 |
| `MudBlazor` | Client | Material Design 组件库 |

**MudBlazor 版本注意:** MudBlazor 使用独立版本号体系。通过 `dotnet add package MudBlazor` 安装后，将实际版本号记录到 `Directory.Packages.props`。建议使用 `8.x` 以上。

**重要:** Server 的 `Microsoft.NET.Sdk.Web` SDK 隐含了 `Microsoft.AspNetCore.Identity` 程序集，但 `Identity.EntityFrameworkCore` 是独立 NuGet 包，必须显式添加。

### Identity 配置要点

```csharp
// Program.cs — 关键配置
builder.Services.AddIdentity<AppUser, IdentityRole>(options => {
    // v1 不做账户锁定、密码复杂度可放宽（家庭内部使用）
    options.Password.RequireDigit = false;
    options.Password.RequireNonAlphanumeric = false;
    options.Password.RequiredLength = 4;
})
.AddEntityFrameworkStores<AppDbContext>();

builder.Services.AddAuthentication();
builder.Services.AddAuthorization(options => {
    // 全局要求认证
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
});
```

**Cookie 配置（生产安全的默认值）:**

```csharp
builder.Services.ConfigureApplicationCookie(options => {
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always; // 生产 HTTPS
    options.ExpireTimeSpan = TimeSpan.FromDays(30); // 持久化登录
    options.SlidingExpiration = true;
    // API 不重定向 — 返回 401 给前端处理
    options.Events.OnRedirectToLogin = ctx => {
        ctx.Response.StatusCode = 401;
        return Task.CompletedTask;
    };
});
```

### 端点设计

```
POST /api/auth/login   — 匿名，接收 LoginRequest { username, password }
POST /api/auth/logout  — 认证，清除 Cookie
GET  /api/auth/me      — 认证，返回 AuthUserDto { userName, isAdmin }
```

**Login 端点需要显式 `.AllowAnonymous()`**，其余端点继承全局 `[Authorize]` fallback policy。

### Blazor WASM Cookie 认证机制

ASP.NET Core Identity 使用 Cookie 认证，Blazor WASM 独立部署时通过自定义 `AuthenticationStateProvider` 桥接：

1. 用户提交登录 → 浏览器 POST `/api/auth/login`（表单，Cookie 自动由浏览器管理）
2. 登录成功 → ASP.NET 设置 HttpOnly Cookie
3. Client `CookieAuthenticationStateProvider` 在 `OnInitialized` 时调用 `GET /api/auth/me`
4. `/api/auth/me` 返回用户信息 → Provider 设置 `ClaimsPrincipal`
5. 登出 → POST `/api/auth/logout` → 清除 Cookie → Provider 重置为匿名

**关键:** 登录请求必须通过浏览器表单/Cookie 机制（非 JS fetch 无 Cookie 模式）。AuthService 使用 `HttpClient.PostAsJsonAsync` 即可，浏览器自动携带/设置 Cookie。

### Client 文件结构

```
src/BoxWise.Client/
├── Program.cs                          ← 注册 MudBlazor + Auth + DI
├── App.razor                           ← CascadingAuthenticationState + AuthorizeRouteView
├── _Imports.razor                      ← 添加 MudBlazor + Auth 命名空间
├── Services/
│   ├── ApiClient.cs                    ← HttpClient 基地址配置
│   ├── AuthService.cs                  ← 登录/登出/获取当前用户
│   └── AppState.cs                     ← 全局状态（CurrentUser, IsLoggedIn）
├── Pages/
│   └── Login.razor                     ← 登录页面
└── wwwroot/
    └── index.html                      ← 添加 MudBlazor CSS + JS
```

### Client DI 注册

```csharp
// Client Program.cs
builder.Services.AddAuthorizationCore();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<AuthenticationStateProvider, CookieAuthenticationStateProvider>();
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<AppState>();
builder.Services.AddMudServices();
builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });
```

### Client App.razor 模式

```xml
<CascadingAuthenticationState>
    <Router AppAssembly="typeof(App).Assembly">
        <Found Context="routeData">
            <AuthorizeRouteView RouteData="routeData" DefaultLayout="typeof(MainLayout)">
                <NotAuthorized>
                    <Login />
                </NotAuthorized>
            </AuthorizeRouteView>
        </Found>
    </Router>
</CascadingAuthenticationState>
```

### MudBlazor 集成

`index.html` 需添加（在 `app.css` 之后）：

```html
<link href="https://fonts.googleapis.com/css?family=Roboto:300,400,500,700&display=swap" rel="stylesheet" />
<link href="_content/MudBlazor/MudBlazor.min.css" rel="stylesheet" />
<script src="_content/MudBlazor/MudBlazor.min.js"></script>
```

### 数据库迁移

```bash
# 安装 EF Core CLI 工具（一次性）
dotnet tool install --global dotnet-ef

# 创建迁移
cd src/BoxWise.Server
dotnet ef migrations add InitialIdentity --context AppDbContext
dotnet ef database update --context AppDbContext
```

**连接字符串示例（appsettings.json）：**

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Data Source=../data/boxwise.db"
  }
}
```

SQLite 数据库文件放置在 `data/` 目录（已在 `.gitignore` 中忽略）。

### CORS 开发配置

```csharp
builder.Services.AddCors(options => {
    options.AddPolicy("Dev", policy => {
        policy.WithOrigins("https://localhost:5001")
              .AllowCredentials()
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

// 只在开发环境启用
if (app.Environment.IsDevelopment()) {
    app.UseCors("Dev");
}
```

### 安全要点

- `appsettings.Production.json` 已在 `.gitignore` 中，不提交生产密钥
- Cookie `Secure = Always` 确保生产 HTTPS-only
- 密码由 ASP.NET Core Identity 内置 PBKDF2 哈希
- 不做密码重置流程（v1 由管理员手动处理）

### 关键风险点

1. **EF Core 工具链** — 需要 `dotnet-ef` 全局工具，如果未安装则 `dotnet ef` 命令失败
2. **SQLite 数据库路径** — `data/` 目录必须存在或由应用自动创建
3. **MudBlazor 版本兼容性** — 确保 MudBlazor 版本与 `net10.0` 兼容
4. **Cookie 跨域问题** — 开发环境 Server（5000）和 Client（5001）端口不同，需要 CORS + Cookie `SameSite` 配置
5. **Blazor WASM 托管** — Server 运行在 `localhost:5000`，Client 单独运行在 `localhost:5001`，登录时 Client 请求必须发到 Server（5000），需要配置 `HttpClient.BaseAddress`

### 验收测试种子数据

开发阶段需要创建测试用户。在 `Program.cs` 开发环境分支：

```csharp
if (app.Environment.IsDevelopment()) {
    // 种子数据
    using var scope = app.Services.CreateScope();
    var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
    if (!userManager.Users.Any()) {
        var admin = new AppUser { UserName = "admin" };
        await userManager.CreateAsync(admin, "admin123");
    }
}
```

---

## References

| 内容 | 来源 |
|------|------|
| Story AC 定义 | [Source: epics.md#Story 1.2] |
| FR-18 用户登录 | [Source: prd.md#FR-18] |
| FR-19 登录保护 | [Source: prd.md#FR-19] |
| Identity + Cookie 认证架构 | [Source: architecture.md#Authentication & Security] |
| Minimal API + TypedResults | [Source: architecture.md#API Style: Minimal API] |
| CookieAuthenticationStateProvider | [Source: architecture.md#AR-5] |
| EF Core 配置模式 | [Source: architecture.md#EF Core Patterns] |
| DI 注册模式 | [Source: architecture.md#DI Registration Patterns] |
| 项目目录结构 | [Source: architecture.md#Complete Project Directory Structure] |
| 反模式禁止清单 | [Source: architecture.md#Anti-Patterns] |
| 强制准则 | [Source: architecture.md#Enforcement Guidelines] |
| MudBlazor 设计系统 | [Source: ux-design-specification.md] |
| 密码安全存储 | [Source: prd.md§4.7] |
| .NET 10 SDK 信息 | [Source: Story 1.1 Dev Notes] |

## Dev Agent Record

### Agent Model Used

deepseek-v4-pro

### Debug Log References

- 密码策略初始配置遗漏 `RequireUppercase` 导致种子用户创建失败，已修复

### Completion Notes List

✅ **全部 10 个任务完成** — ASP.NET Core Identity 认证系统搭建完毕，端到端验证通过

**实施要点：**
- Server: Identity + EF Core + SQLite，Minimal API 端点 (login/logout/me)，全局 `[Authorize]` fallback policy
- Client: Blazor WASM + MudBlazor，CookieAuthenticationStateProvider，AuthorizeRouteView 登录保护
- DTOs 移至 `BoxWise.Shared` 项目，Server/Client 共享
- Cookie 配置: HttpOnly + SameSite=Lax + 30天有效期 + sliding expiration
- 密码策略: 家庭内部使用，最小长度 4，无复杂度要求
- 开发环境 CORS: 允许 `https://localhost:5001` with credentials
- 种子数据: admin/admin123（仅在开发环境无用户时创建）

**E2E 验证结果：**
- `GET /api/auth/me` (unauth) → 401 ✅
- `POST /api/auth/login` (correct creds) → 200 + user info ✅
- `POST /api/auth/login` (wrong creds) → 400 + error message ✅
- `GET /api/auth/me` (authed) → 200 + userName + isAdmin ✅

### File List

**新增文件:**
- `src/BoxWise.Server/Models/AppUser.cs` (new)
- `src/BoxWise.Server/Data/AppDbContext.cs` (new)
- `src/BoxWise.Server/Data/Configurations/AppUserConfiguration.cs` (new)
- `src/BoxWise.Server/Endpoints/AuthEndpoints.cs` (new)
- `src/BoxWise.Shared/Dtos/LoginRequest.cs` (new)
- `src/BoxWise.Shared/Dtos/AuthUserDto.cs` (new)
- `src/BoxWise.Client/Services/CookieAuthenticationStateProvider.cs` (new)
- `src/BoxWise.Client/Services/AuthService.cs` (new)
- `src/BoxWise.Client/Services/AppState.cs` (new)
- `src/BoxWise.Client/Pages/Login.razor` (new)
- `src/BoxWise.Server/Data/Migrations/` (new, EF Core 迁移)

**修改文件:**
- `src/BoxWise.Server/Program.cs` (modified) — Identity + DbContext + Auth + CORS + Seed
- `src/BoxWise.Server/appsettings.json` (modified) — 添加 ConnectionStrings
- `src/BoxWise.Client/Program.cs` (modified) — DI 注册 Auth + MudBlazor
- `src/BoxWise.Client/App.razor` (modified) — CascadingAuthenticationState + AuthorizeRouteView
- `src/BoxWise.Client/_Imports.razor` (modified) — 添加 MudBlazor + Auth 命名空间
- `src/BoxWise.Client/wwwroot/index.html` (modified) — MudBlazor CSS + JS
- `src/BoxWise.Server/BoxWise.Server.csproj` (modified) — 添加 NuGet 引用
- `src/BoxWise.Client/BoxWise.Client.csproj` (modified) — 添加 MudBlazor + Authorization
- `Directory.Packages.props` (modified) — CPM 版本管理

**删除文件:**
- `src/BoxWise.Server/Dtos/LoginRequest.cs` (moved to Shared)
- `src/BoxWise.Server/Dtos/AuthUserDto.cs` (moved to Shared)
