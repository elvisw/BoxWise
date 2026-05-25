# 箱知 · BoxWise

家庭物品收纳管理 PWA。拍照→AI 识别→选位置→保存，搜一下就知道东西在哪。

## 技术栈

| 层 | 技术 |
|----|------|
| 前端 | Blazor WASM (PWA) + MudBlazor 9.x |
| 后端 | ASP.NET Core Minimal API (.NET 10) |
| 数据库 | SQLite + EF Core |
| 认证 | ASP.NET Core Identity + Cookie |
| AI | OpenAI 兼容 Vision API |
| 图片 | SkiaSharp (300px + 1200px 缩略图) |

## 配置

### 管理员账户

应用启动时通过配置自动创建管理员。**本地开发已预置，生产环境必须手动设置。**

| 环境 | 配置方式 | 账户 |
|------|---------|------|
| **本地开发** | `launchSettings.json` + `appsettings.Development.json`（已预置） | `admin` / `admin123` |
| **Docker** | `docker-compose.yml` 环境变量 | 无默认，必须设置 |
| **二进制** | systemd / shell 环境变量 | 无默认，必须设置 |

配置键名：

| 配置键（JSON） | 环境变量 | 说明 |
|---------------|---------|------|
| `Admin:Username` | `Admin__Username` | 管理员用户名，默认 `admin` |
| `Admin:Password` | `Admin__Password` | 管理员密码（必填） |

行为说明：

| 场景 | 行为 |
|------|------|
| `Admin:Password` 已配置 | 首次启动自动创建管理员，重启时若密码变更则自动更新 |
| `Admin:Password` 未配置 | 不创建管理员账户，日志输出警告 |
| 管理员被误删 | 只要密码环境变量仍存在，重启即自动重建 |

> **安全提示：** 生产环境务必使用强密码，不要使用默认的 `admin123`。

### 创建家庭成员账户

1. 以 `admin` 登录，点击首页底部"管理后台" → `/admin`
2. 点击"+ 创建账户"，填写用户名和密码分发给家人
3. **所有成员共享同一物品库，权限相同**

### AI 识别（可选）

AI 识别为可选功能。未配置时拍照后自动切换为手动输入，不阻塞录入。支持任意 OpenAI 兼容提供商（OpenAI、火山方舟、Kimi、Qwen 等）。

配置键名：

| 配置键（JSON） | 环境变量 | 默认值 | 说明 |
|---------------|---------|--------|------|
| `Llm:BaseUrl` | `Llm__BaseUrl` | `https://api.openai.com/v1` | API 端点 |
| `Llm:ApiKey` | `Llm__ApiKey` | （空） | API 密钥，未填则不启用 AI |
| `Llm:Model` | `Llm__Model` | `gpt-4o` | 模型名称 |

```bash
# 本地开发：User Secrets（推荐，不会误提交到 git）
cd src/BoxWise.Server
dotnet user-secrets set "Llm:ApiKey" "sk-xxx"
dotnet user-secrets set "Llm:Model" "gpt-4o-mini"
# 可选：切换提供商
dotnet user-secrets set "Llm:BaseUrl" "https://api.openai.com/v1"

# 本地开发备选：直接编辑 appsettings.Development.json
# 本地开发备选：launchSettings.json 环境变量 Llm__ApiKey
```

API 调用超时 15s，失败时静默降级为手动输入。

## 运行

### 本地开发

```bash
# 构建 + 测试
dotnet build BoxWise.slnx
dotnet test BoxWise.slnx

# 启动 Server（同时提供 API + WASM 静态文件回退）
cd src/BoxWise.Server && dotnet run
# → https://localhost:5000
# 登录: admin / admin123

# （可选）启动 Client 开发服务器（热重载）
cd src/BoxWise.Client && dotnet run
# → https://localhost:5001
```

| 地址 | 说明 |
|------|------|
| `https://localhost:5000` | Server，同时提供 API + WASM 静态文件 |
| `https://localhost:5001` | Client 开发服务器（热重载） |

### 二进制部署（Linux VPS）

适合无 Docker 环境，通过 systemd 管理进程。

**前置条件：** .NET 10 Runtime（`apt install dotnet-runtime-10.0`）、Caddy 或 Nginx。

```bash
# 1. 发布（在开发机上执行）
dotnet publish src/BoxWise.Server -c Release -o publish

# 2. 上传到服务器
scp -r publish/* user@server:/opt/boxwise/
mkdir -p /opt/boxwise/data/images

# 3. 创建生产配置（AI 可选，不做 AI 可跳过）
cat > /opt/boxwise/appsettings.Production.json << 'EOF'
{
  "Llm": {
    "BaseUrl": "https://api.openai.com/v1",
    "ApiKey": "sk-xxx",
    "Model": "gpt-4o-mini"
  }
}
EOF

# 4. 安装 systemd 服务
sudo cat > /etc/systemd/system/boxwise.service << 'EOF'
[Unit]
Description=BoxWise Server
After=network.target

[Service]
WorkingDirectory=/opt/boxwise
ExecStart=dotnet /opt/boxwise/BoxWise.Server.dll
Restart=always
RestartSec=10
Environment=ASPNETCORE_ENVIRONMENT=Production
Environment=ASPNETCORE_URLS=http://+:5000
Environment=Admin__Username=admin
Environment=Admin__Password=你的强密码
Environment=DataDirectory=/opt/boxwise/data

[Install]
WantedBy=multi-user.target
EOF

sudo systemctl daemon-reload
sudo systemctl enable --now boxwise
```

**配置反向代理（Caddy）：**

```bash
sudo apt install caddy

# /etc/caddy/Caddyfile
你的域名 {
    root * /opt/boxwise/wwwroot
    try_files {path} /index.html
    file_server
    encode gzip
    reverse_proxy /api/* localhost:5000
    reverse_proxy /admin/* localhost:5000
    header /api/images/* Cache-Control "public, max-age=86400"
}

sudo systemctl restart caddy
```

### Docker 部署

**前置条件：** Docker + Docker Compose、域名指向服务器 IP。

```bash
# 1. 创建生产配置（AI 可选）
cat > src/BoxWise.Server/appsettings.Production.json << 'EOF'
{
  "Llm": {
    "BaseUrl": "https://api.openai.com/v1",
    "ApiKey": "sk-xxx",
    "Model": "gpt-4o-mini"
  }
}
EOF

# 2. 修改 Caddyfile 中的域名
sed -i 's/boxwise.example.com/你的域名/' Caddyfile

# 3. 修改 docker-compose.yml 中的管理员密码
#    将 Admin__Password=请替换为强密码 改为实际密码

# 4. 启动
docker compose up -d

# 5. 查看状态
docker compose ps
docker compose logs -f
```

**持久化目录：**

| 路径 | 内容 |
|------|------|
| `./data/boxwise.db` | SQLite 数据库 |
| `./data/images/{itemId}/` | 物品原图 + 缩略图 |
| `./data/caddy/` | Caddy TLS 证书 |

容器重启后数据完整保留。

## 日常使用

**录入物品：** 登录 → 点击"录入物品" → 拍照（AI 识别）或跳过 → 填写名称 → 选位置 → 加标签 → 保存。连续收纳模式下位置自动继承。

**查找物品：** 首页搜索框输入关键词（模糊匹配名称/备注/标签），或点击"浏览物品"按位置树/标签筛选。

**查看详情 / 删除：** 点击物品卡片 → 详情页 → 查看完整信息或点击"删除物品"（需确认，不可撤销）。

**PWA 安装：** 支持的浏览器访问应用 → 地址栏"安装"按钮 → 桌面独立启动 → 离线浏览已缓存的物品信息。

## 项目结构

```
BoxWise/
├── src/
│   ├── BoxWise.Client/        # Blazor WASM (PWA)
│   │   ├── Pages/             # 页面组件
│   │   ├── Components/        # 可复用组件
│   │   └── Services/          # HTTP 客户端服务
│   ├── BoxWise.Server/        # ASP.NET Core Web API
│   │   ├── Endpoints/         # Minimal API 路由
│   │   ├── Repositories/      # 数据访问层
│   │   ├── Services/          # 业务逻辑 + AI + 图片
│   │   ├── Models/            # EF Core 实体
│   │   └── Data/              # DbContext + Configurations
│   └── BoxWise.Shared/        # 共享 DTO
├── Dockerfile                 # 多阶段构建
├── docker-compose.yml         # 双服务编排
├── Caddyfile                  # 反向代理配置
└── CLAUDE.md                  # AI 辅助开发上下文
```

## 功能

- 物品拍照录入 + AI 自动识别（OpenAI 兼容 API，15s 超时静默降级）
- 层级位置管理（用户自定义深度，物化路径）
- 标签系统（多对多，跨位置筛选）
- 连续收纳（自动继承位置）
- 全文搜索（模糊匹配名称/备注/标签）
- 缩略图网格浏览（响应式 2/3/4/6 列）
- 位置 + 标签组合筛选
- 物品删除（级联清理图片文件）
- PWA（安装到桌面 + 离线浏览缓存图片）
- 管理员后台（Razor Pages，创建家庭账户）

## 维护

```bash
# EF Core 迁移
cd src/BoxWise.Server
dotnet ef migrations add <MigrationName>
dotnet ef database update

# 备份数据
cp -r data/ backup-$(date +%Y%m%d)/

# 查看日志（二进制 / systemd）
journalctl -u boxwise -f

# 查看日志（Docker）
docker compose logs -f
```

## 许可证

MIT
