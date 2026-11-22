# 迁移路线图

> 配套 SPEC.md。每个 Phase 应独立 commit，支持精确回滚。

## Phase 1: 脚手架 + 构建验证（~30 分钟）

**目标：** 17 个 Identity Razor Pages 生成完成，`dotnet build` 0 错误。

- [ ] 1.1 安装 NuGet 包: `Microsoft.VisualStudio.Web.CodeGeneration.Design`, `Microsoft.AspNetCore.Identity.UI`
- [ ] 1.2 安装 CLI 工具: `dotnet tool install --global dotnet-aspnet-codegenerator`
- [ ] 1.3 执行脚手架命令:

```powershell
dotnet aspnet-codegenerator identity `
  -dc BoxWise.Server.Data.AppDbContext `
  -u BoxWise.Server.Models.AppUser `
  --files "Account.Login;Account.LoginWith2fa;Account.LoginWithRecoveryCode;Account.Logout;Account.Lockout;Account.ConfirmEmail;Account.Manage._Layout;Account.Manage._ManageNav;Account.Manage._StatusMessage;Account.Manage.Index;Account.Manage.ChangePassword;Account.Manage.Email;Account.Manage.EnableAuthenticator;Account.Manage.ResetAuthenticator;Account.Manage.Disable2fa;Account.Manage.TwoFactorAuthentication;Account.Manage.GenerateRecoveryCodes"
```

- [ ] 1.4 删除 `Areas/Identity/IdentityHostingStartup.cs`（重复 Identity 注册）
- [ ] 1.5 删除 `Areas/Identity/Data/` 下脚手架生成的冗余文件（通常为 `AppDbContext.cs` 变体 + `AppUser.cs` 变体——验证命名空间包含 "Identity" 关键字以避免误删）
- [ ] 1.6 验证 NuGet 包版本与 `Directory.Packages.props` CPM 一致
- [ ] 1.7 `dotnet build` → 0 错误
- [ ] 1.8 `git commit -m "feat(identity): scaffold 17 Identity Razor Pages"`

## Phase 2: IEmailSender + .NET 10 Bug + 认证桥接（~30 分钟）

- [ ] 2.1 创建 `Services/IdentityEmailSender.cs`（`IEmailSender` 适配器，通过 `SmtpConfigurationService` + MimeKit 发送）
- [ ] 2.2 在 `Program.cs` 注册: `builder.Services.AddTransient<IEmailSender, IdentityEmailSender>()`
- [ ] 2.3 `dotnet build` → 0 错误
- [ ] 2.4 ⚠️ 验证 `LoginWith2fa.cshtml` 是否受 .NET 10 Bug 影响
  - 如受影响：修改 `LoginWith2fa.cshtml.cs` 的 `OnGetAsync`，应用 PageModel 版 workaround
- [ ] 2.5 配置 `CookieAuthenticationOptions.LoginPath = "/Identity/Account/Login"`
- [ ] 2.6 验证认证桥接: Identity Login → Cookie 签发 → 重定向到 `/` → `CookieAuthenticationStateProvider` 调用 `GET /api/auth/me` → UI 更新
- [ ] 2.7 `git commit -m "feat(identity): IEmailSender adapter + auth bridge"`

## Phase 3: 前端适配（~30 分钟）

- [ ] 3.1 `Login.razor`: 删除 `HandleLogin`/`HandleTwoFactorVerify`/`HandleRecoveryCodeVerify`/`LoadTwoFactorChallengeAsync`/`SelectMethod`/`ResendEmailCode`/`BackToCredentials` + 2FA UI 表单。保留 `HandlePasskeyLogin` + 通行密钥按钮。
- [ ] 3.2 `Settings.razor`: 删除 `TwoFactorManage` 组件引用，替换为跳转链接:

```razor
@inject IConfiguration Config

<MudButton Href="@GetServerUrl("Identity/Account/Manage")"
           Target="_blank"
           StartIcon="@Icons.Material.Filled.Security"
           Color="Color.Primary">
    管理账户设置
</MudButton>

@code {
    private string GetServerUrl(string path)
    {
        var apiBase = Config["ApiBaseUrl"];
        if (!string.IsNullOrEmpty(apiBase)) return $"{apiBase}{path}";
        return $"/{path}";
    }
}
```

- [ ] 3.3 Client `Program.cs` 必须加载开发环境配置: 在 `builder.HostEnvironment.IsDevelopment()` 时 `builder.Configuration.AddJsonFile("appsettings.Development.json", optional: true)`——因为 `ApiBaseUrl` 定义在该文件中，Blazor WASM 默认不加载环境特定配置文件
- [ ] 3.4 `dotnet build` → 0 错误
- [ ] 3.5 `git commit -m "feat(identity): frontend links to Identity pages"`

## Phase 4: 退役旧代码（~20 分钟）

- [ ] 4.1 执行退役前 Grep 验证（见 `decommission-checklist.md`）
- [ ] 4.2 删除 `TwoFactorModifyEndpoints.cs`（5 端点）
- [ ] 4.3 删除 `EmailVerificationEndpoints.cs`（2 端点）
- [ ] 4.4 删除 `TwoFactorManage.razor`（587 行）
- [ ] 4.5 删除 `AuthEndpoints.LoginAsync` / `LogoutAsync`
- [ ] 4.6 删除 `TwoFactorEndpoints.cs` 中退役方法：VerifyAsync、VerifyRecoveryCodeDuringLoginAsync（无条件）+ RegenerateRecoveryCodesAsync、GenerateQrCodeAsync、GetStatusAsync、SetupTotpAsync、VerifyTotpAsync、SetupEmailAsync、VerifyEmailAsync（Story 2.3 统一退役）+ ChallengeAsync/SendChallengeCodeAsync（条件退役：确认无调用方）
- [ ] 4.7 删除 `AuthService.cs` 中退役方法（见 `decommission-checklist.md`）
- [ ] 4.8 `dotnet build` → 0 错误
- [ ] 4.9 `git commit -m "refactor(identity): decommission hand-written auth code"`

## Phase 5: 测试更新（~20 分钟）

- [ ] 5.1 删除退役端点的测试方法（保留测试文件）
- [ ] 5.2 `dotnet test` → 全部通过
- [ ] 5.3 手动验证清单:

| 操作 | 预期 |
|------|------|
| 访问 Blazor WASM → 未登录 → 重定向 `/Identity/Account/Login` | 显示 Bootstrap 登录页 |
| 输入用户名/密码 → 登录 | 重定向回 WASM 首页，显示已登录 |
| 已配置 2FA 用户登录 | 跳转 `LoginWith2fa` → 输入验证码 → 登录成功 |
| 使用通行密钥登录 | WebAuthn 弹窗 → 验证通过 → 登录成功 |
| Settings → 管理账户设置 | 新标签页打开 Identity Manage 页面 |
| Manage 页面 → 修改 TOTP | QR 码扫描 → 配置成功 |
| Manage 页面 → 修改邮箱 | 发送确认邮件 → 验证 → 更新 |
| Manage 页面 → 修改密码 | 输入旧/新密码 → 更新成功 |
| Manage 页面 → 生成恢复码 | 显示 8 个新码 |
| 登出 | Cookie 清除，回到未登录状态 |

- [ ] 5.4 `git commit -m "test(identity): update tests for migration"`

## Phase 6: 清理 + 文档（~15 分钟）

- [ ] 6.1 移除未使用的 NuGet 引用（如有）
- [ ] 6.2 SameSite 生产环境优化: `options.Cookie.SameSite = env.IsDevelopment() ? SameSiteMode.None : SameSiteMode.Lax; options.Cookie.SecurePolicy = env.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always`（三方已统一：SPEC C7 + Epic Story 2.4 + migration-phases）
- [ ] 6.3 更新 CLAUDE.md 架构说明
- [ ] 6.4 `dotnet build && dotnet test` → 全部通过
- [ ] 6.5 `git commit -m "chore(identity): cleanup + docs for migration"`
