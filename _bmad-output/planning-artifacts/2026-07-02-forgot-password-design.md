# 忘记密码功能设计

**日期：** 2026-07-02
**范围：** BoxWise 用户自助密码重置
**状态：** 已确认

## 背景

当前系统用户忘记密码后无法自助重置——必须联系管理员通过 `/admin/{id}/password` 手动重置。SMTP 基础设施已就绪（MailKit + SMTP 管理后台），但未与密码重置流程连接。

`ForgotPassword` 和 `ResetPassword` 页面在初始脚手架中被排除（v1 未实现），现需补全。

## 方案

延续现有模式：Server 端 Identity Razor Pages（方案 A），与 `ChangePassword`、`Login`、2FA 页面保持一致。

## 涉及文件

| 文件 | 操作 | 说明 |
|------|------|------|
| `Areas/Identity/Pages/Account/ForgotPassword.cshtml` | 新增 | 用户名输入，发送重置邮件 |
| `Areas/Identity/Pages/Account/ForgotPassword.cshtml.cs` | 新增 | 基于脚手架模板，Email → Username 适配 |
| `Areas/Identity/Pages/Account/ForgotPasswordConfirmation.cshtml` | 新增 | 纯提示页："请检查邮箱" |
| `Areas/Identity/Pages/Account/ForgotPasswordConfirmation.cshtml.cs` | 新增 | 无后端逻辑，PageModel 空壳 |
| `Areas/Identity/Pages/Account/ResetPassword.cshtml` | 新增 | 新密码输入，userId + code 通过 URL 传递 |
| `Areas/Identity/Pages/Account/ResetPassword.cshtml.cs` | 新增 | 基于脚手架模板，用 FindByIdAsync 替代 FindByEmailAsync |
| `Areas/Identity/Pages/Account/ResetPasswordConfirmation.cshtml` | 新增 | 纯提示页："密码已重置，点击登录" |
| `Areas/Identity/Pages/Account/ResetPasswordConfirmation.cshtml.cs` | 新增 | 无后端逻辑，PageModel 空壳 |
| `Areas/Identity/Pages/Account/Login.cshtml` | 修改 | 底部加"忘记密码？"链接 |
| `Program.cs` | 修改 | 配置 `DataProtectionTokenProviderOptions.TokenLifespan = 1h` |
| `docs/identity-scaffold-modifications.md` | 修改 | 记录新增修改项 |

## 数据流

```
Login.cshtml
  └─ "忘记密码？" → ForgotPassword.cshtml [AllowAnonymous]
       ├─ 输入用户名 → OnPost
       ├─ FindByNameAsync 查用户
       ├─ 用户不存在/无邮箱/EmailConfirmed=false → 统一跳转 Confirmation（防枚举）
       ├─ GeneratePasswordResetTokenAsync → UTF8 Bytes → Base64UrlEncode
       ├─ 构造回调 URL（绝对路径: protocol: Request.Scheme）
       ├─ SendEmailAsync(email, "BoxWise - 密码重置", htmlBody)
       └─ 跳转 ForgotPasswordConfirmation [AllowAnonymous]

邮件链接 → ResetPassword.cshtml [AllowAnonymous]
  ├─ OnGet: 解析 userId + code，Base64UrlDecode → UTF8 string，查用户，显示脱敏邮箱
  ├─ OnPost: ResetPasswordAsync(user, code, newPassword)
  ├─ 成功 → 发送"密码已重置"安全通知邮件 → ResetPasswordConfirmation [AllowAnonymous]
  └─ 失败 → 显示错误（token 过期/无效）

ResetPasswordConfirmation
  └─ 点击 asp-page="./Login" → Login.cshtml
```

## ForgotPassword.cshtml

- 标题："忘记密码？"
- 副标题："请输入您的用户名。"
- 输入框：Username（`autocomplete="username"`，`aria-required="true"`）
- 按钮："发送重置邮件"
- 底部链接："返回登录"
- 样式：`form-floating mb-3` + `w-100 btn btn-lg btn-primary`（与 Login.cshtml 一致）

## ForgotPassword.cshtml.cs

- `[AllowAnonymous]`
- `[EnableRateLimiting("forgot-password")]`（Razor Page 通过属性启用，非 `.RequireRateLimiting` 扩展方法）
- 注入 `UserManager<AppUser>` + `IEmailSender`
- `InputModel.Username`（`[Required]`）
- `OnPostAsync`：
  1. 校验 ModelState
  2. `FindByNameAsync(Input.Username)` 查用户
  3. 用户 null / 邮箱为空 / `EmailConfirmed == false` → 跳转 ForgotPasswordConfirmation（统一响应，防枚举）
  4. `var code = await _userManager.GeneratePasswordResetTokenAsync(user)`
  5. `code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(code))`（双重编码，避免 URL 中 `+` `/` `=` 字符问题）
  6. `var callbackUrl = Url.Page("/Account/ResetPassword", pageHandler: null, values: new { area = "Identity", userId = user.Id, code }, protocol: Request.Scheme)`（绝对 URL，邮件客户端可点击）
  7. `SendEmailAsync(user.Email, "BoxWise - 密码重置", htmlBody)` → 统一跳转 Confirmation（不暴露邮件发送结果）

## ResetPassword.cshtml

- 标题："重置密码"
- 脱敏邮箱提示："正在为 xxx@xxx.com 重置密码"
- Hidden: `Input.UserId`, `Input.Code`
- 隐藏输入框：`autocomplete="username"`（配合密码管理器"保存新密码"提示）
- 输入框：`Input.Password`（`autocomplete="new-password"`）
- 输入框：`Input.ConfirmPassword`（`[Compare("Password")]`）
- 按钮："重置密码"
- 密码规则标注：最小长度 8 位

## ResetPassword.cshtml.cs

- `[AllowAnonymous]`
- 注入 `UserManager<AppUser>` + `IEmailSender`
- `InputModel`：`UserId` (Hidden), `Code` (Hidden), `Password` (`[Required][StringLength(100, MinimumLength=8)]`), `ConfirmPassword` (`[Compare]`)
- `OnGet(string userId, string code)`：
  1. userId 或 code 为空 → BadRequest
  2. `FindByIdAsync(userId)` → null 则跳转 ResetPasswordConfirmation（防暴露）
  3. `Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(code))` 解码
  4. 获取脱敏邮箱显示，返回 Page
- `OnPostAsync`：
  1. 校验 ModelState
  2. `FindByIdAsync(Input.UserId)` → null 则跳转 Confirmation
  3. `ResetPasswordAsync(user, Input.Code, Input.Password)`
     - 成功 → 发送"您的密码已被重置"安全通知邮件 → 跳转 ResetPasswordConfirmation
     - 失败 → 显示错误到 ModelState
- **不会**自动禁用 2FA（`SecurityStamp` 更新会踢出已登录会话，但 2FA 状态保留）

## 邮件模板

- 主题："BoxWise - 密码重置"
- 正文：HTML，包含重置链接按钮，提示 1 小时有效期 + 非本人操作忽略
- 语言：中文

## Login.cshtml 改动

在通行密钥链接下方新增：

```html
<div class="mt-2">
    <a asp-page="./ForgotPassword">忘记密码？</a>
</div>
```

## 安全措施

- ForgotPassword 统一响应（无论用户是否存在、是否有邮箱、邮箱是否确认、邮件是否发出）
- ResetPassword token 1 小时过期（`DataProtectionTokenProviderOptions.TokenLifespan`）
- ForgotPassword 速率限制：同一 IP 60 秒内最多 1 次请求（防邮件轰炸和用户名枚举暴力扫描）
- 邮箱未确认的用户不发送重置邮件（防未确认邮箱绕过注册验证）
- Token 一次性使用（ASP.NET Core Identity 内置行为）
- 令牌经过双重编码传输（UTF8 → Base64UrlEncode），URL 安全
- 邮件链接使用绝对路径（`protocol: Request.Scheme`），确保邮件客户端可点击
- 重置成功后不自动登录，需用户手动登录（确保记住新密码）
- 重置成功发送安全通知邮件（用户察觉未授权操作）
- 所有 4 个页面均为 `[AllowAnonymous]`
- SMTP 未配置时静默降级（IdentityEmailSender 已有此行为）
- 令牌通过 URL query string 传输是 Identity 标准模式，1 小时有效期缓解日志泄露风险
- 若用户重置密码时持有有效登录 Cookie，SecurityStamp 更新后该 Cookie 在下次请求时失效（约数秒），用户将被重定向至登录页——这是预期行为

## 与 2FA 的交互

- `ResetPasswordAsync` 会更新 `SecurityStamp`，踢出所有已登录会话
- **不会**自动禁用 2FA —— `TwoFactorEnabled` 和 `ConfiguredMethods` 保持不变
- 如果用户邮箱也被攻破，2FA 仍提供额外保护层（攻击者需持有 TOTP 或通行密钥）

## 还未确定的优化

以下改进点已识别，可在后续迭代中实现：

- 在 ForgotPasswordConfirmation 页面添加 SMTP 故障兜底提示（"如未收到邮件，检查垃圾箱或联系管理员"）
- 邮件发送失败后的用户友好提示（而非静默跳过）

## Program.cs 改动

```csharp
// 配置密码重置令牌有效期为 1 小时（默认 24 小时）
builder.Services.Configure<DataProtectionTokenProviderOptions>(options =>
    options.TokenLifespan = TimeSpan.FromHours(1));
```

在 `AddRateLimiter` 配置中新增 ForgotPassword 策略：

```csharp
options.AddFixedWindowLimiter(policyName: "forgot-password", config =>
{
    config.PermitLimit = builder.Configuration.GetValue("RateLimit:ForgotPasswordPermitLimit", 1);
    config.Window = TimeSpan.FromSeconds(
        builder.Configuration.GetValue("RateLimit:ForgotPasswordWindowSeconds", 60));
    config.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
    config.QueueLimit = 0;
});
```

**注意：** ForgotPassword 是 Razor Page（非 Minimal API），速率限制通过 `[EnableRateLimiting("forgot-password")]` 属性在 PageModel 上启用，而非 `.RequireRateLimiting()` 扩展方法。

## ForgotPasswordConfirmation.cshtml / .cs

- 两个确认页面（ForgotPasswordConfirmation、ResetPasswordConfirmation）均为 `[AllowAnonymous]`
- ForgotPasswordConfirmation：提示"请检查您的邮箱，点击邮件中的链接重置密码。如未收到邮件，请检查垃圾邮件文件夹。"
- ResetPasswordConfirmation：提示"密码已重置。"，链接 `<a asp-page="./Login">点击此处登录</a>`
- 两个 `.cs` 均为 PageModel 空壳（仅 `[AllowAnonymous]` 属性）

## 不用改动的部分

- `IdentityEmailSender`：已实现 `IEmailSender`，无需修改
- 密码策略：复用现有 `NoNumericOnlyValidator` + `CommonPasswordValidator`
- Bootstrap 布局：`_Layout.cshtml` 已覆盖

## 脚手架修改记录

实施完成后在 `docs/identity-scaffold-modifications.md` 新增修改项 #33-36：

- #33：`ForgotPassword` 页面 — 基于模板，Email → Username 适配，中文化
- #34：`ResetPassword` 页面 — 基于模板，Email → userId 适配，中文化
- #35：`Login.cshtml` — 底部加"忘记密码？"链接
- #36：`Program.cs` — 配置 `TokenLifespan = 1h` + ForgotPassword 速率限制
