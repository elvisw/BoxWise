# 架构文档 — BoxWise.Server

> ASP.NET Core Web API + Admin Razor Pages

## 执行摘要

BoxWise.Server 是基于 ASP.NET Core Minimal API 的后端服务，采用分层架构（Endpoint → Repository → EF Core → SQLite），集成 ASP.NET Core Identity 认证、SkiaSharp 图片处理。

## 技术栈

| 层 | 技术 | 版本 |
|----|------|------|
| 运行时 | ASP.NET Core | 10.0 |
| API 风格 | Minimal API + RouteGroupBuilder | - |
| ORM | Entity Framework Core | 10.0.8 |
| 数据库 | SQLite | - |
| 认证 | ASP.NET Core Identity + Cookie | 10.0.8 |
| 图片处理 | SkiaSharp | 3.119.4 |
| OpenAPI | Microsoft.AspNetCore.OpenApi | 10.0.8 |
| Admin UI | Razor Pages (Server-side) | - |

## 架构模式

**分层架构 + Minimal API 路由组**

```
Endpoints (8 Route Groups)
    ↓ 调用
Repositories (3 Scoped)
    ↓ 使用
AppDbContext : IdentityDbContext<AppUser>
    ↓ 持久化
SQLite (data/boxwise.db)

横向服务:
  Identity + Cookie Auth
  ImageStorageService (Singleton)
  ThumbnailService (Singleton, SkiaSharp)

  TwoFactorService (Scoped)
  WebAuthnService (Scoped, Fido2NetLib)
  RecoveryCodeService (Scoped)
  SmtpConfigurationService (Singleton, DPAPI 加密)
  IdentityEmailSender (Scoped)
  EmailTwoFactorService (Scoped)
  CsrfValidationFilter (Singleton)
```

## API 设计

### 路由组（`RouteGroupBuilder` 静态扩展方法）

| 路由组 | 前缀 | 端点数 | 文件 |
|--------|------|--------|------|
| Auth | `/api/auth` | 1 | `AuthEndpoints.cs` |
| Location | `/api/locations` | 5 | `LocationEndpoints.cs` |
| Item | `/api/items` | 5 | `ItemEndpoints.cs` |
| Image | `/api/images` | 2 | `ImageEndpoints.cs` |
| Tag | `/api/tags` | 4 | `TagEndpoints.cs` |

| WebAuthn | `/api/auth/webauthn` | 7 | `WebAuthnEndpoints.cs` |
| Admin 2FA | `/api/admin/users/{userId}/two-factor` | 2 | `AdminTwoFactorEndpoints.cs` |

### 返回类型

- `TypedResults.Ok()` / `TypedResults.Created()` / `TypedResults.NoContent()`
- `TypedResults.Problem()` 直接返回错误（不嵌套在 `BadRequest()` 中）
- `TypedResults.NotFound()`
- `ProblemDetails` 格式

## 数据架构

### 实体模型

```
AppUser (IdentityUser 扩展: TotpSecretKey, ConfiguredMethods, TwoFactorGracePeriodUntil,
         TwoFactorSetupCompletedAt, EmailForTwoFactor)
Location (自引用树: ParentId FK → Id)
Item (LocationId FK, CreatedByUserId FK, UpdatedByUserId FK, Tags M:N)
Tag (Items M:N, 自动连接表 ItemTag)
RecoveryCode (UserId FK, CodeHash — SHA-256)
WebAuthnCredential (UserId FK, CredentialId, PublicKey, SignCount, DeviceName)
TwoFactorMethod ([Flags] 枚举: None=0, TOTP=1, Email=2, WebAuthn=4)
```

### Repository 模式

- 返回 Entity，端点负责 Entity → DTO 映射
- Scoped 生命周期
- `ArgumentException` → Problem(400), `KeyNotFoundException` → NotFound()
- `DbUpdateException` 捕获兜底

## 认证与授权

- **全局策略:** `FallbackPolicy = RequireAuthenticatedUser()`
- **匿名端点:** 显式 `.AllowAnonymous()` 标记（`login`, `index.html`, 静态资源, WebAuthn login-begin/login-complete）
- **管理员策略:** `AdminOnly` → `RequireRole("Admin")`
- **Cookie:** `SameSite=None`, `Secure=Always`, `HttpOnly=true`, 30 天滑动过期
- **API 401:** 返回 JSON 而非重定向
- **CSRF 防护:** `CsrfValidationFilter` 保护写操作端点（POST/PUT/DELETE 涉及状态变更）
- **WebAuthn 通行密钥:** 支持无密码登录（passkey-login 速率限制策略 30次/5分钟）

## Admin UI

- Server 端 Razor Pages（`Pages/Admin/`）
- `AdminOnly` 策略保护
- 功能包括：
  - 创建家庭成员账户（`CreateAccount.cshtml`）
  - 编辑账户信息（`EditAccount.cshtml`）
  - 修改用户密码（`ChangeUserPassword.cshtml`）
  - 重置用户 2FA（`ResetTwoFactor.cshtml`）
  - SMTP 配置管理（`SmtpSettings.cshtml`）
  - 用户列表首页（`Index.cshtml`）

## AI 集成

AI 识别已迁移至客户端浏览器直调火山 ARK API（Story 12.1）。服务端不再参与 AI 调用。

## 图片处理

- **上传:** 10MB 限制，MIME 白名单
- **存储:** 文件系统，`ImageStorageService` 管理
- **缩略图:** SkiaSharp 300px + 1200px 两级，后台异步生成
- **Magic-byte 验证:** 防止内容类型伪造

## 开发工作流

```bash
cd src/BoxWise.Server && dotnet run
# → https://localhost:5000
```

## 测试策略

- **单元测试:** xUnit + EF Core InMemory（`BoxWise.Server.Tests` + `BoxWise.Client.Tests`）
- **Repository 层:** 每个测试独立 DbContext，覆盖正常路径 + 边界条件
- **测试数:** 261 个通过（29 Client + 232 Server）

## 部署

- 二进制: `dotnet publish -c Release -o publish`
- Docker: 多阶段构建 + docker-compose (Caddy 反向代理)
- 数据库自动迁移: `db.Database.MigrateAsync()`
