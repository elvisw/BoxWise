# Deferred Work

> **清偿日期：** 2026-05-29
> **状态：** 全部 31 条已清偿（19 条前期 + 12 条 Epic 8 新清偿）

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

> **清偿日期：** 2026-05-29
> **状态：** 全部 12 条已清偿

- [x] ~~Email 唯一性检查 TOCTOU 竞态 — `AuthEndpoints.cs:225-232`~~ → SetEmailAsync 包裹 try/catch DbUpdateException，冲突时返回 409
- [x] ~~管理员创建/更新密码验证路径不一致 — `Program.cs:259,281`~~ → 添加设计决策注释：种子管理员密码为特权操作，跳过验证器是有意为之
- [x] ~~邮箱格式校验宽松（仅 `Contains('@')`） — `AuthEndpoints.cs:217`~~ → 改用 `System.Net.Mail.MailAddress` 进行 RFC 格式校验
- [x] ~~Email vs EmailForTwoFactor 不同步 — `AppUser.cs:10` vs `AuthEndpoints.cs:234`~~ → Email 变更时同步更新 EmailForTwoFactor（当旧 2FA 邮箱匹配时）
- [x] ~~客户端缺少邮箱格式前端校验 — `AccountInfoDialog.razor:17-19`~~ → 添加 Contains('@') + 首尾位置检查，无效时不提交
- [x] ~~版本回滚时 Email 可能被清空 — `CookieAuthenticationStateProvider.cs:29`~~ → API 返回 null Email 时保留 AppState 原有值
- [x] ~~LoginAsync 未传递 Email 给 SetUser — `AuthService.cs:35`~~ → LoginResponse 新增 Email 字段，LoginAsync 传递 loginResponse.Email
- [x] ~~启动时并发创建管理员竞态 — `Program.cs:253-270`~~ → CreateAsync 和 AddToRoleAsync 包裹 try/catch DbUpdateException
- [x] ~~URL 长度/URI 格式的极端边界 — `TotpSetup.razor:92-94`~~ → 添加 _qrCodeUri.Length > 2048 和 Http.BaseAddress null 检查
- [x] ~~保存按钮 disabled 逻辑无法感知服务器错误 — `AccountInfoDialog.razor:23`~~ → 新增 _hasError 字段，错误后按钮保持可用
- [x] ~~AppState 无线程同步保护 — `AppState.cs:5-8`~~ → SetUser/Clear/SetContinuousLocation/ClearContinuousLocation 方法体包裹 lock
- [x] ~~NewUsername.Trim() 无 null 守卫 — `AuthEndpoints.cs:169`~~ → 改为 `request.NewUsername?.Trim() ?? ""`

## Deferred from: code review of Epic 8 deferred resolution (2026-05-29)

- [ ] QR 码 URL 长度检查基于原始 URI 而非编码后长度 — `TotpSetup.razor:93` — OTP auth URI 通常 ~100 字符，2048 的原始长度几乎不会达到，编码膨胀风险极低
- [ ] 客户端/服务端邮箱校验规则不一致 — `AccountInfoDialog.razor:56` vs `AuthEndpoints.cs:226` — 客户端宽松检查，服务端 RFC 严格，两端不一致
- [ ] CookieAuthStateProvider 过期邮箱残留 — `CookieAuthenticationStateProvider.cs:29-30` — user.Email 为 null 时回退到旧值，无法区分"未设置"和"不返回"
- [ ] 客户端错误提示在输入变更后不清除 — `AccountInfoDialog.razor:48,73` — 用户修正输入后错误文本仍显示
- [ ] IsValidEmail 使用异常作为流程控制 — `AuthEndpoints.cs:332-339` — 无 MailAddress.TryParse，高性能场景可加正则预检
- [ ] QR 码无法显示时无用户反馈 — `TotpSetup.razor:93` — QrCodeImageUrl 返回 null 时静默消失
- [ ] CookieAuthStateProvider 漏检 IsAdmin/PasswordManagedByEnv 变更 — `CookieAuthenticationStateProvider.cs:28` — 角色变更后客户端不更新
- [ ] 客户端邮箱校验允许多重 @ 符号 — `AccountInfoDialog.razor:56` — 服务端会拒绝，但客户端即时反馈缺失
