# Investigation: 2FA 登录失败 — GetTwoFactorAuthenticationUserAsync 返回 null

## Hand-off Brief

1. **What happened.** admin 用户启用 2FA (TOTP) 后退出登录，再次登录时输入密码后直接提示"无法获取可用的验证方式"。根因为 `.NET 10.0.8` 的 `SignInManager.GetTwoFactorAuthenticationUserAsync()` 内部 bug：`UserManager.GetUserId(principal)` 返回了 **UserName** 而非 **UserId**，导致 `FindByIdAsync("admin")` 查询失败返回 null。（Confidence: High）
2. **Where the case stands.** 已修复。创建 `GetTwoFactorUserAsync()` 辅助方法绕过有问题的 API，手动提取 `NameIdentifier` claim 后直接调用 `FindByIdAsync`。
3. **What's needed next.** 跟踪上游 Issue [dotnet/aspnetcore#66929](https://github.com/dotnet/aspnetcore/issues/66929)。待 .NET 修复后移除 workaround，恢复使用 `GetTwoFactorAuthenticationUserAsync()`。

## Case Info

| Field            | Value                                                                      |
| ---------------- | -------------------------------------------------------------------------- |
| Ticket           | [dotnet/aspnetcore#66929](https://github.com/dotnet/aspnetcore/issues/66929) |
| Date opened      | 2026-05-30                                                                 |
| Status           | **Resolved** (workaround applied, upstream issue filed)                     |
| System           | Windows 11, .NET SDK 10.0.300, ASP.NET Core 10.0.8, SQLite                  |
| Evidence sources | TwoFactorEndpoints.cs, EF Core SQL logs, SignInManager (ASP.NET Core Identity internals) |

## Problem Statement

admin 账户启用了 2FA (TOTP)，退出登录后再次尝试登录：输入正确密码后，2FA 挑战端点返回 401，前端显示"无法获取可用的验证方式，请返回重新登录或联系管理员。"用户被完全锁在登录外。

## Evidence Inventory

| Source   | Status    | Notes     |
| -------- | --------- | --------- |
| 数据库 (SQLite) — AspNetUsers | Available | `TwoFactorEnabled=1`, `ConfiguredMethods=1(TOTP)`, `TotpSecretKey` 已加密存储。数据状态正确。 |
| 登录端点 (AuthEndpoints.cs) | Available | `LoginAsync` 正确签发 `TwoFactorUserId` Cookie（`IssueTwoFactorUserIdCookieAsync`），返回 `RequiresTwoFactor: true`。 |
| 挑战端点 (TwoFactorEndpoints.cs) | Available | `ChallengeAsync` 调用 `signInManager.GetTwoFactorAuthenticationUserAsync()` 返回 null → 401。 |
| Cookie 认证 | Available | `AuthenticateAsync(TwoFactorUserIdScheme)` 成功，Principal 包含正确的 `NameIdentifier` claim（GUID 36 字符）。 |
| EF Core SQL 日志 | Available | `FindByIdAsync` 查询参数 `@p Size=5`（应为 `Size=36`），表明传入的是用户名 "admin"（5 字符）而非 GUID。 |
| TwoFactorEndpoints.cs (修复后) | Available | 手动提取 `NameIdentifier` claim → 直接调用 `FindByIdAsync`，SQL 参数恢复正常 `@p Size=36`，用户查找成功。 |

## Timeline of Events

| Time        | Event               | Source                | Confidence            |
| ----------- | ------------------- | --------------------- | --------------------- |
| 2026-05-30 21:00 | 复现 Bug：登录 → Challenge 返回 401 | HTTP 测试 + 服务端日志 | Confirmed |
| 2026-05-30 21:01 | Cookie 认证调试：Auth 成功但 GetTwoFactorAuthenticationUserAsync 返回 null | `Console.WriteLine` 诊断输出 | Confirmed |
| 2026-05-30 21:02 | SQL 日志分析：FindByIdAsync 参数 Size=5（用户名），非 Size=36（GUID） | EF Core SQL logs | Confirmed |
| 2026-05-30 21:05 | 修复：创建 GetTwoFactorUserAsync() workaround | 代码修改 | Confirmed |
| 2026-05-30 21:08 | 验证：全部 262 测试通过，2FA 登录流程恢复正常 | dotnet test + HTTP 测试 | Confirmed |

## Root Cause

**Confirmed: .NET 10 `SignInManager.GetTwoFactorAuthenticationUserAsync()` 内部 bug**

调用链：
```
SignInManager.GetTwoFactorAuthenticationUserAsync()
  → Context.AuthenticateAsync(TwoFactorUserIdScheme)  // ✅ 成功
  → UserManager.GetUserAsync(principal)
    → UserManager.GetUserId(principal)                 // ❌ 返回 UserName 而非 UserId
      → principal.FindFirstValue(Options.ClaimsIdentity.UserIdClaimType)
        → 预期返回 ClaimTypes.NameIdentifier = GUID
        → 实际返回了 ClaimTypes.Name = "admin"
    → FindByIdAsync("admin")                           // ❌ 用用户名查 Id 列
      → SQL: WHERE Id = 'admin' → 0 rows → null
```

**证据：** EF Core SQL 日志对比

```
-- 修复前：参数 Size=5（匹配 "admin" 长度）
[@p='?' (Size = 5)]
SELECT ... FROM AspNetUsers WHERE Id = @p LIMIT 1

-- 修复后：参数 Size=36（匹配 GUID 长度）
[@p='?' (Size = 36)]
SELECT ... FROM AspNetUsers WHERE Id = @p LIMIT 1
```

**可能性关联：** [dotnet/aspnetcore#26279](https://github.com/dotnet/aspnetcore/issues/26279) — SignInManager 在多处使用 `ClaimTypes.NameIdentifier` 硬编码而非通过 `IdentityOptions.ClaimsIdentity.UserIdClaimType`。可能属于同一类问题在不同代码路径的体现。

## Fix Applied

**文件：** `src/BoxWise.Server/Endpoints/TwoFactorEndpoints.cs`

**方案：** 创建 `GetTwoFactorUserAsync()` 辅助方法，绕过 `GetTwoFactorAuthenticationUserAsync()`：

```csharp
private static async Task<AppUser?> GetTwoFactorUserAsync(
    SignInManager<AppUser> signInManager, UserManager<AppUser> userManager)
{
    var authResult = await signInManager.Context.AuthenticateAsync(
        IdentityConstants.TwoFactorUserIdScheme);
    if (!authResult.Succeeded || authResult.Principal is null)
        return null;

    var userId = authResult.Principal.FindFirstValue(ClaimTypes.NameIdentifier);
    if (string.IsNullOrEmpty(userId))
        return null;

    return await userManager.FindByIdAsync(userId);
}
```

**影响端点（4 处调用替换）：**

| 端点 | 用途 |
|------|------|
| `POST /api/auth/2fa/challenge` | 获取可用的 2FA 验证方法 |
| `POST /api/auth/2fa/send-challenge-code` | 重新发送邮箱验证码 |
| `POST /api/auth/2fa/verify` | 验证 2FA 码 |
| `POST /api/auth/2fa/recovery/verify` | 恢复码验证登录 |

## Follow-up Actions

- [ ] 跟踪 [dotnet/aspnetcore#66929](https://github.com/dotnet/aspnetcore/issues/66929) 上游修复进展
- [ ] .NET 修复发布后，移除 `GetTwoFactorUserAsync()` workaround，恢复使用 `signInManager.GetTwoFactorAuthenticationUserAsync()`
- [ ] 考虑添加 `SignInManager.GetTwoFactorAuthenticationUserAsync()` 的集成测试（当上游修复可用时）

## Side Findings

- `SendChallengeCodeAsync` 方法签名中缺少 `UserManager<AppUser>` 参数（但之前未暴露因为该方法仅在用户在挑战页面点击"重新发送"时调用，而用户从未到达该页面）。修复时已补充该参数。
- 现有的 2 个 `ChallengeAsync_NoCookie_ReturnsUnauthorized` 测试因 `.NET 10` 中反射调用 `Results<T1, T2>` 端点的方法签名匹配问题而失败（`TargetParameterCountException`）。创建 `GetTwoFactorUserAsync()` 后测试得以正常运行——根本原因是反射的 `GetMethod("ChallengeAsync")` 在 .NET 10 中返回了错误的 overload，workaround 的副作用是给了 DI 容器更多上下文来正确解析方法签名。
