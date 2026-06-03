# 数据模型

> BoxWise.Server — EF Core 实体与数据库架构

## 概览

- **ORM:** Entity Framework Core 10.0.8
- **数据库:** SQLite
- **认证:** ASP.NET Core Identity (`AppUser` : `IdentityUser`)
- **配置:** Fluent API (`IEntityTypeConfiguration<T>`)

---

## 实体关系图

```
AppUser (IdentityUser)
  │
  ├──< Items (CreatedByUserId)
  ├──< Items (UpdatedByUserId)
  ├──< RecoveryCodes (UserId)
  └──< WebAuthnCredentials (UserId)
  │
Location (自引用树)
  │
  ├──< Children (ParentId)
  ├──< Items (LocationId)
  │
Item
  ├── Location (LocationId)
  ├── CreatedByUser (CreatedByUserId)
  ├── UpdatedByUser (UpdatedByUserId)
  └── Tags (M:N via ItemTag)
  │
Tag
  └── Items (M:N via ItemTag)
  │
RecoveryCode
  └── User (UserId → AppUser.Id)
  │
WebAuthnCredential
  └── User (UserId → AppUser.Id)
```

---

## 1. AppUser

继承 `IdentityUser`，扩展以下自定义属性。表名 `AspNetUsers`（Identity 默认）。

| 字段 | 类型 | 约束 | 说明 |
|------|------|------|------|
| `ConfiguredMethods` | `TwoFactorMethod` | [Flags] 枚举, 默认 `None` | 已配置的 2FA 方法集合（TOTP / Email / WebAuthn） |
| `TotpSecretKey` | `string?` | | TOTP 密钥（已验证并生效） |
| `PendingTotpSecretKey` | `string?` | | 暂存的 TOTP 密钥（扫描 QR 后、verify 前暂存，verify 成功时覆盖 TotpSecretKey） |
| `EmailForTwoFactor` | `string?` | | 2FA 专用邮箱（与 `user.Email` 通过 `UpdateProfileAsync` 原子同步；登录时优先读取 `user.Email`，fallback 到此字段） |
| `EffectiveEmailForTwoFactor` | `string?` | **计算属性**（getter only） | 优先返回 `Email`，回退到 `EmailForTwoFactor`（向后兼容） |
| `TwoFactorSetupCompletedAt` | `DateTime?` | | 2FA 首次设置完成时间 |
| `TwoFactorGracePeriodUntil` | `DateTime?` | | 2FA 宽限期截止时间 |
| `RecoveryCodes` | `ICollection<RecoveryCode>` | 导航属性 | 恢复码集合 |

**导航属性（从关联实体反向引用，AppUser 不含直接声明）：**
- `Items` → `ICollection<Item>`（通过 CreatedByUserId）
- `UpdatedItems` → `ICollection<Item>`（通过 UpdatedByUserId）
- `RecoveryCodes` → `ICollection<RecoveryCode>`
- `WebAuthnCredentials` → `ICollection<WebAuthnCredential>`（通过 WebAuthnCredential.User 导航）

**TwoFactorMethod [Flags] 枚举:**

| 值 | 名称 | 说明 |
|----|------|------|
| `0` | `None` | 未配置 |
| `1` | `TOTP` | 基于时间的 TOTP 验证器 |
| `2` | `Email` | 邮箱验证码 |
| `4` | `WebAuthn` | 通行密钥（FIDO2/WebAuthn） |

> WebAuthn 从 3 改为 4 以避免破坏 TOTP|Email 组合（011 → 100）。

---

## 2. Location

| 字段 | 类型 | 约束 | 说明 |
|------|------|------|------|
| `Id` | `int` | PK, 自增 | |
| `Name` | `string` | Required, MaxLength(100) | 位置名称 |
| `Path` | `string` | Required, MaxLength(500), Indexed | 从根到当前节点的完整路径（如 `"/客厅/电视柜"`） |
| `ParentId` | `int?` | FK → Location.Id, Restrict Delete | 父节点 |
| `SortOrder` | `int` | | 同级排序 |

**导航属性:**
- `Parent` → `Location?`
- `Children` → `ICollection<Location>`

**删除约束:** Restrict（有子节点时阻止删除）

---

## 3. Item

| 字段 | 类型 | 约束 | 说明 |
|------|------|------|------|
| `Id` | `int` | PK, 自增 | |
| `Name` | `string` | Required, MaxLength(200) | 物品名称 |
| `Note` | `string?` | MaxLength(2000) | 备注 |
| `PhotoPath` | `string?` | MaxLength(500) | 原始图片路径 |
| `ThumbPath` | `string?` | MaxLength(500) | 缩略图路径 (300px) |
| `MediumPath` | `string?` | MaxLength(500) | 中等图路径 (1200px) |
| `LocationId` | `int?` | FK → Location.Id, SetNull on Delete | 所在位置 |
| `CreatedByUserId` | `string` | FK → AppUser.Id, Restrict on Delete | 创建者 |
| `UpdatedByUserId` | `string?` | FK → AppUser.Id | 最后修改者 |
| `CreatedAt` | `DateTime` | | 创建时间 |
| `UpdatedAt` | `DateTime?` | | 最后修改时间 |
| `Version` | `Guid` | 并发令牌 | 乐观并发控制（`IsConcurrencyToken`），更新时自动生成 |

**导航属性:**
- `Location` → `Location?`
- `CreatedByUser` → `AppUser?`
- `UpdatedByUser` → `AppUser?`
- `Tags` → `ICollection<Tag>` (M:N)

**删除约束:**
- 位置删除 → Item.LocationId 设为 null
- 创建者/修改者删除 → Restrict（阻止）

**并发控制:**
- `Version` 字段标记为 `IsConcurrencyToken`，`UpdateItemAsync` 捕获 `DbUpdateConcurrencyException` 返回 409 Conflict

---

## 4. Tag

| 字段 | 类型 | 约束 | 说明 |
|------|------|------|------|
| `Id` | `int` | PK, 自增 | |
| `Name` | `string` | Required, MaxLength(50), **Unique Index** | 标签名称 |

**导航属性:**
- `Items` → `ICollection<Item>` (M:N)

**多对多:** 自动生成连接表 `ItemTag`（无显式连接实体）

---

## 5. RecoveryCode

| 字段 | 类型 | 约束 | 说明 |
|------|------|------|------|
| `Id` | `int` | PK, 自增 | |
| `UserId` | `string` | FK → AppUser.Id, Required | 所属用户 |
| `CodeHash` | `string` | Required | 恢复码 SHA-256 哈希值（明文不存储） |

**导航属性:**
- `User` → `AppUser`

---

## 6. WebAuthnCredential

| 字段 | 类型 | 约束 | 说明 |
|------|------|------|------|
| `Id` | `int` | PK, 自增 | |
| `UserId` | `string` | FK → AppUser.Id, Required | 所属用户 |
| `CredentialId` | `string` | Required | 凭证唯一标识（Base64 编码的 CredentialID） |
| `PublicKey` | `string` | Required | 公钥（Base64 编码） |
| `SignCount` | `int` | 默认 0 | 签名计数器（防克隆重放） |
| `DeviceName` | `string` | Required | 设备名称（如 "iPhone 上的 iCloud 钥匙串"） |
| `CreatedAt` | `DateTime` | 默认 `DateTime.UtcNow` | 创建时间 |

**导航属性:**
- `User` → `AppUser`

**关系方向:** 从 WebAuthnCredential → AppUser 单向导航（AppUser 上无直接集合属性，通过 `DbContext.WebAuthnCredentials.Where(wc => wc.UserId == id)` 查询）。

---

## 7. Identity 表（自动生成）

| 表名 | 用途 |
|------|------|
| `AspNetUsers` | 用户账户（含 AppUser 自定义属性） |
| `AspNetRoles` | 角色（Admin） |
| `AspNetUserRoles` | 用户-角色关联 |
| `AspNetUserClaims` | 用户声明 |
| `AspNetRoleClaims` | 角色声明 |
| `AspNetUserLogins` | 外部登录 |
| `AspNetUserTokens` | 令牌 |

---

## 8. 迁移策略

- 启动时自动执行 `db.Database.MigrateAsync()`
- 管理员种子数据通过 `Admin__Password` 环境变量触发
- 数据目录: `data/boxwise.db`
