# 部署与 CI/CD

> BoxWise — 部署架构与持续集成

## 部署模式

### 1. 二进制部署

```bash
dotnet publish src/BoxWise.Server -c Release -o publish
# → 上传 publish/ 到服务器 /opt/boxwise/
# → 反向代理: Caddy/Nginx → localhost:5000
# → systemd 服务管理
```

### 2. Docker 部署

```
Caddy (443→80)
    └── boxwise:5000 (ASP.NET Core)
```

**持久化:**
- `./data:/app/data` — SQLite + 图片
- `./data/caddy:/data` — Caddy 证书

**环境变量:**
| 变量 | 说明 |
|------|------|
| `ASPNETCORE_URLS` | 监听地址 |
| `DataDirectory` | 数据目录 |
| `ConnectionStrings__DefaultConnection` | SQLite 连接字符串 |
| `Admin__Password` | 管理员创建密码 |
| `Admin__Username` | 管理员用户名（默认 admin） |
| `Llm__ApiKey` | AI API Key |
| `Llm__BaseUrl` | AI API 地址 |
| `Llm__Model` | AI 模型名称 |

**Dockerfile:** 多阶段构建（SDK → Runtime），最终镜像基于 `mcr.microsoft.com/dotnet/aspnet:10.0`

### 3. 生产配置

```json
// appsettings.Production.json
{
  "LlmClient": {
    "BaseUrl": "https://api.openai.com/v1",
    "ApiKey": "sk-xxx",
    "Model": "gpt-4o-mini"
  }
}
```

AI 未配置时静默降级为手动输入。

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
| 生产 | 同源 | Server 托管 Client 静态文件 |

---

## 开发 vs 生产

| 配置 | 开发 | 生产 |
|------|------|------|
| `ApiBaseUrl` | `https://localhost:5000/` | 空（同源） |
| CORS | `localhost:5001` | 不需要 |
| Admin 链接 | 绝对 URL 指向 5000 | 相对路径 `/admin` |
| 数据库路径 | `../../data/boxwise.db` | `data/boxwise.db` |
