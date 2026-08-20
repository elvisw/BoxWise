# Identity 脚手架文件修改清单

> **基线：** Story 10.1 commit `392229d` — 脚手架生成 17 个 Identity Razor Pages
> **规则：** 每次修改 `Areas/Identity/` 下任何文件，必须在此文件中记录。

## 修改记录

| # | 文件 | 修改 | 原因 | Story | 上游更新时 |
|---|------|------|------|-------|-----------|
| 1 | `Login.cshtml` | `Input.Email` → `Input.Username`，汉化标签，移除 Register/ForgotPassword/ResendEmailConfirmation 死链接，移除 External Login 区域 | BoxWise 用用户名登录，非邮箱；未生成的页面产生死链接 | 10（回顾修复） | 重新适配 |
| 2 | `Login.cshtml.cs` | 移除 `[EmailAddress]` 校验，属性 `Email` → `Username`，`PasswordSignInAsync` 调用更新 | 同上 | 10（回顾修复） | 重新适配 |
| 3 | `LoginWith2fa.cshtml.cs` | 恢复 `_signInManager.GetTwoFactorAuthenticationUserAsync()` 原生调用；移除 workaround 私有方法 `GetTwoFactorUserAsync()` | dotnet/aspnetcore#66929 在 .NET 10.0.11 上验证不复现（E2E 测试 + 人工验证通过），workaround 安全移除 | 回归修复 (2026-08-20) | 保留 |
| 4 | `LoginWithRecoveryCode.cshtml.cs` | 同上：恢复 `GetTwoFactorAuthenticationUserAsync()` 原生调用；移除 workaround 私有方法 `GetTwoFactorUserAsync()` | 同上 | 回归修复 (2026-08-20) | 保留 |
| 5 | `Logout.cshtml.cs` | 添加 `OnGet` handler（独立实现，不委托给 `OnPost`），重定向 `/Identity/Account/Login`；移除未使用的 `using Microsoft.AspNetCore.Authorization` | 支持 GET 登出（Settings.razor 链接导航）；登出后直接进 Identity 登录页 | 10.3 + 回顾修复 | 保留 |
| 6 | `EnableAuthenticator.cshtml` | `@section Scripts` 中添加 CDN qrcode.js + QR 码渲染脚本 | Identity UI NuGet 包静态资源路径不可用，CDN 提供 QR 码库 | 10（回顾修复） | 保留（CDN URL 可能需要更新） |
| 7 | `EnableAuthenticator.cshtml.cs` | `RedirectToPage("./ShowRecoveryCodes")` → `RedirectToPage("./GenerateRecoveryCodes")` | `ShowRecoveryCodes` 页面未被脚手架生成（已由 `GenerateRecoveryCodes` 覆盖） | 10（回顾修复） | 保留 |
| 8 | `GenerateRecoveryCodes.cshtml` | 添加恢复码展示区域（`@if (Model.RecoveryCodes != null)` 条件渲染 `<pre>` 列表） | 替代不存在的 `ShowRecoveryCodes` 页面 | 10（回顾修复） | 保留 |
| 9 | `GenerateRecoveryCodes.cshtml.cs` | `RedirectToPage("./ShowRecoveryCodes")` → `return Page()` | 同上 | 10（回顾修复） | 保留 |
| 10 | `EnableAuthenticator.cshtml.cs` | `GenerateQrCodeUri` issuer `"Microsoft.AspNetCore.Identity.UI"` → `"BoxWise"` | TOTP App 显示正确的应用名称 | 10（回顾修复） | 保留 |
| 11 | `Pages/_Layout.cshtml` | **新建** — CDN Bootstrap 5.3.3 + 响应式布局 + zh-CN | 替代不存在的 Identity UI NuGet 包布局，提供 Bootstrap 样式 | 10（回顾修复） | 保留（CDN URL 可能需要更新） |
| 11a | `Pages/_Layout.cshtml` | Bootstrap CDN → 本地 `~/lib/bootstrap/bootstrap.min.css` + `.js` | CDN CSS 被浏览器隐私追踪保护拦截，侧边栏不可见 | tech-debt CAP-4 (D-10) | 保留；Bootstrap 升级时更新本地文件 |
| 12 | `_ViewStart.cshtml` | `Layout` 从 `/Pages/Shared/_Layout.cshtml` → `/Areas/Identity/Pages/_Layout.cshtml` | 指向新建的 Identity 区域布局 | 10（回顾修复） | 保留 |
| 13 | `Login.cshtml` | 在 `</form>` 后添加 `<a href="/login">使用通行密钥登录</a>` | 用户从 Identity 密码登录页导航到 Blazor WASM 通行密钥登录 | 11.2 | 保留 |
| 14 | `ConfirmEmail.cshtml.cs` | 添加 `[AllowAnonymous]` | 防御性修复：启用 `RequireConfirmedAccount` 时邮箱确认链接需未登录可访问 | tech-debt CAP-3 (D-07) | 保留 |
| 15 | `LoginWith2fa.cshtml.cs` | `returnUrl ?? Url.Content("~/")` → `string.IsNullOrEmpty(returnUrl) ? Url.Content("~/") : returnUrl` | `?returnUrl=` 空字符串绕过 `??` 守卫，`LocalRedirect("")` 触发异常 | tech-debt CAP-3 (D-09) | 保留 |
| 16 | `LoginWithRecoveryCode.cshtml.cs` | `GetTwoFactorUserAsync()` 两处 `throw new InvalidOperationException` → `return null`；`returnUrl ?? Url.Content("~/")` → `string.IsNullOrEmpty` 守卫 | 直接导航至 RecoveryCode 页面时 throw → 500；空 returnUrl 绕过 `??` | tech-debt CAP-3 (D-08 + D-09) | 保留 |
| 17 | `Manage/Index.cshtml` | 汉化用户可见文本：标题"Profile"→"个人信息"，placeholder 和按钮"Save"→"保存" | Identity 页面简体中文本地化 | Epic 11 | 保留（如上游更新需重新适配中文） |
| 18 | `Manage/Email.cshtml` | 汉化：标题"Manage Email"→"管理邮箱"，placeholder，"Send verification email"→"发送验证邮件"，"Change email"→"修改邮箱" | 同上 | Epic 11 | 同上 |
| 19 | `Manage/ChangePassword.cshtml` | 汉化：标题"Change password"→"修改密码"，placeholder，"Update password"→"更新密码" | 同上 | Epic 11 | 同上 |
| 20 | `Manage/TwoFactorAuthentication.cshtml` | 汉化：标题、恢复码剩余警告、按钮（Forget/Disable/Reset）、验证器应用部分、隐私政策提示 | 同上 | Epic 11 | 同上 |
| 21 | `Manage/EnableAuthenticator.cshtml` | 汉化：标题、步骤说明、placeholder、"Verification Code"→"验证码"、"Verify"→"验证" | 同上 | Epic 11 | 同上 |
| 22 | `Manage/Disable2fa.cshtml` | 汉化：标题、警告说明、"Disable 2FA"→"禁用 2FA" | 同上 | Epic 11 | 同上 |
| 23 | `Manage/GenerateRecoveryCodes.cshtml` | 汉化：标题、安全提示、说明文字、"Generate Recovery Codes"→"生成恢复码" | 同上 | Epic 11 | 同上 |
| 24 | `Manage/ResetAuthenticator.cshtml` | 汉化：标题、警告说明、"Reset authenticator key"→"重置验证器密钥" | 同上 | Epic 11 | 同上 |
| 25 | `Manage/Disable2fa.cshtml.cs` | `SetTwoFactorEnabledAsync(user, false)` → 直接赋值 `user.TwoFactorEnabled = false` + `ConfiguredMethods = None` + 单次 `UpdateAsync` | Issue #3：同步自定义 ConfiguredMethods 字段，合并为原子保存避免两步 UpdateAsync 不一致 | Epic 11 回顾 | 保留 |
| 26 | `Manage/EnableAuthenticator.cshtml.cs` | `SetTwoFactorEnabledAsync(user, true)` → 直接赋值 `user.TwoFactorEnabled = true` + `ConfiguredMethods |= TOTP` + 单次 `UpdateAsync`；失败时回滚内存状态后重渲染 | 同上 + Issue #3：失败后 `LoadSharedKeyAndQrCodeUriAsync` 可能触发保存，需防止脏状态泄露 | Epic 11 回顾 | 保留 |
| 27 | `Manage/ResetAuthenticator.cshtml.cs` | `SetTwoFactorEnabledAsync(user, false)` → 直接赋值 `TwoFactorEnabled=false`；`ConfiguredMethods &= ~TOTP` + `PendingTotpSecretKey=null` 移至 `ResetAuthenticatorKeyAsync` 之前，利用其内部 `UpdateAsync` 一次保存；检查 `ResetAuthenticatorKeyAsync` 返回值 | Issue #3：同步 ConfiguredMethods；Code Review P4：消除两步 UpdateAsync 的部分失败窗口；Code Review P1：检测静默 UpdateAsync 失败 | Epic 11 回顾 + Code Review | 保留 |
| 28 | `Account/LoginWith2fa.cshtml.cs` | `OnGetAsync` 中添加 `AutoFixDataIntegrityAsync` 调用 + 禁用 2FA 后重定向 Login；新增私有方法 `AutoFixDataIntegrityAsync(bool)` — 检测并自动修复 3 类数据损坏；`catch(Exception)` → `catch(DbUpdateException)` + `catch(InvalidOperationException)` | Issue #5：迁移已退役 TwoFactorEndpoints.ChallengeAsync 的防御性检查；Code Review P1/P2/P5：窄化异常捕获、统一单次 UpdateAsync 模式、修复后重定向防用户陷于失效 2FA 页面 | Epic 11 回顾 + Code Review | 保留 |
| 29 | `Manage/_ViewStart.cshtml` | **新建** — `Layout = "_Layout"` 指向 Manage 目录下的 `_Layout.cshtml`（含侧边栏） | 脚手架遗漏：所有 Manage 页面通过父级 `_ViewStart.cshtml` 继承无侧边栏的布局，Manage `_Layout.cshtml` 从未被引用 | Bug 修复 (2026-06-03) | 保留 |
| 30 | `Manage/_ManageNav.cshtml` | 移除 `@inject SignInManager` 指令、`hasExternalLogins` 变量计算和 `@if (hasExternalLogins)` 死链接区块 | ExternalLogins 页面在脚手架排除列表中，侧边栏恢复可见后该链接会产生 404 | Deferred code review (2026-06-03) | 保留 |
| 31 | `Manage/ManageNavPages.cs` | 移除已排除页面的孤立常量和 NavClass 方法（DownloadPersonalData/DeletePersonalData/PersonalData/ExternalLogins） | 消除维护债务——这些页面在脚手架排除列表中，对应常量和方法已失效 | Deferred code review (2026-06-03) | 保留 |
| 32 | `Manage/Index.cshtml`, `Manage/Email.cshtml`, `Manage/ChangePassword.cshtml`, `Manage/EnableAuthenticator.cshtml` | `col-md-6` → `col-md-8` | 侧边栏 `col-md-3` 布局嵌套使表单渲染在 `col-md-9` 内容区而非全宽容器，表单有效宽度缩小；加宽列恢复预期视觉宽度 | Deferred Code Review (2026-06-03) | 保留 |
| 33 | `Account/ForgotPassword.cshtml.cs` | 基于脚手架模板：`FindByEmailAsync` → `FindByNameAsync`，`[AllowAnonymous]` + `[EnableRateLimiting("forgot-password")]`，`EmailConfirmed` 检查，UTF8 → Base64UrlEncode 令牌编码，`protocol: Request.Scheme` 绝对 URL | BoxWise 用户名登录体系；防枚举和邮件轰炸 | Epic 15 | 重新适配 |
| 34 | `Account/ForgotPassword.cshtml` | Email → Username 输入框，中文化，`form-floating` 样式，"返回登录"链接 | 与 Login.cshtml 风格一致 | Epic 15 | 重新适配 |
| 35 | `Account/ForgotPasswordConfirmation.cshtml` | 新建：`[AllowAnonymous]`，中文化提示（含垃圾邮件文件夹提示） | 脚手架排除列表中无此页面，全新创建 | Epic 15 | 重新适配 |
| 35a | `Program.cs` | `DataProtectionTokenProviderOptions.TokenLifespan = 1h` + `AddFixedWindowLimiter("forgot-password")` 速率限制策略 | 默认 TokenLifespan 为 24h；无默认 ForgotPassword 限流策略 | Epic 15 | 保留 |
| 36 | `Account/ResetPassword.cshtml.cs` | 基于脚手架模板：`FindByEmailAsync` → `FindByIdAsync`，`OnGet` 参数 `userId` + `code`，Base64UrlDecode → UTF8 令牌解码，脱敏邮箱显示，重置成功后发送安全通知邮件 | BoxWise 用户名/ID 体系；URL 传递 userId 替代邮箱 | Epic 15 | 重新适配 |
| 37 | `Account/ResetPassword.cshtml` | Email → userId（hidden），脱敏邮箱提示，`autocomplete="username"` 隐藏框，中文化 | 配合 PasswordManager 保存新密码 | Epic 15 | 重新适配 |
| 38 | `Account/ResetPasswordConfirmation.cshtml` | 新建：`[AllowAnonymous]`，中文化，"点击此处登录"链接 | 脚手架排除列表中无此页面，全新创建 | Epic 15 | 重新适配 |
| 39 | `Account/Login.cshtml` | 底部添加"忘记密码？"链接 | 用户入口 | Epic 15 | 保留 |
| 40 | `Account/_StatusMessage.cshtml` | 从 `Manage/_StatusMessage.cshtml` 复制到 `Account/` 目录 | `ConfirmEmail.cshtml` 引用 `_StatusMessage` 分部视图，但该视图仅存在于 `Manage/` 子目录，ASP.NET Core 搜索路径无法找到 → 500 错误 | Bug 修复 (2026-07-02) | 保留 |
| 41 | `Manage/Email.cshtml.cs` | `OnPostSendVerificationEmailAsync` + `OnPostChangeEmailAsync` 邮件内容中文化：主题 `"Confirm your email"` → `"BoxWise - 确认邮箱"`，HTML 正文改为中文；状态消息 `StatusMessage` 中文化 | 用户收到的确认邮件和页面提示为英文 | 本地化修复 (2026-07-02) | 重新适配 |
| 42 | `ConfirmEmail.cshtml.cs` | `StatusMessage` 中文化：`"Thank you for confirming your email." / "Error confirming your email."` → `"邮箱确认成功。" / "邮箱确认失败。"` | 邮箱确认页面提示为英文 | 本地化修复 (2026-07-02) | 保留 |

## 脚手架排除的文件

以下 Identity 页面**有意未生成**，代码中引用这些页面的链接会产生 404：

- `Account.Register` / `Account.RegisterConfirmation` — BoxWise 由 Admin 后台创建用户，无自助注册
- 无（ForgotPassword 和 ResetPassword 已在 Epic 15 实现）
- `Account.ExternalLogin` — 无第三方 OAuth 登录
- `Account.AccessDenied` / `Account.ResendEmailConfirmation` — v1 不需要
- `Account.Manage.ShowRecoveryCodes` — 由 `GenerateRecoveryCodes` 覆盖
- `Account.Manage.DeletePersonalData` / `Account.Manage.PersonalData` / `Account.Manage.ExternalLogins` / `Account.Manage.DownloadPersonalData` — v1 不需要

## 已知残留死链接

- 无 — `ExternalLogins` / `PersonalData` 导航链接已于 2026-06-03 移除（见修改 #30/#31）

## Deferred to Epic 11

- **Identity 页面简体中文本地化** — ✅ 已完成（2026-06-02）。8 个 Manage 页面已汉化，见上方 #17-#24。_Layout.cshtml 等共用布局未汉化（受上游更新影响较大，待后续评估）。

- **Admin 面板 2FA 状态显示适配** — Identity 管理的 2FA 用户 `ConfiguredMethods` 为空，导致：<br>
  (1) 状态列显示 "已启用 ()"<br>
  (2) "重置 2FA" 按钮不显示（判断依赖 `ConfiguredMethods`）。<br>
  方案 B：Admin 面板改用 `TwoFactorEnabled` 判断 + Identity API 查具体方法，不依赖 `ConfiguredMethods`。
