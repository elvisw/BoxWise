# Story 1.3: 后台管理界面 — 账户管理

Status: review

## Story

As a 管理员，
I want 通过后台界面创建家庭成员账户，
so that 其他家庭成员有自己的账号登录使用。

## Acceptance Criteria

1. **AC-1: 管理员查看账户列表** — 管理员（Admin 角色）已登录，访问 `/admin` 时显示所有用户账户列表，包含用户名和管理员状态
2. **AC-2: 管理员创建新账户** — 管理员填写用户名和密码并提交，新账户创建成功，用户名在系统内唯一；用户名重复时显示错误提示
3. **AC-3: 非管理员拒绝访问** — 非管理员（无 Admin 角色）已登录，访问 `/admin` 时返回 403 Forbidden
4. **AC-4: Admin 角色种子数据** — 开发环境启动时，种子数据自动创建 "Admin" 角色并分配给 `admin` 用户

## Tasks / Subtasks

- [x] Task 1: 修复种子数据 — 创建 Admin 角色 + 分配 (AC: #4)
  - [x] 1.1 在 `Program.cs` 种子数据块中，使用 `RoleManager<IdentityRole>` 确保 "Admin" 角色存在
  - [x] 1.2 种子 `admin` 用户创建后，调用 `userManager.AddToRoleAsync(admin, "Admin")` 分配角色

- [x] Task 2: 添加 Razor Pages 服务 + 中间件 (AC: #1, #3)
  - [x] 2.1 `Program.cs` 中 `builder.Services.AddRazorPages()`
  - [x] 2.2 `Program.cs` 中间件管道中 `app.MapRazorPages()` — **必须在 `MapFallbackToFile` 之前**，否则 `/admin` 被 SPA 回退拦截
  - [x] 2.3 配置 Razor Pages 路由：`options.Conventions.AuthorizeAreaFolder("Admin", "/")` 或在 PageModel 上使用 `[Authorize]`

- [x] Task 3: 创建 Admin 授权策略 (AC: #3)
  - [x] 3.1 `Program.cs` 的 `AddAuthorization` 中添加 `"AdminOnly"` 策略：`RequireRole("Admin")`
  - [x] 3.2 Admin Razor Pages 的 PageModel 添加 `[Authorize(Policy = "AdminOnly")]`

- [x] Task 4: 创建 Admin DTOs (AC: #1, #2)
  - [x] 4.1 `src/BoxWise.Shared/Dtos/UserListItemDto.cs` — record: `string UserName, bool IsAdmin`
  - [x] 4.2 `src/BoxWise.Shared/Dtos/CreateAccountRequest.cs` — record: `string Username, string Password`
  - [x] 4.3 遵循现有 DTO 模式（`record` 类型，放在 `BoxWise.Shared.Dtos` 命名空间）

- [x] Task 5: 创建 Admin 布局基础设施 (AC: #1, #2)
  - [x] 5.1 `Pages/Admin/_ViewStart.cshtml` — 指向 Admin 专用 Layout
  - [x] 5.2 `Pages/Admin/_ViewImports.cshtml` — 添加必要的 Tag Helpers + using
  - [x] 5.3 `Pages/Admin/_Layout.cshtml` — 简洁管理后台布局（纯 HTML + inline CSS，项目 Primary 色 `#546E7A`）
  - [x] 5.4 `Pages/_ViewImports.cshtml`（根 Pages 目录）— 添加 Tag Helpers + admin 命名空间

- [x] Task 6: 创建账户列表页 (AC: #1)
  - [x] 6.1 `Pages/Admin/Index.cshtml` — 用户列表表格（用户名 + 是否管理员），导航到创建页的链接
  - [x] 6.2 `Pages/Admin/Index.cshtml.cs` — PageModel，通过 `UserManager<AppUser>` 获取所有用户，传入视图
  - [x] 6.3 路由：`@page "/admin"`，GET 请求

- [x] Task 7: 创建账户创建页 (AC: #2)
  - [x] 7.1 `Pages/Admin/CreateAccount.cshtml` — 表单：用户名输入框 + 密码输入框 + 提交按钮
  - [x] 7.2 `Pages/Admin/CreateAccount.cshtml.cs` — PageModel，POST 处理：验证用户名唯一性 → `userManager.CreateAsync()` → 成功跳转 Index / 失败显示错误
  - [x] 7.3 路由：`@page "/admin/create"`，POST 提交

- [x] Task 8: 构建验证 + 端到端测试 (AC: #1-#4)
  - [x] 8.1 `dotnet build BoxWise.slnx` 零错误零警告
  - [x] 8.2 启动 Server，验证 `admin/admin123` 登录后 `GET /api/auth/me` 返回 `IsAdmin: true`
  - [x] 8.3 验证 `admin` 用户访问 `/admin` 看到账户列表
  - [x] 8.4 验证 `/admin/create` 创建新账户成功，重定向回 `/admin` 列表
  - [x] 8.5 验证创建重复用户名时显示错误提示
  - [x] 8.6 验证非 Admin 用户访问 `/admin` 返回 403

---

## Dev Notes

### 前置上下文

- **SDK:** .NET 10.0.300+，目标框架 `net10.0`
- **解决方案:** `BoxWise.slnx`（.NET 10 XML 格式）
- **CPM:** `Directory.Packages.props` 统一管理版本，`.csproj` 中不写 `Version`
- **Story 1.1 + 1.2 已完成:** 项目骨架 + Identity 认证系统就绪，`dotnet build` 零错误零警告
- **Server 已引用 Client 项目:** `MapFallbackToFile("index.html")` 用于 SPA 回退

### 前序 Story 关键学习

1. **Server → Client 项目引用** 已在 Code Review 修复 — Blazor WASM 托管模式基础就绪
2. **认证端点模式** — `AuthEndpoints.cs` 使用 `RouteGroupBuilder` + 静态扩展方法，所有端点 `[Authorize]` 默认保护
3. **DTO 模式** — 使用 `record` 类型，放在 `BoxWise.Shared.Dtos` 命名空间
4. **EF Core 配置** — `IEntityTypeConfiguration<T>` 在 `Data/Configurations/`，不用 Data Annotations
5. **已知 Bug** — 种子数据创建了 `admin` 用户，但从未创建 `"Admin"` 角色或分配。`AuthEndpoints.cs` 中 `IsInRoleAsync(user, "Admin")` 目前始终返回 `false`

### 关键架构约束

- **Admin UI 独立:** 使用 Server 端 Razor Pages（`Pages/Admin/`），**不走 Blazor WASM**（AR-6）
- **API 风格:** 本 Story 不新增 API 端点 — Admin 页面直接通过 `UserManager` 操作 Identity
- **授权方式:** PageModel 上用 `[Authorize(Policy = "AdminOnly")]`，非管理员返回 403
- **Admin 角色:** 使用 ASP.NET Core Identity 内置 `IdentityRole`（"Admin" 角色），不用自定义 `IsAdmin` 标志 —— 已有代码（`AuthEndpoints.cs`）中 `IsInRoleAsync(user, "Admin")` 已采用此路径
- **反模式禁止:**
  - 不要直接在 Razor Page 中 `new AppDbContext()` — 使用 `UserManager<AppUser>` / `RoleManager<IdentityRole>`
  - 不要混用 Controller `[HttpGet("...")]` / ApiController — 坚持 Razor Pages 或 Minimal API 二选一
  - 不要 `async void` — 始终 `async Task`
  - 不要创建独立的 Admin Service 层 — v1 规模可直接在 PageModel 中使用 `UserManager`

### 种子数据修复（Task 1 — 详细指导）

当前 `Program.cs` 种子数据块（开发环境分支）：

```csharp
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseCors("Dev");

    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();

    var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
    if (!userManager.Users.Any())
    {
        var admin = new AppUser { UserName = "admin" };
        var result = await userManager.CreateAsync(admin, "admin123");
        if (!result.Succeeded)
        {
            var errors = string.Join(", ", result.Errors.Select(e => e.Description));
            app.Logger.LogWarning("Failed to create seed user: {Errors}", errors);
        }
    }
}
```

**修复后**需要在 `userManager.CreateAsync` 成功后添加：

```csharp
// 确保 Admin 角色存在
var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
if (!await roleManager.RoleExistsAsync("Admin"))
{
    await roleManager.CreateAsync(new IdentityRole("Admin"));
}

// 分配 Admin 角色给种子用户
if (!await userManager.IsInRoleAsync(admin, "Admin"))
{
    await userManager.AddToRoleAsync(admin, "Admin");
}
```

**注意：** 角色创建和分配逻辑应放在 `if (!userManager.Users.Any())` 分支之外（或内），确保即使数据库已迁移但无用户时也能正确创建角色。推荐结构：

```
if 无用户:
    创建 admin 用户
创建/确保 Admin 角色存在
如果 admin 用户不在 Admin 角色中:
    加入 Admin 角色
```

### Razor Pages 管道顺序（Task 2 — 关键决策）

**`MapRazorPages()` 必须在 `MapFallbackToFile` 之前注册**，否则 SPA 回退会拦截 `/admin` 路由。

```csharp
app.UseAuthentication();
app.UseAuthorization();

app.MapAuthEndpoints();
app.MapRazorPages();           // ← Razor Pages 在这里
app.MapFallbackToFile("index.html"); // ← SPA 回退在最后
app.Run();
```

**Razor Pages 根目录约定:** ASP.NET Core 默认在项目根目录的 `Pages/` 下查找 Razor Pages。无需额外配置 `AddRazorPagesOptions` 的 `RootDirectory`。

### 授权策略配置（Task 3 — 完整代码）

```csharp
builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();

    // Admin 专用策略
    options.AddPolicy("AdminOnly", policy =>
        policy.RequireRole("Admin"));
});
```

**PageModel 授权:**
```csharp
[Authorize(Policy = "AdminOnly")]
public class IndexModel : PageModel
{
    // ...
}
```

**403 返回处理:** ASP.NET Core 默认对认证但未授权的请求返回 `AccessDenied` 路径重定向。对于 Admin Razor Pages（非 API），需配置返回 403：

```csharp
builder.Services.ConfigureApplicationCookie(options =>
{
    // ... 现有配置 ...
    options.Events.OnRedirectToAccessDenied = ctx =>
    {
        ctx.Response.StatusCode = 403;
        return Task.CompletedTask;
    };
});
```

### Admin Razor Pages 页面模型（Task 6, 7 — 关键代码路径）

**`Index.cshtml.cs` — 账户列表：**
```csharp
[Authorize(Policy = "AdminOnly")]
public class IndexModel : PageModel
{
    private readonly UserManager<AppUser> _userManager;

    public IndexModel(UserManager<AppUser> userManager)
    {
        _userManager = userManager;
    }

    public List<UserListItemDto> Users { get; set; } = [];

    public async Task OnGetAsync()
    {
        var users = await _userManager.Users.ToListAsync();
        Users = [];
        foreach (var user in users)
        {
            var isAdmin = await _userManager.IsInRoleAsync(user, "Admin");
            Users.Add(new UserListItemDto(user.UserName ?? "", isAdmin));
        }
    }
}
```

**`CreateAccount.cshtml.cs` — 创建账户：**
```csharp
[Authorize(Policy = "AdminOnly")]
public class CreateAccountModel : PageModel
{
    private readonly UserManager<AppUser> _userManager;

    public CreateAccountModel(UserManager<AppUser> userManager)
    {
        _userManager = userManager;
    }

    [BindProperty]
    public CreateAccountRequest Input { get; set; } = new("", "");

    [TempData]
    public string? ErrorMessage { get; set; }

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (string.IsNullOrWhiteSpace(Input.Username) || string.IsNullOrWhiteSpace(Input.Password))
        {
            ErrorMessage = "用户名和密码不能为空";
            return Page();
        }

        var existingUser = await _userManager.FindByNameAsync(Input.Username);
        if (existingUser is not null)
        {
            ErrorMessage = $"用户名 '{Input.Username}' 已存在";
            return Page();
        }

        var user = new AppUser { UserName = Input.Username };
        var result = await _userManager.CreateAsync(user, Input.Password);

        if (!result.Succeeded)
        {
            ErrorMessage = string.Join("; ", result.Errors.Select(e => e.Description));
            return Page();
        }

        return RedirectToPage("/Admin/Index");
    }
}
```

**关键：** `CreateAccountRequest` DTO 使用 `record` 的 `required` 属性或构造函数参数——在 Razor Pages 的 `[BindProperty]` 中需注意 `record` 的 model binding 兼容性。推荐使用 positional record：`public record CreateAccountRequest(string Username, string Password);`

### Admin DTOs（Task 4 — 定义）

```csharp
// src/BoxWise.Shared/Dtos/UserListItemDto.cs
namespace BoxWise.Shared.Dtos;

public record UserListItemDto(string UserName, bool IsAdmin);
```

```csharp
// src/BoxWise.Shared/Dtos/CreateAccountRequest.cs
namespace BoxWise.Shared.Dtos;

public record CreateAccountRequest(string Username, string Password);
```

### 项目引用情况

当前 Server 通过以下引用链共享 DTOs：
```
BoxWise.Server → BoxWise.Shared ← BoxWise.Client
```

新增 DTOs 放在 `BoxWise.Shared` 中，Server 和 Client 均可引用（Client 侧不直接使用 Admin DTOs，但 Shared 保持统一）。

### 文件结构变更总览

```
src/BoxWise.Shared/Dtos/
  UserListItemDto.cs              (new)
  CreateAccountRequest.cs         (new)

src/BoxWise.Server/
  Program.cs                      (modified — seed fix + Razor Pages + Admin policy)
  Pages/
    _ViewImports.cshtml           (new)
    Admin/
      _ViewStart.cshtml           (new)
      _ViewImports.cshtml         (new)
      _Layout.cshtml              (new)
      Index.cshtml                (new)
      Index.cshtml.cs             (new)
      CreateAccount.cshtml        (new)
      CreateAccount.cshtml.cs     (new)
```

**无 Client 侧变更** — Admin UI 为 Server 端 Razor Pages，不涉及 Blazor WASM 客户端。

### 构建与验证

```bash
# 1. 完整构建
dotnet build BoxWise.slnx

# 2. 运行 Server
cd src/BoxWise.Server && dotnet run

# 3. 验证种子数据
curl -k https://localhost:5000/api/auth/login -d '{"username":"admin","password":"admin123"}' -H "Content-Type: application/json" -c cookies.txt
curl -k https://localhost:5000/api/auth/me -b cookies.txt
# 预期输出: {"userName":"admin","isAdmin":true}

# 4. 验证 Admin 页面
# 浏览器访问 https://localhost:5000/admin（需携带 Cookie）
# 预期：显示用户列表页

# 5. 验证权限拒绝
# 创建普通用户 → 用普通用户登录 → 访问 /admin
# 预期：返回 403
```

### 关键风险点

1. **Razor Pages 与 SPA 回退冲突** — `MapRazorPages()` 必须在 `MapFallbackToFile` 之前注册，否则所有 `/admin` 请求被 SPA 回退拦截
2. **`record` 与 Razor Pages Model Binding** — `record` 类型默认没有无参构造函数，Razor Pages model binding 可能失败。如果遇到绑定问题，改用 `class` 或添加 `[BindProperty]` 属性的显式配置
3. **种子数据幂等性** — 确保多次启动不会重复创建 Admin 角色或报错。使用 `RoleExistsAsync` 和 `IsInRoleAsync` 检查
4. **403 处理** — `OnRedirectToAccessDenied` 需在 Cookie 配置中显式重写，否则 Identity 默认重定向到 `/Account/AccessDenied`（不存在）

---

## References

| 内容 | 来源 |
|------|------|
| Story AC 定义 | [Source: epics.md#Story 1.3] |
| FR-17 用户注册 | [Source: prd.md#FR-17] |
| Admin Razor Pages 架构决策 | [Source: architecture.md#AR-6: Admin Razor Pages 独立区域] |
| Admin IsAdmin 标记 | [Source: architecture.md#Admin Identification: IsAdmin Flag] |
| API 授权策略 | [Source: architecture.md#API Authorization: Authenticated-Only] |
| Identity 角色检查（现有代码路径） | [Source: AuthEndpoints.cs#IsInRoleAsync(user, "Admin")] |
| EF Core 配置模式 | [Source: architecture.md#EF Core Patterns] |
| DTO 模式（record） | [Source: Story 1.2: AuthUserDto.cs, LoginRequest.cs] |
| Minimal API 模式 | [Source: AuthEndpoints.cs#RouteGroupBuilder] |
| Cookie + Blazor WASM 认证 | [Source: Story 1.2: CookieAuthenticationStateProvider] |
| 种子数据当前实现（待修复） | [Source: Program.cs lines 69-85] |
| UX 设计系统颜色 | [Source: ux-design-specification.md#Design System: MudBlazor] |
| 项目目录结构 | [Source: Story 1.1 Dev Notes#最终目录结构] |
| 全局 FallbackPolicy | [Source: Program.cs lines 43-48] |

## Dev Agent Record

### Agent Model Used

deepseek-v4-pro

### Debug Log References

- `[TempData]` 在 `return Page()` 时不生效 —— 修复：改用普通属性存储错误消息

### Completion Notes List

✅ **全部 8 个任务完成** — 管理员后台界面搭建完毕，所有 AC 端到端验证通过

**实施要点：**
- 种子数据修复：创建 "Admin" 角色 + 分配给 admin 用户（`RoleManager<IdentityRole>` → `AddToRoleAsync`）
- Admin UI 使用 Server 端 Razor Pages（不在 Blazor WASM 客户端）
- 授权基于 `IdentityRole`（"Admin" 角色），使用 `[Authorize(Policy = "AdminOnly")]`
- `MapRazorPages()` 在 `MapFallbackToFile` 之前注册，确保 `/admin` 不被 SPA 回退拦截
- 403 返回通过 `OnRedirectToAccessDenied` 事件重写实现
- 账户创建使用 ASP.NET Core Identity 内置反伪造令牌保护

**E2E 验证结果：**
- `GET /api/auth/me` (admin) → `{"userName":"admin","isAdmin":true}` ✅
- `GET /admin` (admin) → 200 + 账户列表 ✅
- `POST /admin/create` (admin) → 302 重定向到 `/admin` ✅
- `POST /admin/create` (重复用户名) → 200 + 错误提示 ✅
- `GET /admin` (非管理员) → 403 ✅

### File List

**新增文件:**
- `src/BoxWise.Shared/Dtos/UserListItemDto.cs` (new)
- `src/BoxWise.Shared/Dtos/CreateAccountRequest.cs` (new)
- `src/BoxWise.Server/Pages/_ViewImports.cshtml` (new)
- `src/BoxWise.Server/Pages/Admin/_ViewStart.cshtml` (new)
- `src/BoxWise.Server/Pages/Admin/_ViewImports.cshtml` (new)
- `src/BoxWise.Server/Pages/Admin/_Layout.cshtml` (new)
- `src/BoxWise.Server/Pages/Admin/Index.cshtml` (new)
- `src/BoxWise.Server/Pages/Admin/Index.cshtml.cs` (new)
- `src/BoxWise.Server/Pages/Admin/CreateAccount.cshtml` (new)
- `src/BoxWise.Server/Pages/Admin/CreateAccount.cshtml.cs` (new)

**修改文件:**
- `src/BoxWise.Server/Program.cs` (modified) — 种子数据修复 + AddRazorPages + MapRazorPages + AdminOnly 策略 + OnRedirectToAccessDenied
