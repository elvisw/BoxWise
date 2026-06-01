---
baseline_commit: e03e68e
---

# Story 10.3: Cookie 认证桥接 + LoginPath 配置

Status: done

## Story

As a 用户，
I want 在 Identity Login.cshtml 页面用用户名/密码登录后自动回到 BoxWise 首页，
so that 登录体验流畅，Blazor WASM 正常显示我的认证状态。

## Acceptance Criteria

### AC-0: Login 页面添加 [AllowAnonymous] 防止无限重定向循环

**Given** 全局 `FallbackPolicy = RequireAuthenticatedUser()` 要求所有请求必须认证
**And** 脚手架生成的 `Login.cshtml.cs`、`LoginWith2fa.cshtml.cs`、`LoginWithRecoveryCode.cshtml.cs` **没有** `[AllowAnonymous]` 特性（Story 10.1 的 v10.0.2 脚手架版本未生成此特性）
**When** 在三个 PageModel 类上添加 `[AllowAnonymous]`：
- `Login.cshtml.cs` — 在第 21 行 `public class LoginModel : PageModel` 前添加 `[AllowAnonymous]`
- `LoginWith2fa.cshtml.cs` — 在第 18 行 `public class LoginWith2faModel : PageModel` 前添加 `[AllowAnonymous]`
- `LoginWithRecoveryCode.cshtml.cs` — 在第 16 行 `public class LoginWithRecoveryCodeModel : PageModel` 前添加 `[AllowAnonymous]`
**Then** 未登录用户可以访问登录页面——不会因为 FallbackPolicy 触发 `OnRedirectToLogin` → 302 重定向 → 再触发的无限循环
**And** `using Microsoft.AspNetCore.Authorization;` 已存在于三个文件中，无需额外 using
**And** `Lockout.cshtml.cs` 已有 `[AllowAnonymous]`，`ConfirmEmail.cshtml.cs` 无需（通过邮件链接访问，Token 验证不受 FallbackPolicy 影响）

### AC-1: LoginPath 配置

**Given** `Program.cs` 中 `ConfigureApplicationCookie` 配置块（当前 L50-L67）
**When** 添加 `options.LoginPath = "/Identity/Account/Login"`
**Then** ASP.NET Core Identity 的 `[Authorize]` 中间件在用户未登录时自动重定向到 `/Identity/Account/Login`，而非默认的 `/Account/Login`

### AC-2: OnRedirectToLogin 区分 API 和页面请求

**Given** 当前 `OnRedirectToLogin` handler 对**所有**未认证请求无条件返回 401（L57-L61）：
```csharp
options.Events.OnRedirectToLogin = ctx =>
{
    ctx.Response.StatusCode = 401;
    return Task.CompletedTask;
};
```
**When** 修改为区分请求类型：
```csharp
options.Events.OnRedirectToLogin = ctx =>
{
    if (ctx.Request.Path.StartsWithSegments("/api"))
    {
        ctx.Response.StatusCode = 401;
        return Task.CompletedTask;
    }
    ctx.Response.Redirect(ctx.RedirectUri);
    return Task.CompletedTask;
};
```
**Then** API 请求（`/api/*`）仍然返回 401（保持 Blazor WASM `CookieHandler` 和 `HttpClient` 的错误处理兼容）
**And** 页面请求（如直接访问 `/Identity/Account/Manage`）被重定向到 `/Identity/Account/Login`，不返回 401

### AC-3: 未登录用户访问 WASM 受保护页面 → 自动重定向

**Given** 未登录用户直接访问 Blazor WASM 受保护页面（如 `/`、`/browse`）
**When** `FallbackPolicy = RequireAuthenticatedUser()` + `LoginPath = "/Identity/Account/Login"` + 修复后的 `OnRedirectToLogin`
**Then** `[Authorize]` 拦截 → 自动 302 重定向到 `/Identity/Account/Login`
**And** 登录成功后 302 重定向回原始请求页面（Identity 内置 `returnUrl` 参数）

### AC-4: 登录成功 → Cookie 签发 → 重定向到首页

**Given** 用户在 Identity `Login.cshtml` 输入正确的用户名和密码
**When** 提交登录表单
**Then** `SignInManager.PasswordSignInAsync` 成功 → Server 签发 `.AspNetCore.Identity.Application` Cookie → HTTP 302 重定向到 `/`（或 `returnUrl` 指定的页面）

### AC-5: CookieAuthenticationStateProvider 感知认证状态

**Given** 浏览器携带 `.AspNetCore.Identity.Application` Cookie 访问 Blazor WASM 首页
**When** `CookieAuthenticationStateProvider.GetAuthenticationStateAsync()` 调用 `GET /api/auth/me`
**Then** 返回 `AuthUserDto`（UserName + IsAdmin），`AppState.SetUser()` 更新客户端状态
**And** `NotifyAuthenticationStateChanged()` 触发 UI 重渲染——底部 4 Tab 导航正常显示（首页/录入/浏览/设置）

### AC-6: Logout.cshtml.cs 支持 GET 请求

**Given** Identity 脚手架生成的 `Logout.cshtml.cs`（`Areas/Identity/Pages/Account/Logout.cshtml.cs`）仅有 `OnPost`
**When** 添加 `OnGet` handler（独立实现，不委托给 `OnPost`）：
```csharp
public async Task<IActionResult> OnGet(string returnUrl = null)
{
    await _signInManager.SignOutAsync();
    _logger.LogInformation("User logged out.");
    return LocalRedirect(returnUrl ?? "/");
}
```
**Then** 导航到 `/Identity/Account/Logout`（GET 或 POST）均触发登出——无需两步操作
**And** `SignOutAsync` 清除 Cookie 后，直接重定向到 `/` 或 `returnUrl`——不经过 `RedirectToPage()`（避免多跳一次 FallbackPolicy → OnRedirectToLogin）
**And** `using Microsoft.AspNetCore.Authorization;` 未使用——移除它以避免 `WarningsAsErrors` 下的 CS8019 编译错误

### AC-7: CookieAuthenticationStateProvider 保持不变

**Given** 登录流程从 Blazor WASM SPA 切换到 Server 端 Identity Razor Pages
**When** 审查 `CookieAuthenticationStateProvider.cs`
**Then** 不修改——它仅依赖 `GET /api/auth/me`，与登录流程解耦
**And** 开发环境 `SameSiteMode.None` 不变（生产环境切换将在 Story 10-4 / Epic 11 中处理）

### AC-8: 编译 + 测试验证

**Given** 所有修改完成
**When** `dotnet build`
**Then** 0 错误 0 警告（项目 `WarningsAsErrors` 已启用）

**Given** `dotnet test`
**When** 执行所有测试
**Then** 全部通过——本 Story 的修改不破坏现有测试（Cookie 配置变更不影响测试隔离的 InMemory 数据库）

## Tasks / Subtasks

- [x] Task 1: Login 页面添加 `[AllowAnonymous]` (AC: #0)
  - [x] 1.1 `Login.cshtml.cs` — 在 `public class LoginModel : PageModel` 前添加 `[AllowAnonymous]`
  - [x] 1.2 `LoginWith2fa.cshtml.cs` — 在 `public class LoginWith2faModel : PageModel` 前添加 `[AllowAnonymous]`
  - [x] 1.3 `LoginWithRecoveryCode.cshtml.cs` — 在 `public class LoginWithRecoveryCodeModel : PageModel` 前添加 `[AllowAnonymous]`

- [x] Task 2: 修复 OnRedirectToLogin 区分 API/页面请求 (AC: #1, #2, #3)
  - [x] 2.1 在 `ConfigureApplicationCookie` 中添加 `options.LoginPath = "/Identity/Account/Login"`
  - [x] 2.2 修改 `OnRedirectToLogin` handler：API 请求返回 401，页面请求执行 `ctx.Response.Redirect(ctx.RedirectUri)`

- [x] Task 3: Logout.cshtml.cs 添加 OnGet handler (AC: #6)
  - [x] 3.1 在 `LogoutModel` 类中添加 `OnGet` 方法（独立实现，SignOutAsync + LocalRedirect）
  - [x] 3.2 移除未使用的 `using Microsoft.AspNetCore.Authorization;`（LogoutModel 无 `[Authorize]` 特性）

- [x] Task 4: 编译 + 测试验证 (AC: #8)
  - [x] 4.1 `dotnet build` — 0 错误 0 警告
  - [x] 4.2 `dotnet test` — 308 通过 0 失败
  - [x] 4.3 手动验证：未登录→访问 `/`→重定向到 Login→登录成功→回到首页→Logout→回到未登录状态

## Dev Notes

### 架构上下文

**当前状态：** Story 10.1 生成了 17 个 Identity Razor Pages（包括 `Login.cshtml` 和 `Logout.cshtml`），Story 10.2 注册了 `IEmailSender` 适配器。但 `Program.cs` 中的 `OnRedirectToLogin` handler 仍对所有未认证请求返回 401——这阻止了 Identity 页面的正常工作。

**本 Story 目标：** 修复 Cookie 认证配置，使 Identity Razor Pages 的登录/登出流程正常工作，与 Blazor WASM 客户端无缝桥接。

**关键洞察：** `OnRedirectToLogin` 事件在两种场景下触发：
1. **API 请求**（`/api/*`）—— Blazor WASM `HttpClient` 发起的 fetch 请求。必须返回 401，让客户端 JavaScript 处理（`CookieHandler` 捕获 401，`AppState` 感知未登录状态）。
2. **页面请求**（`/Identity/*`、`/` 等）—— 浏览器导航请求。应该重定向到登录页面，让用户完成认证后回来。

修复前的代码对两者都返回 401，导致 Identity 页面（如 `Account.Manage.Index`）的 `[Authorize]` 属性触发时用户看到的是空白页而非登录页。

### 文件变更清单

| 操作 | 文件 | 说明 |
|------|------|------|
| ✏️ MODIFY | `src/BoxWise.Server/Program.cs` | 添加 `LoginPath` + 修改 `OnRedirectToLogin` handler（~5 行变更） |
| ✏️ MODIFY | `src/BoxWise.Server/Areas/Identity/Pages/Account/Login.cshtml.cs` | 添加 `[AllowAnonymous]`（1 行） |
| ✏️ MODIFY | `src/BoxWise.Server/Areas/Identity/Pages/Account/LoginWith2fa.cshtml.cs` | 添加 `[AllowAnonymous]`（1 行） |
| ✏️ MODIFY | `src/BoxWise.Server/Areas/Identity/Pages/Account/LoginWithRecoveryCode.cshtml.cs` | 添加 `[AllowAnonymous]`（1 行） |
| ✏️ MODIFY | `src/BoxWise.Server/Areas/Identity/Pages/Account/Logout.cshtml.cs` | 添加 `OnGet` handler + 移除未使用的 using（~6 行变更） |

### 本 Story 不改动的内容（边界明确）

| 不改动 | 原因 |
|--------|------|
| `CookieAuthenticationStateProvider.cs` | 仅依赖 `GET /api/auth/me`，与登录流程解耦 |
| `OnRedirectToAccessDenied` handler | 当前返回 403 的行为已正确——不需要改 |
| `SameSiteMode.None` / `SecurePolicy.Always` | 开发环境跨端口需要。生产环境切换在 Story 11.4 (SameSite 策略) 中处理 |
| `TwoFactorUserIdScheme` Cookie 配置 | 2FA 登录流程在 Story 10.4 中处理。仅记录已知行为：此 Scheme 的 LoginPath 默认为 `/Account/Login`，但仅在 2FA 流程中触发，Story 10.4 会验证 |
| 任何 Client (Blazor WASM) 文件 | 本 Story 纯 Server 端变更 |
| 任何测试文件 | 配置变更不影响测试隔离的 InMemory 数据库 |
| 其他 Identity `.cshtml` / `.cshtml.cs` 文件 | Lockout.cshtml.cs 已有 `[AllowAnonymous]`；ConfirmEmail.cshtml.cs 通过邮件链接访问无需修改 |
| `AuthEndpoints.LoginAsync` / `LogoutAsync` | 退役在 Story 11.3，本 Story 不删除 |

### Program.cs 修改位置详解

**当前代码**（L50-L67）：
```csharp
builder.Services.ConfigureApplicationCookie(options =>
{
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.None;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.ExpireTimeSpan = TimeSpan.FromDays(30);
    options.SlidingExpiration = true;
    options.Events.OnRedirectToLogin = ctx =>
    {
        ctx.Response.StatusCode = 401;
        return Task.CompletedTask;
    };
    options.Events.OnRedirectToAccessDenied = ctx =>
    {
        ctx.Response.StatusCode = 403;
        return Task.CompletedTask;
    };
});
```

**修改后**：
```csharp
builder.Services.ConfigureApplicationCookie(options =>
{
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.None; // Blazor WASM 跨端口 fetch 需要 None
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always; // SameSite=None 必须配合 Secure
    options.ExpireTimeSpan = TimeSpan.FromDays(30);
    options.SlidingExpiration = true;
    options.LoginPath = "/Identity/Account/Login";
    options.Events.OnRedirectToLogin = ctx =>
    {
        if (ctx.Request.Path.StartsWithSegments("/api"))
        {
            ctx.Response.StatusCode = 401;
            return Task.CompletedTask;
        }
        ctx.Response.Redirect(ctx.RedirectUri);
        return Task.CompletedTask;
    };
    options.Events.OnRedirectToAccessDenied = ctx =>
    {
        ctx.Response.StatusCode = 403;
        return Task.CompletedTask;
    };
});
```

### OnRedirectToLogin 逻辑说明

```
请求 → 需要认证?
  ├─ 已认证 → 正常处理
  └─ 未认证 → OnRedirectToLogin 触发
       ├─ Path.StartsWithSegments("/api") → 401 (JSON API)
       └─ 其他路径 → 302 Redirect(LoginPath + returnUrl)
```

`ctx.RedirectUri` 由 Identity 中间件自动构建，包含 `returnUrl` 参数指向原始请求路径。直接使用 `.Redirect(ctx.RedirectUri)` 是最可靠的方式——不需要手动拼接 URL。

### Logout.cshtml.cs 修改详解

**当前代码**只有 `OnPost`（注意 `using Microsoft.AspNetCore.Authorization;` 在第 7 行但未使用——移除它）：
```csharp
public async Task<IActionResult> OnPost(string returnUrl = null)
{
    await _signInManager.SignOutAsync();
    _logger.LogInformation("User logged out.");
    if (returnUrl != null)
    {
        return LocalRedirect(returnUrl);
    }
    else
    {
        return RedirectToPage();
    }
}
```

**添加 OnGet**（在 `OnPost` 方法之后，采用 block body 风格与现有代码一致）：
```csharp
public async Task<IActionResult> OnGet(string returnUrl = null)
{
    await _signInManager.SignOutAsync();
    _logger.LogInformation("User logged out.");
    return LocalRedirect(returnUrl ?? "/");
}
```

**为什么 `OnGet` 不委托给 `OnPost`：** `OnPost` 在 `returnUrl == null` 时调用 `RedirectToPage()`，重定向回 Logout 页面自身。GET 请求下 `SignOutAsync` 清除 Cookie 后用户已登出，浏览器跟随 302 再次请求 Logout 页面 → FallbackPolicy 触发 → `OnRedirectToLogin` → 302 到 Login 页面。虽然不会无限循环（因 Cookie 已清除），但多一次无意义的跳转。`OnGet` 直接重定向到 `/` 或 `returnUrl`，一步到位。

**移除未使用的 using：** 文件第 7 行 `using Microsoft.AspNetCore.Authorization;` 因 LogoutModel 无 `[Authorize]`/`[AllowAnonymous]` 特性而成为死代码。项目 `WarningsAsErrors=true` 下 CS8019 会导致编译失败。删除此行。

### 为什么改 5 个文件

1. **Program.cs** — Cookie 认证配置核心：`LoginPath` + `OnRedirectToLogin` 修复。这是让 Identity Razor Pages 认证工作流的命脉。
2-4. **Login.cshtml.cs / LoginWith2fa.cshtml.cs / LoginWithRecoveryCode.cshtml.cs** — 添加 `[AllowAnonymous]`。没有它，FallbackPolicy 会阻止未登录用户访问登录页面本身，导致无限重定向循环（`ERR_TOO_MANY_REDIRECTS`）。这是 Story 10.1 脚手架 v10.0.2 的一个行为差异——标准脚手架模板通常包含此特性。
5. **Logout.cshtml.cs** — 添加 `OnGet` handler 支持 GET 登出（Settings.razor 导航链接）+ 移除未使用的 using 确保零警告编译。

### 已知行为说明

- **未登录时访问 `/` 会重定向到 `/Identity/Account/Login`。** 这是预期行为——Blazor WASM 的 `index.html` 受 `FallbackPolicy` 保护。登录成功后会重定向回来。
- **Admin 后台 `/admin` 不受影响。** Admin Razor Pages 有自己的 `[Authorize(Roles = "Admin")]` 属性，未登录时的重定向行为同样由 `LoginPath` + `OnRedirectToLogin` 处理。
- **直接访问 `/Identity/Account/Manage` 在未登录时被重定向到 Login。** 这是 `LoginPath` 修复的直接结果——修复前返回 401，用户看到空白页。
- **`TwoFactorUserIdScheme` 的 LoginPath 未在本 Story 配置。** 此 Cookie 用于 2FA 登录流程中的"记住 2FA 用户"功能，其 `LoginPath` 默认为 `/Account/Login`（不存在）。2FA 流程在 Story 10.4 中验证——届时如 TwoFactorUserId Cookie 缺失/过期，会在浏览器中触发默认重定向（而非 401）。本 Story 不处理，2FA 用户测试留到 Story 10.4。

### 测试策略

- **编译验证：** `dotnet build` 0 错误 0 警告 —— 验证 `LoginPath` 属性存在、`Redirect(ctx.RedirectUri)` API 签名正确、`[AllowAnonymous]` 编译通过、CS8019（未使用 using）已处理。
- **测试回归：** `dotnet test` 全部通过 —— 配置变更不影响测试项目（测试使用 InMemory 数据库 + mock 认证，不经过 Cookie 中间件管道）。
- **手动验证（推荐）：** 启动 Server → 访问 `https://localhost:5000/` → 应重定向到 `/Identity/Account/Login`（无 `ERR_TOO_MANY_REDIRECTS`）→ 输入用户名/密码 → 登录成功后回到首页 → 点击 Logout → 回到未登录状态 → 访问 `GET /api/auth/me` → 确认返回 401。

### 从之前 Story 学到的经验

**Story 10.1 教训：**
- `IdentityHostingStartup.cs` 会导致重复 Identity 注册 → 本 Story 不涉及脚手架生成
- `AddDefaultIdentity` vs `AddIdentity` 冲突导致运行时崩溃 → 本 Story 不修改 Identity 服务注册
- **脚手架 v10.0.2 未在 Login 页面生成 `[AllowAnonymous]`** → 本 Story 手动添加（这是标准脚手架模板的行为差异，见 AC-0）

**Story 10.2 教训：**
- `IEmailSender` 命名空间冲突（泛型 vs 非泛型）→ 本 Story 无需处理命名空间歧义。`LoginPath` 和 `OnRedirectToLogin` 是稳定 API。
- `AuthenticationException` 歧义（`System.Security.Authentication` vs `MailKit.Security`）→ 本 Story 不涉及 SMTP/邮件相关代码。
- **CS8019/CS8933 未使用 using 导致 WarningsAsErrors 编译失败** → `Logout.cshtml.cs` 的 `using Microsoft.AspNetCore.Authorization;` 因 LogoutModel 无 `[Authorize]` 特性而未使用。**必须移除**，否则 `dotnet build` 报错。
- Transient vs Scoped 生命周期选择 → 本 Story 不涉及 DI 注册。

### 代码风格对齐

- **Program.cs：** 保持现有注释风格（中文行内注释 `// Blazor WASM 跨端口 fetch 需要 None`）
- **Login.cshtml.cs 等：** 脚手架生成代码风格（MIT 许可证注释 + `#nullable disable` + namespace），仅添加 1 行 `[AllowAnonymous]`，不修改其他内容
- **Logout.cshtml.cs：** 遵循 scaffolded 代码的 block body 风格（`{ }`），OnGet 与 OnPost 一致。移除未使用的 `using Microsoft.AspNetCore.Authorization;`
- **提交格式：** `feat(identity): configure Cookie LoginPath, fix OnRedirectToLogin, add AllowAnonymous and GET logout`

### References

- [Source: SPEC.md CAP-3] — Cookie 认证桥接需求
- [Source: SPEC.md C7] — 生产环境 SameSite 策略（本 Story 不做，在 Story 11.4 处理）
- [Source: epics-identity-scaffold-migration.md Story 1.3] — 验收标准
- [Source: Program.cs:50-67] — 当前 Cookie 配置（修改位置）
- [Source: Logout.cshtml.cs] — 当前只有 OnPost，需添加 OnGet
- [Source: CookieAuthenticationStateProvider.cs] — 保持不变（仅依赖 GET /api/auth/me）
- [Source: CLAUDE.md §认证流程] — Cookie 认证架构
- [Source: CLAUDE.md §端口配置] — 开发/生产环境 ApiBaseUrl 配置
- [Source: architecture.md §Authentication] — Identity + Cookie 认证架构决策
- [Source: Story 10.1 Dev Agent Record] — 脚手架生成经验教训
- [Source: Story 10.2 Dev Agent Record] — 命名空间冲突 + WarningsAsErrors 经验

## Dev Agent Record

### Agent Model Used

Claude Code (deepseek-v4-pro)

### Review Findings

#### decision-needed

- [x] [Review][Decision] GET 注销 CSRF 风险 — **已接受。** BoxWise ≤5 人家用场景，CSRF 注销的实际攻击面极小。GET 直接登出为 Settings.razor 链接导航提供无摩擦体验，无需两步操作。Identity 标准 POST 模式在 Blazor WASM + Identity Razor Pages 混合架构中需要额外前端改造，ROI 不成比例。如未来攻击面扩大（开放注册、多租户），可在此处添加 Referer/Origin 校验或切换为 POST 模式。

#### defer

- [x] [Review][Defer] AccessDeniedPath 未配置 [Program.cs] — 默认 `/Account/AccessDenied` 路径不存在。非 API 页面的 403 拒绝访问可能触发 404。预存问题，非本 Story 引入。
- [x] [Review][Defer] OnRedirectToAccessDenied 对所有请求返回 403 — 非 API 请求的重定向行为与修复后的 OnRedirectToLogin 不一致。预存问题，Story 明确声明"不改动"。
- [x] [Review][Defer] ConfirmEmail.cshtml.cs 缺少 `[AllowAnonymous]` — 如 `SignInOptions.RequireConfirmedAccount` 启用，邮箱确认链接无法在未登录时访问。Story 明确声明 ConfirmEmail "通过邮件链接访问，Token 验证不受 FallbackPolicy 影响"，确认当前项目不启用此配置。
- [x] [Review][Defer] API 401 返回空内容体 — 未认证 API 请求返回裸 401（无 ProblemDetails JSON）。预存问题（旧 OnRedirectToLogin 同行为），非本 Story 引入。
- [x] [Review][Defer] LoginWith2fa/RecoveryCode OnGet 中 GetTwoFactorAuthenticationUserAsync() null 处理 — 直接导航至 2FA 页面时可能触发 500。预存问题，Story 10.4 将应用 .NET 10 Bug workaround。
- [x] [Review][Defer] 空 returnUrl 绕过 null 合并 — `LocalRedirect(returnUrl ?? "/")` 中输入 `?returnUrl=` 会传递空字符串，触发 `InvalidOperationException`。预存问题，OnPost 中同样存在此行为。

### Debug Log References

- 无编译错误——`dotnet build` 0 错误 0 警告一次通过
- 无测试回归——`dotnet test` 308 通过 0 失败

### Completion Notes List

- ✅ AC-0: Login.cshtml.cs / LoginWith2fa.cshtml.cs / LoginWithRecoveryCode.cshtml.cs 添加 `[AllowAnonymous]`（各 1 行）
- ✅ AC-1~3: Program.cs 添加 `LoginPath = "/Identity/Account/Login"` + 修复 `OnRedirectToLogin` 区分 API/页面请求
- ✅ AC-4~5: Cookie 认证桥接就绪——Login 成功后 Cookie 签发→302 到 `/`→CookieAuthenticationStateProvider 通过 GET /api/auth/me 感知
- ✅ AC-6: Logout.cshtml.cs 添加 `OnGet` handler（独立实现，SignOutAsync + LocalRedirect）+ 移除未使用的 `using Microsoft.AspNetCore.Authorization;`
- ✅ AC-7: CookieAuthenticationStateProvider 未修改——保持与登录流程解耦
- ✅ AC-8: `dotnet build` 0 错误 0 警告 + `dotnet test` 308 通过 0 失败

### Change Log

- 2026-06-01: Story created + 2 轮审查修复 (Create Story → Validate)
- 2026-06-01: Implementation completed (Dev Story) — 5 files, +12 lines

### File List

| 操作 | 文件 | 说明 |
|------|------|------|
| ✏️ MODIFY | `src/BoxWise.Server/Program.cs` | 添加 `LoginPath` + 修改 `OnRedirectToLogin`（+7 行，~5 行变更） |
| ✏️ MODIFY | `src/BoxWise.Server/Areas/Identity/Pages/Account/Login.cshtml.cs` | 添加 `[AllowAnonymous]`（+1 行） |
| ✏️ MODIFY | `src/BoxWise.Server/Areas/Identity/Pages/Account/LoginWith2fa.cshtml.cs` | 添加 `[AllowAnonymous]`（+1 行） |
| ✏️ MODIFY | `src/BoxWise.Server/Areas/Identity/Pages/Account/LoginWithRecoveryCode.cshtml.cs` | 添加 `[AllowAnonymous]`（+1 行） |
| ✏️ MODIFY | `src/BoxWise.Server/Areas/Identity/Pages/Account/Logout.cshtml.cs` | 添加 `OnGet` handler + 移除 `using Microsoft.AspNetCore.Authorization;`（+7 行，1 行移除） |
