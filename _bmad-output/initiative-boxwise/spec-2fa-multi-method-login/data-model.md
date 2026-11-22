# Data Model Changes

## 设计原则：方法隔离

每种 2FA 方法的配置数据独立存储，互不越界：

| 方法 | 配置标识 | 密钥/令牌存储 | 存储位置 |
|------|---------|-------------|---------|
| TOTP | `ConfiguredMethods` 的 TOTP 位 | `TotpSecretKey`（DP 加密的 base32 密钥） | `AppUser` 列 |
| Email | `ConfiguredMethods` 的 Email 位 | 自包含 Data Protection 令牌（含 userId+email+code+expiry） | **零服务端存储**，仅 HTTP 传递 |
| WebAuthn | `ConfiguredMethods` 的 WebAuthn 位 | `WebAuthnCredentials` 导航属性 | 独立表 |

**规则**：TOTP 的设置/验证流程不得读写 `EmailForTwoFactor`；Email 的设置/验证流程不得读写 `TotpSecretKey`。`ConfiguredMethods` 的修改仅通过 `|=` 添加或 `&= ~` 移除各自的方法位。

## AppUser 字段变更

### Before (current — buggy)

```csharp
public TwoFactorMethod TwoFactorMethod { get; set; } = TwoFactorMethod.None;  // 单值
public string? TotpSecretKey { get; set; }
public string? EmailForTwoFactor { get; set; }
```

### After

```csharp
public TwoFactorMethod ConfiguredMethods { get; set; } = TwoFactorMethod.None;  // [Flags]
public string? TotpSecretKey { get; set; }
public string? EmailForTwoFactor { get; set; }
```

### TwoFactorMethod 枚举变更

```csharp
[Flags]
public enum TwoFactorMethod
{
    None = 0,
    TOTP = 1,
    Email = 2,
    WebAuthn = 4   // 预留，本次不改
}
```

### 兼容性分析

| 旧值 | 旧语义 (单值枚举) | 新语义 (Flags) | 兼容？ |
|------|------------------|----------------|--------|
| 0 | None | None | ✅ |
| 1 | TOTP | TOTP | ✅ |
| 2 | Email | Email | ✅ |
| 3 | WebAuthn（预留，从未实现） | TOTP \| Email | ⚠️ 语义变化但无实际影响 |

`[Flags]` 仅影响 C# 端按位操作，SQLite 存储仍是 INT，无需数据迁移脚本。**迁移前需确认生产数据库无 `TwoFactorMethod=3` 的记录**（WebAuthn 从未实现，该值不应存在）。

## EF Core 迁移

需生成新迁移：
1. 重命名列 `TwoFactorMethod` → `ConfiguredMethods`（或在 OnModelCreating 中映射）
2. 无数据转换需要（值相同）

### 涉及的查询变更

所有引用 `user.TwoFactorMethod` 的代码需改为：
- **检查是否配置了某方法**：`user.ConfiguredMethods.HasFlag(TwoFactorMethod.TOTP)`
- **添加方法**：`user.ConfiguredMethods |= TwoFactorMethod.Email`
- **移除方法**：`user.ConfiguredMethods &= ~TwoFactorMethod.TOTP`
- **判断是否有任何方法**：`user.ConfiguredMethods != TwoFactorMethod.None`

## 关键修复点

### VerifyEmailAsync（核心 bug 修复）

```diff
- // 如果从另一种方法切换，清除旧密钥
- if (user.TwoFactorMethod != TwoFactorMethod.None)
- {
-     user.TotpSecretKey = null;
- }
- user.TwoFactorEnabled = true;
- user.TwoFactorMethod = TwoFactorMethod.Email;
+ // 添加 Email 方法，不清除已有的 TOTP 配置
+ user.ConfiguredMethods |= TwoFactorMethod.Email;
+ user.TwoFactorEnabled = true;
```

### VerifyTotpSetupAsync

```diff
- user.TwoFactorMethod = TwoFactorMethod.TOTP;
+ user.ConfiguredMethods |= TwoFactorMethod.TOTP;
```

### GetTwoFactorStatusAsync

当前实现硬编码方法列表（总是返回 TOTP + WebAuthn，Email 仅取决于 SMTP 配置），修复为基于 `ConfiguredMethods` flags 分解：

```diff
- var availableMethods = new List<string> { "TOTP" };
- if (_emailTwoFactorService.IsSmtpConfigured())
-     availableMethods.Add("Email");
- availableMethods.Add("WebAuthn");  // 硬编码，功能未实现

+ var availableMethods = new List<string>();
+ if (user.ConfiguredMethods.HasFlag(TwoFactorMethod.TOTP))
+     availableMethods.Add("TOTP");
+ if (user.ConfiguredMethods.HasFlag(TwoFactorMethod.Email)
+     && _emailTwoFactorService.IsSmtpConfigured())
+     availableMethods.Add("Email");
```

Email 可用性需同时满足：用户已配置 `|=` Email **且** SMTP 已配置。

### SwitchMethodAsync（废弃）

```diff
- // 该方法在 [Flags] 模型下语义不明确（添加？替换？）。
- // 由独立的 setup/verify 端点通过 |= 添加方法替代。
- public Task<bool> SwitchMethodAsync(...)
- {
-     // 移除整个方法，不再使用
- }
```

端点 `PUT /api/auth/2fa/switch-method` 保留路由但返回 410 Gone，或直接移除路由注册。
