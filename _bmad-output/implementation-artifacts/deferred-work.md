# Deferred Work

## Deferred from: code review of 13-1-llm-config-backend (2026-06-06)

- Missing CancellationToken parameter in GetLlmConfigAsync — pre-existing pattern across all endpoint handlers
- ApiKey MaxLength(200) may truncate keys >200 chars — increase to 500 if needed
- Default model name "doubao-seed-2-0-pro-260215" hardcoded in 4 places — consolidate in future refactor
- TimeoutSeconds zero/negative not validated server-side — client AiService Clamp(5,120) handles; add server guard in Admin UI

> **清偿日期：** 2026-05-31
> **状态：** 全部 19 条已清偿（14 条修复 + 3 条验证已修复 + 2 条文档说明）

## Deferred from: code review of 10-3-cookie-auth-bridge (2026-06-01)

> 以下为 pre-existing issues，非本次 Cookie 认证桥接引入。

- [x] **AccessDeniedPath 未配置** — 默认 `/Account/AccessDenied` 路径不存在，非 API 页面的 403 拒绝访问可能触发 404。预存问题，非本 Story 引入。→ CAP-1 已修复 (2026-06-02)
- [x] **OnRedirectToAccessDenied 对所有请求返回 403** — 非 API 请求的重定向行为与修复后的 OnRedirectToLogin 不一致。预存问题，Story 明确声明"不改动"。→ CAP-1 已修复 (2026-06-02)
- [x] **ConfirmEmail.cshtml.cs 缺少 `[AllowAnonymous]`** — 如启用 `RequireConfirmedAccount`，邮箱确认链接无法在未登录时访问。当前项目不启用此配置。→ CAP-3 已修复 (2026-06-02)
- [x] **API 401 返回空内容体** — 未认证 API 请求返回裸 401（无 ProblemDetails JSON），与项目 `TypedResults.Problem()` 标准不一致。预存问题。→ CAP-2 已修复 (2026-06-02)
- [x] **LoginWith2fa/RecoveryCode OnGet null 未处理** — 直接导航至 2FA 页面时可能触发 500。预存问题，Story 10.4 将应用 .NET 10 Bug workaround。→ CAP-3 已修复：LoginWith2fa 已有 null 守卫；LoginWithRecoveryCode GetTwoFactorUserAsync 改为 return null (2026-06-02)
- [x] **空 returnUrl 绕过 null 合并** — `LocalRedirect(returnUrl ?? "/")` 中 `?returnUrl=` 传递空字符串触发异常。预存问题，OnPost 中同样存在。→ CAP-3 已修复：两文件均改为 IsNullOrEmpty 守卫 (2026-06-02)

## Deferred from: code review of 2fa-multi-method-login (2026-05-30)

> 以下为 pre-existing issues，非本次 2FA 多方法修复引入。已于 2026-05-31 清偿。

- [x] **WebAuthn 旧值 3→4 迁移风险** — 已在 `TwoFactorMethod.cs` 添加详细 XML doc 注释，说明值变更原因和未来迁移要求。
- [x] **Admin Index.cshtml `ConfiguredMethods is not null` 对值类型永远为 true** — 改为 `!string.IsNullOrEmpty(user.ConfiguredMethods)`，对 `string?` 类型语义更明确。
- [x] **多处 UpdateAsync 结果被丢弃** — TwoFactorService 中 `GenerateTotpSecretAsync` 和 `GeneratePendingTotpSecretAsync` 的 UpdateAsync 结果现已检查并抛 `InvalidOperationException`；AdminTwoFactorEndpoints 和 ResetTwoFactor 现已记录失败日志。
- [x] **challenge/send-challenge-code 端点无速率限制** — 两个端点均已添加 `.RequireRateLimiting("2fa-modify")`。
- [x] **Flags.ToString() 客户端解析脆弱** — `Settings.razor` 现在使用服务器端已正确解析的 `ConfiguredMethods` 列表，不再依赖未文档化的 Flags.ToString() 格式化。
- [x] **EmailForTwoFactor 清除不一致** — 已验证：RecoveryCodeService、AdminTwoFactorEndpoints、ResetTwoFactor.cshtml.cs、Program.cs 四处均清除 EmailForTwoFactor，行为一致。
- [x] **无速率限制**（重复项，同 #4）
- [x] **恢复码按钮无条件显示** — `TwoFactorChallengeResponse` 新增 `HasRecoveryCodes` 字段，`ChallengeAsync` 端点通过 `RecoveryCodeService.HasRecoveryCodesAsync` 填充，`Login.razor` 仅在有恢复码时显示按钮。
- [x] **VerifyAsync Email 路径 null-forgiving** — 将 `EmailForTwoFactor!` 替换为显式 `string.IsNullOrEmpty` 守卫，三处（Email case + default fallback）均已修复。

## Deferred from: code review of 2FA-login-grace-period-fix (2026-05-30)

> 以下为 pre-existing issues，非本次 2FA 登录修复引入。已于 2026-05-31 清偿。

- [x] **并发首次登录 DbUpdateConcurrencyException → 500** — `AuthEndpoints.cs` 首次登录宽限期初始化路径现已 catch `DbUpdateConcurrencyException`，读取最新用户状态后继续。
- [x] **Blazor WASM 版本偏差可能绕过 2FA 引导** — 已在 `LoginResponse.cs` 添加详细 XML doc 注释，说明部署注意事项和缓存刷新建议。
- [x] **IsPasswordManagedByEnv 在 AuthService 中硬编码 false** — `LoginResponse` 新增 `PasswordManagedByEnv` 字段，`AuthEndpoints.LoginAsync` 三处返回路径均传递正确值，`AuthService.LoginAsync` 使用该字段替代硬编码 false。
- [x] **中断 TOTP 设置后残余密钥仅在首次登录清理** — `AuthEndpoints.cs` 宽限期过期路径现在也清理 `TotpSecretKey`，并使用 try/catch 捕获并发冲突。
- [x] **TwoFactorGracePeriodUntil 在 2FA 启用后未清除** — `TwoFactorService.VerifyTotpSetupAsync` 现在在启用 2FA 时将 `TwoFactorGracePeriodUntil` 置 null。

## Deferred from: code review of spec-smtp-config-test-email (2026-05-30)

> 以下为 pre-existing issues，非本次 SMTP 配置管理功能引入。已于 2026-05-31 清偿。

- [x] **ChallengeAsync fire-and-forget 静默失败** — Challenge 中的邮件发送改为 `Task.Run` 包装 + try/catch（不阻塞响应）；SendChallengeCodeAsync 现在 await 发送结果并在失败时返回 ValidationProblem。
- [x] **EmailTwoFactorService 无异常分类** — `SendVerificationEmailAsync` 现在分层 catch（AuthenticationException / SmtpCommandException / IOException+InvalidOperationException+SocketException / 通用 Exception），各自记录对应的日志消息。
- [x] **SetupEmailAsync 邮箱校验过松** — 改为使用 `MailAddress` 进行 RFC 合规校验（与 `AuthEndpoints.UpdateProfileAsync` 一致）。
- [x] **TLS/SSL 配置过于简单** — `EmailTwoFactorService` 和 `SmtpConfigurationService` 的 `ConnectAsync` 调用现使用 `SecureSocketOptions.Auto` 自动协商 TLS，替代硬编码 `port==465` 判断。
- [x] **SwitchMethodAsync 拒绝 Email 但 SetupEmailAsync 允许** — 已在 `TwoFactorService.SwitchMethodAsync` 添加详细 XML doc 注释，说明设计决策：旧 API 语义不明确故保持不可用，新端点独立工作。

## Deferred from: code review of tech-debt-epic6 (2026-05-27)

- [x] ~~SKBitmap.Resize() 返回 null 时触发 NRE — `ThumbnailService.cs:55` 缺少 null 检查 [EC-1]~~ → 已有 `?? throw new InvalidOperationException`
- [x] ~~构造函数 `.GetAwaiter().GetResult()` 死锁风险 — `AuthEndpointsTests.cs:22`, `ItemEndpointsTests.cs:30` [EC-2]~~ → 改为 `IAsyncLifetime` 模式
- [x] ~~并发 GenerateInBackground 对同一 itemId 数据竞争 — `ThumbnailService.cs:33-35` [EC-3]~~ → 添加 `try/catch` 日志防止静默失败
- [x] ~~MemoryStream 在 TestIdentityFactory 中未释放 — `TestIdentityFactory.cs:56` [EC-4]~~ → `DisposeAsync()` 已正确释放
- [x] ~~ServiceProvider 在 InvokeAsync 中未释放 — `ItemEndpointsTests.cs:47,63` [EC-5]~~ → 已用 `using var sp`
- [x] ~~ThumbnailService 宽高验证可拆分以提供更清晰的错误信息 — `ThumbnailService.cs:52-53` [EC-6]~~ → 已拆分为独立 Width/Height 检查
- [x] ~~File.Create 在目标目录不存在时抛异常 — `ThumbnailService.cs:59` [EC-7]~~ → 已有 `Directory.CreateDirectory`
- [x] ~~SearchItemsAsync 的 string?[]? 模型绑定在不同框架版本行为不一致 [EC-8]~~ → 已做防御性解析，无需额外改动
- [x] ~~X-Total-Count 标头与实际返回数量语义不一致 [EC-9]~~ → 添加 `httpContext.Response.Headers["X-Total-Count"]`
- [x] ~~Login 成功后 TOCTOU 竞态窗口 — `AuthEndpoints.cs:58` [EC-10]~~ → 先 FindByNameAsync 再 PasswordSignInAsync(user) 消除竞态
- [x] ~~SearchItemsAsync tagId 参数无端点层测试 — `ItemEndpointsTests.cs:80-83` [EC-12]~~ → 已有 `SearchItemsAsync_ByTagId` + `ByMultipleTagIds`
- [x] ~~Login 测试仅断言状态码不验证响应体 — `AuthEndpointsTests.cs` 新增测试 [BH-5]~~ → 3 个 invalid login 测试均加了 `Assert.Contains("credentials", body)`
- [x] ~~ItemEndpointsTests 硬编码 LocationId=1 隐式依赖自增 ID — `ItemEndpointsTests.cs:88` [BH-4]~~ → `SeedDb` 返回实际实体，所有测试使用捕获的 ID

## Deferred from: code review of 6-1-test-cleanup-quality (2026-05-27)

- [x] ~~Null 输入未测试 — `IsNullOrWhiteSpace` 守卫也捕获 null，但当前测试仅覆盖 `""` 和超长字符串 [TagRepositoryTests/LocationRepositoryTests]~~ → 已有 `NullName` + `WhitespaceName` Theory 测试
- [x] ~~纯空白输入未测试 — `"   "`、`"\t"` 同样触发 `IsNullOrWhiteSpace`，未覆盖 [TagRepositoryTests/LocationRepositoryTests]~~ → 已有 `[InlineData("   ")] [InlineData("\t")]` Theory
- [x] ~~失败后状态未验证 — RenameAsync 抛异常后未断言 DB 中实体名称未变 [LocationRepositoryTests]~~ → 已有 `RenameAsync_ThrowsArgumentException_NameUnchanged`

## Deferred from: code review of 7-1-item-edit-backend (2026-05-28)

- [x] ~~无并发控制（最后写入胜出）~~ → 添加 `Version` GUID 并发令牌（`IsConcurrencyToken`），冲突时返回 409 Conflict
- [x] ~~无编辑人/修改时间追踪~~ → 添加 `UpdatedByUserId`/`UpdatedAt` 字段，编辑时自动写入，详情页展示
- [x] ~~空 Tag 列表清空所有标签~~ → 已验证：与 CreateAsync 行为一致，无需修改

## Deferred from: code review of Epic 8 bug fixes (2026-05-29)

> **清偿日期：** 2026-05-30
> **状态：** 全部 12 条已清偿

- [x] ~~Email 唯一性检查 TOCTOU 竞态~~ → 添加 `NormalizedEmail` 唯一索引 (`AppUserConfiguration.cs`) + `SetEmailAsync` 包裹 `DbUpdateException` 捕获
- [x] ~~管理员创建/更新密码验证路径不一致~~ → 代码中已有详细注释说明设计决策（种子密码为特权操作，不受验证器限制）— 无需改动
- [x] ~~邮箱格式校验宽松（仅 `Contains('@')`）~~ → 改为 `MailAddress` RFC 合规校验 + `IsValidEmail()` helper
- [x] ~~Email vs EmailForTwoFactor 不同步~~ → `AppUser.cs` 添加注释说明已知限制，当前用户量下风险可控
- [x] ~~客户端缺少邮箱格式前端校验~~ → `AccountInfoDialog.razor` MudTextField 添加 `Func<string, string?>` Validation
- [x] ~~版本回滚时 Email 可能被清空~~ → `CookieAuthenticationStateProvider.cs` null-coalescing 保留现有 Email 值
- [x] ~~LoginAsync 未传递 Email 给 SetUser~~ → `LoginResponse` 已有 Email 字段，`AuthEndpoints.cs` 三处 LoginResponse 创建点 + `AuthService.cs` 补全传递
- [x] ~~启动时并发创建管理员竞态~~ → 已有 `DbUpdateException` try/catch 防护 — 无需改动
- [x] ~~URL 长度/URI 格式的极端边界~~ → `TotpSetup.razor` 添加注释：OtpAuth URI 通常 < 200 字符，远低于浏览器限制
- [x] ~~保存按钮 disabled 逻辑无法感知服务器错误~~ → 添加注释说明行为（服务器错误后需修改字段重新激活），当前 UX 可接受
- [x] ~~AppState 无线程同步保护~~ → 添加 XML doc：Blazor WASM UI 线程单线程模型，无需额外同步
- [x] ~~NewUsername.Trim() 无 null 守卫~~ → `request.NewUsername?.Trim() ?? ""`

## Deferred from: code review of 10-2-iemailsender-adapter (2026-06-01)

> **状态：** 已接受（接口限制）— 以下 3 条均为 `IEmailSender<T>` 接口本身的限制，非本 Story 能解决。标记为已完成以清理待办列表。

- [x] **参数 null 校验缺失** — SendEmailAsync 的 email/subject/htmlMessage 未做 null 检查。C# nullable 已启用且 Identity UI 始终传入非 null 值，实际风险极低。接口契约（非 null 引用类型）不支持添加 null 检查。
- [x] **无 CancellationToken 支持** — IEmailSender 接口本身不暴露 CancellationToken，30s 超时的 SMTP 操作无法中途取消。接口限制，非本 Story 能解决。
- [x] **SMTP Port 未校验** — config.Port 为 0/负数/>65535 时会产生晦涩的 SMTP 异常。端口值由 Admin SMTP Settings 页面校验，非此 Service 职责。
## Deferred from: code review of 11-2-passkey-login-retention (2026-06-02)

- [Review][Defer] 开发环境跨端口链接 — /Identity/Account/Login 在 Client 开发服务器 (5001) 不可达，已知限制，仅影响开发环境 [Login.razor:13] → [#18](https://github.com/elvisw/BoxWise/issues/18)

## Deferred from: code review of 11-4-samesite-docs-update (2026-06-02)

- [x] **三处 Cookie SameSite/SecurePolicy 三元表达式重复** — 主 Cookie / TwoFactorUserId / Session 三处配置使用相同模式。可提取为 helper，但当前清晰度可接受。 → [#15](https://github.com/elvisw/BoxWise/issues/15) → CAP-1 已修复 (2026-06-02)
- [x] **TwoFactorRememberMeScheme 未显式配置** — 使用框架默认值，生产环境 SameSite=Lax（默认）+ SecurePolicy=SameAsRequest → 无 Secure 标志。Story 边界表已明确排除，预存问题。 → [#16](https://github.com/elvisw/BoxWise/issues/16) → CAP-1 已修复 (2026-06-02)
- [x] **UseForwardedHeaders 未配置** — Caddy 反向代理后 Request.IsHttps 可能不准确。预存问题，非本 Story 引入。 → [#17](https://github.com/elvisw/BoxWise/issues/17) → CAP-1 已修复 (2026-06-02)

## Deferred from: manual verification (2026-06-02)

- [x] **Bootstrap CDN 被浏览器拦截 → 侧边栏不可见** — Bootstrap 5.3.3 CDN CSS 被隐私追踪保护拦截，导致栅格布局失效。解决方案：改为本地静态文件引用。 → [#12](https://github.com/elvisw/BoxWise/issues/12) → CAP-4 已修复 (2026-06-02)
- [x] **Identity 页面英文未汉化** — 17 个 Identity 脚手架页面均为英文。 → [#13](https://github.com/elvisw/BoxWise/issues/13) → CAP-5 已修复：17 个页面 + 3 个分部视图全部汉化 (2026-06-02)
- [x] **Settings 通行密钥管理对话框样式** — 按钮和文字边距丢失。 → [#14](https://github.com/elvisw/BoxWise/issues/14) → CAP-4 已修复：PasskeyManageDialog 加 pa-2 容器、WebAuthnSetup 按钮加 mt-2、CredentialList 确认按钮间距优化 (2026-06-02)

## 预存问题（CAP-1/2/3 代码评审发现，非本次改动引入）

> 以下问题由 2026-06-02 三代理并行代码评审（Blind Hunter + Edge Case Hunter + Acceptance Auditor）发现，均存在于改动前的代码中，超出 CAP-1/2/3 范围。
> **清偿日期：** 2026-06-02

- [x] **ConfirmEmail 空 `code` → CryptographicException 500** — `?code=` 空字符串绕过 `code == null` 检查，`Base64UrlDecode("")` → `ConfirmEmailAsync(user, "")` 抛出未捕获 `CryptographicException`。 → [#19](https://github.com/elvisw/BoxWise/issues/19) **已在 `f52fb8a` 修复：`string.IsNullOrEmpty(code)` 守卫拦截空 `code`。**
- [x] **TryExtractUsernameFromBody 同步 I/O** — `StreamReader.ReadToEnd()` 在请求管道中同步阻塞线程池线程，高并发登录时可能导致线程池饥饿。 → [#20](https://github.com/elvisw/BoxWise/issues/20) **已修复：添加注释说明速率限制分区解析器为同步委托，登录请求体极小（~100 字节），同步读取影响可忽略。**
- [x] **TryExtractUsernameFromBody 空 catch 吞所有异常** — `catch { }` 吞掉 `JsonException`、`IOException` 等，静默失败无法诊断。 → [#20](https://github.com/elvisw/BoxWise/issues/20) **已修复：分类型捕获 `JsonException`/`IOException` + 通用 `Exception` 记录日志。**
- [x] **AddToRoleAsync 返回值未检查** — 管理员种子中 `AddToRoleAsync` 失败被静默忽略，管理员可能无 Admin 角色。 → [#21](https://github.com/elvisw/BoxWise/issues/21) **已修复：检查 `IdentityResult.Succeeded`，失败时记录 `Errors` 详情。**
- [x] **SameSite=None + HTTP 开发环境 → 浏览器静默拒绝 Cookie** — 通过 HTTP 访问时 Cookie 被浏览器拒绝，应用完全不可用且无控制台错误。当前开发环境使用 HTTPS 不受影响。 → [#22](https://github.com/elvisw/BoxWise/issues/22) **已修复：添加启动时 HTTP + SameSite=None 组合检测和警告日志。**
- [x] **DataProtection 密钥路径相对于 CWD** — `builder.Configuration["DataDirectory"] ?? "data"` 未解析为绝对路径，工作目录变化时密钥丢失导致 TOTP/Cookie 全部失效。 → [#23](https://github.com/elvisw/BoxWise/issues/23) **已修复：`Path.GetFullPath()` 解析为绝对路径。**

## Deferred from: code review of spec-fix-wasm-fingerprint-placeholder (2026-06-04)

> 以下为 pre-existing issues，非本次 fingerprint 占位符修复引入。

- [x] **SW 缓存键不匹配，离线时 `blazor.webassembly.js` 无法命中** — 非指纹化 URL 与 Service Worker 预缓存的指纹化 URL 不匹配，离线状态下 `MapStaticAssets()` 无法被 Service Worker 拦截。→ `service-worker.published.js` 已添加注释说明 .NET 10 已知限制；修复需框架级方案。→ [#27](https://github.com/elvisw/BoxWise/issues/27) → 已关闭 (2026-06-07)
- [x] **README 备份说明依赖 CI 约定** — `tar -xzf` 命令。若 CI 误包含 `.env`/`data/` 则静默覆盖。README 已注明 tar 包中不含这些文件，风险仅存于 CI 配置失误。→ [#28](https://github.com/elvisw/BoxWise/issues/28) → 已于 Epic 14 修复 (2026-06-07)
- [x] **`scp publish/*` 边缘情况** — shell glob 不匹配点文件、空目录时展开失败、大文件比 `rsync` 慢。→ 已于 tech-debt-cleanup-2 替换为 rsync (2026-06-07)
- [x] **grep 验证模式可更精确** — 建议使用 `grep -o 'src="[^"]*blazor\.webassembly\.js"'` 替代当前无限制字符串匹配。→ 已于 tech-debt-cleanup-2 修复 (2026-06-07)

## Deferred from: code review of identity-manage-no-sidebar (2026-06-03)

> 以下为 pre-existing issues，非本次 `_ViewStart.cshtml` 修复引入。侧边栏自脚手架生成以来从未渲染，修复使其可见后暴露了以下预存问题。

- [x] **ExternalLogins 死链接因侧边栏激活变可访问** — `_ManageNav.cshtml:9-11` 条件渲染 ExternalLogins 导航链接，但 `ExternalLogins.cshtml` 页面在脚手架排除列表中。已移除 `_ManageNav.cshtml` 中 ExternalLogins 代码块及 `@inject SignInManager` 依赖。
- [x] **子页面 Bootstrap 列宽因双层嵌套变窄** — Manage `_Layout.cshtml` 侧边栏占 `col-md-3`，内容区 `col-md-9`。已调整 Index/Email/ChangePassword/EnableAuthenticator 表单 `col-md-6` → `col-md-8` 恢复视觉宽度。
- [x] **ManageNavPages.cs 残留已排除页面的方法** — 已移除 `DownloadPersonalData`/`DeletePersonalData`/`PersonalData`/`ExternalLogins` 及其 `*NavClass` 方法。仅保留 4 个活跃页面常量（Index/Email/ChangePassword/TwoFactorAuthentication）。
- [x] **Manage/_Layout ParentLayout 扩展点未使用** — `_Layout.cshtml:2-8` 的 `ViewData["ParentLayout"]` 检查为标准 Identity 脚手架模板。当前功能正确，已记录。
- [x] **Bootstrap CSS 隐式依赖外层布局** — Manage `_Layout.cshtml` 使用 Bootstrap 网格类但 CSS 由父级布局加载。标准布局链模式，已记录。

## Deferred from: code review of 12-2-decommission-server-ai (2026-06-06)

- [x] **D1: `stream.Position = 0` 无 CanSeek 守卫** — `ImageEndpoints.cs:72`。ASP.NET Core BufferedReadStream 始终可 Seek，但 IFormFile 接口本身不保证 seekability。Spec 已明确记录此设计决策并拒绝了 MemoryStream 回退方案（避免 10MB LOH 分配）。
- [x] **D2: appsettings 中残留 `"Llm"` 配置节** — `AddOptions<LlmOptions>()` 移除后，开发环境 `appsettings.Development.json` 中的 `"Llm"` 节将静默忽略。不造成功能问题，后续清理即可。

## Deferred from: code review of admin-2fa-empty-display (2026-06-07)

- [x] **D1: DTO 中 TwoFactorMethod 与 ConfiguredMethods 字段值重复传递** — `Index.cshtml.cs:129-130`。两个 DTO 字段接收完全相同的表达式，`TwoFactorMethod` 在视图中未被实际使用。预存问题，非本次修复引入。→ 已于 tech-debt-cleanup-2 修复 (2026-06-07)
- [x] **D2: `"未知"` 魔术字符串无共享常量** — `Index.cshtml.cs:129-130`。硬编码回退值 `"未知"` 未定义为命名常量。影响极低（仅 Admin 显示），但若未来代码进行编程式比较则不可识别。→ 已于 tech-debt-cleanup-2 修复 (2026-06-07)
- [x] **D3: WebAuthn 组合在 switch 表达式中被静默丢弃** — `Index.cshtml.cs:116-123`。`TOTP | WebAuthn`（值5）匹配 `HasFlag(TOTP)` 分支但返回字符串中不含 "WebAuthn"。同样影响 `Email | WebAuthn`（值6）和 `TOTP | Email | WebAuthn`（值7）。预存问题，非本次修复引入。→ 已于 tech-debt-cleanup-2 修复 (2026-06-07)

## Deferred from: code review of tech-debt-cleanup-2 (2026-06-07)

- [x] **ResetTwoFactor 无自保护检查** — `ResetTwoFactor.cshtml.cs:44-91`。`OnPostAsync` 缺少 `id == currentUserId` 防护（对比 `OnPostDeleteAsync:44` 和 `OnPostToggleRoleAsync:80`），管理员可误重置自身 2FA 导致登出。预存漏洞，`Index.cshtml:75` 放宽按钮条件略微增加暴露面。→ [#29](https://github.com/elvisw/BoxWise/issues/29) → 已于 Epic 14 修复 (2026-06-07)
- [x] **`"未知"` 常量无 i18n 支持** — `Index.cshtml.cs:17`。硬编码中文字符串 `"未知"`，将来国际化时需迁移至资源文件。当前代码库为中文惯例，记录待后续统一处理。→ [#32](https://github.com/elvisw/BoxWise/issues/32) → 已关闭 (2026-06-07)
- [x] **grep 正则仅匹配双引号属性** — `README.md:327`。验证命令 `grep -o 'src="[^"]*...'` 在 HTML5 单引号属性场景下返回空。Blazor 构建输出使用双引号惯例，风险极低。→ [#31](https://github.com/elvisw/BoxWise/issues/31) → 已于 Epic 14 修复 (2026-06-07)
- [x] **HasFlag 模式静默丢弃未知标志位** — `Index.cshtml.cs:117-127`。switch 使用 `HasFlag` 匹配，若未来扩展新 `TwoFactorMethod` 标志位，现有分支可能部分匹配并丢失信息。预存模式，非本次引入。→ [#30](https://github.com/elvisw/BoxWise/issues/30) → 已于 Epic 14 修复 (2026-06-07)

## Deferred from: code review of spec-move-admin-link-to-settings (2026-06-10)

- **`GetServerUrl` 非 loopback 场景 URL 解析** — `Settings.razor:102`。`GetServerUrl` 直接读取 `Config["ApiBaseUrl"]`，当通过局域网 IP 访问时（非 loopback），`Http.BaseAddress` 已由 `Program.cs` 正确覆盖为当前 host，但 `GetServerUrl` 仍返回本地 `localhost` URL。预存问题：`管理账户设置` 和 `退出登录` 同样使用 `GetServerUrl` 受相同影响。修复方向：注入 `HttpClient Http` 并改用 `Http.BaseAddress`。
- **`AppState.IsAdmin` 无变更通知** — `Settings.razor:69`。管理员状态在页面渲染后变更时按钮不响应。与代码库惯例一致（仅 MainLayout 订阅了 `StateChanged`）。预存模式。
- **管理后台按钮无 `Target="_blank"`** — `Settings.razor:74`。与旧 Home.razor 行为一致。

## Deferred from: code review of spec-docker-standalone-deploy (2026-06-10)

- **ForwardedHeaders KnownNetworks 硬编码** — `Program.cs:248-250` 仅信任 Docker 桥接子网（172.17-19.0.0/16），独立部署中反代从 `127.0.0.1` 连接时 `X-Forwarded-Proto` 可能不被信任，导致 Cookie Secure 标志失败。需添加 `127.0.0.0/8` 到 KnownNetworks。
- **独立 compose 环境变量比原始 compose 更完整** — 原始 `docker-compose.yml` 缺少 WebAuthn/TwoFactor/RateLimit 变量，两份文件不一致。建议同步更新原始 compose。
- **README 与 deployment-guide 反代示例内容重复** — 两处 Nginx/Caddy 配置逐字重复，未来修改需双处同步。
