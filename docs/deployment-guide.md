# 部署与 CI/CD

> BoxWise — 部署架构与持续集成

## 部署模式

### 1. 二进制部署

```bash
dotnet publish src/BoxWise.Server -c Release -o publish
# → 上传 publish/ 到服务器 /opt/boxwise/
# → 反向代理: Caddy/Nginx → Unix Domain Socket（http://unix:/opt/boxwise/boxwise.sock）
# → systemd 服务管理

# 生产环境配置
# ASPNETCORE_URLS=http://unix:/opt/boxwise/boxwise.sock
# 使用 Unix Domain Socket 代替 TCP 端口：零端口冲突、无网络栈开销、文件系统权限隔离
```

### 2. Docker 部署

```
Caddy (443→80)
    └── boxwise:5000 (ASP.NET Core)
```

**持久化:**
- `./data:/app/data` — SQLite + 图片 + Data Protection 密钥环 + SMTP 加密配置
- `./data/caddy:/data` — Caddy 证书

**环境变量:**
| 变量 | 说明 |
|------|------|
| `ASPNETCORE_URLS` | 监听地址（生产推荐 `http://unix:/opt/boxwise/boxwise.sock`） |
| `DataDirectory` | 数据目录（含 `keys/` 密钥环、`images/` 图片、`smtp-config.json`） |
| `ConnectionStrings__DefaultConnection` | SQLite 连接字符串 |
| `Admin__Password` | 管理员创建密码 |
| `Admin__Username` | 管理员用户名（默认 admin） |
| `Admin__Email` | 管理员邮箱 |
| `Llm__ApiKey` | AI API Key |
| `Llm__BaseUrl` | AI API 地址 |
| `Llm__Model` | AI 模型名称 |
| `WebAuthn__Origin` | WebAuthn 允许的 origin（生产环境必需，如 `https://boxwise.example.com`） |
| `WebAuthn__ServerDomain` | WebAuthn 服务器域名（默认从 Origin 解析） |

**WebAuthn 配置说明：**

| 配置键（JSON） | 环境变量 | 默认值 | 说明 |
|---------------|---------|--------|------|
| `WebAuthn:Origin` | `WebAuthn__Origin` | `https://localhost:5001` | WebAuthn 允许的 origin，与浏览器地址栏的协议+域名+端口一致 |
| `WebAuthn:ServerDomain` | `WebAuthn__ServerDomain` | 从 Origin 解析的 Host | 服务器域名，用于 FIDO2 RP（Relying Party）标识 |

**Docker Compose 配置示例（含 WebAuthn + 2FA 配置）：**

```yaml
services:
  boxwise:
    build: .
    restart: unless-stopped
    environment:
      - ASPNETCORE_ENVIRONMENT=Production
      - ASPNETCORE_URLS=http://+:5000
      - DataDirectory=/app/data
      - ConnectionStrings__DefaultConnection=Data Source=/app/data/boxwise.db
      # 管理员账户
      - Admin__Username=admin
      - Admin__Email=admin@boxwise.local
      - Admin__Password=请替换为强密码
      # WebAuthn（生产环境必需）
      - WebAuthn__Origin=https://boxwise.example.com
      - WebAuthn__ServerDomain=boxwise.example.com
      # TOTP 2FA 配置
      - TwoFactor__SetupGracePeriodHours=24
      - TwoFactor__RecoveryCodeCount=8
      - TwoFactor__RecoveryCodeLength=10
      # 速率限制配置（可选，默认值见 appsettings.json）
      - RateLimit__LoginPermitLimit=5
      - RateLimit__LoginWindowMinutes=15
    volumes:
      - ./data:/app/data

  caddy:
    image: caddy:2-alpine
    restart: unless-stopped
    ports:
      - "443:443"
      - "80:80"
    volumes:
      - ./Caddyfile:/etc/caddy/Caddyfile
      - ./data/caddy:/data
    depends_on:
      boxwise:
        condition: service_started
```

**Dockerfile:** 多阶段构建（SDK → Runtime），最终镜像基于 `mcr.microsoft.com/dotnet/aspnet:10.0`

### 3. 生产配置

```json
// appsettings.Production.json
{
  "Llm": {
    "BaseUrl": "https://api.openai.com/v1",
    "ApiKey": "sk-xxx",
    "Model": "gpt-4o-mini"
  }
}
```

AI 未配置时静默降级为手动输入。

**Data Protection 密钥环持久化：**

`data/keys/` 目录存储 ASP.NET Core Data Protection 密钥环，用于加密：
- TOTP 密钥（`TwoFactorService` 使用 `DataProtectionProvider` 加解密）
- SMTP 密码（`SmtpConfigurationService` 加密保存）
- SessionToken（2FA 流程的临时令牌）

**密钥环丢失后已存储的加密数据将无法解密**，需重新配置。Docker 部署确保 `./data:/app/data` 卷映射持久化该目录。

---

## CI/CD

### GitHub Actions — `.github/workflows/release.yml`

发布流水线（具体步骤见文件）。

---

## 端口规划

| 组件 | 端口 | 说明 |
|------|------|------|
| Server HTTPS | `5000` | API + Admin + SPA 回退 |
| Client HTTPS | `5001` | Blazor WASM 开发（热重载） |
| 生产 | 同源 / Unix Socket | Server 托管 Client 静态文件，生产推荐 UDS |

生产环境使用 Unix Domain Socket 时，Caddy/Nginx 通过 `unix//opt/boxwise/boxwise.sock` 转发请求，Kestrel 不占用 TCP 端口。

---

## 开发 vs 生产

| 配置 | 开发 | 生产 |
|------|------|------|
| `ApiBaseUrl` | `https://localhost:5000/` | 空（同源） |
| CORS | `localhost:5001` | 不需要 |
| Admin 链接 | 绝对 URL 指向 5000 | 相对路径 `/admin` |
| WebAuthn Origin | `https://localhost:5001` | 实际域名（如 `https://boxwise.example.com`） |
| WebAuthn ServerDomain | `localhost` | 实际域名 |
| 数据库路径 | `../../data/boxwise.db` | `data/boxwise.db` |
| 监听地址 | TCP `https://localhost:5000` | Unix Socket `http://unix:/opt/boxwise/boxwise.sock` |
