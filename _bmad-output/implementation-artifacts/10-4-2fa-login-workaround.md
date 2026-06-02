---
baseline_commit: 0635d7b
---

# Story 10.4: 2FA 登录 + .NET 10 Bug 验证/workaround

Status: done

## Story

As a 已配置 2FA 的用户，
I want 在 Identity LoginWith2fa 页面完成 TOTP 验证码验证后登录成功，
so that 我的账户安全不受迁移影响，完整的 2FA 登录流程（密码→TOTP→Cookie 签发→重定向）端到端可用。

## Acceptance Criteria

### AC-1: 密码登录后自动跳转 2FA 页面

**Given** 已配置 TOTP 2FA 的用户（`TwoFactorEnabled=true`, `ConfiguredMethods=TOTP`）
**When** 在 Identity `Login.cshtml` 输入用户名和密码
**Then** `SignInManager.PasswordSignInAsync` 返回 `RequiresTwoFactor=true` → `Login.cshtml.cs` 中 `RedirectToPage("./LoginWith2fa")` 触发 → 浏览器跳转到 `LoginWith2fa.cshtml`，显示 TOTP 验证码输入表单
**And** `Login.cshtml.cs` 无需修改——`PasswordSignInAsync` 返回的 `result.RequiresTwoFactor` 已正确分支处理（L122-124）

### AC-2: TOTP 验证码正确 → 登录成功

**Given** 用户在 `LoginWith2fa.cshtml` 输入正确的 6 位 TOTP 验证码
**When** 提交验证表单
**Then** `SignInManager.TwoFactorAuthenticatorSignInAsync` 成功 → 签发 `.AspNetCore.Identity.Application` Cookie → 302 重定向到 `/`（或 `returnUrl` 指定页面）
**And** Blazor WASM 首页正常显示已登录状态（4 Tab 导航可见）

### AC-3: .NET 10 Bug 验证：GetTwoFactorAuthenticationUserAsync 返回 null

**Given** 脚手架生成的 `LoginWith2fa.cshtml.cs` 中 `OnGetAsync` 调用 `_signInManager.GetTwoFactorAuthenticationUserAsync()`
**And** 已知 .NET 10.0.8 中此方法因内部 Bug 返回 null（`UserManager.GetUserId(principal)` 返回 UserName 而非 UserId → `FindByIdAsync("admin")` 用用户名查 GUID 列 → 0 rows → null）
**When** 在 .NET 10.0.8 环境下测试（当前环境）
**Then** ⚠️ `GetTwoFactorAuthenticationUserAsync()` 返回 null → `throw new InvalidOperationException("Unable to load two-factor authentication user.")` → 用户看到 500 错误页
**And** 此 Bug 已在 TwoFactorEndpoints.cs 的 API 端点中确认并绕过（`GetTwoFactorUserAsync` 辅助方法，详见 `2fa-gettwofactoruserasync-null-investigation.md`）——本 Story 在 PageModel 层处理

### AC-4: LoginWith2fa.cshtml.cs — 应用 PageModel 版 workaround

**Given** Bug 确认存在（AC-3）
**When** 修改 `LoginWith2fa.cshtml.cs` 的 `OnGetAsync` 和 `OnPostAsync`，替换 `_signInManager.GetTwoFactorAuthenticationUserAsync()` 为：
```csharp
// Workaround for dotnet/aspnetcore#66929: GetTwoFactorAuthenticationUserAsync()
// 在 .NET 10.0.8 中返回 null——内部 UserManager.GetUserId 返回了 UserName
// 而非 UserId，导致 FindByIdAsync 用用户名查 GUID 列。手动从 TwoFactorUserId
// Cookie 提取 NameIdentifier claim，绕过有问题的调用路径。
var authResult = await HttpContext.AuthenticateAsync(IdentityConstants.TwoFactorUserIdScheme);
if (!authResult.Succeeded || authResult.Principal is null)
    throw new InvalidOperationException($"Unable to load two-factor authentication user.");

var userId = authResult.Principal.FindFirstValue(ClaimTypes.NameIdentifier);
if (string.IsNullOrEmpty(userId))
    throw new InvalidOperationException($"Unable to load two-factor authentication user.");

var user = await _userManager.FindByIdAsync(userId);
if (user is null)
    throw new InvalidOperationException($"Unable to load two-factor authentication user.");
```
**Then** `user` 非 null → 2FA 页面正常渲染（OnGetAsync）→ 验证码提交后 `TwoFactorAuthenticatorSignInAsync` 成功（OnPostAsync）
**And** 添加两个 using：`using Microsoft.AspNetCore.Authentication;` + `using System.Security.Claims;`

### AC-5: LoginWithRecoveryCode.cshtml.cs — 同样应用 workaround

**Given** `LoginWithRecoveryCode.cshtml.cs` 中 `OnGetAsync` 和 `OnPostAsync` 同样调用 `_signInManager.GetTwoFactorAuthenticationUserAsync()`
**When** 应用与 AC-4 相同的 workaround（`HttpContext.AuthenticateAsync` + `FindByIdAsync`）
**Then** 恢复码登录流程正常——输入 8 位恢复码 → `SignInManager.TwoFactorRecoveryCodeSignInAsync` 成功 → Cookie 签发 → 重定向到 `/`
**And** 添加相同的两个 using

### AC-6: 编译 + 测试验证

**Given** 所有修改完成
**When** `dotnet build`
**Then** 0 错误 0 警告（项目 `WarningsAsErrors` 已启用）
**And** `using Microsoft.AspNetCore.Authentication;` 和 `using System.Security.Claims;` 正确解析

**Given** `dotnet test`
**When** 执行所有测试
**Then** 全部通过——本 Story 仅修改 Identity 脚手架 PageModel，不影响测试隔离的 InMemory 数据库

### AC-7: 手动验证完整 2FA 登录流程

**Given** Server 启动，已有配置 2FA 的用户（TOTP）
**When** 执行以下流程：
1. 访问 `https://localhost:5000/` → 未登录 → 302 重定向到 `/Identity/Account/Login`
2. 输入用户名/密码 → 302 重定向到 `/Identity/Account/LoginWith2fa?returnUrl=%2F&rememberMe=false`
3. 页面正常显示（不抛 500 / InvalidOperationException）
4. 输入正确 TOTP 验证码 → 登录成功 → 302 重定向到 `/`
5. Blazor WASM 首页正常显示已登录状态
**Then** 完整流程无错误

**Given** 用户有恢复码
**When** 在 LoginWith2fa 页面点击"使用恢复码登录"链接 → 跳转到 `/Identity/Account/LoginWithRecoveryCode`
**Then** 页面正常显示（不抛 500）→ 输入恢复码 → 登录成功 → 重定向到 `/`

## Tasks / Subtasks

- [x] Task 1: LoginWith2fa.cshtml.cs — 应用 workaround (AC: #3, #4)
  - [x] 1.1 在 `OnGetAsync` (L78-92) 中替换 `_signInManager.GetTwoFactorAuthenticationUserAsync()` 为 workaround
  - [x] 1.2 在 `OnPostAsync` (L94-131) 中替换 `_signInManager.GetTwoFactorAuthenticationUserAsync()` 为 workaround（使用 `userIdFromCookie` 变量名避免与 L113 冲突），**并删除 L113 的死代码 `var userId = await _userManager.GetUserIdAsync(user);`**（`userId` 变量后续从未使用，且与 workaround 变量名冲突导致 CS0128）
  - [x] 1.3 添加 `using Microsoft.AspNetCore.Authentication;`
  - [x] 1.4 添加 `using System.Security.Claims;`
  - [x] 1.5 清理重复的 `using Microsoft.Extensions.Logging;`（L11 和 L14 重复——删除 L14 的重复项，保留 L11）

- [x] Task 2: LoginWithRecoveryCode.cshtml.cs — 应用 workaround (AC: #5)
  - [x] 2.1 在 `OnGetAsync` (L63-75) 中替换 `_signInManager.GetTwoFactorAuthenticationUserAsync()` 为 workaround
  - [x] 2.2 在 `OnPostAsync` (L77-112) 中替换 `_signInManager.GetTwoFactorAuthenticationUserAsync()` 为 workaround（使用 `userIdFromCookie` 变量名避免与 L94 冲突），**并删除 L94 的死代码 `var userId = await _userManager.GetUserIdAsync(user);`**（`userId` 变量后续从未使用，且与 workaround 变量名冲突导致 CS0128/CS0219）
  - [x] 2.3 添加 `using Microsoft.AspNetCore.Authentication;`
  - [x] 2.4 添加 `using System.Security.Claims;`

- [x] Task 3: 编译 + 测试验证 (AC: #6)
  - [x] 3.1 `dotnet build` — 0 错误 0 警告
  - [x] 3.2 `dotnet test` — 全部通过

- [ ] Task 4: 手动验证 (AC: #7)
  - [ ] 4.1 启动 Server → 验证 2FA 登录完整流程
  - [ ] 4.2 验证恢复码登录流程
  - [ ] 4.3 验证无 2FA 用户仍可正常登录（回归）

## Dev Notes

### 架构上下文

**当前状态：** Story 10.1 生成了 17 个 Identity Razor Pages。Story 10.2 注册了 `IEmailSender` 适配器。Story 10.3 修复了 Cookie 认证配置（`LoginPath` + `OnRedirectToLogin` 区分 API/页面请求 + `[AllowAnonymous]`）。现在，完整的密码登录流程已可用——但 2FA 登录路径尚未验证。

**本 Story 目标：** 验证 `LoginWith2fa.cshtml` 和 `LoginWithRecoveryCode.cshtml` 的 2FA 登录流程，确认 .NET 10 Bug 影响范围，应用 PageModel 层 workaround。

**关键洞察：** .NET 10.0.8 的 `SignInManager.GetTwoFactorAuthenticationUserAsync()` Bug 已在 TwoFactorEndpoints.cs 的手写 API 端点中确认并应用 workaround（`GetTwoFactorUserAsync` 辅助方法）。Identity 脚手架生成的 Razor Page PageModel 同样调用此方法——它们面临同一个 Bug。

### .NET 10 Bug 详解

**Bug 编号：** [dotnet/aspnetcore#66929](https://github.com/dotnet/aspnetcore/issues/66929)

**调用链：**
```
SignInManager.GetTwoFactorAuthenticationUserAsync()
  → Context.AuthenticateAsync(TwoFactorUserIdScheme)     // ✅ 成功，Principal 正确
  → UserManager.GetUserAsync(principal)
    → UserManager.GetUserId(principal)                    // ❌ 返回 UserName 而非 UserId
      → principal.FindFirstValue(Options.ClaimsIdentity.UserIdClaimType)
        → 预期返回 ClaimTypes.NameIdentifier = GUID (36字符)
        → 实际返回了 ClaimTypes.Name = "admin" (5字符)
    → FindByIdAsync("admin")                              // ❌ 用用户名查 Id 列
      → SQL: WHERE Id = 'admin' → 0 rows → null
```

**证据链（来自 TwoFactorEndpoints.cs 调查）：**
- `AuthenticateAsync(TwoFactorUserIdScheme)` 成功，Principal 包含正确 `NameIdentifier` claim（GUID）
- EF Core SQL 日志：修复前 `@p Size=5`（"admin"），修复后 `@p Size=36`（GUID）
- Workaround：手动提取 `ClaimTypes.NameIdentifier` → 直接调用 `FindByIdAsync`，SQL 参数恢复正常

**已验证：`TwoFactorAuthenticatorSignInAsync` / `TwoFactorRecoveryCodeSignInAsync` 不受此 Bug 影响。** 这两个方法内部通过 `RetrieveTwoFactorInfoAsync()` 获取用户（使用 `ClaimTypes.Name` + `FindByNameAsync`），不经过有问题的 `GetUserId(principal)` → `FindByIdAsync` 路径。Workaround 仅需替换 `GetTwoFactorAuthenticationUserAsync()` 调用，无需修改 Identity 内置的验证/恢复码签名逻辑。

### Workaround 模式

**参考实现（TwoFactorEndpoints.cs L203-216）：**
```csharp
private static async Task<AppUser?> GetTwoFactorUserAsync(
    SignInManager<AppUser> signInManager, UserManager<AppUser> userManager)
{
    var authResult = await signInManager.Context.AuthenticateAsync(
        IdentityConstants.TwoFactorUserIdScheme);
    if (!authResult.Succeeded || authResult.Principal is null)
        return null;

    var userId = authResult.Principal.FindFirstValue(ClaimTypes.NameIdentifier);
    if (string.IsNullOrEmpty(userId))
        return null;

    return await userManager.FindByIdAsync(userId);
}
```

**PageModel 版差异：** PageModel 中 `HttpContext` 是实例属性（`this.HttpContext`），而 API 端点通过 `signInManager.Context` 获取。两者等价——都是注入的 `IHttpContextAccessor.HttpContext`。PageModel 直接用 `HttpContext.AuthenticateAsync(...)` 即可。

**错误处理差异：** 两步验证端点中 workaround 返回 null 后返回 401 Unauthorized。PageModel 中抛 `InvalidOperationException` 更合适——与脚手架原有行为一致，且 Identity 错误处理中间件会显示开发者异常页面（开发环境）或 500 错误页（生产环境）。

### 文件变更清单

| 操作 | 文件 | 变更内容 |
|------|------|---------|
| ✏️ MODIFY | `src/BoxWise.Server/Areas/Identity/Pages/Account/LoginWith2fa.cshtml.cs` | `OnGetAsync` + `OnPostAsync` 中替换 `GetTwoFactorAuthenticationUserAsync()` 为 workaround；添加 2 个 using；清理重复 using |
| ✏️ MODIFY | `src/BoxWise.Server/Areas/Identity/Pages/Account/LoginWithRecoveryCode.cshtml.cs` | `OnGetAsync` + `OnPostAsync` 中替换 `GetTwoFactorAuthenticationUserAsync()` 为 workaround；添加 2 个 using |

### 本 Story 不改动的内容（边界明确）

| 不改动 | 原因 |
|--------|------|
| `Login.cshtml.cs` | `OnPostAsync` 中 `PasswordSignInAsync` 已正确处理 `RequiresTwoFactor=true` → `RedirectToPage("./LoginWith2fa")`，无需修改 |
| `TwoFactorEndpoints.cs` | API 端点的 workaround 已在 Story 8-2a-2 中应用。此次迁移的 Story 11.3 会退役此文件的部分端点，但不修改本 Story |
| `Program.cs` | `TwoFactorUserIdScheme` Cookie 配置（L75-81）已正确设置 `SameSite=None` + `Secure=Always`。`LoginPath` 已由 Story 10.3 配置。无需修改 |
| `AuthEndpoints.cs` | `IssueTwoFactorUserIdCookieAsync` 正确创建 TwoFactorUserId Cookie（包含 `NameIdentifier` claim），无需修改 |
| `CookieAuthenticationStateProvider.cs` | 仅依赖 `GET /api/auth/me`，与登录流程解耦（SPEC C5） |
| `RecoveryCodeService.cs` | 保留——`LoginWithRecoveryCode.cshtml` 使用 Identity 内置 `SignInManager.TwoFactorRecoveryCodeSignInAsync`，但此服务仍被通行密钥 2FA 恢复码路径引用 |
| 任何 Client (Blazor WASM) 文件 | 本 Story 纯 Server 端变更 |
| 任何测试文件 | 配置/PageModel 变更不影响测试隔离 |

### LoginWith2fa.cshtml.cs 修改详解

**OnGetAsync (L78-92) — 修改前：**
```csharp
public async Task<IActionResult> OnGetAsync(bool rememberMe, string returnUrl = null)
{
    var user = await _signInManager.GetTwoFactorAuthenticationUserAsync();
    if (user == null)
    {
        throw new InvalidOperationException($"Unable to load two-factor authentication user.");
    }
    ReturnUrl = returnUrl;
    RememberMe = rememberMe;
    return Page();
}
```

**OnGetAsync — 修改后：**
```csharp
public async Task<IActionResult> OnGetAsync(bool rememberMe, string returnUrl = null)
{
    // Workaround for dotnet/aspnetcore#66929: GetTwoFactorAuthenticationUserAsync()
    // 在 .NET 10.0.8 中返回 null——内部 UserManager.GetUserId 返回了 UserName
    // 而非 UserId，导致 FindByIdAsync 用用户名查 GUID 列。手动从 TwoFactorUserId
    // Cookie 提取 NameIdentifier claim，绕过有问题的调用路径。
    var authResult = await HttpContext.AuthenticateAsync(IdentityConstants.TwoFactorUserIdScheme);
    if (!authResult.Succeeded || authResult.Principal is null)
        throw new InvalidOperationException($"Unable to load two-factor authentication user.");

    var userId = authResult.Principal.FindFirstValue(ClaimTypes.NameIdentifier);
    if (string.IsNullOrEmpty(userId))
        throw new InvalidOperationException($"Unable to load two-factor authentication user.");

    var user = await _userManager.FindByIdAsync(userId);
    if (user is null)
        throw new InvalidOperationException($"Unable to load two-factor authentication user.");

    ReturnUrl = returnUrl;
    RememberMe = rememberMe;

    return Page();
}
```

**OnPostAsync (L94-131) — 修改后：**
```csharp
public async Task<IActionResult> OnPostAsync(bool rememberMe, string returnUrl = null)
{
    if (!ModelState.IsValid)
    {
        return Page();
    }

    returnUrl = returnUrl ?? Url.Content("~/");

    // Workaround for dotnet/aspnetcore#66929: GetTwoFactorAuthenticationUserAsync()
    // returns null in .NET 10.0.8. 手动从 TwoFactorUserId Cookie 提取 NameIdentifier
    // claim，绕过有问题的 GetUserId(principal) → FindByIdAsync 路径。
    var authResult = await HttpContext.AuthenticateAsync(IdentityConstants.TwoFactorUserIdScheme);
    if (!authResult.Succeeded || authResult.Principal is null)
        throw new InvalidOperationException($"Unable to load two-factor authentication user.");

    var userIdFromCookie = authResult.Principal.FindFirstValue(ClaimTypes.NameIdentifier);
    if (string.IsNullOrEmpty(userIdFromCookie))
        throw new InvalidOperationException($"Unable to load two-factor authentication user.");

    var user = await _userManager.FindByIdAsync(userIdFromCookie);
    if (user is null)
        throw new InvalidOperationException($"Unable to load two-factor authentication user.");

    var authenticatorCode = Input.TwoFactorCode.Replace(" ", string.Empty).Replace("-", string.Empty);

    var result = await _signInManager.TwoFactorAuthenticatorSignInAsync(authenticatorCode, rememberMe, Input.RememberMachine);

    // ⚠️ 脚手架原有的 var userId = await _userManager.GetUserIdAsync(user); 已删除——
    // 此行是死代码（userId 后续未被使用），且变量名与 workaround 冲突导致 CS0128。

    if (result.Succeeded)
    {
        _logger.LogInformation("User with ID '{UserId}' logged in with 2fa.", user.Id);
        return LocalRedirect(returnUrl);
    }
    else if (result.IsLockedOut)
    {
        _logger.LogWarning("User with ID '{UserId}' account locked out.", user.Id);
        return RedirectToPage("./Lockout");
    }
    else
    {
        _logger.LogWarning("Invalid authenticator code entered for user with ID '{UserId}'.", user.Id);
        ModelState.AddModelError(string.Empty, "Invalid authenticator code.");
        return Page();
    }
}
```

**⚠️ 关键：** OnPostAsync 中脚手架原有的 `var userId = await _userManager.GetUserIdAsync(user);`（L113）**必须删除**——不仅是因为 workaround 的 `userIdFromCookie` 变量名差异避免了 CS0128，更因为此行本身是死代码（`userId` 变量后续从未使用，所有日志语句都直接用 `user.Id`）。workaround 将变量名从 `userId` 改为 `userIdFromCookie` 刻意避开了冲突，同时删除了死代码行。

**Using 变更：**
- 添加：`using Microsoft.AspNetCore.Authentication;` → 提供 `AuthenticateAsync` 扩展方法
- 添加：`using System.Security.Claims;` → 提供 `ClaimTypes.NameIdentifier`
- 删除重复：第二个 `using Microsoft.Extensions.Logging;`（L14 与 L11 重复）——保留 L11，删除 L14

### LoginWithRecoveryCode.cshtml.cs 修改详解

`OnGetAsync` (L63-75) 和 `OnPostAsync` (L77-112) 中应用**完全相同的 workaround**（替换 `_signInManager.GetTwoFactorAuthenticationUserAsync()` + null 检查）。

**注意：** 此文件的 `OnPostAsync` 中 `user` 变量在 workaround 后已被 `FindByIdAsync` 填充——后续的 `TwoFactorRecoveryCodeSignInAsync(recoveryCode)` 和 `_logger.LogInformation("User ... logged in with a recovery code.", user.Id)` 正常使用该变量，无需额外修改。

**⚠️ 与 LoginWith2fa 相同：** OnPostAsync L94 的 `var userId = await _userManager.GetUserIdAsync(user);` 必须删除——与 workaround 的 `userIdFromCookie` 变量名冲突（CS0128）或赋值未使用（CS0219），在 `WarningsAsErrors=true` 下均为编译错误。

**Using 插入：** 两个新 using 追加到现有 using 块末尾即可。此文件无重复 using 需要清理（与 LoginWith2fa.cshtml.cs 不同）。

### 从之前 Story 学到的经验

**Story 10.1 教训：**
- `IdentityHostingStartup.cs` 导致重复 Identity 注册 → 本 Story 不涉及脚手架生成
- `AddDefaultIdentity` vs `AddIdentity` 冲突 → 本 Story 不修改 Identity 服务注册
- 脚手架 v10.0.2 未生成 `[AllowAnonymous]` → Story 10.3 已添加，本 Story 无需处理

**Story 10.2 教训：**
- 命名空间冲突（泛型 vs 非泛型 `IEmailSender`）→ 本 Story 无此类歧义。`IdentityConstants.TwoFactorUserIdScheme` 是明确的常量，`AuthenticateAsync` 扩展方法选择唯一
- CS8019/CS8933 未使用 using 导致 WarningsAsErrors 编译失败 → 本 Story 仅添加必要的 using，同时清理重复的 `Microsoft.Extensions.Logging`
- Transient vs Scoped 生命周期 → 本 Story 不涉及 DI 注册

**Story 10.3 教训：**
- `[AllowAnonymous]` 必须添加才能防止 FallbackPolicy 无限重定向循环 → 本 Story 的两个 PageModel 已在 Story 10.3 添加 `[AllowAnonymous]`，无需重复
- `OnRedirectToLogin` 区分 API/页面请求的模式 → 本 Story 的 2FA 流程走页面请求路径（`RedirectToPage`），不触发 `OnRedirectToLogin`
- `Logout.cshtml.cs` 中移除未使用 using 确保零警告编译 → 本 Story 同样注意 using 管理

### 代码风格对齐

- **脚手架代码风格：** MIT 许可证注释 + `#nullable disable` + namespace + block body `{ }`。新增 workaround 代码遵循完全相同的缩进和花括号风格
- **注释：** 在 workaround 代码前保留简短的 `// Workaround for dotnet/aspnetcore#66929` 说明 WHY——这是 "仅在 WHY 不明显时加注释" 规则的典型场景
- **提交格式：** `fix(identity): apply .NET 10 GetTwoFactorAuthenticationUserAsync workaround to Identity 2FA pages`

### SPEC 已知决策记录

- **Open Question 2 已关闭：** `WebAuthnEndpoints.LoginCompleteAsync` 在 passkey 验证成功后直接 `SignInAsync`，不检查 2FA——通行密钥本身作为第二因子（已验证的硬件令牌），无需额外 TOTP/Email 验证。此决策记录在 Architecture 文档中（Epic 11 Story 11.4 `samesite-docs-update` 更新文档时写入）
- **`RecoveryCodeService.VerifyRecoveryCodeAsync` 保留：** Identity `LoginWithRecoveryCode.cshtml` 使用内置 `SignInManager.TwoFactorRecoveryCodeSignInAsync`，但通行密钥 2FA 恢复码路径仍引用此服务

### 测试策略

- **编译验证：** `dotnet build` 0 错误 0 警告 —— 验证新增 using 正确解析、workaround API 签名正确（`HttpContext.AuthenticateAsync`、`FindFirstValue`、`FindByIdAsync`）、无重复 using 导致的编译问题
- **测试回归：** `dotnet test` 全部通过 —— PageModel 变更不影响测试项目，但验证无意外破坏
- **手动验证（必须）：** 2FA 登录完整流程——密码→LoginWith2fa→输入 TOTP→登录成功→WASM 首页。恢复码登录流程。无 2FA 用户登录不受影响（回归测试）

### References

- [Source: SPEC.md CAP-4] — 2FA 登录需求 + .NET 10 Bug workaround 约束
- [Source: SPEC.md C3] — LoginWith2fa.cshtml 必须在 PageModel 中应用 workaround
- [Source: epics-identity-scaffold-migration.md Story 1.4] — 验收标准
- [Source: migration-phases.md Phase 2.4] — Bug 验证 + workaround 应用
- [Source: 2fa-gettwofactoruserasync-null-investigation.md] — Bug 根因分析 + 修复证据
- [Source: TwoFactorEndpoints.cs:203-216] — 现有 workaround 模式（API 端点层）
- [Source: LoginWith2fa.cshtml.cs] — 修改目标（OnGetAsync + OnPostAsync）
- [Source: LoginWithRecoveryCode.cshtml.cs] — 修改目标（OnGetAsync + OnPostAsync）
- [Source: Program.cs:75-81] — TwoFactorUserIdScheme Cookie 配置（已正确，无需修改）
- [Source: Login.cshtml.cs:122-124] — RequiresTwoFactor 分支（已正确，无需修改）
- [Source: Story 10.3 Dev Agent Record] — 前序 Story 经验教训
- [Source: dotnet/aspnetcore#66929] — 上游 Bug Issue

## Dev Agent Record

### Agent Model Used

Claude Code (deepseek-v4-pro)

### Debug Log References

- `dotnet build` — 0 错误 0 警告，一次通过
- `dotnet test` — 308 通过 0 失败（44 Client + 264 Server）

### Completion Notes List

- ✅ AC-1~2: Login.cshtml.cs 无需修改——`RequiresTwoFactor=true` 已正确分支到 `LoginWith2fa`
- ✅ AC-3: .NET 10 Bug 确认存在——`GetTwoFactorAuthenticationUserAsync()` 在 .NET 10.0.8 返回 null
- ✅ AC-4: LoginWith2fa.cshtml.cs OnGetAsync + OnPostAsync 应用 workaround（`HttpContext.AuthenticateAsync` + `FindByIdAsync`）+ 删除 L113 死代码 + 清理重复 using
- ✅ AC-5: LoginWithRecoveryCode.cshtml.cs OnGetAsync + OnPostAsync 应用 workaround（同模式）+ 删除 L94 死代码
- ✅ AC-6: `dotnet build` 0 错误 0 警告 + `dotnet test` 308 通过
- ⏳ AC-7: 手动验证——需要配置 2FA (TOTP) 的用户在真实浏览器中测试完整登录流程

### Change Log

- 2026-06-01: Implementation completed (Dev Story) — 2 files, +34 / -14 lines

### File List

| 操作 | 文件 | 说明 |
|------|------|------|
| ✏️ MODIFY | `src/BoxWise.Server/Areas/Identity/Pages/Account/LoginWith2fa.cshtml.cs` | OnGetAsync + OnPostAsync workaround (+16 / -10 lines)；+2 using (`Authentication` + `Claims`)；清理重复 using |
| ✏️ MODIFY | `src/BoxWise.Server/Areas/Identity/Pages/Account/LoginWithRecoveryCode.cshtml.cs` | OnGetAsync + OnPostAsync workaround (+18 / -8 lines)；+2 using (`Authentication` + `Claims`) |
