# Investigation: Admin 后台 2FA 状态显示空括号 + 无重置按钮

## Hand-off Brief

1. **What happened.** 用户 `elvis` 启用了 TOTP 2FA，但 Admin 后台显示"已启用 ()"（括号为空），且缺少"重置 2FA"按钮。**根因：数据不一致 — `ConfiguredMethods = None(0)` 但 `TwoFactorEnabled = true`。**
2. **Where the case stands.** 已修复：代码层面 2 处修复 + 数据层面 1 处修复。279 测试全部通过。
3. **What's needed next.** 无。修复完整。

## Case Info

| Field            | Value                                    |
| ---------------- | ---------------------------------------- |
| Ticket           | N/A                                      |
| Date opened      | 2026-06-07                               |
| Status           | Concluded                                |
| System           | Windows 11, .NET 10.0, SQLite             |
| Evidence sources | 数据库 `data/boxwise.db`, `Index.cshtml`, `Index.cshtml.cs`, `TwoFactorMethod.cs`, `UserListItemDto.cs` |

## Problem Statement

Admin 后台账户列表页面：
- `elvis` 用户的 2FA 状态列显示 "已启用 ()"（括号内无文字）
- 该用户无"重置 2FA"操作按钮
- 另一用户 `catevs`（WebAuthn）正常显示 "已启用 (WebAuthn)" 且有重置按钮

## Evidence Inventory

| Source                        | Status    | Notes                                         |
| ----------------------------- | --------- | ---------------------------------------------- |
| 数据库 `AspenNetUsers` 表      | Available | `elvis`: TwoFactorEnabled=1, ConfiguredMethods=0 |
| `Index.cshtml.cs` (line 116-123) | Available | 开关表达式，ConfiguredMethods=None → methodDisplay=null |
| `Index.cshtml` (line 58, 75)   | Available | 显示: `已启用 (@user.ConfiguredMethods)`; 条件: `!string.IsNullOrEmpty(user.ConfiguredMethods)` |
| `TwoFactorMethod.cs`           | Available | None=0, TOTP=1, Email=2, WebAuthn=4 [Flags]   |
| `UserListItemDto.cs`           | Available | TwoFactorMethod 用于显示, ConfiguredMethods 用于重置按钮条件 |
| `LoginWith2fa.cshtml.cs` (line 144-151) | Available | 已有自动修复逻辑，但仅在 2FA 登录时触发 |

## Timeline of Events

| Time        | Event                                                     | Source          | Confidence |
| ----------- | --------------------------------------------------------- | --------------- | ---------- |
| 未知        | `elvis` 的 ConfiguredMethods 被置为 None(0) 但 TwoFactorEnabled 保持 true | 数据库记录       | Deduced    |
| 2026-06-07  | 管理员查看后台发现显示异常                                 | 用户报告         | Confirmed  |
| 2026-06-07  | 数据库查询确认 ConfiguredMethods=0, TwoFactorEnabled=1    | sqlite3 查询     | Confirmed  |
| 2026-06-07  | 代码修复 + 数据修复，279 测试通过                          | dotnet test      | Confirmed  |

## Confirmed Findings

### Finding 1: 数据库状态不一致

**Evidence:** `data/boxwise.db` — `SELECT Id, UserName, TwoFactorEnabled, ConfiguredMethods FROM AspNetUsers;`

```
admin  | TwoFactorEnabled=0 | ConfiguredMethods=0
elvis  | TwoFactorEnabled=1 | ConfiguredMethods=0  ← 不一致
catevs | TwoFactorEnabled=1 | ConfiguredMethods=4 (WebAuthn)
```

`elvis` 的 `ConfiguredMethods = None(0)` 但 `TwoFactorEnabled = true`，这是非法状态。

### Finding 2: 开关表达式对 None 返回 null，视图无兜底

**Evidence:** `src/BoxWise.Server/Pages/Admin/Index.cshtml.cs:116-123`

```csharp
var methodDisplay = u.ConfiguredMethods switch
{
    TwoFactorMethod m when m.HasFlag(TwoFactorMethod.TOTP) && m.HasFlag(TwoFactorMethod.Email) => "TOTP + Email",
    TwoFactorMethod m when m.HasFlag(TwoFactorMethod.TOTP) => "TOTP",
    TwoFactorMethod m when m.HasFlag(TwoFactorMethod.Email) => "Email",
    TwoFactorMethod.None => null,  // ← 数据不一致时返回 null
    _ => u.ConfiguredMethods.ToString()
};
```

当 `ConfiguredMethods = None` 时返回 `null`，但 `TwoFactorEnabled = true` 时视图直接拼接 `"已启用 (" + null + ")"` → "已启用 ()"。

### Finding 3: 重置按钮条件仅检查 ConfiguredMethods

**Evidence:** `src/BoxWise.Server/Pages/Admin/Index.cshtml:75`

```razor
@if (!string.IsNullOrEmpty(user.ConfiguredMethods))
```

只检查了 `ConfiguredMethods`，未考虑 `TwoFactorEnabled=true` 但 `ConfiguredMethods` 为空的数据不一致场景。

### Finding 4: LoginWith2fa.cshtml.cs 已有自动修复但未触发

**Evidence:** `LoginWith2fa.cshtml.cs:144-151`

代码在 2FA 登录时会检测并自动修复此不一致状态（`user.TwoFactorEnabled = false`），但仅当用户通过 2FA 登录时触发，Admin 后台查看不触发。

## Deduced Conclusions

### Deduction 1: 数据不一致的可能来源

**Based on:** Confirmed Findings 1

**Reasoning:** 所有已知的 2FA 禁用路径（`Disable2fa.cshtml.cs`、`RecoveryCodeService.VerifyRecoveryCodeAsync`）都同时设置 `TwoFactorEnabled=false` 和 `ConfiguredMethods=None`。不一致最可能发生在以下场景之一：
1. 数据库表中 `ConfiguredMethods` 列是后期添加的（迁移 `20260530104645_RenameTwoFactorMethodToConfiguredMethods`），迁移时未正确回填历史数据
2. TOTP 设置过程中某次 `UpdateAsync` 失败，导致只更新了部分字段

**Conclusion:** 无法确定确切时间点，但数据不一致状态本身是确定的。

## Source Code Trace

| Element       | Detail                                                    |
| ------------- | --------------------------------------------------------- |
| Error origin  | `Index.cshtml.cs:116-123` — 开关表达式对 None 返回 null     |
| Trigger       | Admin 访问 `/admin` 查看用户列表                           |
| Condition     | `ConfiguredMethods = None` 且 `TwoFactorEnabled = true`   |
| Related files | `Index.cshtml:58,75`, `TwoFactorMethod.cs`, `UserListItemDto.cs` |

## Conclusion

**Confidence:** High

**根因：** 数据库记录不一致 — `elvis` 用户的 `ConfiguredMethods = None(0)` 但 `TwoFactorEnabled = true`。Admin 页面的显示和重置按钮逻辑未处理此边缘情况。

**修复（3 处）：**

| # | 文件 | 变更 | 说明 |
|---|------|------|------|
| 1 | `Index.cshtml.cs:129` | `methodDisplay ?? (u.TwoFactorEnabled ? "未知" : null)` | TwoFactorEnabled=true 但 ConfiguredMethods=None 时显示"未知"而非空括号 |
| 2 | `Index.cshtml:75` | 条件改为 `user.TwoFactorEnabled \|\| !string.IsNullOrEmpty(...)` | 2FA 启用状态也使重置按钮可见 |
| 3 | `data/boxwise.db` | `UPDATE ... SET TwoFactorEnabled=0, ConfiguredMethods=0 WHERE UserName='elvis'` | 修复实际数据 |

## Recommended Next Steps

### Fix direction

已实施。代码层面增加了防御性兜底：当 `TwoFactorEnabled=true` 但 `ConfiguredMethods=None` 时，显示"未知"并提供重置按钮。数据层面修复了实际记录。

### Diagnostic

无需额外诊断。

## Reproduction Plan

1. 手动将某用户的 `ConfiguredMethods` 设为 0，`TwoFactorEnabled` 设为 1
2. 访问 `/admin` 查看用户列表
3. **修复前：** 显示"已启用 ()"，无重置按钮
4. **修复后：** 显示"已启用 (未知)"，有重置按钮
5. 点击"重置 2FA"可正常进入重置页面清除状态
