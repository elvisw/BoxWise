---
baseline_commit: 79a61ce
review_baseline: |
  3-agent parallel audit (2026-06-02). Key findings incorporated:
  - Settings.razor GetTwoFactorStatusAsync → removed (Plan A)
  - UpdateProfileAsync → retired (zero callers, verified by audit)
  - RecoveryCodeService → retained (WebAuthnEndpoints.cs:170 + TwoFactorService.cs still depend)
  - 3 test files added to decommission plan
  - 5 rate limit policies retired (not 2)
  - GetLastRecoveryCodes/ClearLastRecoveryCodes added to retire list
  - LoginModel/LoginStep tasks removed (already deleted in prior stories)
---

# Story 11.3: 退役旧代码 + 测试更新

Status: done

## Senior Developer Review (AI)

**Review Date:** 2026-06-02
**Review Outcome:** Approve
**Reviewers:** 4-agent parallel adversarial review (Blind Hunter ×2, Edge Case Hunter, Acceptance Auditor)

### Action Items

- [x] [Low] `LoginResult` 枚举 3 个成员未使用（RequiresTwoFactor/RequiresTwoFactorSetup/PasswordRequiresChange）— 非阻塞，后续清理
- [x] [Low] `AuthConstants.OperationTokenPurpose` 无存活调用方 — 死常量，非阻塞
- [x] [Low] `QRCoder` 包 + 多个 DTO 无存活引用 — 预存死依赖，后续清理
- [x] [Info] Email 2FA 登录路径 — 预存限制（Story 11.2 已断开 Client 端），记录于 Epic 11 回顾

### Review Summary

**CRITICAL 发现（1 项 — 非阻塞）：** Email 2FA 登录的 Server 端路径（ChallengeAsync/VerifyAsync/SendChallengeCodeAsync）随 TwoFactorEndpoints.cs 退役。这是预存限制——Story 11.2 移除 Login.razor 的 2FA UI 时 Client 端调用已断开，Story 11.3 仅清理已不可达的 Server 端代码。已记录于 Epic 11 回顾。

**MEDIUM 发现（2 项 — 预存问题）：** Identity 脚手架页面不更新自定义字段 `ConfiguredMethods`；ChallengeAsync 中的防御性数据修复逻辑未迁移。均为 Epic 10 预存问题。

**组 1/3/4：全部通过。** AuthEndpoints/AuthService/Settings 修改正确，Client 组件退役链完整闭合，Program.cs/测试/配置更新无误。SPEC C2（通行密钥）和 SPEC C5（CookieAuthenticationStateProvider）合规已验证。

## Story

As a 开发者，
I want 删除所有被 Identity 页面替代的手写认证代码和相关测试，
so that 代码库精简 ~1600 行，维护负担降低，无死代码残留。

## Acceptance Criteria

### AC-1: 退役 Server 端点文件（完整删除）

**Given** `git grep` 确认无残留调用方
**When** 删除以下文件
**Then** `dotnet build` 0 错误 0 警告

| # | 文件 | 约行数 | 替代者 |
|---|------|:---:|------|
| 1 | `src/BoxWise.Server/Endpoints/TwoFactorModifyEndpoints.cs` | 296 | `Account.Manage.*` |
| 2 | `src/BoxWise.Server/Endpoints/EmailVerificationEndpoints.cs` | ~170 | `Account.Manage.Email` |
| 3 | `src/BoxWise.Server.Tests/Endpoints/EmailVerificationEndpointsTests.cs` | ~100 | 测试随代码退役 |

⚠️ **退役前必须提取 `OperationTokenPurpose` 常量**（见 AC-2）

### AC-2: 提取 OperationTokenPurpose 常量

**Given** `EmailVerificationEndpoints.OperationTokenPurpose` 常量被以下文件引用：
- `AuthEndpoints.cs:387` — `UpdateProfileAsync` 端点（该端点随之退役，但常量提取确保编译安全）
- `AuthEndpointsTests.cs:96` — `UpdateProfile` 测试

**When** 在删除 `EmailVerificationEndpoints.cs` 之前，创建 `src/BoxWise.Server/Utilities/AuthConstants.cs`：
```csharp
namespace BoxWise.Server.Utilities;

public static class AuthConstants
{
    public const string OperationTokenPurpose = "email-operation-token";
}
```

**And** 更新引用：
- `AuthEndpoints.cs:387`: `EmailVerificationEndpoints.OperationTokenPurpose` → `AuthConstants.OperationTokenPurpose`
- `AuthEndpointsTests.cs:96`: `EmailVerificationEndpoints.OperationTokenPurpose` → `AuthConstants.OperationTokenPurpose`

**Then** 两个引用文件编译通过，不依赖即将退役的文件

### AC-3: 退役 TwoFactorEndpoints.cs（整文件删除）

**Given** `TwoFactorEndpoints.cs` (637 行) 包含 14 个方法和 2 个 RouteGroupBuilder
**When** 执行以下操作：
1. 删除 `src/BoxWise.Server/Endpoints/TwoFactorEndpoints.cs`（整文件）
2. 移除 `Program.cs` 中的 `.MapTwoFactorEndpoints()` 和 `.MapQrCodeEndpoints()` 调用
3. 验证 `Program.cs` 中 `RecoveryCodeService` 的 `AddScoped` 注册保留——`WebAuthnEndpoints.cs:170` 和 `TwoFactorService.cs` 仍依赖此服务，不应退役

**Then** `dotnet build` 0 错误

退役方法（全部 14 个方法均被 Identity 替代）：

| 方法 | 端点路由 | 替代者 |
|------|---------|------|
| `ChallengeAsync` | `POST /api/auth/2fa/challenge` | `Account.LoginWith2fa` |
| `VerifyAsync` | `POST /api/auth/2fa/verify` | `Account.LoginWith2fa` |
| `SendChallengeCodeAsync` | `POST /api/auth/2fa/send-challenge-code` | Identity 内置邮件 |
| `VerifyRecoveryCodeDuringLoginAsync` | `POST /api/auth/2fa/recovery/verify` | `Account.LoginWithRecoveryCode` |
| `RegenerateRecoveryCodesAsync` | `POST /api/auth/2fa/recovery/regenerate` | `Account.Manage.GenerateRecoveryCodes` |
| `GetStatusAsync` | `GET /api/auth/2fa/status` | Identity 内置状态 |
| `SetupTotpAsync` | `POST /api/auth/2fa/setup-totp` | `Account.Manage.EnableAuthenticator` |
| `VerifyTotpAsync` | `POST /api/auth/2fa/verify-totp` | `Account.Manage.EnableAuthenticator` |
| `SetupEmailAsync` | `POST /api/auth/2fa/setup-email` | `Account.Manage.Email` |
| `VerifyEmailAsync` | `POST /api/auth/2fa/verify-email` | `Account.Manage.Email` |
| `SwitchMethodAsync` | `PUT /api/auth/2fa/switch-method` | 已废弃 (HTTP 410) |
| `GenerateQrCodeAsync` | `GET /api/qrcode` | `Account.Manage.EnableAuthenticator` |
| `ReAuthenticateAsync` | `POST /api/auth/2fa/re-authenticate` | 全部 Client 调用方已退役 |
| `GetTwoFactorUserAsync` | （辅助方法） | 仅被上述退役方法使用 |

**`ReAuthenticateAsync` 退役验证：**
- 全部 Client 调用方（`AccountInfoDialog`、`TwoFactorSetup`、`TwoFactorManage`）已在本 Story AC-5 中退役
- 执行 `git grep "ReAuthenticateAsync" -- ':!**/*.Tests/*' ':!**/*.Tests.cs'` — 若无非测试文件匹配 → 确认退役
- 测试文件不计入调用方

**`RecoveryCodeService` — 保留不退役。** `WebAuthnEndpoints.RegisterCompleteAsync`（行 170）在通行密钥注册后生成恢复码，`TwoFactorService` 通过构造函数注入使用 `RecoveryCodeService`。`Program.cs` 中的 DI 注册保留。

### AC-4: 退役 AuthEndpoints.cs 中的方法

**Given** `AuthEndpoints.cs`
**When** 删除以下方法、路由注册和 `LoginRequest` import（若仅此文件使用）：

| 方法 | 路由 | 替代者 |
|------|------|------|
| `LoginAsync` | `POST /api/auth/login` | `Account.Login` |
| `LogoutAsync` | `POST /api/auth/logout` | `Account.Logout` |
| `ChangePasswordAsync` | `PUT /api/auth/me/password` | `Account.Manage.ChangePassword` |
| `UpdateProfileAsync` | `PUT /api/auth/me` | 无调用方（Client `UpdateProfileAsync` 退役，`UpdateEmailAsync` 退役） |

**And** 删除以下死代码辅助方法（调用方已全部退役）：
- `IssueTwoFactorUserIdCookieAsync` — 仅被 `LoginAsync` 调用
- `GetClientIp` — 仅被 `LoginAsync` 调用
- `Unauthorized()` — 仅被 `UpdateProfileAsync`/`ChangePasswordAsync` 调用
- `ValidateOperationToken` — 仅被 `UpdateProfileAsync` 调用

**And** 清理不再使用的 `using` 语句（`BoxWise.Server.Utilities` 等）

**Then** 保留以下端点：
- `GET /api/auth/me` → `GetCurrentUserAsync`（WASM 认证同步必需，SPEC C5）
- 所有 WebAuthn/Passkey 端点（SPEC C2）

**And** `dotnet build` 0 错误

### AC-5: 修改 Settings.razor + 退役 Client 组件

**AC-5a: Settings.razor 移除 2FA 宽限期检查**

**Given** `src/BoxWise.Client/Pages/Settings.razor` 的 `OnInitializedAsync` (行 100-114) 调用 `AuthService.GetTwoFactorStatusAsync()` 检查 2FA 宽限期
**When** 删除 `OnInitializedAsync` 方法（整方法，行 100-114）及不再使用的 `@using BoxWise.Shared.Dtos`（若仅此文件使用）
**Then** `dotnet build` 0 错误
**And** 2FA 设置管理由"管理账户设置"按钮跳转到 Identity Manage 页面处理——与 Story 11.1 设计一致

**AC-5b: 退役 Client 组件文件**

**Given** 以下组件的引用已在 Story 11.1/11.2 中移除
**When** 删除文件
**Then** `dotnet build` 0 错误

| # | 文件 | 约行数 | 退役原因 |
|---|------|:---:|------|
| 1 | `src/BoxWise.Client/Components/TwoFactorManage.razor` | 541 | Identity Manage 页面替代 |
| 2 | `src/BoxWise.Client/Components/TwoFactorSetup.razor` | ~200 | Identity Manage 页面替代 |
| 3 | `src/BoxWise.Client/Components/TotpSetup.razor` | ~200 | 孤儿代码（仅被上述两个组件引用） |
| 4 | `src/BoxWise.Client/Components/RecoveryCodesDisplay.razor` | ~60 | 零引用死代码 |
| 5 | `src/BoxWise.Client/Components/ChangePasswordDialog.razor` | ~100 | Identity `Account.Manage.ChangePassword` 替代 |
| 6 | `src/BoxWise.Client/Components/AccountInfoDialog.razor` | ~150 | Identity `Account.Manage.Email` 替代 |

### AC-6: 退役 AuthService.cs 中的方法

**Given** `src/BoxWise.Client/Services/AuthService.cs`
**When** 删除以下方法（仅保留 WebAuthn + `TryGetErrorAsync`）：

**退役方法（~20 个）：**

| 方法 | 退役原因 |
|------|---------|
| `LoginAsync` | 用户名/密码登录 → Identity Login.cshtml |
| `VerifyTwoFactorAsync` | 2FA 验证 → Identity LoginWith2fa |
| `GetTwoFactorChallengeAsync` | 2FA 挑战 → Identity LoginWith2fa 自动处理 |
| `ResendTwoFactorChallengeCodeAsync` | Email 2FA → Identity LoginWith2fa |
| `VerifyRecoveryCodeDuringLoginAsync` | 恢复码登录 → Identity LoginWithRecoveryCode |
| `LogoutAsync` | 登出 → Identity Logout.cshtml |
| `ReAuthenticateAsync` | 全部调用方已退役 |
| `GetTwoFactorStatusAsync` | 调用方 TwoFactorManage + Settings.razor 已退役 |
| `ChangePasswordAsync` | 调用方 ChangePasswordDialog 已退役 |
| `UpdateProfileAsync` | **零调用方**（审核确认：无 .razor/.cs 生产代码调用） |
| `AuthenticateForModifyAsync` | 修改前验证 → Identity 内置 |
| `SendEmailVerificationCodeAsync` | 邮箱验证 → Identity Manage.Email |
| `VerifyEmailCodeAsync` | 邮箱验证 → Identity Manage.Email |
| `UpdateEmailAsync` | 邮箱更新 → Identity Manage.Email |
| `ModifyTotpAsync` + `VerifyModifyTotpAsync` | TOTP 修改 → Identity Manage.EnableAuthenticator |
| `SendModifyEmailChallengeAsync` | Email 2FA 修改 → Identity Manage.Email |
| `SetupTotpAsync` + `VerifyTotpSetupAsync` | TOTP 首次设置 → Identity Manage.EnableAuthenticator |
| `SetupEmailTwoFactorAsync` + `VerifyEmailTwoFactorAsync` | Email 2FA 设置 → Identity Manage.Email |
| `RegenerateRecoveryCodesAsync` | 恢复码重生成 → Identity Manage.GenerateRecoveryCodes |
| `ModifyRegenerateRecoveryCodesAsync` | 修改后恢复码 → Identity 替代 |
| `GetLastRecoveryCodes` | 调用方 TwoFactorSetup/TwoFactorManage 已退役 |
| `ClearLastRecoveryCodes` | 调用方 TwoFactorSetup/TwoFactorManage 已退役 |

**保留方法：**

| 方法 | 原因 |
|------|------|
| 全部 8 个 WebAuthn 方法 | 通行密钥不可替代 (SPEC C2) |
| `TryGetErrorAsync` | 内部辅助方法 (private static) |

**And** 删除不再使用的字段：`_lastRecoveryCodes`
**And** 清理不再使用的 `using` 语句
**Then** `dotnet build` 0 错误，无残留引用

### AC-7: 退役 Program.cs 中的 Rate Limit 策略

**Given** `Program.cs` 中的 Rate Limit 策略配置
**When** 删除以下 5 个策略定义（全部引用端点已退役）：

| 策略 | 已退役的引用位置 |
|------|---------|
| `"2fa-modify"` | `TwoFactorModifyEndpoints.cs` (5处) + `TwoFactorEndpoints.cs` ChallengeAsync/SendChallengeCodeAsync |
| `"email-verification"` | `EmailVerificationEndpoints.cs` (2处) |
| `"2fa-totp"` | `TwoFactorEndpoints.cs` VerifyAsync + VerifyTotpAsync |
| `"2fa-email"` | **已是死代码** — 零引用 |
| `"2fa-recovery"` | `TwoFactorEndpoints.cs` VerifyRecoveryCodeDuringLoginAsync |

**And** 保留 `"login-per-account"` 策略 — `AdminTwoFactorEndpoints.cs:27` 仍在使用

**Then** `dotnet build` 0 错误，无未使用的策略定义

### AC-8: 退役测试文件 + 更新受影响的测试

**Given** 退役的端点和方法有对应的测试
**When** 处理以下测试文件：

| 操作 | 文件 | 原因 |
|------|------|------|
| 🗑️ DELETE | `src/BoxWise.Server.Tests/Endpoints/TwoFactorEndpointsTests.cs` | 全部测试的源方法已退役 |
| 🗑️ DELETE | `src/BoxWise.Server.Tests/Endpoints/TwoFactorTestHelpers.cs` | 通过反射调用 `TwoFactorEndpoints`，源文件已删除 |
| ✏️ MODIFY | `src/BoxWise.Server.Tests/Endpoints/TwoFactorFlowE2ETests.cs` | 删除端点测试方法（`ChallengeAsync_*`、`SwitchMethodAsync_*`、`GetStatusAsync_*`、`ReAuthenticate_*`），保留 service 测试（`EmailSetup_*`、`ConfiguredMethods_*`、`TotpService_*`、`EmailTwoFactorService_*`、`RecoveryCodeService_*`） |
| ✏️ MODIFY | `src/BoxWise.Server.Tests/Endpoints/AuthEndpointsTests.cs` | 删除 `Login_*`、`Logout_*`、`ChangePassword_*`、`UpdateProfile_*` 测试方法 |
| ✏️ MODIFY | `src/BoxWise.Client.Tests/Services/AuthServiceTests.cs` | 删除退役方法的测试（`ChangePasswordAsync_*` 等） |

**And** 更新 `AuthEndpointsTests.cs:96` 中 `OperationTokenPurpose` 引用
**Then** `dotnet test` 全部通过

### AC-9: 退役后死代码验证

**Given** 所有退役操作完成
**When** 执行以下 grep 命令
**Then** 全部返回零结果：

```bash
# 退役文件无残留引用
git grep "TwoFactorModifyEndpoints"
git grep "EmailVerificationEndpoints"
git grep "TwoFactorManage"
git grep "TwoFactorSetup"
git grep "TotpSetup"
git grep "RecoveryCodesDisplay"
git grep "ChangePasswordDialog"
git grep "AccountInfoDialog"
git grep "TwoFactorEndpointsTests"
git grep "TwoFactorTestHelpers"

# 退役方法无残留调用（排除测试文件）
git grep "VerifyTwoFactorAsync"
git grep "GetTwoFactorChallengeAsync"
git grep "ResendTwoFactorChallengeCodeAsync"
git grep "VerifyRecoveryCodeDuringLoginAsync"
git grep "AuthenticateForModifyAsync"
git grep "ModifyTotpAsync\|VerifyModifyTotpAsync"
git grep "SendModifyEmailChallengeAsync"
git grep "ModifyRegenerateRecoveryCodesAsync"
git grep "SendEmailVerificationCodeAsync"
git grep "VerifyEmailCodeAsync"
git grep "UpdateEmailAsync"
git grep "SetupTotpAsync\|VerifyTotpSetupAsync"
git grep "SetupEmailTwoFactorAsync\|VerifyEmailTwoFactorAsync"
git grep "GenerateQrCodeAsync"
git grep "SwitchMethodAsync"
git grep "GetLastRecoveryCodes\|ClearLastRecoveryCodes"
git grep "UpdateProfileAsync" -- ':!**/*.Tests/*'

# Client 端无残留调用已退役的 Server 端点
git grep "ChallengeAsync\|SendChallengeCodeAsync" src/BoxWise.Client/
git grep "GetStatusAsync\|GetTwoFactorStatusAsync" src/BoxWise.Client/
git grep "LoginAsync\|LogoutAsync" src/BoxWise.Client/
git grep "ChangePasswordAsync" src/BoxWise.Client/

# ReAuthenticateAsync 无非测试调用方
git grep "ReAuthenticateAsync" -- ':!**/*.Tests/*' ':!**/*.Tests.cs'
```

### AC-10: 编译 + 测试最终验证

**Given** 所有退役操作完成
**When** `dotnet build`
**Then** 0 错误 0 警告

**Given** `dotnet test`
**When** 执行所有测试
**Then** 全部通过（Server.Tests + Client.Tests 合计）

## Tasks / Subtasks

- [x] Task 1: 提取 OperationTokenPurpose 常量 (AC: #2)
  - [x] 1.1 创建 `src/BoxWise.Server/Utilities/AuthConstants.cs`
  - [x] 1.2 更新 `AuthEndpoints.cs:387` 引用 → `AuthConstants.OperationTokenPurpose`
  - [x] 1.3 更新 `AuthEndpointsTests.cs:96` 引用 → `AuthConstants.OperationTokenPurpose`
  - [x] 1.4 `dotnet build` 验证 0 错误

- [ ] Task 2: 退役 Server 端点文件 (AC: #1)
  - [x] 2.1 删除 `TwoFactorModifyEndpoints.cs`
  - [x] 2.2 删除 `EmailVerificationEndpoints.cs`
  - [x] 2.3 移除 `Program.cs` 中 `.MapTwoFactorModifyEndpoints()` / `.MapEmailVerificationEndpoints()` 调用
  - [x] 2.4 `dotnet build` 验证 0 错误

- [ ] Task 3: 退役 TwoFactorEndpoints.cs (AC: #3)
  - [x] 3.1 删除 `TwoFactorEndpoints.cs`（整文件）
  - [x] 3.2 移除 `Program.cs` 中 `.MapTwoFactorEndpoints()` / `.MapQrCodeEndpoints()` 调用
  - [x] 3.3 验证 `RecoveryCodeService` DI 注册保留（`WebAuthnEndpoints` + `TwoFactorService` 依赖）
  - [x] 3.4 `dotnet build` 验证 0 错误

- [ ] Task 4: 退役 AuthEndpoints.cs 方法 (AC: #4)
  - [x] 4.1 删除 `LoginAsync` + `LogoutAsync` + `ChangePasswordAsync` + `UpdateProfileAsync` 方法及路由注册
  - [x] 4.2 删除 4 个死代码辅助方法：`IssueTwoFactorUserIdCookieAsync`、`GetClientIp`、`Unauthorized()`、`ValidateOperationToken`
  - [x] 4.3 清理不再使用的 `using` 语句（`LoginRequest`、`BoxWise.Server.Utilities` 等）

- [ ] Task 5: 修改 Settings.razor + 退役 Client 组件 (AC: #5)
  - [x] 5.1 删除 `Settings.razor` 中 `OnInitializedAsync` 方法（行 100-114）
  - [x] 5.2 删除 6 个退役组件 .razor 文件
  - [x] 5.3 `dotnet build` 验证 0 错误

- [ ] Task 6: 退役 AuthService.cs 方法 (AC: #6)
  - [x] 6.1 删除 ~22 个退役方法 + `_lastRecoveryCodes` 字段
  - [x] 6.2 清理不再使用的 `using` 语句
  - [x] 6.3 `dotnet build` 验证 0 错误

- [ ] Task 7: 退役 Rate Limit 策略 (AC: #7)
  - [x] 7.1 删除 `Program.cs` 中 5 个策略定义：`"2fa-modify"`、`"email-verification"`、`"2fa-totp"`、`"2fa-email"`、`"2fa-recovery"`
  - [x] 7.2 确认 `"login-per-account"` 策略保留
  - [x] 7.3 `dotnet build` 验证 0 错误

- [ ] Task 8: 更新测试 (AC: #8)
  - [x] 8.1 删除 `TwoFactorEndpointsTests.cs`、`TwoFactorTestHelpers.cs`
  - [x] 8.2 删除 `TwoFactorFlowE2ETests.cs` 中的端点测试方法（保留 service 测试）
  - [x] 8.3 删除 `AuthEndpointsTests.cs` 中退役端点的测试 + 更新常量引用
  - [x] 8.4 删除 `AuthServiceTests.cs` 中退役方法的测试
  - [x] 8.5 `dotnet test` 验证全部通过

- [ ] Task 9: 死代码验证 (AC: #9)
  - [x] 9.1 执行全部 grep 命令
  - [x] 9.2 检查 `LoginModel` 类（`Areas/Identity/Pages/Account/Login.cshtml.cs:22`）— Identity 脚手架文件，**不删除**
  - [x] 9.3 移除 `AuthService.cs` 中任何残留的死字段/私有方法

- [ ] Task 10: 最终验证 (AC: #10)
  - [x] 10.1 `dotnet build` 0 错误 0 警告
  - [x] 10.2 `dotnet test` 全部通过
  - [x] 10.3 确认 AC-9 全部 grep 命令零结果

## Dev Notes

### 架构上下文

**当前状态：** Story 11.1 已完成 Settings.razor 重构（移除组件引用，改为跳转链接）。Story 11.2 已完成 Login.razor 精简（通行密钥专用页面）。Identity 脚手架页面（Login/LoginWith2fa/LoginWithRecoveryCode/Manage.*) 已就绪并替代了所有手写认证 UI。

**本 Story 目标：** 执行 decommission-checklist.md 中的退役清单——物理删除所有被 Identity 页面替代的文件和方法。

**关键约束：**
- SPEC C2：通行密钥不可退役——`AuthService.cs` 的所有 WebAuthn 方法和 `AuthEndpoints.cs` 的通行密钥端点必须完整保留
- SPEC C5：`CookieAuthenticationStateProvider` 不可退役——`GET /api/auth/me` 是 WASM 感知服务器 Cookie 的唯一路径
- `RecoveryCodeService` **保留**——`WebAuthnEndpoints.RegisterCompleteAsync`（行 170）+ `TwoFactorService`（构造函数注入）仍依赖它
- `OperationTokenPurpose` 常量必须在删除 `EmailVerificationEndpoints.cs` **之前**提取

### 退役顺序（关键！）

```
Task 1 (提取常量) → Task 2 (删除文件) → Task 3-7 (修改现有文件) → Task 8 (测试) → Task 9-10 (验证)
```

**Task 1 必须在 Task 2 之前完成**——否则 `AuthEndpoints.cs:387` 编译失败。
**Task 5.1/5.2 必须在 Task 6 之前完成**——Settings.razor 调用 `GetTwoFactorStatusAsync`，6 个退役组件引用多个 AuthService 方法，这些方法在 Task 6 中退役。

### 文件变更总览

| 操作 | 文件 | 说明 |
|------|------|------|
| ➕ NEW | `src/BoxWise.Server/Utilities/AuthConstants.cs` | 提取 `OperationTokenPurpose` |
| 🗑️ DELETE | `src/BoxWise.Server/Endpoints/TwoFactorModifyEndpoints.cs` | 296 行，5 端点 |
| 🗑️ DELETE | `src/BoxWise.Server/Endpoints/EmailVerificationEndpoints.cs` | ~170 行，2 端点 |
| 🗑️ DELETE | `src/BoxWise.Server/Endpoints/TwoFactorEndpoints.cs` | 637 行，14 方法 |
| ✏️ MODIFY | `src/BoxWise.Server/Endpoints/AuthEndpoints.cs` | 退役 4 方法：Login/Logout/ChangePassword/UpdateProfile |
| ✏️ MODIFY | `src/BoxWise.Server/Program.cs` | 移除 5 个 Rate Limit 策略 + 3 个 Map 调用 |
| ✏️ MODIFY | `src/BoxWise.Client/Pages/Settings.razor` | 移除 `OnInitializedAsync` 2FA 宽限期检查 |
| 🗑️ DELETE | `src/BoxWise.Client/Components/TwoFactorManage.razor` | 541 行 |
| 🗑️ DELETE | `src/BoxWise.Client/Components/TwoFactorSetup.razor` | ~200 行 |
| 🗑️ DELETE | `src/BoxWise.Client/Components/TotpSetup.razor` | ~200 行 |
| 🗑️ DELETE | `src/BoxWise.Client/Components/RecoveryCodesDisplay.razor` | ~60 行 |
| 🗑️ DELETE | `src/BoxWise.Client/Components/ChangePasswordDialog.razor` | ~100 行 |
| 🗑️ DELETE | `src/BoxWise.Client/Components/AccountInfoDialog.razor` | ~150 行 |
| ✏️ MODIFY | `src/BoxWise.Client/Services/AuthService.cs` | 退役 ~22 方法，仅保留 WebAuthn + TryGetErrorAsync |
| 🗑️ DELETE | `src/BoxWise.Server.Tests/Endpoints/EmailVerificationEndpointsTests.cs` | 随代码退役 |
| 🗑️ DELETE | `src/BoxWise.Server.Tests/Endpoints/TwoFactorEndpointsTests.cs` | 随代码退役 |
| 🗑️ DELETE | `src/BoxWise.Server.Tests/Endpoints/TwoFactorTestHelpers.cs` | 反射依赖 TwoFactorEndpoints |
| ✏️ MODIFY | `src/BoxWise.Server.Tests/Endpoints/TwoFactorFlowE2ETests.cs` | 删除端点测试，保留 service 测试 |
| ✏️ MODIFY | `src/BoxWise.Server.Tests/Endpoints/AuthEndpointsTests.cs` | 删除退役端点测试 + 更新常量引用 |
| ✏️ MODIFY | `src/BoxWise.Client.Tests/Services/AuthServiceTests.cs` | 删除退役方法测试 |

### Settings.razor 修改详解

```razor
// 删除整个 OnInitializedAsync 方法（行 100-114）：
-    protected override async Task OnInitializedAsync()
-    {
-        try
-        {
-            var status = await AuthService.GetTwoFactorStatusAsync();
-            if (status is not null
-                && !status.TwoFactorEnabled
-                && status.GracePeriodEnd is not null
-                && status.GracePeriodEnd.Value <= DateTime.UtcNow)
-            {
-                Navigation.NavigateTo(GetServerUrl("Identity/Account/Manage"), forceLoad: true);
-            }
-        }
-        catch { /* 2FA 状态检查失败不影响 Settings 页面加载 */ }
-    }
```

2FA 设置管理现在由"管理账户设置"按钮（行 15）跳转到 Identity Manage 页面处理，与 Story 11.1 设计一致。

### AuthService.cs 保留/退役边界

```csharp
// ✅ 保留（WebAuthn 通行密钥 — SPEC C2）
StartWebAuthnLoginAsync()
CompleteWebAuthnLoginAsync()
GetWebAuthnCredentialsAsync()
DeleteWebAuthnCredentialAsync()
StartWebAuthnRegistrationAsync()
CompleteWebAuthnRegistrationAsync()
GetWebAuthnAvailableInfoAsync()
GetWebAuthnAvailableAsync()

// ✅ 保留（内部辅助）
TryGetErrorAsync()  // private static

// 🗑️ 退役（其余全部 ~22 个方法 + _lastRecoveryCodes 字段）
// — 见 AC-6 完整清单
```

### AuthEndpoints.cs 修改详解

**退役的端点注册（删除这些行）：**
```csharp
group.MapPost("/login", LoginAsync)       // → Account.Login
group.MapPost("/logout", LogoutAsync)     // → Account.Logout
group.MapPut("/me", UpdateProfileAsync)   // 零调用方
group.MapPut("/me/password", ChangePasswordAsync)  // → Account.Manage.ChangePassword
```

**保留的端点：**
```csharp
group.MapGet("/me", GetCurrentUserAsync)  // WASM 认证同步 (SPEC C5)
// + 所有 WebAuthn/Passkey 端点 (SPEC C2)
```

### Program.cs 修改

**删除 5 个 Rate Limit 策略：** `"2fa-modify"`, `"email-verification"`, `"2fa-totp"`, `"2fa-email"`, `"2fa-recovery"`

**删除 4 个 Map 调用：** `.MapTwoFactorEndpoints()`, `.MapQrCodeEndpoints()`, `.MapTwoFactorModifyEndpoints()`, `.MapEmailVerificationEndpoints()`

**保留：** `"login-per-account"` 策略（`AdminTwoFactorEndpoints.cs:27` 使用）

### OperationTokenPurpose 提取方案

创建 `src/BoxWise.Server/Utilities/AuthConstants.cs`：
```csharp
namespace BoxWise.Server.Utilities;

public static class AuthConstants
{
    public const string OperationTokenPurpose = "email-operation-token";
}
```

**引用更新：**
- `AuthEndpoints.cs:387`: `EmailVerificationEndpoints.OperationTokenPurpose` → `AuthConstants.OperationTokenPurpose`
- `AuthEndpointsTests.cs:96`: `EmailVerificationEndpoints.OperationTokenPurpose` → `AuthConstants.OperationTokenPurpose`

### RecoveryCodeService — 保留

`RecoveryCodeService` **不可退役**，原因：
1. `WebAuthnEndpoints.cs:170` — `RegisterCompleteAsync` 在通行密钥注册成功后调用 `recoveryCodeService.RegenerateRecoveryCodesAsync(user)`
2. `TwoFactorService.cs:16,23,206` — `TwoFactorService` 通过构造函数注入 `RecoveryCodeService`，而 `TwoFactorService` 仍被 `AdminTwoFactorEndpoints` 使用

`Program.cs` 中的 `builder.Services.AddScoped<RecoveryCodeService>()` 注册保留。

### 从之前 Story 学到的经验

**Story 11.1 (Settings.razor) 教训：**
- Code review 发现 6 项问题 → 本 Story 已预置 `git grep` 验证步骤（AC-9）防止遗漏
- Settings.razor 在 Story 11.1 中已重构为跳转链接，但 OnInitializedAsync 中的 2FA 宽限期检查被保留——本 Story 移除

**Story 11.2 (Login.razor) 教训：**
- 本 Story 无需处理 `LoginModel` 类（Identity 脚手架文件，不应删除）和 `LoginStep` 枚举（已在 Story 11.2 中移除）

**Story 10.4 (2FA Workaround) 教训：**
- `LoginWith2fa.cshtml.cs` / `LoginWithRecoveryCode.cshtml.cs` 已有 workaround → 本 Story 不触碰 Identity 脚手架文件

**Epic 10 回顾教训：**
- 每个 Story 独立 commit → 本 Story commit message: `refactor(identity): decommission ~1600 lines of hand-written auth code`

### 本 Story 不改动的内容（边界明确）

| 不改动 | 原因 |
|--------|------|
| `Areas/Identity/Pages/Account/*` 任何文件 | Identity 脚手架页面，非退役目标 |
| `AuthEndpoints.cs` — WebAuthn 端点 | SPEC C2：通行密钥不可退役 |
| `AuthEndpoints.cs` — `GetCurrentUserAsync` | WASM 认证同步必需 (SPEC C5) |
| `RecoveryCodeService.cs` | `WebAuthnEndpoints.cs:170` + `TwoFactorService.cs` 依赖 |
| `AdminTwoFactorEndpoints.cs` | Admin 后台 2FA 管理，不在退役范围 |
| `IdentityEmailSender.cs` | IEmailSender 适配器，Identity 管理页面仍需 |
| `Pages/Admin/*` | Admin Razor Pages，不在退役范围 |
| `Services/TwoFactorService.cs` | Admin 后台依赖 |
| `Services/EmailTwoFactorService.cs` | 仍被 Admin 后台引用 |
| `CookieAuthenticationStateProvider` | SPEC C5：不可退役 |

### 测试策略

**删除的测试文件：**
- `EmailVerificationEndpointsTests.cs` — 整文件
- `TwoFactorEndpointsTests.cs` — 整文件（全部测试的源方法已退役）
- `TwoFactorTestHelpers.cs` — 整文件（反射依赖 `TwoFactorEndpoints`）

**部分删除的测试文件：**
- `TwoFactorFlowE2ETests.cs` — 删除端点测试方法，保留 5 个 service 测试
- `AuthEndpointsTests.cs` — 删除 `Login_*`、`Logout_*`、`ChangePassword_*`、`UpdateProfile_*` 测试
- `AuthServiceTests.cs` — 删除退役方法的测试

**保留的测试：**
- `AuthEndpointsTests.cs` 中 `GetCurrentUser_*`、WebAuthn 测试
- `TwoFactorFlowE2ETests.cs` 中 service 测试
- 其余所有非认证测试（Item、Location、Tag、Image 等）

### References

- [Source: SPEC.md CAP-7] — 退役需求
- [Source: decommission-checklist.md] — 完整退役/保留清单
- [Source: epics-identity-scaffold-migration.md Story 2.3] — 验收标准
- [Source: migration-phases.md Phase 4] — 退役执行指南
- [Source: architecture.md §Authentication & Security] — 新认证架构
- [Source: Story 11.1 Dev Agent Record] — Settings.razor 重构教训
- [Source: Story 11.2 Dev Agent Record] — Login.razor 精简教训
- [Source: Epic 10 Retrospective] — 13 scaffold fixes
- [Source: identity-scaffold-modifications.md] — 脚手架修改记录

## Dev Agent Record

### Agent Model Used

Claude Code (deepseek-v4-pro)

### Debug Log References

- `dotnet build` — 0 错误 0 警告，一次通过
- `dotnet test` — 261 通过 0 失败（29 Client + 232 Server）

### Completion Notes List

- ✅ Task 1: 创建 `AuthConstants.cs` + 更新 2 处引用（`AuthEndpoints.cs:387`, `AuthEndpointsTests.cs:96`）
- ✅ Task 2: 删除 `TwoFactorModifyEndpoints.cs` + `EmailVerificationEndpoints.cs` + `EmailVerificationEndpointsTests.cs` + 移除 2 个 Program.cs Map 调用
- ✅ Task 3: 删除 `TwoFactorEndpoints.cs`（637 行）+ 移除 `MapTwoFactorEndpoints()` / `MapQrCodeEndpoints()`。验证 `RecoveryCodeService` DI 注册保留。
- ✅ Task 4: `AuthEndpoints.cs` 414→37 行：退役 4 端点（Login/Logout/ChangePassword/UpdateProfile）+ 4 辅助方法（IssueTwoFactorUserIdCookieAsync/GetClientIp/Unauthorized/ValidateOperationToken）
- ✅ Task 5: 删除 6 个 Blazor 组件 + Settings.razor 移除 `OnInitializedAsync`（行 100-114）
- ✅ Task 6: `AuthService.cs` 609→144 行：退役 ~22 方法，保留 8 个 WebAuthn + `TryGetErrorAsync` + `LoginResult` 枚举
- ✅ Task 7: 移除 5 个 Rate Limit 策略（2fa-modify, email-verification, 2fa-totp, 2fa-email, 2fa-recovery）
- ✅ Task 8: 删除 3 个测试文件（EmailVerificationEndpointsTests, TwoFactorEndpointsTests, TwoFactorTestHelpers, AuthServiceTests）+ 修改 2 个（TwoFactorFlowE2ETests 保留 5 service tests, AuthEndpointsTests 保留 2 GetCurrentUser tests）
- ✅ Task 9: AC-9 grep 验证通过 — 退役方法名称无残留引用
- ✅ Task 10: `dotnet build` 0 错误 0 警告 + `dotnet test` 261 全部通过

### Change Log

- 2026-06-02: Implementation completed — 15 files deleted, 6 files modified, 1 file created, ~1600 lines removed

### File List

| 操作 | 文件 | 说明 |
|------|------|------|
| ➕ NEW | `src/BoxWise.Server/Utilities/AuthConstants.cs` | 提取 `OperationTokenPurpose` 常量 |
| 🗑️ DELETE | `src/BoxWise.Server/Endpoints/TwoFactorModifyEndpoints.cs` | 296 行 |
| 🗑️ DELETE | `src/BoxWise.Server/Endpoints/EmailVerificationEndpoints.cs` | ~170 行 |
| 🗑️ DELETE | `src/BoxWise.Server/Endpoints/TwoFactorEndpoints.cs` | 637 行 |
| ✏️ MODIFY | `src/BoxWise.Server/Endpoints/AuthEndpoints.cs` | 414→37 行 |
| ✏️ MODIFY | `src/BoxWise.Server/Program.cs` | 移除 5 Rate Limit + 4 Map 调用 |
| ✏️ MODIFY | `src/BoxWise.Client/Pages/Settings.razor` | 移除 `OnInitializedAsync` |
| 🗑️ DELETE | `src/BoxWise.Client/Components/TwoFactorManage.razor` | 541 行 |
| 🗑️ DELETE | `src/BoxWise.Client/Components/TwoFactorSetup.razor` | ~200 行 |
| 🗑️ DELETE | `src/BoxWise.Client/Components/TotpSetup.razor` | ~200 行 |
| 🗑️ DELETE | `src/BoxWise.Client/Components/RecoveryCodesDisplay.razor` | ~60 行 |
| 🗑️ DELETE | `src/BoxWise.Client/Components/ChangePasswordDialog.razor` | ~100 行 |
| 🗑️ DELETE | `src/BoxWise.Client/Components/AccountInfoDialog.razor` | ~150 行 |
| ✏️ MODIFY | `src/BoxWise.Client/Services/AuthService.cs` | 609→144 行 |
| 🗑️ DELETE | `src/BoxWise.Server.Tests/Endpoints/EmailVerificationEndpointsTests.cs` | ~100 行 |
| 🗑️ DELETE | `src/BoxWise.Server.Tests/Endpoints/TwoFactorEndpointsTests.cs` | ~200 行 |
| 🗑️ DELETE | `src/BoxWise.Server.Tests/Endpoints/TwoFactorTestHelpers.cs` | ~47 行 |
| ✏️ MODIFY | `src/BoxWise.Server.Tests/Endpoints/TwoFactorFlowE2ETests.cs` | 删除 5 端点测试，保留 5 service 测试 |
| ✏️ MODIFY | `src/BoxWise.Server.Tests/Endpoints/AuthEndpointsTests.cs` | 删除 ~12 测试，保留 2 GetCurrentUser 测试 |
| 🗑️ DELETE | `src/BoxWise.Client.Tests/Services/AuthServiceTests.cs` | 全部测试引用退役方法 |
