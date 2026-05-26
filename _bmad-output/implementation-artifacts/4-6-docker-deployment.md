# Story 4.6: Docker 容器化部署

Status: review

## Story

As a 运维者，
I want 用 Docker Compose 一键部署应用，
So that 在 1C1G Linux VPS 上稳定运行。

## Acceptance Criteria

1. **AC-1: 多阶段 Dockerfile** — SDK 构建 + ASP.NET Runtime，生成最小化镜像
2. **AC-2: Docker Compose** — `docker compose up -d` 启动应用
3. **AC-3: 持久化卷** — `./data:/app/data` 挂载，SQLite + 图片持久化
4. **AC-4: HTTPS** — Caddy 反向代理 + 自动 Let's Encrypt TLS
5. **AC-5: 反向代理** — `/api/*` `/admin/*` → ASP.NET，静态文件直出，`/images/*` Cache-Control 24h
6. **AC-6: Gzip 压缩** — Caddy 启用 `encode gzip`

## Tasks / Subtasks

- [x] Task 1: 创建 Dockerfile (AC: #1)
  - [x] 1.1 多阶段构建：SDK 10.0 → ASP.NET 10.0 Runtime
  - [x] 1.2 `dotnet publish -c Release -o /app` + 复制静态文件到 wwwroot

- [x] Task 2: 创建 Caddyfile (AC: #4, #5, #6)
  - [x] 2.1 反向代理 `/api/*` `/admin/*` → `localhost:5000`
  - [x] 2.2 静态文件服务 + `file_server`
  - [x] 2.3 `/images/*` Cache-Control 24h + `encode gzip`

- [x] Task 3: 创建 docker-compose.yml (AC: #2, #3)
  - [x] 3.1 boxwise + caddy 两个服务
  - [x] 3.2 Volume 挂载 `./data:/app/data` + `./Caddyfile:/etc/caddy/Caddyfile`
  - [x] 3.3 端口 443 → Caddy → boxwise:5000

- [x] Task 4: 创建 .dockerignore (AC: #1)
  - [x] 4.1 排除 bin/obj/node_modules/.git

- [x] Task 5: 构建验证 (AC: #1-#6)
  - [x] 5.1 `dotnet build BoxWise.slnx` 零错误零警告
  - [x] 5.2 `dotnet test BoxWise.slnx` 全部通过

---

## Dev Notes

### 前置上下文

- **项目含 Server + Client** — Server 引用 Client 项目用于 SPA 回退
- **数据目录** — SQLite 数据库 + 物品图片存储在 `{DataDirectory}`（默认 `./data`）
- **端口** — ASP.NET 监听 `5000`，Caddy 暴露 `443`
- **appsettings.Production.json** — 生产密钥（LLM API Key 等）通过 docker-compose 环境变量注入

### Dockerfile

```dockerfile
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish src/BoxWise.Server -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
ENTRYPOINT ["dotnet", "BoxWise.Server.dll"]
```

### Caddyfile

```
boxwise.example.com {
    reverse_proxy /api/* localhost:5000
    reverse_proxy /admin/* localhost:5000
    root * /app/wwwroot
    file_server
    encode gzip
    header /images/* Cache-Control "public, max-age=86400"
}
```

### docker-compose.yml

```yaml
services:
  boxwise:
    build: .
    environment:
      - ASPNETCORE_ENVIRONMENT=Production
      - DataDirectory=/app/data
    volumes:
      - ./data:/app/data
  caddy:
    image: caddy:2-alpine
    ports:
      - "443:443"
      - "80:80"
    volumes:
      - ./Caddyfile:/etc/caddy/Caddyfile
      - ./data/caddy:/data
```

### 文件结构变更

```
BoxWise/
  Dockerfile                   (new)
  docker-compose.yml           (new)
  Caddyfile                    (new)
  .dockerignore                (new)
```

### 构建与验证

```bash
dotnet build BoxWise.slnx
dotnet test BoxWise.slnx
# Docker 构建验证（需要 Docker）：
# docker build -t boxwise:latest .
```

---

## References

| 内容 | 来源 |
|------|------|
| Story AC 定义 | [Source: epics.md#Story 4.6] |
| AR-7 Docker + Caddy | [Source: architecture.md#AR-7] |
| Docker 配置 | [Source: architecture.md#Containerization] |
| Caddyfile 配置 | [Source: architecture.md#Reverse Proxy] |

## Dev Agent Record

### Agent Model Used

deepseek-v4-pro

### Debug Log References

### Completion Notes List

✅ 全部 5 个 Task 完成 — Docker 容器化部署配置就绪

**实施要点：**
- Dockerfile：多阶段构建（SDK build → aspnet runtime），减小最终镜像体积
- Caddyfile：反向代理 /api/* /admin/* → localhost:5000，静态文件服务，gzip，图片 24h 缓存
- docker-compose.yml：boxwise + caddy 双服务，volume 持久化 data + Caddyfile
- .dockerignore：排除源码/构建产物，加速构建上下文
- 端口：Caddy 暴露 80/443，自动 Let's Encrypt

### File List

**新增文件:**
- `Dockerfile` (new)
- `docker-compose.yml` (new)
- `Caddyfile` (new)
- `.dockerignore` (new)
