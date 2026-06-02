# 技术债务清单

> 每条的来源、文件位置、症状、建议方案。CAP 编号对应 SPEC.md 中的 Capability。

---

## CAP-1: Cookie & 安全配置一致性

### D-01: AccessDeniedPath 指向不存在路径

- **来源:** 10-3-cookie-auth-bridge code review
- **文件:** `src/BoxWise.Server/Program.cs`
- **症状:** `AccessDeniedPath = "/Account/AccessDenied"` 默认路径在项目中不存在，非 API 页面的 403 拒绝访问可能触发 404
- **建议:** 设为 `/` 或创建简单的 AccessDenied 页面

### D-02: OnRedirectToAccessDenied 对所有请求返回 403

- **来源:** 10-3-cookie-auth-bridge code review
- **文件:** `src/BoxWise.Server/Program.cs`
- **症状:** 非 API 请求的 OnRedirectToAccessDenied 直接返回 `StatusCodes.Status403Forbidden`，与修复后的 OnRedirectToLogin（区分 API vs 页面）行为不一致
- **建议:** 参照 OnRedirectToLogin 的模式，API 请求返回 403，页面请求重定向

### D-03: SameSite/SecurePolicy 三元表达式重复 3 处

- **来源:** 11-4-samesite-docs-update code review → [#15](https://github.com/elvisw/BoxWise/issues/15)
- **文件:** `src/BoxWise.Server/Program.cs`
- **症状:** 主 Cookie、TwoFactorUserId、Session 三处配置使用相同的 `env.IsDevelopment() ? SameSiteMode.None : SameSiteMode.Lax` 和 `env.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always` 三元表达式
- **建议:** 提取为 `static SameSiteMode GetSameSiteMode(IWebHostEnvironment env)` 和 `static CookieSecurePolicy GetSecurePolicy(IWebHostEnvironment env)` helper 方法

### D-04: TwoFactorRememberMeScheme 未显式配置

- **来源:** 11-4-samesite-docs-update code review → [#16](https://github.com/elvisw/BoxWise/issues/16)
- **文件:** `src/BoxWise.Server/Program.cs`
- **症状:** 使用框架默认值。生产环境 SameSite=Lax（默认）+ SecurePolicy=SameAsRequest（默认）→ 无 Secure 标志，与其他 3 个 Cookie 的显式配置不一致
- **建议:** 添加 `.TwoFactorRememberMeScheme` 显式配置，与主 Cookie 相同的 SameSite/SecurePolicy 策略

### D-05: UseForwardedHeaders 未配置

- **来源:** 11-4-samesite-docs-update code review → [#17](https://github.com/elvisw/BoxWise/issues/17)
- **文件:** `src/BoxWise.Server/Program.cs`
- **症状:** Caddy 反向代理后 Request.IsHttps 可能不准确，影响 Cookie Secure 标志和重定向 URL 生成
- **建议:** 在生产环境块中添加 `app.UseForwardedHeaders()` 并配置 `ForwardedHeadersOptions`

---

## CAP-2: API 错误响应标准化

### D-06: API 401 返回空内容体

- **来源:** 10-3-cookie-auth-bridge code review
- **文件:** `src/BoxWise.Server/Program.cs`（Cookie 认证配置的 OnRedirectToLogin / challenge 处理）
- **症状:** 未认证 API 请求返回裸 401（无响应体），与项目中所有其他端点使用 `TypedResults.Problem()` 返回 ProblemDetails JSON 的标准不一致
- **建议:** 在 API 请求的 401 路径中写入 `ProblemDetails` JSON 响应体（`application/problem+json`），包含 status=401、title="Unauthorized"、detail 说明

---

## CAP-3: Identity 脚手架页面健壮性

### D-07: ConfirmEmail.cshtml.cs 缺少 [AllowAnonymous]

- **来源:** 10-3-cookie-auth-bridge code review
- **文件:** `src/BoxWise.Server/Areas/Identity/Pages/Account/ConfirmEmail.cshtml.cs`
- **症状:** 如启用 `RequireConfirmedAccount`，邮箱确认链接需要登录后才能访问，导致确认流程中断
- **建议:** 在 PageModel 类上添加 `[AllowAnonymous]`（防御性修复）

### D-08: LoginWith2fa/RecoveryCode OnGetAsync null User 未处理

- **来源:** 10-3-cookie-auth-bridge code review
- **文件:** `src/BoxWise.Server/Areas/Identity/Pages/Account/LoginWith2fa.cshtml.cs`、`LoginWithRecoveryCode.cshtml.cs`
- **症状:** 用户直接导航到 2FA 页面（无 TwoFactorUserId Cookie）时 `User` 为 null，PageModel 代码可能触发 NullReferenceException → 500
- **建议:** OnGetAsync 开头检查 `User` 或 `SignInManager.GetTwoFactorAuthenticationUserAsync()` 返回 null 时重定向到 Login 页面

### D-09: 空 returnUrl 绕过 null 合并

- **来源:** 10-3-cookie-auth-bridge code review
- **文件:** `src/BoxWise.Server/Areas/Identity/Pages/Account/LoginWith2fa.cshtml.cs`、`LoginWithRecoveryCode.cshtml.cs`
- **症状:** `?returnUrl=` 传递空字符串时 `returnUrl ?? "/"` 不生效（空字符串不是 null），`LocalRedirect("")` 触发异常
- **建议:** 将 `returnUrl ?? "/"` 改为 `string.IsNullOrEmpty(returnUrl) ? "/" : returnUrl`

---

## CAP-4: UI 资产本地化

### D-10: Bootstrap CDN CSS 被浏览器拦截

- **来源:** manual verification (2026-06-02) → [#12](https://github.com/elvisw/BoxWise/issues/12)
- **文件:** `src/BoxWise.Server/Areas/Identity/Pages/Shared/_Layout.cshtml`（Identity 布局页）
- **症状:** Bootstrap 5.3.3 CDN CSS 被浏览器隐私追踪保护（Tracking Prevention）拦截，导致栅格布局失效，侧边栏不可见
- **建议:** 将 Bootstrap CSS 下载到 Server 的 `wwwroot/` 目录，通过 `<link rel="stylesheet" href="~/lib/bootstrap/css/bootstrap.min.css">` 本地引用

### D-11: 通行密钥管理对话框样式异常

- **来源:** manual verification (2026-06-02) → [#14](https://github.com/elvisw/BoxWise/issues/14)
- **文件:** `src/BoxWise.Client/Pages/Settings.razor`（通行密钥管理对话框部分）
- **症状:** 对话框内按钮和文字边距丢失，视觉效果与其他设置区域不一致
- **建议:** 检查 MudBlazor 组件（MudDialog、MudButton、MudText）的 Class/Style 参数，补充缺失的间距设置

---

## CAP-5: Identity 页面中文化

### D-12: Identity 脚手架页面未汉化

- **来源:** manual verification (2026-06-02) → [#13](https://github.com/elvisw/BoxWise/issues/13)
- **文件:** `src/BoxWise.Server/Areas/Identity/Pages/Account/` 下 17 个 `.cshtml` 文件
- **症状:** 所有 Identity 脚手架页面（登录、注册、2FA、账户管理、邮箱确认、密码重置等）均为英文默认文案
- **建议:** 将各 `.cshtml` 文件中的静态文本（标题、标签、按钮文字、提示信息）替换为中文
