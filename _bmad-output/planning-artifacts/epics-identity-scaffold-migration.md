---
stepsCompleted: [1, 2, 3, 4]
inputDocuments:
  - _bmad-output/specs/spec-identity-scaffold-migration/SPEC.md
  - _bmad-output/specs/spec-identity-scaffold-migration/migration-phases.md
  - _bmad-output/specs/spec-identity-scaffold-migration/decommission-checklist.md
---

# BoxWise — Identity 脚手架混合模式迁移

## Overview

本文档将 SPEC-identity-scaffold-migration 的 7 个 Capability 拆分为可执行的 Epic 和 Story。迁移目标：用 ASP.NET Core Identity 脚手架 Razor Pages 替换 BoxWise 手写的认证 UI 和 2FA 设置管理，退役 ~1500 行代码。

> **主需求来源:** SPEC.md（3 轮审核，9 问题已修复）
> **实施细节:** migration-phases.md + decommission-checklist.md

## Requirements Inventory

### Functional Requirements

| 编号 | 需求 | 来源 |
|------|------|------|
| FR-1 | Identity 脚手架页面已生成且可编译运行在 Server 项目中，覆盖登录和账户管理操作，复用现有 AppDbContext 和 AppUser | SPEC CAP-1 |
| FR-2 | IEmailSender 适配器注册（ISmtpConfigurationService + MimeKit），Identity 管理页面邮件发送正常 | SPEC CAP-2 |
| FR-3 | 用户从 Identity Login.cshtml 用户名/密码登录后 Cookie 签发，重定向回 Blazor WASM 首页，CookieAuthenticationStateProvider 通过 GET /api/auth/me 感知认证状态 | SPEC CAP-3 |
| FR-4 | 已配置 2FA 的用户通过 Identity LoginWith2fa.cshtml 完成验证后登录成功；如受 .NET 10 Bug 影响则应用 workaround | SPEC CAP-4 |
| FR-5 | 用户从 Blazor WASM Settings 页面链接跳转到 Server 端 Identity 管理页面（/Identity/Account/Manage/*），在新页面中管理 2FA 设置、修改密码、修改邮箱 | SPEC CAP-5 |
| FR-6 | Login.razor 中通行密钥（WebAuthn/Passkey）登录功能完整保留，用户可从 Identity Login 页面导航到 Blazor WASM /login 使用通行密钥 | SPEC CAP-6 |
| FR-7 | 退役所有被 Identity 页面替代的手写代码：TwoFactorModifyEndpoints.cs(5端点)、EmailVerificationEndpoints.cs(2端点)、TwoFactorEndpoints.cs 登录 2FA 端点(2端点)、AuthEndpoints.LoginAsync/LogoutAsync、TwoFactorManage.razor(587行)、AuthService.cs 对应客户端方法，相关测试 | SPEC CAP-7 |
| FR-8 | 迁移完成后更新 Architecture + UX Design 文档，反映新认证流程和双 UI 风格架构 | migration-phases.md Phase 6 |

### NonFunctional Requirements

| 编号 | 需求 | 来源 |
|------|------|------|
| NFR-1 | Identity 页面使用其默认 Bootstrap 样式，不与 MudBlazor 做样式桥接。双 UI 风格并存是已接受的权衡 | SPEC C1 |
| NFR-2 | 通行密钥（WebAuthn/Passkey）功能必须完整保留，不可被迁移影响 | SPEC C2 |
| NFR-3 | LoginWith2fa.cshtml 如受 dotnet/aspnetcore#66929 影响，必须在 PageModel 中应用 HttpContext.AuthenticateAsync + FindByIdAsync workaround | SPEC C3 |
| NFR-4 | IEmailSender 必须通过 ISmtpConfigurationService + MimeKit 实现，不委托给 EmailTwoFactorService.SendVerificationEmailAsync（API 签名不兼容） | SPEC C4 |
| NFR-5 | CookieAuthenticationStateProvider 不可退役——它仅依赖 GET /api/auth/me，是 WASM 感知服务器 Cookie 的核心桥接 | SPEC C5 |
| NFR-6 | MapRazorPages() 必须在 MapFallbackToFile() 之前，确保 Identity 页面路由不被 Blazor WASM SPA 回退拦截 | SPEC C6 |
| NFR-7 | 生产环境必须使用 SameSiteMode.Lax + SecurePolicy.SameAsRequest，Phase 6 用 env.IsDevelopment() 条件判断自动切换 | SPEC C7 |
| NFR-8 | 每个 Phase 独立 commit，支持精确回滚到任意阶段 | migration-phases.md |

### UX Design Requirements

| 编号 | 需求 | 来源 |
|------|------|------|
| UX-1 | 登录/注册页面从 MudBlazor SPA 风格切换为 Bootstrap 独立页面风格，用户在新页面中完成认证 | SPEC NFR-1 |
| UX-2 | 2FA 设置管理从 Blazor WASM 对话框（TwoFactorManage.razor）切换为 Server 端独立页面跳转，操作完成关闭标签页回到 Blazor WASM | SPEC CAP-5 |
| UX-3 | Settings.razor 中"管理账户设置"按钮变为链接跳转，开发环境指向 Server 端口，生产环境同域相对路径 | SPEC CAP-5 + migration-phases.md Phase 3 |
| UX-4 | Login.razor 保留通行密钥按钮 UI，标准的用户名/密码表单移除（由 Identity Login.cshtml 替代） | SPEC CAP-6 |
| UX-5 | Identity Login.cshtml 页面增加"使用通行密钥登录"链接，指向 Blazor WASM /login | SPEC CAP-6 |

### FR Coverage Map

| FR | Epic | Description |
|----|------|-------------|
| FR-1 | Epic 1 | Identity 脚手架页面生成 + 编译通过 |
| FR-2 | Epic 1 | IEmailSender 适配器注册 |
| FR-3 | Epic 1 | 密码登录→Cookie→Blazor WASM 桥接 |
| FR-4 | Epic 1 | 2FA 登录 + .NET 10 Bug workaround |
| FR-5 | Epic 2 | Settings→Identity 管理页面链接跳转 |
| FR-6 | Epic 2 | 通行密钥登录完整保留 |
| FR-7 | Epic 2 | 退役手写代码 + 测试更新 |
| FR-8 | Epic 2 | 更新 Architecture + UX Design 文档 |

## Epic List

### Epic 1: Server 端 Identity 脚手架 + 认证流程

用户可以通过新的 Identity Bootstrap 页面完成登录（含 2FA 验证），Cookie 签发后无缝桥接到 Blazor WASM 首页。Server 项目的 Identity 脚手架页面编译通过、IEmailSender 适配器注册、.NET 10 Bug workaround 就绪。

**FRs covered:** FR-1, FR-2, FR-3, FR-4

### Epic 2: 前端适配 + 退役 + 文档更新

Blazor WASM Settings 页面替换为跳转链接、通行密钥登录完整保留、~1500 行手写认证代码退役、相关测试更新、Architecture 和 UX Design 文档同步新架构。

**FRs covered:** FR-5, FR-6, FR-7, FR-8

## Epic 1: Server 端 Identity 脚手架 + 认证流程

用户可以通过新的 Identity Bootstrap 页面完成登录（含 2FA 验证），Cookie 签发后无缝桥接到 Blazor WASM 首页。

**FRs covered:** FR-1, FR-2, FR-3, FR-4

### Story 1.1: 脚手架 Identity 页面 + 构建验证

As a 开发者，
I want 在 Server 项目中执行 Identity 脚手架生成 17 个 Razor Pages，
So that 登录、2FA 验证和账户管理页面在 Server 端可用。

**前置条件：**
- `dotnet tool install --global dotnet-aspnet-codegenerator`
- `Directory.Packages.props` 添加 `Microsoft.VisualStudio.Web.CodeGeneration.Design` (10.0.8)
- `Directory.Packages.props` 添加 `Microsoft.AspNetCore.Identity.UI` (10.0.8)
- NuGet 版本与现有 `Microsoft.AspNetCore.Identity.EntityFrameworkCore 10.0.8` 一致

**Acceptance Criteria:**

**Given** 前置条件已满足
**When** 执行脚手架命令（17 个文件，精确命令可直接复制执行）：
```powershell
dotnet aspnet-codegenerator identity `
  -dc BoxWise.Server.Data.AppDbContext `
  -u BoxWise.Server.Models.AppUser `
  --files "Account.Login;Account.LoginWith2fa;Account.LoginWithRecoveryCode;Account.Logout;Account.Lockout;Account.ConfirmEmail;Account.Manage._Layout;Account.Manage._ManageNav;Account.Manage._StatusMessage;Account.Manage.Index;Account.Manage.ChangePassword;Account.Manage.Email;Account.Manage.EnableAuthenticator;Account.Manage.ResetAuthenticator;Account.Manage.Disable2fa;Account.Manage.TwoFactorAuthentication;Account.Manage.GenerateRecoveryCodes"
```
**Then** `Areas/Identity/Pages/Account/` 下生成 17 个文件

> **17 文件清单:** Account.Login;Account.LoginWith2fa;Account.LoginWithRecoveryCode;Account.Logout;Account.Lockout;**Account.ConfirmEmail**;Account.Manage._Layout;Account.Manage._ManageNav;Account.Manage._StatusMessage;Account.Manage.Index;Account.Manage.ChangePassword;Account.Manage.Email;Account.Manage.EnableAuthenticator;Account.Manage.ResetAuthenticator;Account.Manage.Disable2fa;Account.Manage.TwoFactorAuthentication;Account.Manage.GenerateRecoveryCodes
>
> `Account.ConfirmEmail` 必须包含——`Account.Manage.Email` 发送的确认链接指向此页面，缺失会导致邮箱修改流程 404。

**Given** 脚手架完成
**When** 删除 `Areas/Identity/IdentityHostingStartup.cs` 和 `Areas/Identity/Data/` 下冗余 DbContext/User 文件
**And** 验证 NuGet 包版本与 `Directory.Packages.props` CPM 一致
**Then** `dotnet build` 0 错误

**Given** `MapRazorPages()` 在 `MapFallbackToFile()` 之前
**When** Server 启动后浏览器访问 `/Identity/Account/Login`
**Then** 显示 Bootstrap 样式的登录页面，不返回 404

**Given** 浏览器访问 `/Identity/Account/Manage`
**Then** 显示账户管理导航页面

> Identity 表已存在——脚手架后无需执行 EF 迁移。`dotnet build` 0 错误即验证成功。

### Story 1.2: IEmailSender 适配器注册

As a 用户，
I want Identity 管理页面的邮件发送功能正常工作，
So that 我可以收到邮箱确认邮件和 TOTP 设置验证邮件。

**Acceptance Criteria:**

**Given** `Services/IdentityEmailSender.cs` 已创建，实现 `IEmailSender` 接口
**When** 构造函数注入 `ISmtpConfigurationService`（接口，与现有 `EmailTwoFactorService` 约定一致）
**Then** `SendEmailAsync(string email, string subject, string htmlMessage)` 方法通过 `SmtpConfigurationService.GetSnapshot()` 获取 SMTP 配置，用 MimeKit 构建并发送邮件

**Given** `Program.cs` 中注册 `builder.Services.AddTransient<IEmailSender, IdentityEmailSender>()`
**When** `dotnet build`
**Then** 0 错误——`MailKit` 包已存在，`ISmtpConfigurationService` 已注册

**Given** 已配置 SMTP 的 Identity 页面（Account.Manage.Email）
**When** 用户输入新邮箱并点击"Send verification email"
**Then** 邮件通过配置的 SMTP 服务器成功发送，不抛 `Unable to resolve service for type 'IEmailSender'` 异常

**And** 不委托给 `EmailTwoFactorService.SendVerificationEmailAsync`（API 签名不兼容）
**And** `SendEmailAsync` 包含 try/catch + `ILogger` 日志——SMTP 未配置或发送失败时记录错误，不抛 500（与现有 `EmailTwoFactorService` 的静默降级行为一致）
**And** 使用 `MailboxAddress(config.FromName, config.FromAddress)`——属性名经 `SmtpConfigDto` record 验证
**And** Phase 完成 commit

### Story 1.3: Cookie 认证桥接 + LoginPath 配置

As a 用户，
I want 在 Identity Login.cshtml 页面用用户名/密码登录后自动回到 BoxWise 首页，
So that 登录体验流畅，Blazor WASM 正常显示我的认证状态。

**Acceptance Criteria:**

**Given** `Program.cs` 中 `ConfigureApplicationCookie` 配置 `LoginPath = "/Identity/Account/Login"`
**And** `OnRedirectToLogin` handler 修复：区分 API 请求（返回 401）和页面请求（重定向到 LoginPath）
```csharp
options.Events.OnRedirectToLogin = ctx =>
{
    if (ctx.Request.Path.StartsWithSegments("/api"))
        ctx.Response.StatusCode = 401;
    return Task.CompletedTask;
};
```
**When** 未登录用户访问 Blazor WASM 受保护页面
**Then** `[Authorize]` 拦截 → 自动重定向到 `/Identity/Account/Login`

**Given** 未登录用户直接访问 `/Identity/Account/Manage`
**When** Identity PageModel 的 `[Authorize]` 触发
**Then** 浏览器重定向到 `/Identity/Account/Login`，不返回 401（修复前 `OnRedirectToLogin` 无条件返回 401 阻止此行为）

**Given** 用户在 Identity `Login.cshtml` 输入正确的用户名和密码
**When** 提交登录表单
**Then** Server 签发 `.AspNetCore.Identity.Application` Cookie → HTTP 302 重定向到 `/`（Blazor WASM 首页）

**Given** 浏览器携带 Cookie 访问 Blazor WASM 首页
**When** `CookieAuthenticationStateProvider.GetAuthenticationStateAsync()` 调用 `GET /api/auth/me`
**Then** 返回 `AuthUserDto`（UserName + IsAdmin），`AppState.SetUser()` 更新客户端状态，`NotifyAuthenticationStateChanged()` 触发 UI 重渲染——底部 4 Tab 导航正常显示

**Given** 用户点击 Logout（Identity `Logout.cshtml` POST 表单）
**When** 登出完成
**Then** Cookie 清除，用户回到未登录状态

**And** `CookieAuthenticationStateProvider` 保持不变——仅依赖 `GET /api/auth/me`，与登录流程解耦
**And** 开发环境 `SameSiteMode.None` 不变，生产环境切换将在 Epic 2 Story 2.4 处理
**Given** Identity `Logout.cshtml.cs` 需要 GET 请求支持
**When** 在 `Logout.cshtml.cs` 添加：
```csharp
public IActionResult OnGet() => OnPost();
```
**Then** 导航到 `/Identity/Account/Logout`（GET 或 POST）均触发登出，无需两步操作

> Settings.razor 退出登录按钮改造（导航到 `/Identity/Account/Logout` + 不调用 `AuthService.LogoutAsync`）由 Story 2.1 统一处理，避免同一文件在两个 Story 中修改导致 git 冲突。
**And** Phase 完成 commit

### Story 1.4: 2FA 登录 + .NET 10 Bug 验证/workaround

As a 已配置 2FA 的用户，
I want 在 Identity LoginWith2fa 页面完成 TOTP 验证码验证后登录成功，
So that 我的账户安全不受迁移影响。

**Acceptance Criteria:**

**Given** 已配置 TOTP 2FA 的用户
**When** 在 `Login.cshtml` 输入用户名/密码
**Then** Identity 自动重定向到 `LoginWith2fa.cshtml`，显示验证码输入表单

**Given** 用户在 `LoginWith2fa.cshtml` 输入正确的 TOTP 验证码
**When** 提交验证
**Then** 登录成功 → Cookie 签发 → 重定向到 `/`

**Given** `LoginWith2fa.cshtml.cs` 的 `OnGetAsync` 调用 `SignInManager.GetTwoFactorAuthenticationUserAsync()`
**When** 在 .NET 10.0.8 环境下测试
**Then** ⚠️ 验证是否返回 null（受 dotnet/aspnetcore#66929 影响）

**Given** Bug 确认存在
**When** 在 `OnGetAsync` 中应用 PageModel 版 workaround：
```csharp
var principal = await HttpContext.AuthenticateAsync(IdentityConstants.TwoFactorUserIdScheme);
var userId = principal?.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
var user = userId != null ? await _userManager.FindByIdAsync(userId) : null;
```
**Then** `user` 非 null，2FA 验证流程正常继续

**Given** 使用恢复码登录
**When** 点击"使用恢复码登录"链接 → 输入 8 位恢复码
**Then** `LoginWithRecoveryCode.cshtml` 验证通过 → 登录成功

**And** 验证并记录 SPEC Open Question 2 结论：`WebAuthnEndpoints.LoginCompleteAsync` 在 passkey 验证成功后直接调用 `SignInAsync`，不检查 2FA——通行密钥本身就是第二因子，无需额外 2FA 验证。此架构决策记录在 Story 2.4 的 Architecture 文档更新中
**And** `RecoveryCodeService.VerifyRecoveryCodeAsync` 保留——Identity `LoginWithRecoveryCode.cshtml` 使用内置 `SignInManager`，但通行密钥 2FA 恢复码路径可能引用此服务
**And** Phase 完成 commit

---

## Epic 2: 前端适配 + 退役 + 文档更新

Blazor WASM Settings 页面替换为跳转链接、通行密钥登录完整保留、旧代码退役、文档同步。

**FRs covered:** FR-5, FR-6, FR-7, FR-8

### Story 2.1: Settings.razor 替换为跳转链接

As a 用户，
I want 从 Blazor WASM Settings 页面点击按钮跳转到 Identity 账户管理页面，
So that 我可以管理 2FA、修改密码和邮箱。

**Acceptance Criteria:**

**Given** `Settings.razor` 中 `TwoFactorManage` 和 `TwoFactorSetup` 组件引用已移除
**When** 渲染 Settings 页面
**Then** 不显示 2FA 设置/管理对话框，显示"管理账户设置"按钮（Identity Manage 页面同时覆盖首次设置和后续管理）

**Given** 开发环境 `appsettings.Development.json` 中 `ApiBaseUrl = "https://localhost:5000/"`
**And** Client `Program.cs` 在 `IsDevelopment()` 时加载 `appsettings.Development.json`
**When** 点击"管理账户设置"按钮
**Then** 新标签页打开 `https://localhost:5000/Identity/Account/Manage`

**Given** 生产环境 `ApiBaseUrl` 为空
**When** 点击"管理账户设置"按钮
**Then** 同域相对路径 `/Identity/Account/Manage`，不跨端口

**Given** 已登录用户的 Cookie 有效
**When** 访问 `/Identity/Account/Manage`
**Then** 直接显示管理页面，不要求重新登录

**Given** `ChangePasswordDialog` 和 `AccountInfoDialog` 组件引用已移除（密码修改和账户信息修改由 Identity Manage 页面接管，SPEC CAP-5）
**When** 渲染 Settings 页面
**Then** 不显示这两个对话框，对应功能通过"管理账户设置"链接跳转到 Identity 页面

**Given** Settings.razor 的"退出登录"按钮
**When** 点击时
**Then** 导航到 `/Identity/Account/Logout`（Identity `Logout.cshtml.cs` 已在 Story 1.3 添加 `OnGet` handler），不调用 `AuthService.LogoutAsync`

**Given** 通行密钥管理需要独立入口（Identity Manage 页面不提供 WebAuthn credentials 管理）
**When** Settings.razor 将"双因素认证与通行密钥"拆分为两个条目：
- "账户安全设置" → `/Identity/Account/Manage`（TOTP/Email 2FA + 密码 + 邮箱）
- "通行密钥管理" → 保留内联组件/对话框，调用 `AuthService.GetWebAuthnCredentialsAsync()` / `DeleteWebAuthnCredentialAsync()`
**Then** 用户可查看已注册的通行密钥列表并删除不再使用的密钥

**And** Settings.razor 所有修改集中在本 Story（不再横跨 Story 1.3），避免 git 冲突
**And** Phase 完成 commit

### Story 2.2: Login.razor 保留通行密钥 + 适配 Identity 登录

As a 用户，
I want 在 Identity Login 页面点击"使用通行密钥登录"链接后跳转到 Blazor WASM 使用 WebAuthn，
So that 通行密钥登录功能不被迁移破坏。

**Acceptance Criteria:**

**Given** `Login.razor` 中标准登录和 2FA 验证部分已移除：
- `HandleLogin`, `HandleTwoFactorVerify`, `HandleRecoveryCodeVerify`
- `LoadTwoFactorChallengeAsync`, `SelectMethod`, `ResendEmailCode`, `BackToCredentials`
- 2FA UI 表单（`LoginStep.TwoFactor` 分支）
- `_hasRecoveryCodes`, `_allowedMethods` 等 2FA 专用字段
**When** `dotnet build`
**Then** Client 项目 0 错误

**Given** `Login.razor` 保留内容：
- `HandlePasskeyLogin` 方法
- 通行密钥按钮 UI（`MudButton StartIcon="@Icons.Material.Filled.Fingerprint"`）
- `LoginModel` 类, `LoginStep` 枚举
- `_passkeyLoading` 状态
**When** 渲染 `/login` 页面
**Then** 显示通行密钥登录按钮

**Given** Identity `Login.cshtml`（Server 端 Razor Page，`Areas/Identity/Pages/Account/Login.cshtml`）底部增加链接
**When** 渲染 Identity 登录页面
**Then** 显示"使用通行密钥登录"链接，指向 `/login`（Blazor WASM 路由）——这是 Server 端 .cshtml 文件修改，通过在 `<form>` 外添加 `<a href="/login">` 实现

**Given** Login.razor 迁移后只保留通行密钥按钮，无用户名/密码输入框
**When** 渲染 `/login` 页面
**Then** 页面顶部显示引导提示："密码登录请访问登录页面"（附链接 `/Identity/Account/Login`）——确保没有通行密钥的用户不被卡住

**Given** 用户在 `/login` 点击通行密钥按钮
**When** `JS.InvokeAsync("webauthn.getCredential")` 成功
**Then** `CompleteWebAuthnLoginAsync` → `AppState.SetUser()` → `NotifyAuthenticationStateChanged()` → 导航到 `/`

**And** `AuthService.cs` 中 WebAuthn 方法全部保留：`StartWebAuthnLoginAsync`, `CompleteWebAuthnLoginAsync`, `GetWebAuthnCredentialsAsync`, `DeleteWebAuthnCredentialAsync`, `StartWebAuthnRegistrationAsync`, `CompleteWebAuthnRegistrationAsync`
**And** Phase 完成 commit

### Story 2.3: 退役旧代码 + 测试更新

As a 开发者，
I want 删除所有被 Identity 页面替代的手写代码和相关测试，
So that 代码库精简，维护负担降低。

**Acceptance Criteria:**

**Given** 退役前 Grep 验证无遗漏调用方
**When** 执行退役操作
**Then** 以下文件被删除：
- `src/BoxWise.Server/Endpoints/TwoFactorModifyEndpoints.cs`（296 行，5 端点）
- `src/BoxWise.Server/Endpoints/EmailVerificationEndpoints.cs`（~170 行，2 端点）
- `src/BoxWise.Client/Components/TwoFactorManage.razor`
- `src/BoxWise.Client/Components/TwoFactorSetup.razor`（首次 2FA 设置对话框，Identity Manage 页面替代）
- `src/BoxWise.Client/Components/TotpSetup.razor`（仅被 TwoFactorManage/TwoFactorSetup 引用，两者退役后成为孤儿代码）
- `src/BoxWise.Client/Components/RecoveryCodesDisplay.razor`（代码库中零引用，已是死代码）
- `src/BoxWise.Client/Components/ChangePasswordDialog.razor`（Settings.razor 引用已移除，Identity `Account.Manage.ChangePassword` 替代）
- `src/BoxWise.Client/Components/AccountInfoDialog.razor`（Settings.razor 引用已移除，其内部调用的 `SendModifyEmailChallengeAsync` 已退役——即使保留也无法工作）

**Given** `AuthEndpoints.cs` 修改
**When** 删除 `LoginAsync` 和 `LogoutAsync` 方法
**Then** `GetCurrentUserAsync` 和 WebAuthn 端点保留

**Given** `TwoFactorEndpoints.cs` 修改
**When** 删除 `VerifyAsync` 和 `VerifyRecoveryCodeDuringLoginAsync`
**Then** `ChallengeAsync` 和 `SendChallengeCodeAsync` 条件退役——仅当 `git grep` 确认 Blazor WASM 端无残留调用

**Given** `AuthService.cs` 修改
**When** 删除以下方法（保留 WebAuthn + Setup 方法）：
- LoginAsync, VerifyTwoFactorAsync, GetTwoFactorChallengeAsync, ResendTwoFactorChallengeCodeAsync, VerifyRecoveryCodeDuringLoginAsync, LogoutAsync
- 全部 Modify 方法（AuthenticateForModifyAsync ~ ModifyRegenerateRecoveryCodesAsync）
- RegenerateRecoveryCodesAsync, SendEmailVerificationCodeAsync, VerifyEmailCodeAsync, UpdateEmailAsync, ModifyTotpAsync, VerifyModifyTotpAsync, SendModifyEmailChallengeAsync
**Then** 保留：全部 WebAuthn 方法、UpdateProfileAsync（仍被 Settings.razor 用户信息编辑调用）
**And** `ChangePasswordAsync`、`ReAuthenticateAsync`、`GetTwoFactorStatusAsync` 一并退役——调用方已移除（ChangePasswordDialog/AccountInfoDialog/TwoFactorManage），Identity Manage 页面覆盖对应功能
> 注：Research 阶段曾提议保留 `SetupTotpAsync`/`VerifyTotpSetupAsync`/`SetupEmailTwoFactorAsync`/`VerifyEmailTwoFactorAsync`，但因其调用方 TotpSetup.razor/TwoFactorSetup.razor 已退役，此处统一清理。
**And** `SetupTotpAsync`、`VerifyTotpSetupAsync`、`SetupEmailTwoFactorAsync`、`VerifyEmailTwoFactorAsync`、`RegenerateRecoveryCodesAsync` 一并退役——调用方 TotpSetup.razor/TwoFactorSetup.razor 已退役，Identity Manage 页面覆盖这些功能

**Given** Server 端对应端点也需要退役
**When** 删除 `TwoFactorEndpoints.cs` 中的以下方法：
- `RegenerateRecoveryCodesAsync` (POST /api/auth/2fa/recovery/regenerate)
- `GenerateQrCodeAsync` (POST /api/auth/2fa/qr-code)
- `GetStatusAsync` (GET /api/auth/2fa/status)
- `SetupTotpAsync`、`VerifyTotpAsync`、`SetupEmailAsync`、`VerifyEmailAsync`
**Then** `dotnet build` 0 错误

**Given** `EmailVerificationEndpoints.OperationTokenPurpose` 常量被 `AuthEndpoints.cs:387` 和 `AuthEndpointsTests.cs:96` 引用
**When** 退役 `EmailVerificationEndpoints.cs` 前
**Then** 将常量提取到 `AuthEndpoints.cs` 自身或 `BoxWise.Server.Utilities` 共享类——否则两个引用文件编译失败

**Given** Rate Limit 策略清理
**When** 删除 `Program.cs` 中 `"2fa-modify"` 和 `"email-verification"` 策略配置
**And** 检查 `"login-per-account"` 策略是否仍被使用
**Then** 无多余的 Rate Limit 策略注册代码

**Given** 退役完成
**When** `dotnet build`
**Then** 0 错误——无残留引用

**Given** 测试文件更新
**When** 删除退役方法和端点的测试
**Then** `dotnet test` 全部通过

**Given** 死代码复查（`decommission-checklist.md` 中的 grep 命令）
**When** 逐项执行
**Then** 无遗漏的死代码引用

**And** Phase 完成 commit: `refactor(identity): decommission hand-written auth code`

### Story 2.4: SameSite 策略 + 更新 Architecture/UX 文档

As a 开发者，
I want 生产环境 SameSite 策略正确配置，Architecture 和 UX Design 文档反映迁移后的新架构，
So that 安全配置完整，后续开发者有准确的参考文档。

**Acceptance Criteria:**

**Given** `Program.cs` 中 Cookie 配置
**When** 添加环境判断：
```csharp
options.Cookie.SameSite = env.IsDevelopment() ? SameSiteMode.None : SameSiteMode.Lax;
options.Cookie.SecurePolicy = env.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
```
**Then** 开发环境 SameSite=None（跨端口），生产环境 Lax + Always（Caddy 反向代理场景下强制 Secure）
**And** `Program.cs` 中 `IdentityConstants.TwoFactorUserIdScheme` Cookie（`Program.cs:69-74`）同步应用相同的 `env.IsDevelopment()` 判断

**Given** Lockout 参数使用 Identity 默认值（BoxWise `Program.cs` 未显式配置）
**When** 验证 `Account.Lockout.cshtml` 的锁定时长
**Then** 确认 `MaxFailedAccessAttempts=5` / `DefaultLockoutTimeSpan=5min` 在 Identity 脚手架页面中一致

**Given** `_bmad-output/planning-artifacts/architecture.md`
**When** 更新认证流程章节
**Then** 反映：登录/2FA 从 Blazor WASM SPA → Server 端 Identity Razor Pages，Cookie 通过 `CookieAuthenticationStateProvider` + `GET /api/auth/me` 桥接到 WASM
**And** 记录通行密钥架构决策：`WebAuthnEndpoints.LoginCompleteAsync` 在验证成功后直接 `SignInAsync`，不检查 2FA——通行密钥本身作为第二因子（已验证的硬件令牌），无需额外 TOTP/Email 验证

**Given** `_bmad-output/planning-artifacts/ux-design-specification.md`
**When** 更新登录/设置章节
**Then** 反映：登录页面和 2FA 设置管理使用 Bootstrap 独立页面，Blazor WASM 通行密钥按钮保留，"双 UI 风格并存"是可接受的 UX 模式

**Given** `CLAUDE.md`
**When** 更新架构说明
**Then** 反映：新增 `Areas/Identity/Pages/`、IEmailSender 适配器、退役端点清单

**And** `dotnet build && dotnet test` 最终验证通过
**And** `git log --oneline` 显示每个 Story 的独立 commit
**And** Phase 完成 commit: `chore(identity): docs + SameSite policy for migration`
