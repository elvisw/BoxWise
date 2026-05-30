# Deferred Work

> **清偿日期：** 2026-05-28
> **状态：** 全部 19 条已清偿（7 条修复 + 9 条验证已修复 + 3 条已验证无需改动）

## Deferred from: code review of 2fa-multi-method-login (2026-05-30)

> 以下为 pre-existing issues，非本次 2FA 多方法修复引入，留待后续专项处理。

- [ ] **WebAuthn 旧值 3→4 迁移风险** — `TwoFactorMethod.cs` 将 WebAuthn 从 3 改为 4，旧数据库中如有 TwoFactorMethod=3 的记录会被解释为 TOTP|Email。当前 WebAuthn 从未实现，生产无风险。WebAuthn 上线前需补充数据迁移。
- [ ] **Admin Index.cshtml `ConfiguredMethods is not null` 对值类型永远为 true** — 预存问题，不影响运行时行为（代码层映射为 string?）。
- [ ] **多处 UpdateAsync 结果被丢弃** — ChallengeAsync、AdminTwoFactorEndpoints、ResetTwoFactor 等处不检查 IdentityResult.Succeeded。
- [ ] **challenge/send-challenge-code 端点无速率限制** — 攻击者可无限发送邮件。
- [ ] **Flags.ToString() 客户端解析脆弱** — Settings.razor 用 Contains("TOTP") 解析 flags 字符串，依赖未文档化的 .NET 格式化行为。
- [ ] **EmailForTwoFactor 清除不一致** — RecoveryCodeService 清除 EmailForTwoFactor，但 AdminTwoFactorEndpoints/ResetTwoFactor/Program.cs 不清除。
- [ ] **无速率限制** — challenge/send-challenge-code 端点未配置 RequireRateLimiting。
- [ ] **恢复码按钮无条件显示** — Login.razor 不检查用户是否有恢复码，需要 TwoFactorChallengeResponse 增加 HasRecoveryCodes 字段。
- [ ] **VerifyAsync Email 路径 null-forgiving** — EmailForTwoFactor! 在数据损坏时传递 null 给 VerifyCode。

## Deferred from: code review of 2FA-login-grace-period-fix (2026-05-30)

> 以下为 pre-existing issues，非本次 2FA 登录修复引入，留待后续专项处理。

- [ ] **并发首次登录 DbUpdateConcurrencyException → 500** — `AuthEndpoints.cs:86`，同一用户双请求并发到达时，第二个 `UpdateAsync` 因 ConcurrencyStamp 乐观并发抛异常，`LoginAsync` 未捕获。低流量下概率极低。建议：catch 并重试（重新读取用户后重试）
- [ ] **Blazor WASM 版本偏差可能绕过 2FA 引导** — `LoginResponse.cs` 新增字段在旧版 WASM 客户端被 JSON 反序列化忽略，旧客户端直接视为登录成功。建议：添加版本检查或服务端降级路径
- [ ] **IsPasswordManagedByEnv 在 AuthService 中硬编码 false** — `AuthService.cs:37`，`RequiresTwoFactorSetup` 分支（及正常路径）手动传 false。服务器端已完成 `isSpecificAdmin` 计算但未利用。建议：LoginResponse 传递该字段并由 AuthService 使用
- [ ] **中断 TOTP 设置后残余密钥仅在首次登录清理** — `AuthEndpoints.cs:82-85`，宽限期过期路径不清理残留。建议：在宽限期过期分支中也清理或统一在 GenerateTotpSecretAsync 中覆盖
- [ ] **TwoFactorGracePeriodUntil 在 2FA 启用后未清除** — `TwoFactorService.cs:87-91`，`VerifyTotpSetupAsync` 设置 `TwoFactorEnabled=true` 但不清除宽限期字段。建议：启用 2FA 后将 `TwoFactorGracePeriodUntil` 置 null

## Deferred from: code review of spec-smtp-config-test-email (2026-05-30)

> 以下为 pre-existing issues，非本次 SMTP 配置管理功能引入，留待后续专项处理。

- [ ] **ChallengeAsync fire-and-forget 静默失败** — `TwoFactorEndpoints.cs:211` 中 `_ = SendVerificationEmailAsync(...).ContinueWith(...)` 不检查发送结果，SMTP 不可用时用户被锁在登录页无回退路径。建议：await 发送并在失败时提供 TOTP 备选
- [ ] **EmailTwoFactorService 无异常分类** — `SendVerificationEmailAsync` 用裸 `catch(Exception)` 统一返回 false，无法区分认证失败/连接超时/DNS 失败。建议：分层 catch AuthenticationException/SmtpCommandException/SocketException
- [ ] **SetupEmailAsync 邮箱校验过松** — `TwoFactorEndpoints.cs:377` 仅 `email.Contains('@')`，与 `SendTestEmailAsync` 的 `MailAddress` 严格校验不一致。建议：统一使用 `MailAddress` 或 `MailboxAddress.TryParse`
- [ ] **TLS/SSL 配置过于简单** — `useSsl: port == 465` 硬编码，建议改用 `SecureSocketOptions.Auto` 自动协商，并显式设置 `SslProtocols = Tls12 | Tls13`
- [ ] **SwitchMethodAsync 拒绝 Email 但 SetupEmailAsync 允许** — API 行为不一致，`TwoFactorService.cs:177` 返回 false 但设置流程已完成。建议：移除限制或更新注释说明设计原因

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
