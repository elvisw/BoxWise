# 架构文档 — BoxWise.Shared

> .NET 10 类库 — 共享 DTO 契约

## 执行摘要

BoxWise.Shared 是零依赖的 .NET 类库，定义 Client 和 Server 之间共享的所有 DTO 类型。所有类型均为 C# `record`（positional），保证 JSON 序列化一致性和不可变性。

## 技术栈

| 项目 | 值 |
|------|-----|
| SDK | `Microsoft.NET.Sdk` |
| Target | net10.0 |
| 引用 | 无外部 NuGet 依赖 |
| 使用者 | BoxWise.Client, BoxWise.Server |

## DTO 清单 (30 个)

### 认证

| DTO | 类型 | 方向 |
|-----|------|------|
| `LoginRequest` | `record (string Username, string Password)` | Client → Server |
| `AuthUserDto` | `record (string UserName, bool IsAdmin, bool PasswordManagedByEnv, bool PasswordRequiresChange, string? Email)` | Server → Client |
| `UserListItemDto` | `record (string Id, string UserName, bool IsAdmin, bool TwoFactorEnabled, string? ConfiguredMethods)` | Server → Client (Admin) |
| `CreateAccountRequest` | `class (string Username, string Password, string Email)` | Admin → Server |

### 位置

| DTO | 类型 | 方向 |
|-----|------|------|
| `LocationDto` | `record (int Id, string Name, string Path, int? ParentId, int SortOrder)` | Server → Client |
| `CreateLocationRequest` | `record (string Name, int? ParentId, int SortOrder = 0)` | Client → Server |
| `RenameLocationRequest` | `record (string Name)` | Client → Server |

### 标签

| DTO | 类型 | 方向 |
|-----|------|------|
| `TagDto` | `record (int Id, string Name, int ItemCount)` | Server → Client |
| `CreateTagRequest` | `record (string Name)` | Client → Server |
| `RenameTagRequest` | `record (string Name)` | Client → Server |

### 物品

| DTO | 类型 | 方向 |
|-----|------|------|
| `CreateItemRequest` | `record (string Name, int LocationId, List<int> TagIds, string? Note)` | Client → Server |
| `UpdateItemRequest` | `record (string Name, int LocationId, List<int> TagIds, string? Note)` | Client → Server |
| `ItemDto` | `record` — 完整字段含位置/标签/创建者/更新者 | Server → Client |
| `ItemSummaryDto` | `record` — 精简字段用于列表/搜索 | Server → Client |

### 图片

| DTO | 类型 | 方向 |
|-----|------|------|
| `UploadResultDto` | `record (int ItemId, string OriginalUrl)` | Server → Client |

### AI

| DTO | 类型 | 方向 |
|-----|------|------|
| `RecognitionResultDto` | `record (string Name, string Note)` | Client 端内部使用 |

### 双因素认证 (2FA)

| DTO | 类型 | 方向 |
|-----|------|------|
| `TwoFactorStatusDto` | `record` — 含 `TwoFactorEnabled`, `TwoFactorMethod`, `AvailableMethods`, `ConfiguredMethods`, `HasRecoveryCodes`, `GracePeriodEnd`, `SetupCompletedAt` | Server → Client |
| `VerifyTwoFactorRequest` | `record (string Code, string? Token, string? Method)` | Client → Server |
| `SwitchMethodRequest` | `record (string Method)` | Client → Server |
| `SetupEmailTwoFactorRequest` | `record (string Email)` | Client → Server |
| `RecoveryCodesResponse` | `record (List<string> Codes)` | Server → Client |
| `ReAuthenticateRequest` | `record (string Password)` | Client → Server |

### WebAuthn / 通行密钥

| DTO | 类型 | 方向 |
|-----|------|------|
| `WebAuthnAvailableResponse` | `record (bool Available, string Origin, string? UserHandle)` | Server → Client |
| `WebAuthnChallengeResponse` | `record (string Challenge)` | Server → Client |
| `WebAuthnCredentialDto` | `record (int Id, string DeviceName, DateTime CreatedAt, string CredentialId)` | Server → Client |

### 个人资料管理

| DTO | 类型 | 方向 |
|-----|------|------|
| `UpdateProfileRequest` | `record (string? NewUsername, string? NewEmail, string? OperationToken)` | Client → Server |
| `ChangePasswordRequest` | `record (string CurrentPassword, string NewPassword)` | Client → Server |

### Admin / SMTP 配置

| DTO | 类型 | 方向 |
|-----|------|------|
| `SmtpConfigDto` | `sealed record` — 含 Host, Port, Username, Password, FromAddress, FromName（ToString 遮蔽密码） | 双向 |
| `SmtpTestResult` | `sealed record (bool Success, string? ErrorMessage)` | Server → Client (Admin) |
| `AdminTwoFactorStatusResponse` | `record (string UserName, TwoFactorStatusDto Status)` | Server → Client (Admin) |

## 设计原则

1. **不可变性:** 所有 DTO 使用 `record`，构造后不可修改
2. **零依赖:** 无外部 NuGet 包，纯 C# 类型
3. **JSON 友好:** `System.Text.Json` 原生支持 record 序列化
4. **单一来源:** Client 和 Server 共享同一份 DTO 定义，避免不一致
