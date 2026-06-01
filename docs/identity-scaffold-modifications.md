# Identity 脚手架文件修改清单

> **基线：** Story 10.1 commit `392229d` — 脚手架生成 17 个 Identity Razor Pages
> **规则：** 每次修改 `Areas/Identity/` 下任何文件，必须在此文件中记录。

## 修改记录

| # | 文件 | 修改 | 原因 | Story | 上游更新时 |
|---|------|------|------|-------|-----------|
| 1 | `Login.cshtml` | `Input.Email` → `Input.Username`，汉化标签，移除 Register/ForgotPassword/ResendEmailConfirmation 死链接，移除 External Login 区域 | BoxWise 用用户名登录，非邮箱；未生成的页面产生死链接 | 10（回顾修复） | 重新适配 |
| 2 | `Login.cshtml.cs` | 移除 `[EmailAddress]` 校验，属性 `Email` → `Username`，`PasswordSignInAsync` 调用更新 | 同上 | 10（回顾修复） | 重新适配 |
| 3 | `LoginWith2fa.cshtml.cs` | 提取 `GetTwoFactorUserAsync()` 辅助方法，`NameIdentifier → FindByIdAsync` 优先，`Name → FindByNameAsync` 兜底；删除死代码 `var userId`；添加 `using Microsoft.AspNetCore.Authentication` + `using System.Security.Claims`；清理重复 `using Microsoft.Extensions.Logging` | dotnet/aspnetcore#66929 + Identity `CreateTwoFactorIdentityAsync` 中 NameIdentifier 可能缺失 | 10.4 + 回顾修复 | **等上游修复后删除 workaround，恢复原始调用** |
| 4 | `LoginWithRecoveryCode.cshtml.cs` | 同上：提取 `GetTwoFactorUserAsync()` 辅助方法 + 双路径 fallback + 清理 using | 同上 | 10.4 + 回顾修复 | 同上 |
| 5 | `Logout.cshtml.cs` | 添加 `OnGet` handler（独立实现，不委托给 `OnPost`），重定向 `/Identity/Account/Login`；移除未使用的 `using Microsoft.AspNetCore.Authorization` | 支持 GET 登出（Settings.razor 链接导航）；登出后直接进 Identity 登录页 | 10.3 + 回顾修复 | 保留 |
| 6 | `EnableAuthenticator.cshtml` | `@section Scripts` 中添加 CDN qrcode.js + QR 码渲染脚本 | Identity UI NuGet 包静态资源路径不可用，CDN 提供 QR 码库 | 10（回顾修复） | 保留（CDN URL 可能需要更新） |
| 7 | `EnableAuthenticator.cshtml.cs` | `RedirectToPage("./ShowRecoveryCodes")` → `RedirectToPage("./GenerateRecoveryCodes")` | `ShowRecoveryCodes` 页面未被脚手架生成（已由 `GenerateRecoveryCodes` 覆盖） | 10（回顾修复） | 保留 |
| 8 | `GenerateRecoveryCodes.cshtml` | 添加恢复码展示区域（`@if (Model.RecoveryCodes != null)` 条件渲染 `<pre>` 列表） | 替代不存在的 `ShowRecoveryCodes` 页面 | 10（回顾修复） | 保留 |
| 9 | `GenerateRecoveryCodes.cshtml.cs` | `RedirectToPage("./ShowRecoveryCodes")` → `return Page()` | 同上 | 10（回顾修复） | 保留 |
| 10 | `EnableAuthenticator.cshtml.cs` | `GenerateQrCodeUri` issuer `"Microsoft.AspNetCore.Identity.UI"` → `"BoxWise"` | TOTP App 显示正确的应用名称 | 10（回顾修复） | 保留 |
| 11 | `Pages/_Layout.cshtml` | **新建** — CDN Bootstrap 5.3.3 + 响应式布局 + zh-CN | 替代不存在的 Identity UI NuGet 包布局，提供 Bootstrap 样式 | 10（回顾修复） | 保留（CDN URL 可能需要更新） |
| 12 | `_ViewStart.cshtml` | `Layout` 从 `/Pages/Shared/_Layout.cshtml` → `/Areas/Identity/Pages/_Layout.cshtml` | 指向新建的 Identity 区域布局 | 10（回顾修复） | 保留 |

## 脚手架排除的文件

以下 Identity 页面**有意未生成**，代码中引用这些页面的链接会产生 404：

- `Account.Register` / `Account.RegisterConfirmation` — BoxWise 由 Admin 后台创建用户，无自助注册
- `Account.ForgotPassword` / `Account.ResetPassword` — v1 未实现
- `Account.ExternalLogin` — 无第三方 OAuth 登录
- `Account.AccessDenied` / `Account.ResendEmailConfirmation` — v1 不需要
- `Account.Manage.ShowRecoveryCodes` — 由 `GenerateRecoveryCodes` 覆盖
- `Account.Manage.DeletePersonalData` / `Account.Manage.PersonalData` / `Account.Manage.ExternalLogins` / `Account.Manage.DownloadPersonalData` — v1 不需要

## 已知残留死链接（Epic 11 处理）

- `_ManageNav.cshtml` 中的 `ExternalLogins` / `PersonalData` 导航链接

## Deferred to Epic 11

- **Identity 页面简体中文本地化** — 所有 17 个脚手架页面为硬编码英文。方案：直接汉化 .cshtml 文本。

- **Admin 面板 2FA 状态显示适配** — Identity 管理的 2FA 用户 `ConfiguredMethods` 为空，导致：<br>
  (1) 状态列显示 "已启用 ()"<br>
  (2) "重置 2FA" 按钮不显示（判断依赖 `ConfiguredMethods`）。<br>
  方案 B：Admin 面板改用 `TwoFactorEnabled` 判断 + Identity API 查具体方法，不依赖 `ConfiguredMethods`。
