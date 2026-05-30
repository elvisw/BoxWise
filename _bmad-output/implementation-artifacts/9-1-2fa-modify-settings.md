# Story 9.1: 2FA 设置管理 —— 服务端与客户端完整实现

---
baseline_commit: ce4b6b5954f4cfa2f6a558aa440ac82881627270
---
Status: done

## Story

As a 已配置 2FA 的用户，
I want 通过 2FA 验证后修改邮箱地址、重置 TOTP 应用、重新生成恢复码，
so that 我可以自行管理 2FA 设置，无需管理员介入。

## Acceptance Criteria

1. **Given** 用户已配置 TOTP 2FA **When** 打开设置 → 双因素认证 → 管理对话框 → 2FA 验证通过 → 选择重置 TOTP → 扫描新 QR 码 → 输入新验证码 **Then** TOTP 密钥已更新，旧密钥失效，2FA 保持启用状态
2. **Given** 用户已配置 Email 2FA **When** 打开管理对话框 → 2FA 验证通过 → 选择修改邮箱 → 输入新邮箱 → 发送验证码 → 验证新邮箱 **Then** EmailForTwoFactor 更新为新邮箱，后续登录验证码发送到新邮箱
3. **Given** 用户已配置 2FA **When** 打开管理对话框 → 2FA 验证通过 → 点击重新生成恢复码 **Then** 获得 8 个新恢复码，旧恢复码全部失效
4. **Given** 用户未通过 2FA 验证 **When** 尝试调用任何修改端点 **Then** 返回 401 或 session token 无效错误
5. **Given** 用户有 N 个有效恢复码 **When** 使用其中一个恢复码通过 `/modify/authenticate` 验证 **Then** 成功获得 session token，且该用户仍有 N 个有效恢复码（未消耗）
6. **And** `dotnet test BoxWise.slnx` 全部通过，新增测试覆盖：端点正常/错误路径 + TOTP 双密钥窗口 + 恢复码非消耗验证 + session token purpose 校验

## Tasks / Subtasks

- [x] Task 1: 数据模型 + Service 层变更 (AC: 4, 5)
  - [x] 1.1 AppUser 新增 `PendingTotpSecretKey` 字段，生成 EF 迁移
  - [x] 1.2 TwoFactorService.GenerateSessionToken 新增可选 purpose 参数；ValidateSessionToken 新增可选 expectedPurpose 参数（默认 "2fa-setup" 保持向后兼容）；提取私有 TryParseToken helper 避免重复
  - [x] 1.3 TwoFactorService 新增 `GeneratePendingTotpSecretAsync` / `VerifyPendingTotpSetupAsync`
  - [x] 1.4 TwoFactorService.VerifyTotpChallengeAsync 支持双密钥窗口（仅 modify 验证时检查 PendingTotpSecretKey fallback；登录时仅检查 TotpSecretKey，保持旧密钥优先）
  - [x] 1.5 RecoveryCodeService 新增 `ValidateRecoveryCodeAsync`（非消耗验证）

- [ ] Task 2: API 端点实现 (AC: 1-5)
  - [x] 2.1 创建 `TwoFactorModifyEndpoints.cs`（7 个端点，全部加 `.RequireRateLimiting("2fa-modify")`）
  - [x] 2.2 在 `Program.cs` 中注册 `2fa-modify` 速率限制策略
  - [x] 2.3 旧 `/recovery/regenerate` 端点加 XML doc 标记废弃（不改代码逻辑）
  - [x] 2.4 Program.cs 注册 `MapTwoFactorModifyEndpoints()`

- [ ] Task 3: Client Service + UI 组件 (AC: 1-3)
  - [x] 3.1 AuthService 新增 7 个 modify API 方法
  - [x] 3.2 TotpSetup.razor / EmailTwoFactorSetup.razor 新增 `ModifyMode` 参数
  - [x] 3.3 创建 `TwoFactorManage.razor` 管理对话框
  - [x] 3.4 Settings.razor 根据 2FA 状态路由到管理/设置对话框

- [ ] Task 4: 测试 (AC: 6)
  - [x] 4.1 RecoveryCodeServiceTests 新增 ValidateRecoveryCodeAsync 测试
  - [x] 4.2 创建 TwoFactorModifyEndpointsTests（端点层测试）
  - [x] 4.3 全量回归测试通过

## Dev Notes

### 核心架构决策

1. **SessionToken purpose 参数化**：`GenerateSessionToken(userId, clientIp, purpose = "2fa-setup")` — modify 流程传 "2fa-modify"，15 分钟过期
2. **TOTP 双密钥窗口**：`PendingTotpSecretKey` 暂存新密钥，旧密钥保持有效直到 verify 确认
3. **恢复码非消耗验证**：`ValidateRecoveryCodeAsync` 仅校验哈希不销毁，区别于登录时的核选项 `VerifyRecoveryCodeAsync`
4. **端点独立文件**：`TwoFactorModifyEndpoints.cs` — 独立于 `TwoFactorEndpoints.cs`，purpose 不同
5. **组件复用**：`TotpSetup`/`EmailTwoFactorSetup` 加 `ModifyMode` 参数，默认 false 保持向后兼容

### 端点规格

所有端点位于 `/api/auth/2fa/modify/`，需 `X-Session-Token` header（purpose="2fa-modify"）：

| 端点 | 方法 | 用途 | 请求体 | 响应 | 速率限制 |
|------|------|------|--------|------|---------|
| `/authenticate` | POST | 2FA 身份验证（TOTP/Email/RecoveryCode） | `VerifyTwoFactorRequest` | `ReAuthenticateResponse` | `2fa-modify` |
| `/send-code` | POST | 向已配置邮箱发送验证码 | 空 body | `EmailTwoFactorSetupResponse` | `2fa-modify` |
| `/email` | POST | 修改邮箱 — 发送验证码到新邮箱 | `SetupEmailTwoFactorRequest` | `EmailTwoFactorSetupResponse` | `2fa-modify` |
| `/email/verify` | POST | 修改邮箱 — 验证新邮箱 | `VerifyTwoFactorRequest` | `200 OK` | `2fa-modify` |
| `/totp` | POST | 重置 TOTP — 生成新密钥 | 空 body | `TwoFactorSetupResponse` | `2fa-modify` |
| `/totp/verify` | POST | 重置 TOTP — 验证新密钥 | `VerifyTwoFactorRequest` | `200 OK` | `2fa-modify` |
| `/recovery/regenerate` | POST | 重新生成恢复码 | 空 body | `RecoveryCodesResponse` | `2fa-modify` |

**注意：** 所有 modify 端点统一使用 POST（与现有 2FA 端点保持一致）。sync modify-flow.md 中对应的 PUT 也改为 POST。

### authenticate 端点路由逻辑

```
method=TOTP → TwoFactorService.VerifyTotpChallengeAsync(user, code)
method=Email → 需先调 /send-code 获取 email token，再传 code+token
method=RecoveryCode → RecoveryCodeService.ValidateRecoveryCodeAsync(user, code)
成功 → 返回 modify session token (purpose="2fa-modify", 15min)
```

### 关键文件（UPDATE 文件需保留现有行为）

| 文件 | 变更类型 | 注意事项 |
|------|---------|---------|
| `AppUser.cs` | UPDATE | 在 `TotpSecretKey` 后添加 `PendingTotpSecretKey`，不修改现有字段 |
| `TwoFactorService.cs` | UPDATE | GenerateSessionToken/ValidateSessionToken 新增可选 purpose 参数（默认值保持向后兼容）；新增 GeneratePendingTotpSecretAsync/VerifyPendingTotpSetupAsync；修改 VerifyTotpChallengeAsync 支持双密钥窗口 |
| `RecoveryCodeService.cs` | UPDATE | 新增方法，`VerifyRecoveryCodeAsync` 不变 |
| `TwoFactorEndpoints.cs` | UPDATE | 仅加注释标记废弃，不改代码 |
| `Program.cs` | UPDATE | 加一行 `MapTwoFactorModifyEndpoints()` |
| `AuthService.cs` | UPDATE | 新增 7 个方法，不改现有方法 |
| `TotpSetup.razor` | UPDATE | 新增 `ModifyMode` 参数默认 false，不改现有流程 |
| `EmailTwoFactorSetup.razor` | UPDATE | 同上 |
| `Settings.razor` | UPDATE | `OpenTwoFactorSetupDialog` 加 `_twoFactorEnabled` 分支 |
| `TwoFactorModifyEndpoints.cs` | NEW | 遵循 `TwoFactorEndpoints.cs` 的模式 |
| `TwoFactorManage.razor` | NEW | 遵循 `TwoFactorSetup.razor` 的 MudDialog 模式 |
| `TwoFactorModifyEndpointsTests.cs` | NEW | 遵循 `TwoFactorEndpointsTests.cs` 的测试模式 |

### 端点实现模式（严格遵循 TwoFactorEndpoints.cs 模式）

```csharp
public static class TwoFactorModifyEndpoints
{
    public static RouteGroupBuilder MapTwoFactorModifyEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth/2fa/modify");
        group.MapPost("/authenticate", AuthenticateForModifyAsync)
            .WithTags("2FA/Modify").ProducesProblem(401);
        // ... 其余端点
        return group;
    }

    private static async Task<Results<Ok<T>, UnauthorizedHttpResult, ValidationProblem>>
        HandlerName(..., HttpContext httpContext, UserManager<AppUser> userManager, TwoFactorService twoFactorService)
    {
        // 1. 提取 X-Session-Token
        // 2. 校验 token purpose="2fa-modify"
        // 3. 获取用户
        // 4. 业务逻辑
        // 5. 返回 TypedResults.Ok() 或 Problem
    }
}
```

### 测试模式

```csharp
// 端点测试使用反射调用 private static 方法（与 TwoFactorEndpointsTests 相同）
var method = typeof(TwoFactorModifyEndpoints).GetMethod(
    "HandlerName", BindingFlags.NonPublic | BindingFlags.Static)!;
var task = (Task)method.Invoke(null, args)!;
await task;
```

使用 `TestIdentityFactory.CreateAsync()` 创建隔离的 InMemory DbContext。

### 恢复码模式

- 设置完成时：`AuthService._lastRecoveryCodes` 存储，组件通过 `GetLastRecoveryCodes()` 读取
- 重新生成时：`ModifyRegenerateRecoveryCodesAsync` 返回新码并更新 `_lastRecoveryCodes`

## References

- [SPEC: 2FA 设置管理](_bmad-output/specs/spec-2fa-modify-settings/SPEC.md)
- [SPEC Companion: modify-flow.md](_bmad-output/specs/spec-2fa-modify-settings/modify-flow.md)
- [SPEC: 2FA 多方法并存](_bmad-output/specs/spec-2fa-multi-method-login/SPEC.md)
- [Architecture](_bmad-output/planning-artifacts/architecture.md)
- [Project Context](_bmad-output/project-context.md)
- [TwoFactorEndpoints.cs](src/BoxWise.Server/Endpoints/TwoFactorEndpoints.cs) — 端点实现模板
- [TwoFactorService.cs](src/BoxWise.Server/Services/TwoFactorService.cs) — SessionToken 机制
- [RecoveryCodeService.cs](src/BoxWise.Server/Services/RecoveryCodeService.cs) — 恢复码 CRUD

## Dev Agent Record

### Agent Model Used

Claude Opus 4.x (via Claude Code CLI)

### Completion Notes List

<!-- Populated by dev agent after each implementation session -->

### File List

<!-- Populated by dev agent after implementation -->
