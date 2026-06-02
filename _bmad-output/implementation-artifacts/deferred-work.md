# Deferred Work

> **清偿日期：** 2026-05-31
> **状态：** 全部 19 条已清偿（14 条修复 + 3 条验证已修复 + 2 条文档说明）

## Deferred from: code review of 10-3-cookie-auth-bridge (2026-06-01)

> 以下为 pre-existing issues，非本次 Cookie 认证桥接引入。

- [ ] **AccessDeniedPath 未配置** — 默认 `/Account/AccessDenied` 路径不存在，非 API 页面的 403 拒绝访问可能触发 404。预存问题，非本 Story 引入。
- [ ] **OnRedirectToAccessDenied 对所有请求返回 403** — 非 API 请求的重定向行为与修复后的 OnRedirectToLogin 不一致。预存问题，Story 明确声明"不改动"。
- [ ] **ConfirmEmail.cshtml.cs 缺少 `[AllowAnonymous]`** — 如启用 `RequireConfirmedAccount`，邮箱确认链接无法在未登录时访问。当前项目不启用此配置。
- [ ] **API 401 返回空内容体** — 未认证 API 请求返回裸 401（无 ProblemDetails JSON），与项目 `TypedResults.Problem()` 标准不一致。预存问题。
- [ ] **LoginWith2fa/RecoveryCode OnGet null 未处理** — 直接导航至 2FA 页面时可能触发 500。预存问题，Story 10.4 将应用 .NET 10 Bug workaround。
- [ ] **空 returnUrl 绕过 null 合并** — `LocalRedirect(returnUrl ?? "/")` 中 `?returnUrl=` 传递空字符串触发异常。预存问题，OnPost 中同样存在。

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

- [ ] **参数 null 校验缺失** — SendEmailAsync 的 email/subject/htmlMessage 未做 null 检查。C# nullable 已启用且 Identity UI 始终传入非 null 值，实际风险极低。接口契约（非 null 引用类型）不支持添加 null 检查。
- [ ] **无 CancellationToken 支持** — IEmailSender 接口本身不暴露 CancellationToken，30s 超时的 SMTP 操作无法中途取消。接口限制，非本 Story 能解决。
- [ ] **SMTP Port 未校验** — config.Port 为 0/负数/>65535 时会产生晦涩的 SMTP 异常。端口值由 Admin SMTP Settings 页面校验，非此 Service 职责。
## Deferred from: code review of 11-2-passkey-login-retention (2026-06-02)

- [Review][Defer] 开发环境跨端口链接 — /Identity/Account/Login 在 Client 开发服务器 (5001) 不可达，已知限制，仅影响开发环境 [Login.razor:13]

## Deferred from: code review of 11-4-samesite-docs-update (2026-06-02)

- [ ] **三处 Cookie SameSite/SecurePolicy 三元表达式重复** — 主 Cookie / TwoFactorUserId / Session 三处配置使用相同模式。可提取为 helper，但当前清晰度可接受。
- [ ] **TwoFactorRememberMeScheme 未显式配置** — 使用框架默认值，生产环境 SameSite=Lax（默认）+ SecurePolicy=SameAsRequest → 无 Secure 标志。Story 边界表已明确排除，预存问题。
- [ ] **UseForwardedHeaders 未配置** — Caddy 反向代理后 Request.IsHttps 可能不准确。预存问题，非本 Story 引入。
