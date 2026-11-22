# 2FA 设置修改 — 流程

> Companion to SPEC-2fa-modify-settings. Documents API and UI flows.

## API 端点

### 新增

```
POST /api/auth/2fa/modify/authenticate
```

**用途：** 修改模式的身份验证。用户使用已配置的任一 2FA 方法验证身份。

**请求：** `VerifyTwoFactorRequest`（复用现有 DTO）
```json
{ "code": "123456", "method": "TOTP", "token": null }
```

**响应：** `200 { "sessionToken": "..." }` — modify session token（purpose=`"2fa-modify"`，15 分钟有效）

**路由逻辑：**
- `method=TOTP` → `VerifyTotpChallengeAsync(user, code)`（复用）
- `method=Email` → 先调用 `modify/send-code` 获取 token，再传入 token + code 验证
- `method=RecoveryCode` → `ValidateRecoveryCodeAsync(user, code)`（新增方法，仅校验哈希不删除恢复码）

**错误：** `400` 验证码无效 / `401` 未登录

**注意：** 恢复码用于修改验证时不消耗——与登录时使用恢复码的核选项行为不同。需在 `RecoveryCodeService` 中新增 `ValidateRecoveryCodeAsync` 方法（仅校验不销毁）。

---

```
POST /api/auth/2fa/modify/send-code
```

**用途：** 修改模式身份验证前，向用户已配置的邮箱发送验证码。

**请求：** 空 body（用户身份从 Application Cookie 获取）

**流程：**
1. 从 `HttpContext.User` 获取当前登录用户
2. 校验用户已配置 Email 2FA（`ConfiguredMethods.HasFlag(Email)`）且 `EmailForTwoFactor` 非空
3. 调用 `EmailTwoFactorService.GenerateCode(user.Id, user.EmailForTwoFactor)` 生成验证码
4. 调用 `SendVerificationEmailAsync` 发送
5. 返回 `200 { "token": "<email-token>" }` — 自包含 Data Protection 令牌

**错误：** `400` 用户未配置 Email / `422` 发送失败

---

```
POST /api/auth/2fa/modify/email
```

**用途：** 修改 Email 2FA 的邮箱地址。

**Headers：** `X-Session-Token: <modify-session-token>`

**请求：**
```json
{ "email": "new@example.com" }
```

**流程：**
1. 校验 modify session token（purpose=`"2fa-modify"`）
2. 校验用户已配置 Email 2FA（`ConfiguredMethods.HasFlag(Email)`）
3. 生成验证码发送到新邮箱（复用 `EmailTwoFactorService`）
4. 返回 `200 { "token": "<verify-token>" }` — 新邮箱的验证令牌

**错误：** `400` session token 无效/用户未配置 Email / `422` 邮箱格式无效或发送失败

---

```
POST /api/auth/2fa/modify/email/verify
```

**用途：** 验证新邮箱地址并更新。

**Headers：** `X-Session-Token: <modify-session-token>`

**请求：**
```json
{ "code": "123456", "token": "<verify-token>" }
```

**流程：**
1. 校验 modify session token
2. 调用 `VerifyCode(userId, newEmail, code, token)`
3. 更新 `user.EmailForTwoFactor = newEmail`
4. 返回 `200 OK`

**错误：** `400` 验证码无效 / `401` session token 无效

---

```
POST /api/auth/2fa/modify/totp
```

**用途：** 重置 TOTP 密钥（第一步：生成新密钥+二维码）。旧密钥保持有效直到 verify 确认。

**Headers：** `X-Session-Token: <modify-session-token>`

**流程：**
1. 校验 modify session token
2. 校验用户已配置 TOTP（`ConfiguredMethods.HasFlag(TOTP)`）
3. 生成新密钥和二维码 URI
4. 将新密钥加密后存入临时字段 `PendingTotpSecretKey`（Data Protection 加密，与 `TotpSecretKey` 同级）
5. 返回 `200 { "secretKey": "...", "qrCodeUri": "..." }`

**注意：** 旧密钥 `TotpSecretKey` 保持不变，用户仍可用旧 TOTP 码登录。新密钥暂存于 `PendingTotpSecretKey`，仅当 verify 成功后才提升为正式密钥。如果用户中断操作或 session token 过期，旧密钥继续有效，下次 modify/totp 调用会覆盖 `PendingTotpSecretKey`。

---

```
POST /api/auth/2fa/modify/totp/verify
```

**用途：** 验证新 TOTP 密钥（第二步：确认用户已保存新密钥）。

**Headers：** `X-Session-Token: <modify-session-token>`

**请求：**
```json
{ "code": "123456" }
```

**流程：**
1. 校验 modify session token
2. 用 `PendingTotpSecretKey` 验证 TOTP 码
3. 验证通过后：`TotpSecretKey = PendingTotpSecretKey`；`PendingTotpSecretKey = null`
4. 返回 `200 OK`

**错误：** `400` 验证码无效（旧密钥仍有效，用户可重试或重新调用 modify/totp 生成新的待定密钥）

---

## UI 流程

### TwoFactorSetup.razor — ChooseMethod 步骤变更

当前：已配置方法显示绿色"已启用" chip（不可交互）
改为：已配置方法显示"已启用" chip + "修改"按钮

```
┌──────────────────────────────┐
│ ✅ 双因素认证已启用            │
│                              │
│ 📱 TOTP 验证器  ✅已启用 [修改]│  ← 新增"修改"按钮
│ 📧 邮箱验证码    ✅已启用 [修改]│  ← 新增"修改"按钮
│                              │
│ （未配置的方法显示"设置"按钮）  │
└──────────────────────────────┘
```

### 修改 TOTP 流程（新增子组件或步骤）

```
ChooseMethod → ModifyAuthenticate → ModifyTotp → ModifyTotpVerify → Complete

ModifyTotp:
  - 显示新 QR 码 + 密钥
  - 用户扫描后输入新验证码
  - "确认重置"按钮

ModifyTotpVerify:
  - 成功后显示 "TOTP 已重置"
  - 旧密钥立即失效
```

### 修改 Email 流程（新增子组件或步骤）

```
ChooseMethod → ModifyAuthenticate → ModifyEmail → ModifyEmailVerify → Complete

ModifyEmail:
  - 输入新邮箱地址
  - "发送验证码"按钮
  - 输入收到的验证码

ModifyEmailVerify:
  - 成功后显示 "邮箱已更新"
```

### ModifyAuthenticate（通用身份验证步骤）

```
显示：请先验证身份以修改 2FA 设置
  - 根据已配置方法显示对应的验证选项
  - 输入验证码/恢复码
  - "验证"按钮
```

---

```
POST /api/auth/2fa/modify/recovery/regenerate
```

**用途：** 重新生成恢复码（旧码全部失效）。

**Headers：** `X-Session-Token: <modify-session-token>`

**流程：**
1. 校验 modify session token（purpose=`"2fa-modify"`）
2. 调用 `RecoveryCodeService.RegenerateRecoveryCodesAsync(user)` 生成新码
3. 返回 `200 { "codes": ["...", ...] }` — 8 个新恢复码明文

**错误：** `401` session token 无效

**注意：** 此端点替代现有 `POST /api/auth/2fa/recovery/regenerate`（该端点无 modify session token 门控）。旧端点保留但不再在前端使用，或改为内部转发到新端点。

---

## 数据模型变更

### AppUser 新增字段

| 字段 | 类型 | 用途 |
|------|------|------|
| `PendingTotpSecretKey` | `string?` | TOTP 重置流程中的待定密钥（DP 加密）。非 null 表示有未确认的 TOTP 重置。verify 成功后提升为 `TotpSecretKey` 并清空；modify/totp 再次调用时覆盖。 |

此字段需要 EF Core 迁移。

---

## Session Token 扩展

现有 `GenerateSessionToken(userId, clientIp)` 签名不变。扩展内部 payload：

```
旧: {userId}|{expires}|2fa-setup|{clientIp}
新: {userId}|{expires}|{purpose}|{clientIp}
```

`purpose` 取值：`2fa-setup`（现有）/ `2fa-modify`（新增）

`ValidateSessionToken` 增加可选 `expectedPurpose` 参数（默认 `"2fa-setup"` 保持向后兼容）。修改端点传入 `"2fa-modify"`。
