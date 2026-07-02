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
| `docs/identity-scaffold-modifications.md` | 修改 | 记录新增修改项 |

## 数据流

```
Login.cshtml
  └─ "忘记密码？" → ForgotPassword.cshtml [AllowAnonymous]
       ├─ 输入用户名 → OnPost
       ├─ FindByNameAsync 查用户
       ├─ 用户不存在/无邮箱 → 统一跳转 ForgotPasswordConfirmation（防枚举）
       ├─ 生成 ResetToken（1小时有效）
       ├─ 构造回调 URL: /Identity/Account/ResetPassword?userId=xxx&code=xxx
       ├─ SendEmailAsync(email, "BoxWise - 密码重置", htmlBody)
       └─ 跳转 ForgotPasswordConfirmation

邮件链接 → ResetPassword.cshtml [AllowAnonymous]
  ├─ OnGet: 解析 userId + code，查用户，显示脱敏邮箱
  ├─ OnPost: ResetPasswordAsync(user, code, newPassword)
  ├─ 成功 → ResetPasswordConfirmation（"点击此处登录"）
  └─ 失败 → 显示错误（token 过期/无效）

ResetPasswordConfirmation.html
  └─ 点击 → Login.cshtml
```

## ForgotPassword.cshtml

- 标题："忘记密码？"
- 副标题："请输入您的用户名。"
- 输入框：Username（`autocomplete="username"`，`aria-required="true"`）
- 按钮："发送重置邮件"
- 底部链接："返回登录"
- 样式：`form-floating mb-3` + `w-100 btn btn-lg btn-primary`（与 Login.cshtml 一致）

## ForgotPassword.cshtml.cs

- 注入 `UserManager<AppUser>` + `IEmailSender`
- `InputModel.Username`（`[Required]`）
- `OnPostAsync`：
  1. 校验 ModelState
  2. `FindByNameAsync(Input.Username)` 查用户
  3. 用户 null 或邮箱为空 → 跳转 ForgotPasswordConfirmation
  4. `GeneratePasswordResetTokenAsync(user)` → Base64UrlEncode
  5. `Url.Page("/Account/ResetPassword", values: new { area = "Identity", userId = user.Id, code })`
  6. `SendEmailAsync(user.Email, "BoxWise - 密码重置", htmlBody)` → 统一跳转 Confirmation（不暴露邮件发送结果）

## ResetPassword.cshtml

- 标题："重置密码"
- 脱敏邮箱提示："正在为 xxx@xxx.com 重置密码"
- Hidden: `Input.UserId`, `Input.Code`
- 输入框：`Input.Password`（`autocomplete="new-password"`）
- 输入框：`Input.ConfirmPassword`（`[Compare("Password")]`）
- 按钮："重置密码"
- 密码规则标注：最小长度 8 位

## ResetPassword.cshtml.cs

- 注入 `UserManager<AppUser>`
- `InputModel`：`UserId` (Hidden), `Code` (Hidden), `Password` (`[Required][StringLength(100, MinimumLength=8)]`), `ConfirmPassword` (`[Compare]`)
- `OnGet(string userId, string code)`：
  1. userId 或 code 为空 → BadRequest
  2. `FindByIdAsync(userId)` → null 则跳转 ResetPasswordConfirmation
  3. 解码 code，获取脱敏邮箱
  4. 返回 Page
- `OnPostAsync`：
  1. 校验 ModelState
  2. `FindByIdAsync(Input.UserId)` → null 则跳转 Confirmation
  3. `ResetPasswordAsync(user, Input.Code, Input.Password)`
     - 成功 → 跳转 ResetPasswordConfirmation
     - 失败 → 显示错误到 ModelState

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

- ForgotPassword 统一响应（无论用户是否存在、是否有邮箱、邮件是否发出）
- ResetPassword token 1 小时过期
- 重置成功后不自动登录，需用户手动登录（确保记住新密码）
- Token 一次性使用（ASP.NET Core Identity 内置行为）
- 两个页面均为 `[AllowAnonymous]`（未登录用户可访问）
- SMTP 未配置时静默降级（IdentityEmailSender 已有此行为）

## 不用改动的部分

- `IdentityEmailSender`：已实现 `IEmailSender`，无需修改
- `Program.cs`：`IEmailSender` 已注册为 `IdentityEmailSender`
- 密码策略：复用现有 `NoNumericOnlyValidator` + `CommonPasswordValidator`
- Bootstrap 布局：`_Layout.cshtml` 已覆盖

## 脚手架修改记录

实施完成后在 `docs/identity-scaffold-modifications.md` 新增修改项 #33-35：

- #33：`ForgotPassword` 页面 — 基于模板，Email → Username 适配，中文化
- #34：`ResetPassword` 页面 — 基于模板，Email → userId 适配，中文化
- #35：`Login.cshtml` — 底部加"忘记密码？"链接
