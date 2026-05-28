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
  │
Location (自引用树)
  │
  ├──< Children (ParentId)
  ├──< Items (LocationId)
  │
Item
  ├── Location (LocationId)
  ├── CreatedByUser (CreatedByUserId)
  └── Tags (M:N via ItemTag)
  │
Tag
  └── Items (M:N via ItemTag)
```

---

## 1. AppUser

继承 `IdentityUser`，无自定义属性。表名 `AspNetUsers`（Identity 默认）。

---

## 2. Location

| 字段 | 类型 | 约束 | 说明 |
|------|------|------|------|
| `Id` | `int` | PK, 自增 | |
| `Name` | `string` | Required, MaxLength(100) | 位置名称 |
| `Path` | `string` | Required, MaxLength(500), Indexed | 从根到当前节点的完整路径（如 `"/客厅/电视柜"`) |
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
| `CreatedAt` | `DateTime` | | 创建时间 |

**导航属性:**
- `Location` → `Location?`
- `CreatedByUser` → `AppUser?`
- `Tags` → `ICollection<Tag>` (M:N)

**删除约束:**
- 位置删除 → Item.LocationId 设为 null
- 用户删除 → Restrict（阻止）

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

## 5. Identity 表（自动生成）

| 表名 | 用途 |
|------|------|
| `AspNetUsers` | 用户账户 |
| `AspNetRoles` | 角色（Admin） |
| `AspNetUserRoles` | 用户-角色关联 |
| `AspNetUserClaims` | 用户声明 |
| `AspNetRoleClaims` | 角色声明 |
| `AspNetUserLogins` | 外部登录 |
| `AspNetUserTokens` | 令牌 |

---

## 6. 迁移策略

- 启动时自动执行 `db.Database.MigrateAsync()`
- 管理员种子数据通过 `Admin__Password` 环境变量触发
- 数据目录: `data/boxwise.db`
