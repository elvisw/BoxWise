---
id: SPEC-identity-scaffold-migration
companions:
  - decommission-checklist.md
  - migration-phases.md
sources:
  - ../planning-artifacts/research/technical-identity-scaffold-hybrid-migration-research-2026-05-31.md
---

> **Canonical contract.** This SPEC and the files in `companions:` are the complete, preservation-validated contract for what to build, test, and validate. Source documents listed in frontmatter are for traceability only — consult them only if you need narrative rationale or prose color this contract intentionally omits.

# Identity 脚手架混合模式迁移

## Why

**这是一个痛苦要解决。** BoxWise 当前有超过 1500 行手写代码专门处理 ASP.NET Core Identity 的认证 UI 和 2FA 设置管理——包括 `Login.razor`(462行)、`TwoFactorManage.razor`(587行)、`TwoFactorModifyEndpoints.cs`(296行) 及 `AuthService.cs` 中对应的客户端方法。这些是通用安全基础设施，不是 BoxWise 的业务差异化功能。每次修改（修改邮箱、重置 TOTP、重新生成恢复码）都需要安全审计、手动测试、回归验证。.NET 10 的 `GetTwoFactorAuthenticationUserAsync()` Bug 已经证明了手写认证代码的脆弱性。ASP.NET Core Identity 脚手架提供了一套微软维护的 Razor Pages 来覆盖这些功能——我们应该让它干它的活。

## Capabilities

- id: CAP-1
  intent: Identity 脚手架页面已生成且可编译运行在 Server 项目中，覆盖登录和账户管理操作，复用现有数据库上下文和用户类。
  success: `dotnet build` 0 错误，无 DI 冲突，访问 `/Identity/Account/Login` 显示 Bootstrap 样式的登录页面，访问 `/Identity/Account/Manage` 显示账户管理导航。

- id: CAP-2
  intent: 系统注册 `IEmailSender` 适配器，使 Identity 管理页面的邮件发送功能（邮箱确认链接、TOTP 设置验证）通过现有的 `SmtpConfigurationService` 发送。
  success: 在 `Account.Manage.Email` 页面点击"Send verification email"不报错，邮件通过配置的 SMTP 服务器成功发送。

- id: CAP-3
  intent: 用户在 Identity `Login.cshtml` 页面用用户名/密码登录后，Cookie 被签发，重定向回 Blazor WASM 首页，`CookieAuthenticationStateProvider` 通过 `GET /api/auth/me` 感知认证状态，UI 更新为已登录。
  success: 完整流程：访问 Blazor WASM → 未登录重定向到 `/Identity/Account/Login` → 输入凭据 → 登录成功 → 重定向回 `/` → 底部 4 Tab 导航正常显示 → `GET /api/auth/me` 返回用户名+IsAdmin。

- id: CAP-4
  intent: 已配置 TOTP 2FA 的用户在 Identity `LoginWith2fa.cshtml` 页面完成 2FA 验证后登录成功。如受 .NET 10 Bug 影响，在 PageModel 中应用 `GetTwoFactorUserAsync` workaround。
  success: TOTP 2FA 用户输入用户名/密码 → 跳转到 2FA 验证码页面 → 输入正确验证码 → 登录成功 → 重定向回 WASM 首页。

- id: CAP-5
  intent: 用户从 Blazor WASM Settings 页面通过链接跳转到 Server 端 Identity 管理页面（`/Identity/Account/Manage/*`），在新页面中管理 2FA 设置、修改密码、修改邮箱，操作完成后关闭标签页回到 Blazor WASM。
  success: Settings.razor 中"管理账户设置"按钮打开新标签页到 `/Identity/Account/Manage`，已登录用户不要求重新认证，各项管理操作正常。

- id: CAP-6
  intent: 用户可以从 Identity `Login.cshtml` 页面导航到 Blazor WASM `Login.razor`，使用通行密钥（WebAuthn/Passkey）登录，不受迁移影响。Identity 登录页面增加"使用通行密钥登录"链接指向 Blazor WASM `/login`。
  success: 在 Identity `Login.cshtml` 页面点击"使用通行密钥登录"→ 跳转到 Blazor WASM `/login`（已通过 Cookie 认证）→ 点击通行密钥按钮 → 浏览器弹出 WebAuthn 对话框 → 验证通过 → 登录成功。

- id: CAP-7
  intent: 退役所有被 Identity 页面替代的手写代码：`TwoFactorModifyEndpoints.cs`(5端点)、`EmailVerificationEndpoints.cs`(2端点)、`TwoFactorEndpoints.cs` 登录 2FA 端点(2端点无条件 + 2端点条件退役)、`AuthEndpoints.LoginAsync/LogoutAsync`、`TwoFactorManage.razor`、`AuthService.cs` 对应客户端方法，以及相关测试。
  success: `dotnet build` 0 错误，`dotnet test` 全部通过，`git grep` 确认退役文件/方法无残留引用。

## Constraints

- **C1: Bootstrap 样式隔离。** Identity 页面使用其默认 Bootstrap 样式，不与 MudBlazor 做样式桥接。用户在 Identity 页面和 Blazor WASM 之间切换时会看到不同的 UI 风格——这是已接受的权衡。
- **C2: 通行密钥不可退役。** `Login.razor` 中 WebAuthn/Passkey 相关代码（`HandlePasskeyLogin`、`webauthn.getCredential` JS 互操作）和对应的 API 端点必须保留。Identity UI 不提供 Passkey 支持。
- **C3: .NET 10 Bug workaround。** `LoginWith2fa.cshtml.cs` 如受 dotnet/aspnetcore#66929 影响，必须在 PageModel 中应用 workaround（通过 `HttpContext.AuthenticateAsync` + `FindByIdAsync`），不能等待上游修复。
- **C4: IEmailSender 必须注册。** 使用 `ISmtpConfigurationService` + MimeKit 实现 `IEmailSender` 接口（与现有 `EmailTwoFactorService` 遵循相同的接口注入约定），直接发送 Identity 页面生成的邮件内容（主题+HTML正文），不委托给 `EmailTwoFactorService.SendVerificationEmailAsync`（API 签名不兼容）。
- **C5: `CookieAuthenticationStateProvider` 不可退役。** 它仅依赖 `GET /api/auth/me`，与登录流程解耦。Identity 页面签发 Cookie 后，WASM 客户端通过它感知认证状态。
- **C6: `MapRazorPages()` 必须在 `MapFallbackToFile()` 之前。** 确保 Identity 页面路由不被 Blazor WASM SPA 回退拦截。当前顺序已正确，迁移后需验证。
- **C7: 生产环境 SameSite 策略。** 生产环境必须使用 `SameSiteMode.Lax` + `SecurePolicy.Always`（Caddy 443→80 反向代理场景下 ASP.NET 接收 HTTP 请求，`SameAsRequest` 不会设置 Secure 标记），不得沿用开发环境的 `SameSiteMode.None`。Phase 6 在 `Program.cs` 中用 `env.IsDevelopment()` 条件判断自动切换。

## Non-goals

- **NG1: 不做 MudBlazor/Bootstrap 样式桥接。** 用户已确认接受两种 UI 风格并存。
- **NG2: 不迁移 `Account.Register`（自助注册）。** BoxWise v1 通过 Admin 后台创建用户，无自助注册需求。
- **NG3: 不迁移 `Account.ForgotPassword` / `Account.ResetPassword`。** 需要额外 SMTP 配置和邮件模板，v1 优先级低。
- **NG4: 不使用 `MapIdentityApi<TUser>()` JSON API 方案。** 与现有 Cookie 认证架构冲突，且仍需手写 Blazor WASM UI。
- **NG5: 不迁移到 Blazor Web App 模板。** 工作量大，ROI 低。
- **NG6: 不需要 EF 迁移。** Identity 表已存在于数据库中。
- **NG8: 2FA 宽限期强制机制（`TwoFactorGracePeriodUntil`）随退役端点一同移除。** Identity 的 `Login.cshtml` 不检查此自定义字段，迁移后用户不会在宽限期过期后被强制设置 2FA。这是已知的行为变更——BoxWise ≤5 人家用场景下可接受。如需保留，应在后续 Story 中在 `Login.cshtml.cs` 的自定义逻辑中重新实现。

## Success signal

`dotnet test` 全部通过，手动验证完整流程——未登录→Identity `Login.cshtml`→2FA 验证→登录成功→Blazor WASM 首页→Settings→Identity `Manage` 页面→修改 TOTP/邮箱/密码→通行密钥登录正常——`git grep` 确认退役代码零残留引用。

## Assumptions

- BoxWise 的 `AppDbContext` 已继承 `IdentityDbContext<AppUser>`，脚手架可以复用。
- `SmtpConfigurationService` 已注册为 DI 服务，`GetSnapshot()` 返回的 `SmtpConfigDto` 包含 `Host`、`Port`、`Username`、`Password`、`FromAddress`、`FromName`。
- 开发环境 `SameSiteMode=None` 配置在迁移期间保持不变。
- `MailKit` NuGet 包已在项目中可用。

## Open Questions

- `LoginWith2fa.cshtml` 是否实际受 dotnet/aspnetcore#66929 影响？（Phase 2 验证后关闭）
- ~~通行密钥登录后的 2FA 流程是否走自定义端点还是 Identity 页面？~~ **已关闭：** `WebAuthnEndpoints.LoginCompleteAsync` 在 passkey 验证成功后直接 `SignInAsync`，不检查 2FA。通行密钥本身作为第二因子（已验证的硬件令牌），无需额外 TOTP/Email 验证。此决策记录在 Architecture 文档中（Story 2.4）。
- `TwoFactorEndpoints.ChallengeAsync` / `SendChallengeCodeAsync` 在退役后是否仍有调用方？（Phase 4 用 `git grep` 确认后关闭）
