# Investigation: 2FA 验证码无效 — TOTP 与 Email 双因子冲突

## Hand-off Brief

1. **What happened.** 用户设置邮箱 2FA 后，`VerifyEmailAsync` 端点（`TwoFactorEndpoints.cs:461-463`）因 `TwoFactorMethod != None` 清除了 `TotpSecretKey`，导致 TOTP 验证码永远失败。数据库确认 `TwoFactorMethod=2(Email)`, `TotpSecretKey=NULL`。（Confidence: High）
2. **Where the case stands.** 根因已确认。涉及 5 层问题：数据清除、单一方法架构、登录路由、前端缺失邮件验证、邮件发送 fire-and-forget。
3. **What's needed next.** 重构 `AppUser` 支持多方法并存，修复 `ChallengeAsync`/`VerifyAsync` 登录路由，增强前端支持方法选择，修复 `VerifyEmailAsync` 不清除无关密钥。

## Case Info

| Field            | Value                                                                      |
| ---------------- | -------------------------------------------------------------------------- |
| Ticket           | N/A                                                                        |
| Date opened      | 2026-05-30                                                                 |
| Status           | Concluded (root cause confirmed)                                           |
| System           | Windows 11, .NET 10, ASP.NET Core, SQLite                                  |
| Evidence sources | TwoFactorEndpoints.cs, TwoFactorService.cs, AuthEndpoints.cs, Login.razor, AuthService.cs, AppUser.cs, TwoFactorMethod.cs, smtp-config.json, AspNetUsers 表 |

## Problem Statement

用户已设置 TOTP 生成器 2FA，之后设置了邮箱 2FA。登录时输入 TOTP 动态验证码，服务端返回"验证码无效，请重试"。用户也未收到邮箱验证码（SMTP 配置已确认）。

## Evidence Inventory

| Source   | Status    | Notes     |
| -------- | --------- | --------- |
| 数据库 (SQLite) — AspNetUsers | Available | `TwoFactorMethod=2(Email)`, `TotpSecretKey=NULL`, `EmailForTwoFactor=admin@elvisw.com` |
| AppUser.cs | Available | `TwoFactorMethod` 是单值枚举（None/TOTP/Email/WebAuthn），不支持多方法并存 |
| TwoFactorMethod.cs | Available | 枚举定义 None=0, TOTP=1, Email=2, WebAuthn=3 |
| TwoFactorService.cs | Available | `VerifyTotpChallengeAsync` 仅检查 `TotpSecretKey`，不关心 `TwoFactorMethod` |
| TwoFactorEndpoints.cs | Available | `VerifyEmailAsync:461-463` 清除 TOTP 密钥；`ChallengeAsync:203-213` 仅返回当前单一方法；`VerifyAsync:252-264` 按 `TwoFactorMethod` 单选路由 |
| AuthEndpoints.cs | Available | `LoginAsync:94-98` 发布 `TwoFactorUserId` Cookie 后返回 `RequiresTwoFactor` |
| EmailTwoFactorService.cs | Available | 邮件发送逻辑正常，`IsSmtpConfigured()` 委托 SmtpConfigurationService |
| SmtpConfigurationService.cs | Available | SMTP 配置已加载（smtp.zoho.com:587），密码 DP 加密 |
| smtp-config.json | Available | `host: smtp.zoho.com, port: 587, username: admin@elvisw.com` |
| AuthService.cs (Client) | Available | `VerifyTwoFactorAsync` 的 `token` 参数有默认值 null，`GetTwoFactorChallengeAsync` 返回的 Token 未被前端保存 |
| Login.razor (Client) | Available | 2FA 页面无方法选择 UI，始终调 `VerifyTwoFactorAsync(_totpCode)` 无 token |

## Investigation Backlog

| # | Path to Explore | Priority | Status | Notes     |
| - | --------------- | -------- | ------ | --------- |
| 1 | 数据库 2FA 字段状态 | High | Done | 确认 TOTP 密钥为空，方法为 Email |
| 2 | TwoFactorService 验证逻辑 | High | Done | `VerifyTotpChallengeAsync` 仅校验密钥，不校验方法 |
| 3 | AuthEndpoints 登录流程 | High | Done | `LoginAsync` 签发 TwoFactorUserId Cookie → 前端调 Challenge → Verify |
| 4 | SMTP 邮件发送 | Medium | Done | 配置正确，fire-and-forget 导致发送失败无反馈 |
| 5 | 前端 2FA 验证页面 | Medium | Done | 无方法选择，不传 emailToken |

## Timeline of Events

| Time        | Event               | Source                | Confidence            |
| ----------- | ------------------- | --------------------- | --------------------- |
| 2026-05-30 09:41 | 用户完成邮箱 2FA 设置 | `TwoFactorSetupCompletedAt` in DB | Confirmed |
| 2026-05-30 09:41 | `VerifyEmailAsync` 清除 `TotpSecretKey` | `TwoFactorEndpoints.cs:461-463` | Confirmed |
| 2026-05-30 | 用户尝试 TOTP 登录 → "验证码无效" | 用户报告 + 代码分析 | Confirmed |
| 2026-05-30 17:11 | 数据库快照确认 | sqlite3 query | Confirmed |

## Confirmed Findings

### Finding 1: 数据库确认 TotpSecretKey 已被清除

**Evidence:** `sqlite3 data\boxwise.db` → `TwoFactorMethod=2`, `TotpSecretKey=NULL`

**Detail:** admin 用户的 `TwoFactorMethod` 为 2（Email），`TotpSecretKey` 列为空。`EmailForTwoFactor` 为 `admin@elvisw.com`。TOTP 密钥已不可恢复地被删除。

### Finding 2: VerifyEmailAsync 是直接原因

**Evidence:** `src/BoxWise.Server/Endpoints/TwoFactorEndpoints.cs:460-464`

```csharp
// 如果从另一种方法切换，清除旧密钥
if (user.TwoFactorMethod != TwoFactorMethod.None)
{
    user.TotpSecretKey = null;
}
user.TwoFactorEnabled = true;
user.TwoFactorMethod = TwoFactorMethod.Email;
```

**Detail:** 当用户从 TOTP 切换到 Email 时，`TwoFactorMethod != None`（为 TOTP），代码**有意**清除了 `TotpSecretKey`。注释写的是"清除旧密钥"，但这导致 TOTP 完全不可用。

### Finding 3: AppUser.TwoFactorMethod 是单值，不支持多方法并存

**Evidence:** `src/BoxWise.Server/Models/AppUser.cs:7` — `public TwoFactorMethod TwoFactorMethod { get; set; } = TwoFactorMethod.None;`
`src/BoxWise.Server/Models/TwoFactorMethod.cs` — `enum TwoFactorMethod { None = 0, TOTP = 1, Email = 2, WebAuthn = 3 }`

**Detail:** 架构设计为单一激活方法，而非多选。用户不能同时启用 TOTP + Email。

### Finding 4: ChallengeAsync 仅返回当前单一方法

**Evidence:** `src/BoxWise.Server/Endpoints/TwoFactorEndpoints.cs:203-213`

```csharp
if (user.TwoFactorMethod == TwoFactorMethod.TOTP)
    methods.Add("TOTP");

if (user.TwoFactorMethod == TwoFactorMethod.Email && ...)
    methods.Add("Email");
```

**Detail:** 使用 `==` 严格相等检查，而非检查所有已配置的方法。当 Email 为当前方法时，TOTP 从列表中消失。

### Finding 5: VerifyAsync 按 TwoFactorMethod 单选路由

**Evidence:** `src/BoxWise.Server/Endpoints/TwoFactorEndpoints.cs:252-264`

```csharp
if (user.TwoFactorMethod == TwoFactorMethod.Email && !string.IsNullOrEmpty(user.EmailForTwoFactor))
{
    // 仅邮箱验证路径
    valid = emailTwoFactorService.VerifyCode(...);
}
else
{
    // TOTP 路径
    valid = await twoFactorService.VerifyTotpChallengeAsync(user, request.Code);
}
```

**Detail:** 当 `TwoFactorMethod == Email` 时，永远走 Email 验证路径。即使 TOTP 密钥存在也不会尝试。

### Finding 6: 前端 Login.razor 不支持 Email 验证码

**Evidence:** `src/BoxWise.Client/Pages/Login.razor:167` — `AuthService.VerifyTwoFactorAsync(_totpCode)` 不传 token
`src/BoxWise.Client/Services/AuthService.cs:56` — `VerifyTwoFactorAsync(string code, string? token = null)` token 默认 null

**Detail:** 前端：
1. 不保存 `ChallengeResponse.Token`
2. 不区分 TOTP/Email UI
3. 始终只传 code 不传 token
4. 即使后端返回了 Email 方法，前端也无法正确处理

### Finding 7: SMTP 邮件发送是 fire-and-forget

**Evidence:** `src/BoxWise.Server/Endpoints/TwoFactorEndpoints.cs:211-212`

```csharp
_ = emailTwoFactorService.SendVerificationEmailAsync(user.EmailForTwoFactor, code, user.UserName)
    .ContinueWith(t => { if (t.IsFaulted) { /* SMTP 不可用，用户需手动输入 TOTP */ } });
```

**Detail:** 邮件发送不等待结果，失败时用户无感知。SMTP 配置本身正确（Zoho smtp.zoho.com:587），但发送失败被静默吞掉。

## Deduced Conclusions

### Deduction 1: 邮箱 2FA 设置后 TOTP 密钥被有意清除，用户被锁死在单一 Email 方法

**Based on:** Findings 1, 2, 3

**Reasoning:** `VerifyEmailAsync` 设计为"切换"方法（switch），不是"添加"方法（add）。一旦用户验证了邮箱 2FA，TOTP 密钥被删除，`TwoFactorMethod` 变为 Email。此后一切登录尝试都走 Email 路径。

**Conclusion:** 这不是一个简单的 bug，而是一个设计层面的缺陷——系统只支持单一 2FA 方法，切换方法会丢失旧配置。

### Deduction 2: 用户无法通过 Email 完成登录，因为前端也不支持

**Based on:** Findings 5, 6, 7

**Reasoning:** 即使 SMTP 能正常发送邮件（Zoho 配置正确），前端登录页也没有保存 `emailToken` 的代码，`VerifyTwoFactorAsync` 调用不带 token，导致后端验证必然失败。

**Conclusion:** 用户目前**完全无法登录**——既不能通过 TOTP（密钥已清除），也不能通过 Email（前端不支持 + 邮件可能未发送）。

### Deduction 3: 用户可以用恢复码登录作为应急方案

**Based on:** Findings — `VerifyRecoveryCodeDuringLoginAsync` 端点存在（`TwoFactorEndpoints.cs:489`），前端 `AuthService.VerifyRecoveryCodeDuringLoginAsync` 已实现

**Conclusion:** 恢复码是当前唯一的应急登录方式，但登录页 UI 也没有暴露恢复码入口。

## Hypothesized Paths

### Hypothesis 1: 邮箱 2FA 设置覆盖了 TOTP 密钥

**Status:** **Confirmed**

**Theory:** `SetupEmailAsync` 或相关流程在设置邮箱 2FA 时，错误地清除了之前设置的 TOTP 密钥。

**Resolution:** `TwoFactorEndpoints.cs:VerifyEmailAsync:461-463` — 当 `user.TwoFactorMethod != TwoFactorMethod.None`（即之前有 TOTP），清除 `user.TotpSecretKey = null`。数据库确认密钥为空。

### Hypothesis 2: 验证方法选择逻辑有缺陷

**Status:** **Confirmed**

**Theory:** 系统在验证 2FA 码时，根据 `TwoFactorMethod` 选择验证方式，导致 TOTP 验证码被当作 Email 验证码处理。

**Resolution:** `VerifyAsync` 使用 `if (user.TwoFactorMethod == TwoFactorMethod.Email)` 作为路由条件，单一路由。且 `TwoFactorMethod` 已被改为 Email。

## Missing Evidence

| Gap              | Impact                               | How to Obtain   |
| ---------------- | ------------------------------------ | --------------- |
| 邮件是否实际发送成功 | 确认 SMTP 工作正常（vs 静默失败） | 检查服务端日志中的 SendVerificationEmailAsync 的 LogInformation/LogError |
| 用户是否有恢复码 | 应急登录方案 | 询问用户是否保存了设置 2FA 时生成的恢复码 |

## Source Code Trace

| Element       | Detail                                      |
| ------------- | ------------------------------------------- |
| Error origin  | `TwoFactorEndpoints.cs:461-463` (VerifyEmailAsync 清除 TOTP 密钥) |
| Trigger       | 用户在设置页面完成邮箱 2FA 验证（POST /api/auth/2fa/verify-email） |
| Condition     | `user.TwoFactorMethod != TwoFactorMethod.None` — 之前已设置 TOTP |
| Route (setup) | Settings.razor → `SetupEmailTwoFactorAsync` → POST /api/auth/2fa/setup-email → `SetupEmailAsync` → POST /api/auth/2fa/verify-email → `VerifyEmailAsync` |
| Route (login) | Login.razor → `LoginAsync` → POST /api/auth/login → `ChallengeAsync` → POST /api/auth/2fa/challenge → `VerifyTwoFactorAsync` → POST /api/auth/2fa/verify → `VerifyAsync` |
| Related files | `AppUser.cs`, `TwoFactorMethod.cs`, `TwoFactorService.cs`, `EmailTwoFactorService.cs`, `AuthService.cs`, `Login.razor`, `AuthEndpoints.cs` |

## Conclusion

**Confidence:** **High**

**根因已确认为 5 层连锁问题：**

1. **数据层** — `VerifyEmailAsync` (`TwoFactorEndpoints.cs:461-463`) 在切换到 Email 方法时清除了 `TotpSecretKey`，导致 TOTP 密钥永久丢失
2. **模型层** — `AppUser.TwoFactorMethod` 是单值枚举，不支持"同时配置 TOTP + Email"的设计
3. **路由层** — `ChallengeAsync` 仅返回当前单一方法，`VerifyAsync` 按单一路由分发
4. **前端层** — `Login.razor` 无方法选择 UI，`AuthService.VerifyTwoFactorAsync` 不传 `emailToken`
5. **可靠性层** — SMTP 发送是 fire-and-forget，失败无用户反馈

**用户当前状态：完全被锁在登录外** — TOTP 密钥已删除，Email 验证前端不支持，邮件发送可能静默失败。

## Recommended Next Steps

### Fix direction

需要以下改动（按优先级）：

1. **模型重构** — 将 `TwoFactorMethod` 改为 flags/多字段，或新增 `List<TwoFactorMethod> EnabledMethods`，支持多方法并存
2. **修复 VerifyEmailAsync** — 不再清除不相关的密钥（`TotpSecretKey` 不应在设置 Email 时被删除）
3. **重构 ChallengeAsync** — 返回所有已配置的方法，而非仅当前单一方法
4. **重构 VerifyAsync** — 根据请求中显式指定的方法（或前端选择）分发，而非仅依赖 `TwoFactorMethod` 字段
5. **前端改造** — Login.razor 显示方法选择器（TOTP 输入 / 邮箱验证码输入 / 恢复码入口），保存并传递 emailToken
6. **邮箱发送改为 await** — 不再 fire-and-forget，失败时返回明确错误给用户

### Diagnostic

如需进一步验证 SMTP 是否正常：检查服务端日志中 `"验证码邮件已发送到"` 或 `"发送验证码邮件到 ... 失败"` 记录。

### Emergency workaround

用户在修复完成前可使用**恢复码登录**（如果在设置 2FA 时保存了恢复码）。恢复码端点 `POST /api/auth/2fa/recovery/verify` 已实现，但前端登录页未暴露入口。可临时通过直接 API 调用登录。

## Reproduction Plan

1. 创建测试用户 → 设置 TOTP 2FA → 验证 TOTP 登录成功
2. 同一用户 → 设置 Email 2FA → 验证 TOTP 登录失败（"验证码无效"）
3. 查询数据库 → 确认 `TotpSecretKey` 为 NULL，`TwoFactorMethod` 为 Email
4. 尝试 Email 登录 → 前端不支持，验证失败

## Side Findings

- **SwitchMethodAsync 对 Email 硬编码返回 false** — `TwoFactorService.cs:177-178`: `if (newMethod == TwoFactorMethod.Email) return Task.FromResult(false); // 未实现`。`/switch-method` 端点实际上不支持切换到 Email，但 `SetupEmailAsync` 绕过了这个限制。
- **VerifyEmailAsync 不清除 WebAuthn 凭证** — 仅清除 TOTP 密钥，WebAuthn 凭据不受影响。这种不一致可能在未来引起类似问题。
- **前端未暴露恢复码登录入口** — `VerifyRecoveryCodeDuringLoginAsync` 后端已实现，但 Login.razor 没有"使用恢复码"按钮。
- **smtp-config.json 中 fromName 为 null** — 可能导致某些 SMTP 服务器拒收（使用 `MailboxAddress("BoxWise", ...)` 作为回退，但从配置加载时为 null 会走回退逻辑）。
