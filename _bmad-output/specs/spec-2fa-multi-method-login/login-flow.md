# Login Flow Changes

## 当前流程（buggy）

```
Login → POST /api/auth/login
  → ChallengeAsync: 按 TwoFactorMethod == X 返回单一方法
  → Login.razor: 无方法选择，始终调 VerifyTwoFactorAsync(code) 不带 token
  → POST /api/auth/2fa/verify
    → VerifyAsync: 按 TwoFactorMethod == Email ? email路径 : totp路径
```

问题：
1. Challenge 只返回一个方法（哪个字段匹配返回哪个）
2. 前端不保存 emailToken
3. Verify 路由依赖 TwoFactorMethod 单值

## 目标流程

```
Login → POST /api/auth/login
  → ChallengeAsync: 返回 ConfiguredMethods 中所有已配置的方法
    { allowedMethods: ["TOTP", "Email"], emailToken: "..." }
  → Login.razor: 
    - 若单方法 → 直接显示对应输入框（向后兼容）
    - 若多方法 → 显示方法选择器 + 对应输入框
    - Email 被选中 → 保存 emailToken，传给 Verify
  → POST /api/auth/2fa/verify
    body: { method: "TOTP"|"Email", code: "...", token: "..."|null }
    → VerifyAsync: 按 request.Method 路由
```

## API 变更明细

### 1. ChallengeAsync (`POST /api/auth/2fa/challenge`)

**Response 不变**（`TwoFactorChallengeResponse` 已支持返回多项）：
```csharp
public record TwoFactorChallengeResponse(List<string> AllowedMethods, string? Token = null);
```

**逻辑变更**：
```diff
- if (user.TwoFactorMethod == TwoFactorMethod.TOTP)
-     methods.Add("TOTP");
- if (user.TwoFactorMethod == TwoFactorMethod.Email && ...)
-     methods.Add("Email");

+ if (user.ConfiguredMethods.HasFlag(TwoFactorMethod.TOTP))
+     methods.Add("TOTP");
+ if (user.ConfiguredMethods.HasFlag(TwoFactorMethod.Email))
+ {
+     // 防御：EmailForTwoFactor 为 null 的损坏状态
+     if (string.IsNullOrEmpty(user.EmailForTwoFactor))
+     {
+         user.ConfiguredMethods &= ~TwoFactorMethod.Email;
+         await userManager.UpdateAsync(user);
+     }
+     else
+     {
+         methods.Add("Email");
+         var (code, token) = emailTwoFactorService.GenerateCode(user.Id, user.EmailForTwoFactor);
+         emailToken = token;
+         // 发送邮件（fire-and-forget，后续专项优化）
+         // 注意：即使用户最终选择 TOTP，邮件也会发送。这是已知权衡——
+         // 用户可能改变主意选择 Email，提前发送可减少等待。
+         _ = emailTwoFactorService.SendVerificationEmailAsync(...);
+     }
+ }
```

### 2. VerifyTwoFactorRequest（DTO 变更）

```diff
- public record VerifyTwoFactorRequest(string Code, string? Token = null);
+ public record VerifyTwoFactorRequest(string Code, string? Token = null, string? Method = null);
```

`Method` 可选：
- `"TOTP"` → 走 TOTP 验证
- `"Email"` → 走邮箱验证码验证（要求 `Token` 非空）
- `null` → 回退兼容（保证旧客户端不崩溃）：
  - 若仅配置单一方法 → 走该方法（Email 需 Token 非空，旧客户端不传则报错——此场景为既有 bug，升级前后一致）
  - 若多方法 → 降级到 TOTP 验证（宽容策略，不返回 400）

### 3. VerifyAsync (`POST /api/auth/2fa/verify`)

```diff
- if (user.TwoFactorMethod == TwoFactorMethod.Email && ...)
-     valid = emailTwoFactorService.VerifyCode(...);
- else
-     valid = await twoFactorService.VerifyTotpChallengeAsync(user, request.Code);
+ switch (request.Method)
+ {
+     case "TOTP":
+         if (!user.ConfiguredMethods.HasFlag(TwoFactorMethod.TOTP))
+             return TypedResults.ValidationProblem(..., "该方法未配置");
+         valid = await twoFactorService.VerifyTotpChallengeAsync(user, request.Code);
+         break;
+     case "Email":
+         if (!user.ConfiguredMethods.HasFlag(TwoFactorMethod.Email))
+             return TypedResults.ValidationProblem(..., "该方法未配置");
+         valid = emailTwoFactorService.VerifyCode(user.Id, user.EmailForTwoFactor, request.Code, request.Token);
+         break;
+     default:
+         // 回退兼容：单方法用户
+         if (user.ConfiguredMethods.HasFlag(TwoFactorMethod.Email) && !user.ConfiguredMethods.HasFlag(TwoFactorMethod.TOTP))
+             valid = emailTwoFactorService.VerifyCode(...);
+         else
+             valid = await twoFactorService.VerifyTotpChallengeAsync(user, request.Code);
+         break;
+ }
```

### 4. ChallengeAsync 防御性检查

若检测到 `ConfiguredMethods == None` 但 `TwoFactorEnabled == true`（不应存在的损坏状态），自动修复：

```csharp
if (user.ConfiguredMethods == TwoFactorMethod.None && user.TwoFactorEnabled)
{
    user.TwoFactorEnabled = false;
    await userManager.UpdateAsync(user);
    // 签发完整 Cookie（等同于无 2FA 状态），用户可正常登录后在 Settings 重新设置
    await signInManager.SignInAsync(user, isPersistent: true);
    // 返回成功登录响应（非 TwoFactorChallengeResponse），前端直接跳转首页
    return TypedResults.Ok(new LoginResponse(user.UserName, ..., RequiresTwoFactor: false, ...));
}
```

### 5. SendChallengeCodeAsync（重新发送端点改造）

原 `POST /api/auth/2fa/send-challenge-code` 端点当前无返回值。改为返回新 token，避免前端二次调用 Challenge 端点导致双重发信：

```diff
- // 当前：fire-and-forget，不返回 token
- _ = emailTwoFactorService.SendVerificationEmailAsync(...);
- return TypedResults.Ok();

+ // 改造：生成新 code + token，发送邮件，返回新 token
+ var (code, newToken) = emailTwoFactorService.GenerateCode(user.Id, user.EmailForTwoFactor);
+ _ = emailTwoFactorService.SendVerificationEmailAsync(user.EmailForTwoFactor, code, user.UserName);
+ return TypedResults.Ok(new SendChallengeCodeResponse(newToken));
```

新增 DTO：`public record SendChallengeCodeResponse(string Token);`

### 6. SwitchMethodAsync（废弃）

在 `[Flags]` 多方法模型下，"切换到某方法"语义不明确。该端点不再使用，由独立的 setup/verify 端点通过 `|=` 添加方法替代。

```diff
- // 整个 PUT /api/auth/2fa/switch-method 端点废弃
- // 路由保留但返回 410 Gone，或直接从路由注册中移除
```

## 前端变更

### Login.razor

1. 从 `ChallengeResponse` 获取 `AllowedMethods` 和 `Token`
2. 若 `AllowedMethods.Count == 1` → 直接显示对应输入框（向后兼容）
3. 若 `AllowedMethods.Count > 1` → 显示方法选择器（MudRadio / MudToggleGroup）
4. Email 被选中时显示"验证码已发送至您的邮箱"提示 + 重新发送链接
5. 页面底部始终显示"使用恢复码登录"链接
6. `HandleTwoFactorVerify` 传递 `method` 参数和 `emailToken`

**伪代码**：
```csharp
private string? _selectedMethod;  // "TOTP" or "Email"
private string? _emailToken;
private bool _showRecoveryCode;

private async Task LoadTwoFactorChallengeAsync()
{
    var response = await AuthService.GetTwoFactorChallengeAsync();
    _allowedMethods = response.AllowedMethods;
    _emailToken = response.Token;
    _selectedMethod = _allowedMethods.FirstOrDefault();
}

private async Task HandleTwoFactorVerify()
{
    var result = await AuthService.VerifyTwoFactorAsync(
        _totpCode,
        token: _selectedMethod == "Email" ? _emailToken : null,
        method: _selectedMethod);
    ...
}

// 重新发送邮箱验证码（send-challenge-code 端点自身生成新 code+token 并发送邮件，
// 不额外调用 Challenge 端点，避免双重发信）
private async Task ResendEmailCode()
{
    // TODO: ResendTwoFactorChallengeCodeAsync 应返回新 emailToken
    // 当前方案：send-challenge-code 响应中携带新 token，前端直接使用
    var newToken = await AuthService.ResendTwoFactorChallengeCodeAsync();
    if (newToken is not null)
        _emailToken = newToken;
}

// 切换到恢复码输入
private void ShowRecoveryCodeInput()
{
    _showRecoveryCode = true;
}

private async Task HandleRecoveryCodeVerify()
{
    var result = await AuthService.VerifyRecoveryCodeDuringLoginAsync(_recoveryCode);
    ...
}
```

**UI 结构**：
```
┌─────────────────────────────┐
│ 方法选择器（仅多方法时显示）   │
│  ○ TOTP 验证码  ● Email 验证码│
├─────────────────────────────┤
│ [Email 模式]                 │
│ 验证码已发送至 admin@...      │
│ [重新发送]（链接按钮）         │
├─────────────────────────────┤
│ 验证码输入框                  │
│ [验证] 按钮                   │
├─────────────────────────────┤
│ 使用恢复码登录（链接）         │
│ [返回] 按钮                   │
└─────────────────────────────┘
```

### AuthService.cs

```diff
- public async Task<LoginResult> VerifyTwoFactorAsync(string code, string? token = null)
+ public async Task<LoginResult> VerifyTwoFactorAsync(string code, string? token = null, string? method = null)
  {
-     var response = await _http.PostAsJsonAsync("api/auth/2fa/verify", new VerifyTwoFactorRequest(code, token));
+     var response = await _http.PostAsJsonAsync("api/auth/2fa/verify", new VerifyTwoFactorRequest(code, token, method));
  }

+ // 新增：重新发送邮箱验证码，返回新 emailToken
+ public async Task<string?> ResendTwoFactorChallengeCodeAsync()
+ {
+     var response = await _http.PostAsync("api/auth/2fa/send-challenge-code", null);
+     if (response.IsSuccessStatusCode)
+     {
+         var result = await response.Content.ReadFromJsonAsync<SendChallengeCodeResponse>();
+         return result?.Token;
+     }
+     return null;
+ }

+ // 新增 DTO
+ public record SendChallengeCodeResponse(string Token);
```

## 错误处理

| 场景 | 返回 |
|------|------|
| 指定了未配置的方法 | `400: "该方法未配置"` |
| 多方法用户未指定 method | 降级到 TOTP 验证（不返回错误） |
| TOTP 验证码无效 | `400: "验证码无效"` |
| Email 缺少 token | `400: "缺少验证令牌"` |
| Email 验证码无效/过期 | `400: "验证码无效或已过期"` |
| `ConfiguredMethods=None` + `TwoFactorEnabled=true` | 自动修复：清除 `TwoFactorEnabled`，签发完整 Cookie |
