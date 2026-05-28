# 认证与安全

> BoxWise — 身份认证与安全架构

## 认证体系

### Server 端

- **框架:** ASP.NET Core Identity + Cookie 认证
- **用户实体:** `AppUser : IdentityUser`（无自定义字段）
- **角色:** `Admin` 角色（`IdentityRole`）
- **密码策略:** 最小长度 4，无复杂度要求（家用场景）
- **Cookie 配置:**
  - `HttpOnly = true`
  - `SameSite = None`（跨端口开发需要）
  - `Secure = Always`
  - 过期时间 30 天，滑动过期
  - 401 时返回 JSON 而非重定向到登录页

### Client 端

- **认证桥接:** `CookieAuthenticationStateProvider` → 调用 `GET /api/auth/me`
- **跨源 Cookie:** `CookieHandler` 设置 `BrowserRequestCredentials.Include`
- **认证状态:** `AppState.IsAdmin` 控制管理功能可见性

### 授权策略

| 策略 | 范围 | 说明 |
|------|------|------|
| `FallbackPolicy` | 全局 | `RequireAuthenticatedUser()` — 所有端点默认需登录 |
| `AdminOnly` | 管理端点 | `RequireRole("Admin")` — Admin Razor Pages + 用户管理 |

### 端点安全

| 端点 | 授权 |
|------|------|
| `POST /api/auth/login` | `.AllowAnonymous()` |
| `GET /index.html` (SPA 回退) | `.AllowAnonymous()` |
| `MapStaticAssets()` | `.AllowAnonymous()` |
| 所有其他端点 | `FallbackPolicy` → 401 |

### 管理员创建

- 首次启动时通过 `Admin__Password` 环境变量触发种子数据
- 默认用户名: `admin`（可通过 `Admin__Username` 覆盖）
- 密码变更时自动检测并重置

### CORS

- 开发环境: 允许 `https://localhost:5001` 跨源（Cookie + 任意头/方法）
- 生产环境: 同源部署（Server 托管 Client 静态文件），不需要 CORS

---

## 数据安全

- **密码存储:** ASP.NET Core Identity 默认哈希（PBKDF2）
- **数据库:** SQLite 文件位于 `data/boxwise.db`
- **图片存储:** 文件系统，`ImageStorageService` 管理路径
- **HTTPS:** `app.UseHttpsRedirection()` 强制启用
