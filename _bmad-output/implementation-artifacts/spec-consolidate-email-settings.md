---
title: '整合邮箱设置入口'
type: 'refactor'
created: '2026-05-31'
status: 'done'
baseline_commit: '6edb675f3f6b906e9843a705bfac2df9e6573499'
context: ['{project-root}/_bmad-output/project-context.md']
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** 设置页面的"账户信息"弹窗和"双因素认证"管理组件各有一个邮箱设置入口，功能重复且安全级别不一致——账户信息可绕过验证码直接修改邮箱，而 2FA 管理页的修改流程有完整的两步认证。

**Approach:** 以账户信息弹窗为邮箱唯一管理入口，升级其验证流程至不低于现有 2FA 修改流程的安全水平（密码重新认证 + 新邮箱验证码）。从 2FA 设置/管理组件中移除邮箱输入/修改功能，删除 `EmailTwoFactorSetup` 组件。后端新增专用邮箱验证端点，token 实现一次性使用。登录阶段 2FA 验证码优先读取 `user.Email`，逐步消除 `EmailForTwoFactor` 独立依赖。

## Boundaries & Constraints

**Always:**
- 修改邮箱前必须通过密码重新认证；启用 Email 2FA 前同样需要密码重新认证（`POST /api/auth/2fa/re-authenticate`）——与现有 2FA 流程安全级别一致
- 邮箱修改必须经过验证码确认：发送验证码到新邮箱 → 输入验证码 → 确认保存
- 验证码 token 一次性使用，通过 `ConcurrentDictionary` 内存缓存追踪已消费 token（TTL 5 分钟对齐 token 有效期）
- 账户信息弹窗是修改邮箱的唯一前端入口
- 所有新端点必须加 `.ProducesProblem(401)` 和 `RequireRateLimiting("email-verification")`
- 邮箱更新在单个 `UserManager.UpdateAsync` 调用中同时设置 `user.Email` 和 `user.EmailForTwoFactor`，确保原子性
- 邮箱修改成功后向旧邮箱发送通知邮件（旧邮箱非空时）
- 邮箱统一 `ToLowerInvariant()` 规范化后存储
- 用户无邮箱时不允许启用 Email 2FA，返回 400 提示先设置邮箱
- 不允许用户删除或清空邮箱（邮箱一旦设置后不可置空，仅可修改为其他有效邮箱）

**Ask First:**
- 速率限制策略的具体阈值（默认：每 60 秒 1 次发送，每邮箱每小时 3 次；验证码校验失败按用户限流，连续 5 次失败锁定 15 分钟）

**Never:**
- 不在 2FA 管理页保留邮箱修改入口（包括"修改接收验证码的邮箱"按钮、ModifyEmail 步骤）
- 不在 2FA 设置页保留邮箱输入字段——启用 Email 2FA 时直接使用 `user.Email`
- 不删除 `EmailForTwoFactor` 数据库列（仅通过代码层面统一，保持向后兼容）
- 不在客户端侧做最终邮箱验证——服务端 `EmailValidator.IsValid` 是唯一权威校验
- 不保留 `EmailTwoFactorSetup.razor` 组件——其功能内联到 `AccountInfoDialog` 和 `TwoFactorSetup`
- 不允许邮箱清空操作（包括空字符串和 null 值）

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| 账户信息中修改邮箱（正常流程） | 用户输入密码（重新认证）→ 输入新邮箱 → 发验证码 → 输入验证码 → 确认 | 邮箱更新成功，`user.Email` 和 `user.EmailForTwoFactor` 原子同步，向旧邮箱发通知，弹窗关闭 | 密码错误：`"密码错误"`；邮箱格式无效：`"邮箱格式无效"`；邮箱被占用：`"该邮箱已被其他账户使用"` |
| 账户信息中修改邮箱（首次设置，无旧邮箱） | 用户无旧邮箱，输入新邮箱 → 发验证码 → 确认 | UI 显示"设置邮箱"而非"修改邮箱"，不发送旧邮箱通知 | 同上 |
| 账户信息中完成邮箱验证 | 用户输入正确验证码 → 点击确认 | 邮箱更新成功，token 被标记已使用 | 验证码无效：`"验证码无效"`；token 已使用：`"验证码已使用，请重新发送"` |
| 快速连续提交同一 token | 相同 token+code 提交两次 | 第一次成功，第二次拒绝 | 400：`"验证码已使用，请重新发送"` |
| 重复发送验证码 | 用户在 60 秒内再次点击发送 | 拒绝，提示等待 | 429：`"请等待 N 秒后重新发送"` |
| 启用 Email 2FA（用户已有邮箱） | 用户已设置 `user.Email`，选择启用 Email 2FA | 验证码发送到 `user.Email`，无需输入邮箱 | 无邮箱：400 `"请先在账户信息中设置邮箱"` |
| 启用 Email 2FA（用户无邮箱） | `user.Email` 为空，点击 Email 2FA | 拒绝启用 | 400：`"请先在账户信息中设置邮箱"` |
| SMTP 未配置时 Email 2FA 不可见 | SMTP 未配置 | TwoFactorSetup 不显示 Email 方法选项 | 静默（现有行为，不受影响） |
| 修改邮箱验证码过期（5 分钟） | 用户输入过期验证码 | 提示过期 | 400：`"验证码已过期，请重新发送"` |
| 现有用户 `EmailForTwoFactor` 与 `user.Email` 分歧 | 旧数据中两个字段值不同 | 首次修改邮箱时自动同步为一致；部署时运行一次性 SQL 合并脚本 | SQL 脚本：`UPDATE AspNetUsers SET EmailForTwoFactor = Email WHERE EmailForTwoFactor IS NOT NULL AND EmailForTwoFactor != Email` |
| SMTP 发送失败 | SMTP 服务不可用或超时 | 用户看到"邮件发送失败，请稍后再试" | `EmailTwoFactorService.SendVerificationEmailAsync` 返回 false → 500 或降级提示 |
| 同时打开 AccountInfoDialog 和 TwoFactorManage | 两个弹窗同时打开 | AccountInfoDialog 修改邮箱成功后，TwoFactorManage 中的邮箱信息通过 SignalR/轮询不实时更新（下次打开生效） | 非阻塞——接受短暂不一致 |
| 密码重新认证失败 | 用户输入错误密码 | 拒绝修改流程 | 401：`"密码错误"` |

</frozen-after-approval>

## Code Map

- `src/BoxWise.Server/Endpoints/AuthEndpoints.cs` -- `PUT /api/auth/me` 修改邮箱时原子同步 `EmailForTwoFactor`，新增验证 token 校验
- `src/BoxWise.Server/Endpoints/EmailVerificationEndpoints.cs` -- **新文件**：`POST /api/auth/email/send-code` + `POST /api/auth/email/verify-code`
- `src/BoxWise.Server/Endpoints/TwoFactorEndpoints.cs` -- `SetupEmailAsync` 改为使用 `user.Email`；登录阶段验证码读取优先 `user.Email`
- `src/BoxWise.Server/Endpoints/TwoFactorModifyEndpoints.cs` -- 删除 `ModifyEmailAsync` / `VerifyModifyEmailAsync`（邮箱修改统一走账户信息入口）
- `src/BoxWise.Server/Services/EmailTwoFactorService.cs` -- `VerifyCode` 改为 `VerifyCodeOnce`，增加 `ConcurrentDictionary` 追踪已消费 token
- `src/BoxWise.Server/Program.cs` -- 注册 `"email-verification"` 速率限制策略
- `src/BoxWise.Shared/Dtos/UpdateProfileRequest.cs` -- 新增 `OperationToken` 可选字段
- `src/BoxWise.Client/Components/AccountInfoDialog.razor` -- 三步流程（密码认证 → 新邮箱+验证码 → 确认保存），区分"设置"/"修改"UI
- `src/BoxWise.Client/Components/TwoFactorSetup.razor` -- 移除 `EmailTwoFactorSetup` 引用，内联流程（需传 X-Session-Token）
- `src/BoxWise.Client/Components/TwoFactorManage.razor` -- 移除 ModifyEmail 步骤及所有相关方法
- `src/BoxWise.Client/Components/EmailTwoFactorSetup.razor` -- **删除**（最后执行，确保前两步引用已清理）
- `src/BoxWise.Client/Services/AuthService.cs` -- 新增 `SendEmailVerificationCodeAsync`、`VerifyEmailCodeAsync`、`UpdateEmailAsync`；删除 `ModifyEmailAsync`/`VerifyModifyEmailAsync`

## Tasks & Acceptance

**Execution:**

**后端 — 新端点与安全升级：**
- [x] `src/BoxWise.Server/Program.cs` -- 注册 `"email-verification"` 固定窗口速率限制策略（每 60s 1 次发送，每邮箱每小时 3 次；验证校验按用户限流，5 次失败锁定 15 分钟） -- 防滥用
- [x] `src/BoxWise.Server/Services/EmailTwoFactorService.cs` -- `VerifyCode` 改为 `VerifyCodeOnce`（`TryAdd` 原子操作 + 惰性清理过期 token）；`SendVerificationEmailAsync` 增加 `purpose` 参数区分"2fa"/"email-change"邮件模板 -- token 一次性使用 + UX 清晰
- [x] `src/BoxWise.Shared/Dtos/UpdateProfileRequest.cs` -- 新增 `string? OperationToken` 字段（可选；纯用户名更新时传 null） -- DTO 随 API 变更
- [x] `src/BoxWise.Server/Endpoints/EmailVerificationEndpoints.cs` -- **新建**：`POST /api/auth/email/send-code`（需 X-Session-Token，向新邮箱发码，预检查邮箱唯一性）+ `POST /api/auth/email/verify-code`（无需 session token，验证码校验，成功后返回 Data Protection 自包含 operation token，含 userId+verifiedEmail+5min TTL） -- 支撑账户信息验证流程
- [x] `src/BoxWise.Server/Endpoints/AuthEndpoints.cs` -- `UpdateProfileAsync` 改为：仅当 `NewEmail != null` 时要求 operation token（null = 纯用户名更新，保持现有行为）；`NewEmail` 为空字符串时拒绝（400 "邮箱不能为空"）；验证 operation token → 提取已验证邮箱 → 原子更新 `user.Email` + `user.EmailForTwoFactor`（`ToLowerInvariant` 规范化）→ 异步通知旧邮箱（失败不影响主流程） -- 邮箱唯一写入路径
- [x] `src/BoxWise.Server/Endpoints/TwoFactorEndpoints.cs` -- `SetupEmailAsync` 不再接收 email 参数，改为读取 `user.Email`；登录阶段 `ChallengeAsync`/`SendChallengeCodeAsync` 改为 `!string.IsNullOrEmpty(user.Email) ? user.Email : user.EmailForTwoFactor` -- 消除双数据源依赖，覆盖空字符串边界
- [x] `src/BoxWise.Server/Endpoints/TwoFactorModifyEndpoints.cs` -- 删除 `ModifyEmailAsync` 和 `VerifyModifyEmailAsync` 端点 -- 消除残余攻击面

**前端 — UI 整合：**
- [x] `src/BoxWise.Client/Services/AuthService.cs` -- 新增 `SendEmailVerificationCodeAsync(sessionToken, newEmail)`、`VerifyEmailCodeAsync(code, token)`、`UpdateEmailAsync(operationToken, newEmail)`；删除 `ModifyEmailAsync`、`VerifyModifyEmailAsync` 死代码 -- 支持新验证流程 + 防止死代码 404
- [x] `src/BoxWise.Client/Components/AccountInfoDialog.razor` -- 重构为三步流程（密码认证 → 输入新邮箱+验证码 → 确认保存），区分"设置"/"修改"UI，session token 过期时自动回退 Step 1 并保留已输入邮箱，邮箱字段不允许为空 -- 安全且唯一入口
- [x] `src/BoxWise.Client/Components/TwoFactorSetup.razor` -- **先**移除 `EmailTwoFactorSetup` 组件引用和内联 `SetupEmail` 步骤（需确保调用 re-authenticate 获取 X-Session-Token 传入 `SetupEmailAsync`），Email 方法改用 `user.Email` -- 不再要求输入邮箱
- [x] `src/BoxWise.Client/Components/TwoFactorManage.razor` -- **先**移除 ModifyEmail 步骤、`StartModifyEmail`/`SendEmailAuthCodeAsync`/`VerifyEmailAuth` 方法、`EmailTwoFactorSetup` 引用 -- 邮箱不再在此管理
- [x] `src/BoxWise.Client/Components/EmailTwoFactorSetup.razor` -- **最后删除**（确保前两步引用已清理，同一 commit 原子完成） -- 死代码清理

**数据迁移：**
- [x] 部署脚本 -- 运行一次性 SQL 修复已有 `Email ≠ EmailForTwoFactor` 分歧：`UPDATE AspNetUsers SET EmailForTwoFactor = Email WHERE Email IS NOT NULL AND Email != '' AND (EmailForTwoFactor IS NULL OR EmailForTwoFactor != Email)` -- 仅当 Email 有有效值时同步，绝不覆盖 EmailForTwoFactor 为 NULL/空（保留外部登录用户的独立 2FA 邮箱）

**测试：**
- [x] `src/BoxWise.Server.Tests/Endpoints/EmailVerificationEndpointsTests.cs` -- 测试：发送验证码（正常+限流+邮箱预检查）、验证码校验（正确+错误+过期+并发重复使用）、操作 token（正确提取+过期+邮箱不匹配） -- 覆盖新端点
- [x] `src/BoxWise.Server.Tests/Endpoints/AuthEndpointsTests.cs` -- 新增 `UpdateProfile_EmailChange_SyncsEmailForTwoFactor`、`UpdateProfile_EmailChange_WithoutToken_Rejected`、`UpdateProfile_UsernameOnly_NoTokenRequired` -- 覆盖条件 token 校验
- [x] `src/BoxWise.Server.Tests/Services/EmailTwoFactorServiceTests.cs` -- 新增 `VerifyCodeOnce_TokenReuse_ReturnsFalse`、`VerifyCodeOnce_Concurrent_OneSucceeds`、`VerifyCodeOnce_TokenExpired_ReturnsFalse` -- 覆盖一次性使用逻辑和 TOCTOU 安全

**Acceptance Criteria:**
- Given 用户在账户信息弹窗中输入正确密码，when 输入新邮箱并完成验证码验证，then 邮箱更新成功，`user.EmailForTwoFactor` 同步更新，旧邮箱收到通知
- Given 用户提交已使用过的验证码 token（包括并发提交），when 尝试再次验证，then 返回 400 "验证码已使用，请重新发送"
- Given 用户仅修改用户名，when 提交 `UpdateProfileRequest`（`NewEmail = null`），then 不要求 operation token，用户名更新成功
- Given 用户在 60 秒内已发送过验证码，when 再次点击发送，then 返回 429 限流提示
- Given 用户未设置邮箱，when 在 2FA 设置中尝试启用 Email 方法，then 提示先设置邮箱，不发送验证码
- Given 用户已设置邮箱，when 在 2FA 设置中启用 Email 方法，then 验证码直接发送到 `user.Email`，无邮箱输入步骤
- Given 用户打开 2FA 管理页，when 查看已配置的 Email 方法，then 不显示"修改邮箱"按钮或入口
- Given 用户尝试清空邮箱，when 提交空字符串，then 返回 400 "邮箱不能为空"
- Given 现有用户 `EmailForTwoFactor` ≠ `user.Email` 或任一为 NULL/空字符串，when 部署迁移脚本运行，then 两个字段被同步为一致值

## Design Notes

**验证流程 — 修改邮箱（AccountInfoDialog）：**
```
Step 1: 密码重新认证
  [输入当前密码] → POST /api/auth/2fa/re-authenticate → 获得 session token (5min TTL)

Step 2: 输入新邮箱 + 发送验证码
  [显示当前邮箱（如有）] → [输入新邮箱] → POST /api/auth/email/send-code (header: X-Session-Token)
  → 验证码发送到新邮箱 → 返回 verification token
  → EmailTwoFactorService.SendVerificationEmailAsync 接受 purpose 参数:
    "email-change" → 邮件正文："您的 BoxWise 邮箱修改验证码为"
    "2fa" → 邮件正文："您的 BoxWise 双因素认证验证码为"（现有行为）

Step 3: 输入验证码 + 确认
  [输入 6 位验证码] → POST /api/auth/email/verify-code (body: code + verification token)
  → 注意: verify-code 端点 needs-session-token: false
    (登录 cookie 已提供用户身份认证, password re-auth 在 send-code 环节完成,
     verify-code 仅验证邮箱访问权, 与现有 2FA challenge verify 行为一致)
  → 成功后生成 operation token → PUT /api/auth/me (body: operationToken + newEmail)
  → 邮箱更新成功
```

**Operation Token 机制（Data Protection 自包含令牌）：**
```csharp
// EmailVerificationEndpoints.VerifyCodeAsync 成功后生成:
var operationToken = _protector.Protect(
    $"{userId}|{verifiedEmail}|{DateTime.UtcNow.AddMinutes(5):O}");
// purpose: "email-operation-token" (与 verification token 不同 protector purpose)
// 返回给客户端: { operationToken: "<encrypted>" }

// AuthEndpoints.UpdateProfileAsync 验证:
var parts = _protector.Unprotect(operationToken).Split('|');
var tokenUserId = parts[0];  // 提取 token 绑定的用户
var boundEmail = parts[1];   // 提取已验证邮箱
var expiry = DateTime.Parse(parts[2]);
if (tokenUserId != user.Id) return Problem("操作令牌无效", statusCode: 400); // 防止跨用户令牌滥用
if (expiry <= DateTime.UtcNow) return Problem("操作已过期");
if (boundEmail != request.NewEmail) return Problem("邮箱不匹配");
// 通过验证 → 执行更新
```

**Token 一次性使用实现（已修复 TOCTOU + 内存泄漏）：**
```csharp
// EmailTwoFactorService 新增
private static readonly ConcurrentDictionary<string, DateTime> _consumedTokens = new();
private static readonly TimeSpan _tokenTtl = TimeSpan.FromMinutes(5);
private static DateTime _lastCleanup = DateTime.UtcNow;
private static readonly object _cleanupLock = new();

public bool VerifyCodeOnce(string userId, string email, string code, string token)
{
    var tokenHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    // TryAdd 是原子操作 —— 修复 TOCTOU 竞态
    if (!_consumedTokens.TryAdd(tokenHash, DateTime.UtcNow.Add(_tokenTtl)))
        return false; // 已存在 → 已消费

    var result = VerifyCode(userId, email, code, token); // 现有逻辑
    if (!result)
        _consumedTokens.TryRemove(tokenHash, out _); // 验证失败 → 允许重试（token 未被消费）
    else
        CleanupExpiredTokens(); // 惰性清理: 每次成功验证时顺便清理过期条目
    return result;
}

private static void CleanupExpiredTokens()
{
    var now = DateTime.UtcNow;
    if (now - _lastCleanup < TimeSpan.FromMinutes(2)) return; // 最多每 2 分钟清理一次
    lock (_cleanupLock)
    {
        if (now - _lastCleanup < TimeSpan.FromMinutes(2)) return;
        var expired = _consumedTokens.Where(kv => kv.Value < now).Select(kv => kv.Key).ToList();
        foreach (var key in expired) _consumedTokens.TryRemove(key, out _);
        _lastCleanup = now;
    }
}
```

**登录阶段 2FA 邮箱读取（已修复空字符串 fallback）：**
```csharp
// string.IsNullOrEmpty 覆盖 null 和 "" 两种情况
var emailFor2Fa = !string.IsNullOrEmpty(user.Email) ? user.Email : user.EmailForTwoFactor;
```

**UpdateProfileAsync 条件 token 校验：**
```csharp
// PUT /api/auth/me 修改逻辑
if (request.NewEmail != null) // null = 无邮箱变更, 保持现有行为
{
    if (string.IsNullOrEmpty(request.NewEmail))
        return Problem("邮箱不能为空", statusCode: 400); // 不允许清空

    if (string.IsNullOrEmpty(request.OperationToken))
        return Problem("邮箱修改需要验证码确认", statusCode: 400);

    // 验证 operation token + 提取已验证邮箱
    var (ok, verifiedEmail) = ValidateOperationToken(request.OperationToken, user.Id);
    if (!ok) return Problem("操作已过期，请重新验证", statusCode: 400);
    if (verifiedEmail != request.NewEmail) return Problem("邮箱不匹配", statusCode: 400);

    // 原子更新
    var oldEmail = user.Email;
    user.Email = request.NewEmail.ToLowerInvariant();
    user.EmailForTwoFactor = request.NewEmail.ToLowerInvariant();
    await userManager.UpdateAsync(user);

    // 旧邮箱通知（异步，失败不影响主流程）
    if (!string.IsNullOrEmpty(oldEmail))
        _ = Task.Run(() => emailService.SendChangeNotificationAsync(oldEmail, user.UserName));
}
// 用户名等其他字段更新保持现有逻辑
```

**Session Token 过期 UI 处理：**
```
AccountInfoDialog 三步流程中，send-code 或 verify-code 返回"会话令牌无效或已过期"时:
- 显示错误提示"操作超时，请重新验证"
- 自动回退到 Step 1（密码重新认证），保留用户已输入的新邮箱
```

**组件删除依赖顺序：**
```
必须先重构 TwoFactorSetup.razor 和 TwoFactorManage.razor 移除对 EmailTwoFactorSetup 的引用，
再删除 EmailTwoFactorSetup.razor 文件。建议在同一 commit 中原子完成。
```

## Verification

**Commands:**
- `dotnet build` -- expected: 0 errors, 0 warnings
- `dotnet test BoxWise.slnx` -- expected: all existing + new tests pass
- `dotnet ef migrations add ConsolidateEmailManagement` -- expected: 无模型变更（仅列注释，如需要）

**Manual checks:**
- 账户信息弹窗：密码认证 → 修改邮箱 → 验证码 → 确认 → 刷新确认邮箱已更新
- 2FA 设置：启用 Email 2FA → 无邮箱输入 → 验证码直接发送到账户邮箱
- 2FA 管理：已启用 Email 2FA → 无"修改邮箱"按钮 → 仅显示状态信息
- 旧端点验证：`POST /api/auth/2fa/modify/email` → 404

## Spec Change Log

<!-- Append-only. Populated during review loops. -->

## Suggested Review Order

**入口：先看这个理解整体设计**

- 新增的邮箱验证端点（send-code + verify-code）和 operation token 机制
  [`EmailVerificationEndpoints.cs:1`](../../src/BoxWise.Server/Endpoints/EmailVerificationEndpoints.cs#L1)

**核心安全流程**

- `UpdateProfileAsync`——条件 token 校验、NormalizedEmail 修复、原子同步 EmailForTwoFactor
  [`AuthEndpoints.cs:234`](../../src/BoxWise.Server/Endpoints/AuthEndpoints.cs#L234)

- `ValidateOperationToken`——operation token 解析 + userId 绑定 + 一次性消费
  [`AuthEndpoints.cs:374`](../../src/BoxWise.Server/Endpoints/AuthEndpoints.cs#L374)

**邮箱服务层**

- `VerifyCodeOnce`——TryAdd 原子操作 + 惰性清理 + `TryConsumeOperationToken`
  [`EmailTwoFactorService.cs:22`](../../src/BoxWise.Server/Services/EmailTwoFactorService.cs#L22)

- `SendVerificationEmailAsync`——purpose 参数区分邮件模板
  [`EmailTwoFactorService.cs:128`](../../src/BoxWise.Server/Services/EmailTwoFactorService.cs#L128)

**2FA 端点清理**

- `SetupEmailAsync` 改用 `user.Email`，登录 fallback 覆盖空字符串
  [`TwoFactorEndpoints.cs:429`](../../src/BoxWise.Server/Endpoints/TwoFactorEndpoints.cs#L429)

- 删除 `ModifyEmailAsync`/`VerifyModifyEmailAsync` 端点
  [`TwoFactorModifyEndpoints.cs:1`](../../src/BoxWise.Server/Endpoints/TwoFactorModifyEndpoints.cs#L1)

**前端整合**

- AccountInfoDialog——三步验证流程、密码认证→验证码→确认
  [`AccountInfoDialog.razor:1`](../../src/BoxWise.Client/Components/AccountInfoDialog.razor#L1)

- TwoFactorSetup——内联 Email 设置流程，移除 EmailTwoFactorSetup 引用
  [`TwoFactorSetup.razor:1`](../../src/BoxWise.Client/Components/TwoFactorSetup.razor#L1)

- TwoFactorManage——移除邮箱修改入口和 ModifyEmail 步骤
  [`TwoFactorManage.razor:1`](../../src/BoxWise.Client/Components/TwoFactorManage.razor#L1)

- AuthService——新增验证方法 + 删除死代码
  [`AuthService.cs:1`](../../src/BoxWise.Client/Services/AuthService.cs#L1)

**DTO / 配置 / 模型**

- `UpdateProfileRequest` 新增 `OperationToken` 字段
  [`UpdateProfileRequest.cs:1`](../../src/BoxWise.Shared/Dtos/UpdateProfileRequest.cs#L1)

- 新增邮箱验证 DTO
  [`EmailVerificationDtos.cs:1`](../../src/BoxWise.Shared/Dtos/EmailVerificationDtos.cs#L1)

- 注册 `email-verification` 限流策略 + `MapEmailVerificationEndpoints`
  [`Program.cs:1`](../../src/BoxWise.Server/Program.cs#L1)

- 更新 `EmailForTwoFactor` 注释为同步说明
  [`AppUser.cs:17`](../../src/BoxWise.Server/Models/AppUser.cs#L17)

**测试**

- 邮箱验证端点测试（正常+限流+重复+并发）
  [`EmailVerificationEndpointsTests.cs:1`](../../src/BoxWise.Server.Tests/Endpoints/EmailVerificationEndpointsTests.cs#L1)

- AuthEndpoints 条件 token 校验测试
  [`AuthEndpointsTests.cs:1`](../../src/BoxWise.Server.Tests/Endpoints/AuthEndpointsTests.cs#L1)

- EmailTwoFactorService 一次性使用测试
  [`EmailTwoFactorServiceTests.cs:1`](../../src/BoxWise.Server.Tests/Services/EmailTwoFactorServiceTests.cs#L1)
