# 退役清单

> 配套 SPEC.md CAP-7。精确列出退役和保留的代码。

## Server 端点：退役

| 文件 | 端点/方法 | 行数 | 替代者 |
|------|----------|:---:|------|
| `Endpoints/TwoFactorModifyEndpoints.cs` | 5 端点: /authenticate, /totp, /totp/verify, /send-challenge, /recovery/regenerate | 296 | `Account.Manage.*` |
| `Endpoints/EmailVerificationEndpoints.cs` | 2 端点: SendCodeAsync, VerifyCodeAsync | ~170 | `Account.Manage.Email` **⚠️ 退役前将 `OperationTokenPurpose` 常量提取到 `AuthEndpoints.cs` 或共享类**（`AuthEndpoints.cs:387` + `AuthEndpointsTests.cs:96` 引用此常量） |
| `Endpoints/TwoFactorEndpoints.cs` | `VerifyAsync` (POST /api/auth/2fa/verify) | ~50 | `Account.LoginWith2fa` |
| `Endpoints/TwoFactorEndpoints.cs` | `VerifyRecoveryCodeDuringLoginAsync` (POST /api/auth/2fa/recovery/verify) | ~40 | `Account.LoginWithRecoveryCode` |
| `Endpoints/TwoFactorEndpoints.cs` | `RegenerateRecoveryCodesAsync` (POST /api/auth/2fa/recovery/regenerate) | ~30 | `Account.Manage.GenerateRecoveryCodes` |
| `Endpoints/TwoFactorEndpoints.cs` | `GenerateQrCodeAsync` (POST /api/auth/2fa/qr-code) | ~20 | `Account.Manage.EnableAuthenticator` |
| `Endpoints/TwoFactorEndpoints.cs` | `GetStatusAsync` (GET /api/auth/2fa/status) | ~30 | Identity 内置状态检查 |
| `Endpoints/TwoFactorEndpoints.cs` | `SetupTotpAsync` / `VerifyTotpAsync` / `SetupEmailAsync` / `VerifyEmailAsync` | ~100 | `Account.Manage.EnableAuthenticator` / Email 页面 |
| `Endpoints/AuthEndpoints.cs` | `LoginAsync` (POST /api/auth/login) | ~50 | `Account.Login` |
| `Endpoints/AuthEndpoints.cs` | `LogoutAsync` (POST /api/auth/logout) | ~10 | `Account.Logout` |

## Server 端点：条件退役

| 文件 | 端点 | 条件 |
|------|------|------|
| `Endpoints/TwoFactorEndpoints.cs` | `ChallengeAsync` | 通行密钥登录 2FA 不经过此端点后退役 |
| `Endpoints/TwoFactorEndpoints.cs` | `SendChallengeCodeAsync` | 同上 |

## Server 端点：保留

| 文件 | 端点/方法 | 原因 |
|------|----------|------|
| `Endpoints/AuthEndpoints.cs` | `GetCurrentUserAsync` (GET /api/auth/me) | WASM 认证状态同步必需 |
| `Endpoints/AuthEndpoints.cs` | WebAuthn/Passkey 端点 | 通行密钥登录不可替代 |
| `Endpoints/TwoFactorEndpoints.cs` | `ReAuthenticateAsync` (仅此方法保留) | 仍被 AccountInfoDialog.razor 调用 |
| `Endpoints/AdminTwoFactorEndpoints.cs` | 全部 | Admin 后台 2FA 管理 |
| `Services/RecoveryCodeService.cs` | `VerifyRecoveryCodeAsync` | 仅通行密钥 2FA 恢复码路径使用（若验证无调用方则退役） |

## Client 组件：退役

| 文件 | 行数 | 操作 |
|------|:---:|------|
| `Components/TwoFactorManage.razor` | 541 | 🗑️ 完全删除 |
| `Components/TwoFactorSetup.razor` | ~200 | 🗑️ 完全删除（首次 2FA 设置对话框，Identity Manage 页面替代） |
| `Components/TotpSetup.razor` | ~200 | 🗑️ 完全删除（仅被 TwoFactorManage/TwoFactorSetup 引用，贬值后成为孤儿代码） |
| `Components/RecoveryCodesDisplay.razor` | ~60 | 🗑️ 完全删除（代码库中零引用，已是死代码） |
| `Components/ChangePasswordDialog.razor` | ~100 | 🗑️ 完全删除（Settings.razor 引用已移除，Identity `Account.Manage.ChangePassword` 替代） |
| `Components/AccountInfoDialog.razor` | ~150 | 🗑️ 完全删除（Settings.razor 引用已移除，内部调用的 `SendModifyEmailChallengeAsync` 已退役） |

## Client 组件：部分退役

| 文件 | 总行数 | 退役方法 | 保留方法 |
|------|:---:|------|------|
| `Pages/Login.razor` | 462 | `HandleLogin`, `HandleTwoFactorVerify`, `HandleRecoveryCodeVerify`, `LoadTwoFactorChallengeAsync`, `SelectMethod`, `ResendEmailCode`, `BackToCredentials`, 2FA UI 表单 | `HandlePasskeyLogin`, 通行密钥按钮 UI, `LoginModel`, `LoginStep` |
| `Services/AuthService.cs` | 619 | `LoginAsync`, `VerifyTwoFactorAsync`, `GetTwoFactorChallengeAsync`, `ResendTwoFactorChallengeCodeAsync`, `VerifyRecoveryCodeDuringLoginAsync`, `LogoutAsync`, 全部 Modify 方法, `RegenerateRecoveryCodesAsync`, `SendEmailVerificationCodeAsync`, `VerifyEmailCodeAsync`, `UpdateEmailAsync`, `ModifyTotpAsync`, `VerifyModifyTotpAsync`, `SendModifyEmailChallengeAsync`, `ModifyRegenerateRecoveryCodesAsync`, `SetupTotpAsync`, `VerifyTotpSetupAsync`, `SetupEmailTwoFactorAsync`, `VerifyEmailTwoFactorAsync`, `ChangePasswordAsync`, `ReAuthenticateAsync`, `GetTwoFactorStatusAsync` | 全部 WebAuthn 方法, `UpdateProfileAsync` |

## Server 新增文件

| 文件 | 说明 |
|------|------|
| `Areas/Identity/Pages/Account/*` | 脚手架生成的 17 个 Razor Pages（含 ConfirmEmail） |
| `Services/IdentityEmailSender.cs` | `IEmailSender` 适配器——通过 `ISmtpConfigurationService` + MimeKit 发送邮件 |

## 新增 NuGet 包

| 包 | 用途 |
|-----|------|
| `Microsoft.VisualStudio.Web.CodeGeneration.Design` | 脚手架工具支持 |
| `Microsoft.AspNetCore.Identity.UI` | Identity Razor Pages UI |

| `Program.cs` — `"2fa-modify"` rate limit 策略 | 退役——`TwoFactorModifyEndpoints.cs` 退役后无引用 |
| `Program.cs` — `"email-verification"` rate limit 策略 | 退役——`EmailVerificationEndpoints.cs` 退役后无引用 |
| `Program.cs` — `LoginAsync` 上的 `.RequireRateLimiting("login-per-account")` | 退役——`LoginAsync` 端点退役 |

## 退役后验证命令

```bash
# 确认退役文件无残留引用
git grep "TwoFactorModifyEndpoints"
git grep "TwoFactorManage"
git grep "EmailVerificationEndpoints"
git grep "LoginAsync" src/BoxWise.Client/

# 确认退役方法无残留调用
git grep "VerifyTwoFactorAsync"
git grep "GetTwoFactorChallengeAsync"
git grep "ResendTwoFactorChallengeCodeAsync"
git grep "VerifyRecoveryCodeDuringLoginAsync"
git grep "AuthenticateForModifyAsync"
git grep "ModifyTotpAsync"
git grep "SendModifyEmailChallengeAsync"
git grep "ModifyRegenerateRecoveryCodesAsync"
git grep "SendEmailVerificationCodeAsync"
git grep "VerifyEmailCodeAsync"
git grep "UpdateEmailAsync"

# 死代码复查
git grep "ChallengeAsync" src/BoxWise.Client/
git grep "SendChallengeCodeAsync" src/BoxWise.Client/
```
