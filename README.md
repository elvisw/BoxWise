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

# 启动 Server（API + Admin 后台 + Blazor WASM 静态回退）
cd src/BoxWise.Server && dotnet run
# → https://localhost:5000

# （推荐）启动 Client 开发服务器（Blazor WASM 热重载）
cd src/BoxWise.Client && dotnet run
# → https://localhost:5001
```

**开发入口选择：**

| 地址 | 提供内容 | 热重载 | 推荐场景 |
|------|---------|--------|---------|
| `https://localhost:5001` | Blazor WASM 页面 | 有 | **日常 UI 开发（推荐）** |
| `https://localhost:5000` | API + Admin + WASM 静态回退 | 无 | 测试 Admin / 集成测试 |

> **日常开发用 `https://localhost:5001`。** API 请求通过 `wwwroot/appsettings.Development.json` 中的 `ApiBaseUrl` 配置自动跨源发送到 5000 端口。Admin 后台（`/admin`）是 Server 端 Razor Pages，在 5001 点击"管理后台"按钮自动跳转到 5000。
>
> **仅需一个端口时**，只启动 Server，访问 `https://localhost:5000` 即可同时使用页面 + API + Admin。
>
> **生产环境无需配置 `ApiBaseUrl`** — 不配置时 `Http.BaseAddress` 为 null，所有请求走同源，Admin 链接走 `/admin`。

登录: `admin` / `admin123`

### 二进制部署（Linux VPS）

适合无 Docker 环境，通过 systemd 管理进程。**CI 已自动构建，直接从 GitHub 下载即可。**

**前置条件：** .NET 10 Runtime + Caddy 或 Nginx。

```bash
# 1. 下载最新版本（从 GitHub Releases）
curl -L https://github.com/elvisw/BoxWise/releases/latest/download/boxwise-linux-x64.tar.gz -o boxwise-linux-x64.tar.gz

# 2. 解压到服务器
sudo mkdir -p /opt/boxwise
sudo tar -xzf boxwise-linux-x64.tar.gz -C /opt/boxwise
mkdir -p /opt/boxwise/data/images

# 3. 安装 .NET Runtime（如未安装）
# Ubuntu/Debian:
wget https://dot.net/v1/dotnet-install.sh -O dotnet-install.sh
chmod +x dotnet-install.sh
./dotnet-install.sh --channel 10.0 --runtime aspnetcore
# Fedora/CentOS:
sudo dnf install dotnet-runtime-10.0

# 4. 创建生产配置（AI 可选，不做 AI 可跳过）
sudo cat > /opt/boxwise/appsettings.Production.json << 'EOF'
{
  "Llm": {
    "BaseUrl": "https://api.openai.com/v1",
    "ApiKey": "sk-xxx",
    "Model": "gpt-4o-mini"
  }
}
EOF

# 5. 安装 systemd 服务
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

### 二进制部署（Windows Server）

适合 Windows Server 环境，通过 IIS 反向代理运行。**CI 已自动构建，直接从 GitHub 下载即可。**

**前置条件：** [.NET 10 Runtime](https://dotnet.microsoft.com/download/dotnet/10.0) + IIS（推荐）。

```powershell
# 1. 下载最新版本（从 GitHub Releases）
Invoke-WebRequest -Uri "https://github.com/elvisw/BoxWise/releases/latest/download/boxwise-win-x64.zip" -OutFile "boxwise-win-x64.zip"

# 2. 解压
Expand-Archive -Path boxwise-win-x64.zip -DestinationPath "C:\BoxWise"
New-Item -ItemType Directory -Force -Path "C:\BoxWise\data\images"

# 3. 创建生产配置（AI 可选）
@'
{
  "Llm": {
    "BaseUrl": "https://api.openai.com/v1",
    "ApiKey": "sk-xxx",
    "Model": "gpt-4o-mini"
  }
}
'@ | Out-File -FilePath "C:\BoxWise\appsettings.Production.json" -Encoding UTF8

# 4. 直接运行测试
$env:ASPNETCORE_ENVIRONMENT = "Production"
$env:ASPNETCORE_URLS = "http://+:5000"
$env:Admin__Username = "admin"
$env:Admin__Password = "你的强密码"
$env:DataDirectory = "C:\BoxWise\data"
dotnet C:\BoxWise\BoxWise.Server.dll
```

**配置 IIS（推荐生产环境）：**

1. 安装 [ASP.NET Core Hosting Bundle](https://dotnet.microsoft.com/download/dotnet/10.0)（选择 Hosting Bundle 下载）
2. IIS 管理器 → 应用程序池 → 添加应用程序池 → 名称 `BoxWise`，.NET CLR 版本选"无托管代码"
3. 添加网站 → 物理路径 `C:\BoxWise\`，端口 `80`，应用程序池 `BoxWise`
4. IIS 自动通过 ASP.NET Core Module（ANCM）将请求转发到 Kestrel，无需额外配置反向代理

**注册为 Windows 服务（无 IIS 方案）：**

```powershell
# 先确认 dotnet 路径
where.exe dotnet
# 通常位于 C:\Program Files\dotnet\dotnet.exe

# 注意：sc.exe 要求等号后有空格（binPath= 而非 binPath=）
sc.exe create "BoxWise" binPath= "C:\Program Files\dotnet\dotnet.exe C:\BoxWise\BoxWise.Server.dll" start= auto
sc.exe description "BoxWise" "箱知 · BoxWise 家庭物品管理服务"
sc.exe start "BoxWise"
```

> **路径差异：** Windows 版归档文件名为 `boxwise-win-x64.zip`，Linux 版为 `boxwise-linux-x64.tar.gz`。
>
> **自包含发布：** 如需在未安装 .NET Runtime 的机器上运行，可手动执行 `dotnet publish -c Release -r win-x64 --self-contained true`。

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

## 版本管理

版本号由构建时自动从 Git 标签获取，无需手动维护。

```bash
# 发版流程
git tag v1.0.1                  # 1. 打标签
git push origin v1.0.1          # 2. 推送标签（触发 CI 构建）
dotnet build                    # 3. 本地构建自动读取标签作为版本号
```

| Git 状态 | 关于页面显示 |
|----------|-------------|
| HEAD = `v1.0.1` | `v1.0.1` |
| `v1.0.1` 之后 4 个 commit | `v1.0.1-4-gabcdef1` |
| 无 tag（git 可用） | `abcdef1` |
| 非 git 环境 | `v1.0.0`（回退，构建警告） |

**工作原理：** `Directory.Build.targets` 中的 MSBuild Target 在 `dotnet build` 前自动执行 `git describe --tags`，提取版本号写入程序集属性。关于页面（`/about`）读取并显示。

> **CI/Docker 注意事项：** CI 构建前需 `git fetch --tags` 获取标签。Docker 构建因 `.dockerignore` 排除 `.git/` 目录，版本号始终回退到 `1.0.0`；如需嵌入真实版本，应在宿主机构建后 COPY 发布产物进镜像。

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

GNU General Public License v3.0
