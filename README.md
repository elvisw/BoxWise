# 箱知 · BoxWise

把家变成可搜索的。拍照、AI 识别、分类存放——再也不用翻箱倒柜找东西。

家庭物品收纳管理 PWA（Progressive Web App，渐进式 Web 应用）。

[查看最新版本](https://github.com/elvisw/BoxWise/releases)

---

## 功能一览

- **拍照录入 + AI 识别** — 拍照后自动识别物品名称，客户端直调火山 ARK API（30s 超时静默降级为手动输入）
- **层级位置管理** — 用户自定义任意深度位置树，物化路径（通过存储完整路径字符串实现任意深度层级）
- **标签系统** — 多对多标签，跨位置组合筛选
- **连续收纳** — 录入时自动继承上次使用的位置
- **全文搜索** — 模糊匹配物品名称、备注、标签
- **缩略图网格浏览** — 响应式 2/3/4/6 列自适应布局
- **位置 + 标签组合筛选** — 浏览时按位置树与标签交叉筛选
- **物品详情查看与编辑** — 点击卡片进入详情页，可编辑完整信息
- **物品删除（级联）** — 删除物品时自动清理关联的原图与缩略图
- **缩略图自动生成** — 300px + 1200px 两级缩略图（SkiaSharp），后台异步生成
- **TOTP 双因素认证** — TOTP（Time-based One-Time Password，基于时间的一次性密码）身份验证器
- **WebAuthn 通行密钥** — 支持指纹、面容、硬件密钥（如 YubiKey）
- **PWA 安装与离线浏览** — 安装到桌面，离线查看已缓存的物品信息
- **Admin 后台** — 独立 Razor Pages 管理界面，支持账户管理、2FA 重置、SMTP 在线配置
- **通行密钥与 2FA 操作速率限制** — WebAuthn 登录和 2FA 管理端点频率保护

## 技术栈

| 层 | 技术 | 版本 |
|----|------|------|
| 前端 | Blazor WASM (PWA) | .NET 10 |
| UI 组件 | MudBlazor | 9.x |
| 后端 | ASP.NET Core Minimal API | .NET 10 |
| 数据库 | SQLite + EF Core | 10.0 |
| 认证 | ASP.NET Core Identity + Cookie | 10.0 |
| AI | OpenAI 兼容 Vision API | — |
| 图片处理 | SkiaSharp | 3.x |
| 反向代理 | Caddy / IIS / Nginx | — |

## 快速开始（本地开发）

本地开发零配置即可运行，无需数据库、无需 AI 密钥。

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
| `https://localhost:5001` | Blazor WASM 页面（Client 开发服务器） | 有 | **日常 UI 开发（推荐）** |
| `https://localhost:5000` | API + Admin + WASM 静态回退（Server） | 无 | 测试 Admin / 完整集成测试 |

> **日常开发用 `https://localhost:5001`。** API 请求通过 `wwwroot/appsettings.Development.json` 中的 `ApiBaseUrl` 配置自动跨源发送到 5000 端口。Admin 后台（`/admin`）是 Server 端 Razor Pages，在 5001 点击"管理后台"按钮自动跳转到 5000。
>
> **仅需一个端口时**，只启动 Server，访问 `https://localhost:5000` 即可同时使用页面 + API + Admin。
>
> **生产环境无需配置 `ApiBaseUrl`** — 不配置时 `Http.BaseAddress` 为 null，所有请求走同源，Admin 链接走 `/admin`。

登录：`admin` / `BoxWise!2024Dev`

## 配置

### 管理员账户

应用启动时通过配置自动创建管理员。**本地开发已预置，生产环境必须手动设置。**

| 环境 | 配置方式 | 账户 |
|------|---------|------|
| **本地开发** | `launchSettings.json` 环境变量（已预置） | `admin` / `BoxWise!2024Dev` |
| **Docker** | `docker-compose.yml` 环境变量 | 无默认，必须设置 |
| **二进制** | systemd / shell 环境变量 | 无默认，必须设置 |

配置键名：

| 配置键（JSON） | 环境变量 | 默认值 | 说明 |
|---------------|---------|--------|------|
| `Admin:Username` | `Admin__Username` | `admin` | 管理员用户名 |
| `Admin:Email` | `Admin__Email` | `admin@boxwise.local` | 管理员邮箱 |
| `Admin:Password` | `Admin__Password` | （空） | 管理员密码（必填） |
| `ConnectionStrings:DefaultConnection` | `ConnectionStrings__DefaultConnection` | `Data Source=../../data/boxwise.db` | SQLite 数据库路径（开发默认值，生产通过环境变量覆盖） |
| `DataDirectory` | `DataDirectory` | `data/` | 数据目录 — 包含 keys/（密钥环）、images/（图片）、smtp-config.json |

行为说明：

| 场景 | 行为 |
|------|------|
| `Admin:Password` 已配置 | 首次启动自动创建管理员，重启时若密码变更则自动更新 |
| `Admin:Password` 未配置 | 不创建管理员账户，日志输出警告 |
| 管理员被误删 | 只要密码环境变量仍存在，重启即自动重建 |

> **安全提示：** 生产环境务必使用强密码，不要使用默认密码。

### 创建家庭成员账户

1. 以 `admin` 登录，点击首页底部"管理后台" → `/admin`
2. 点击"+ 创建账户"，填写用户名、邮箱和密码分发给家人
3. **所有成员共享同一物品库，权限相同**

### AI 识别（可选）

AI 识别为可选功能。未配置时拍照后自动切换为手动输入，不阻塞录入。v1 通过客户端浏览器直调火山引擎 ARK API（doubao-seed-2-0-pro-260215）。

**配置方式：** 通过 Server 端环境变量 `LlmApi__*` 注入（种子数据自动入库），或部署后通过 Admin 后台 `/admin/llm-config` 在线管理。

| 环境变量 | 说明 | 必填 |
|----------|------|:--:|
| `LlmApi__BaseUrl` | LLM API 地址 | 是 |
| `LlmApi__ApiKey` | API 密钥 | 是 |
| `LlmApi__Model` | 模型名称（默认 `doubao-seed-2-0-pro-260215`） | 否 |
| `LlmApi__TimeoutSeconds` | 超时秒数（默认 30） | 否 |

API 调用超时 30s，失败时静默降级为手动输入。

### SMTP 邮件配置（可选）

SMTP 邮件服务用于发送账户相关邮件（如邮箱修改确认）。登录管理后台 → 点击 "SMTP 设置" 即可在线配置，**无需修改配置文件或重启服务**。

> **注意：** Email 2FA 登录方式已在 v0.11 中退役，当前仅支持 TOTP 和通行密钥作为双因素认证方式。SMTP 配置为可选，仅在使用 Identity 邮箱管理功能时需要。

配置项：

| 配置项 | 说明 | 必填 |
|--------|------|------|
| SMTP 服务器地址 | 邮件发送服务器主机名或 IP | 是 |
| 端口号 | 常用端口：587（STARTTLS）、465（SSL/TLS）、25（明文，不推荐） | 否（默认 587） |
| 用户名 | SMTP 认证用户名（支持无认证中继） | 否 |
| 密码 | SMTP 认证密码，保存后加密存储 | 否 |
| 发件人地址 | 邮件发件人邮箱地址 | 是 |
| 发件人名称 | 邮件发件人显示名称 | 否 |

> **端口选择指南：** 推荐使用 587（STARTTLS）以获得最佳兼容性。465（SSL/TLS）仅用于旧客户端。25 端口通常被云服务商屏蔽，不建议使用。

常见邮箱提供商示例：

| 提供商 | SMTP 服务器 | 端口 | 特殊说明 |
|--------|------------|------|---------|
| **Gmail** | `smtp.gmail.com` | 587 | 需使用 [应用专用密码](https://support.google.com/accounts/answer/185833) |
| **QQ 邮箱** | `smtp.qq.com` | 587 | 需开启 SMTP 服务并获取授权码 |
| **163 邮箱** | `smtp.163.com` | 465 | 需开启 SMTP 服务并获取授权码 |
| **Outlook/Hotmail** | `smtp-mail.outlook.com` | 587 | 使用邮箱密码或应用专用密码 |

> **安全提示：** 密码保存时使用 ASP.NET Core Data Protection API 加密存储。确保 `data/keys/` 目录已持久化（Docker 部署需挂载卷），否则密钥环丢失后密码解密失败，需要重新输入密码。

### WebAuthn 通行密钥（可选）

支持使用指纹、面容或硬件密钥（如 YubiKey）作为双因素认证方式。WebAuthn 凭证与注册时的 **origin（协议 + 域名 + 端口）** 绑定，配置错误将导致通行密钥无法使用。

#### 开发环境

开箱即用，无需额外配置。默认 origin 为 `https://localhost:5001`，同时允许 `https://localhost:5000`。

#### 生产环境

**必须配置 `WebAuthn:Origin` 和 `WebAuthn:ServerDomain`**，否则 FIDO2 使用默认值 `localhost` 作为 RP ID，浏览器会因域名不匹配拒绝通行密钥操作。

**方式一：配置文件**

创建 `src/BoxWise.Server/appsettings.Production.json`：

```json
{
  "WebAuthn": {
    "Origin": "https://你的域名",
    "ServerDomain": "你的域名"
  }
}
```

> 例如域名为 `boxwise.example.com`，则 Origin = `https://boxwise.example.com`，ServerDomain = `boxwise.example.com`。

**方式二：Docker 环境变量**

```yaml
# docker-compose.yml
environment:
  - WebAuthn__Origin=https://你的域名
  - WebAuthn__ServerDomain=你的域名
```

**方式三：二进制部署环境变量**

```bash
# .env 文件
WebAuthn__Origin=https://你的域名
WebAuthn__ServerDomain=你的域名
```

> **常见错误：** 生产环境忘记配置 → 报错"浏览器验证失败，请确保设备支持通行密钥功能" → 检查 `WebAuthn:Origin` 和 `WebAuthn:ServerDomain` 是否已配置且与浏览器地址栏一致。

详见：[WebAuthn 通行密钥配置指南](docs/webauthn-setup-guide.md)

## 部署

三种部署方式：**Docker（推荐，开箱即用）| Linux 二进制（无 Docker 的 VPS）| Windows 二进制（Windows Server 环境）**。

### 二进制部署（Linux VPS）

适合无 Docker 环境，通过 systemd 管理进程。生产通信使用 **Unix Domain Socket** 代替 TCP 端口（零端口冲突、无网络栈开销、文件系统权限隔离）。**CI 已自动构建，直接从 GitHub 下载即可。**

**前置条件：** .NET 10 Runtime + Caddy 或 Nginx。

```bash
# 1. 下载最新版本（从 GitHub Releases）
curl -L https://github.com/elvisw/BoxWise/releases/latest/download/boxwise-linux-x64.tar.gz -o boxwise-linux-x64.tar.gz

# 2. 解压到服务器
sudo mkdir -p /opt/boxwise
sudo tar -xzf boxwise-linux-x64.tar.gz -C /opt/boxwise
sudo mkdir -p /opt/boxwise/data/images

# 3. 创建专用系统用户
sudo useradd -r -s /bin/false boxwise
sudo chown -R boxwise:boxwise /opt/boxwise

# 4. 创建环境变量文件
cat << 'EOF' | sudo tee /opt/boxwise/.env > /dev/null
Admin__Username=admin
Admin__Email=admin@你的域名
Admin__Password=你的强密码
ConnectionStrings__DefaultConnection=Data Source=/opt/boxwise/data/boxwise.db
EOF
sudo chown boxwise:boxwise /opt/boxwise/.env
sudo chmod 600 /opt/boxwise/.env

# 5. 安装 .NET Runtime（如未安装）
# 注意：以下 APT/DNF 方式仅支持 x64 架构。
# ARM64 Ubuntu 可直接使用官方源；其他 ARM 架构请用 dotnet-install.sh。
#
# Ubuntu 24.04+：直接使用 Ubuntu 官方源（无需额外配置）
sudo apt-get update && sudo apt-get install -y aspnetcore-runtime-10.0

# Ubuntu 22.04：需先安装 add-apt-repository（最小化安装可能缺失），再添加 backports PPA
sudo apt-get update && sudo apt-get install -y software-properties-common
sudo add-apt-repository -y ppa:dotnet/backports
sudo apt-get update && sudo apt-get install -y aspnetcore-runtime-10.0

# Debian 13（Trixie）
wget https://packages.microsoft.com/config/debian/13/packages-microsoft-prod.deb -O packages-microsoft-prod.deb
sudo dpkg -i packages-microsoft-prod.deb && rm packages-microsoft-prod.deb
sudo apt-get update && sudo apt-get install -y aspnetcore-runtime-10.0

# Debian 12（Bookworm）
wget https://packages.microsoft.com/config/debian/12/packages-microsoft-prod.deb -O packages-microsoft-prod.deb
sudo dpkg -i packages-microsoft-prod.deb && rm packages-microsoft-prod.deb
sudo apt-get update && sudo apt-get install -y aspnetcore-runtime-10.0

# Fedora 42+：
sudo dnf install aspnetcore-runtime-10.0

# 6. 配置 AI 识别（可选，详见上方 "AI 识别" 章节）：通过 Server 端环境变量注入
cat << 'EOF' | sudo tee -a /opt/boxwise/.env > /dev/null
LlmApi__BaseUrl=https://ark.cn-beijing.volces.com/api/v3
LlmApi__ApiKey=ark-xxx
LlmApi__Model=doubao-seed-2-0-pro-260215
LlmApi__TimeoutSeconds=30
EOF

# 7. 安装 systemd 服务
cat << 'EOF' | sudo tee /etc/systemd/system/boxwise.service > /dev/null
[Unit]
Description=BoxWise Server
After=network.target

[Service]
User=boxwise
Group=boxwise
WorkingDirectory=/opt/boxwise
# 清理上次异常退出残留的 socket 文件（Kestrel 正常退出时会自动删除）
ExecStartPre=/bin/rm -f /opt/boxwise/boxwise.sock
ExecStart=/usr/bin/dotnet /opt/boxwise/BoxWise.Server.dll
Restart=always
RestartSec=10
EnvironmentFile=/opt/boxwise/.env
Environment=ASPNETCORE_ENVIRONMENT=Production
# UMask=0007 确保 socket 文件权限为 770（owner+group 可读写，Caddy 通过 boxwise 组访问）
UMask=0007
# 使用 Unix Domain Socket 代替 TCP 端口 — 零端口冲突、无网络栈开销、文件系统权限隔离
Environment=ASPNETCORE_URLS=http://unix:/opt/boxwise/boxwise.sock
Environment=DataDirectory=/opt/boxwise/data

[Install]
WantedBy=multi-user.target
EOF

sudo systemctl daemon-reload
sudo systemctl enable --now boxwise
```

**配置反向代理（Caddy）：**

```bash
# 安装 Caddy（Ubuntu 24.04+ 已内置；其他版本需先添加官方源）
# 详见：https://caddyserver.com/docs/install#debian-ubuntu-raspbian
sudo apt install caddy

# 允许 Caddy 通过 Unix socket 访问应用
sudo usermod -aG boxwise caddy

# Caddy 语法：unix// 表示 Unix socket 传输，// 后跟 socket 文件绝对路径
# /etc/caddy/Caddyfile
你的域名 {
    handle /api/* {
        reverse_proxy unix//opt/boxwise/boxwise.sock
    }
    handle /admin {
        reverse_proxy unix//opt/boxwise/boxwise.sock
    }
    handle /admin/* {
        reverse_proxy unix//opt/boxwise/boxwise.sock
    }
    handle /Identity/* {
        reverse_proxy unix//opt/boxwise/boxwise.sock
    }
    handle {
        root * /opt/boxwise/wwwroot
        try_files {path} /index.html
        file_server
    }
    encode gzip
    header /api/images/* Cache-Control "public, max-age=86400"
}

sudo systemctl restart caddy
```

> **Caddy 路由说明：** 使用 `handle` 块明确分离路由，避免 `try_files` + `file_server` 在 `reverse_proxy` 之前拦截 API 请求。二进制部署中 Caddy 直接提供 `wwwroot/` 下的静态文件，仅将 `/api/*`、`/admin`、`/admin/*` 和 `/Identity/*` 请求转发到 Kestrel。`/admin` 精确匹配（无尾斜杠）和 `/Identity/*` 是 v0.3.1 新增的规则——缺少它们将导致管理后台和登录页面返回 Blazor WASM 的 404 页面。

#### 更新服务端程序

CI 自动构建发布包，从 GitHub Releases 下载最新版本即可。更新流程：

```bash
# 1. 备份数据（重要！）
sudo cp -r /opt/boxwise/data /opt/boxwise/backup-$(date +%Y%m%d)/

# 2. 下载最新版本
curl -L https://github.com/elvisw/BoxWise/releases/latest/download/boxwise-linux-x64.tar.gz -o boxwise-linux-x64.tar.gz

# 3. 停止服务
sudo systemctl stop boxwise

# 4. 解压覆盖（保留 .env 和 data/ 目录）
sudo tar -xzf boxwise-linux-x64.tar.gz -C /opt/boxwise

# 5. 确保权限正确
sudo chown -R boxwise:boxwise /opt/boxwise

# 6. 启动服务
sudo systemctl start boxwise

# 7. 验证
journalctl -u boxwise -f   # 查看日志，确认正常启动
curl -s https://你的域名/ | grep -oE "src=['\"][^'\"]*blazor\.webassembly\.js[^'\"]*['\"]"   # 确认 script 引用正确
```

> **重要提示：**
> - **必须完整替换所有文件**，不能只更新 `index.html` 或 `wwwroot/` 目录。Server 程序集（`BoxWise.Server.dll`）中的 `MapStaticAssets()` 清单在构建时生成，与静态文件版本一一对应，部分替换会导致路由不匹配。
> - **`.env` 和 `data/` 不会被覆盖**（解压时不存在于 tar 包中），管理员密码和数据均保持原样。
> - **回滚方法：** 如更新后异常，停止服务 → 还原旧版本 tar 包 → 恢复备份的 `data/` → 启动服务。CI 历史版本见 [GitHub Releases](https://github.com/elvisw/BoxWise/releases)。

**从源码构建更新（不通过 CI）：**

如果修改了源码需要自行构建，在本地执行后上传：

```bash
# 本地构建（Windows/Mac/Linux）
dotnet publish src/BoxWise.Server -c Release -o publish

# 上传到服务器（如果服务器未安装 rsync：apt install rsync）
rsync -avz publish/ elvisw@你的服务器:/opt/boxwise/

# SSH 到服务器重启
ssh elvisw@你的服务器 "sudo systemctl restart boxwise"
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

# 3. 配置管理员信息（可通过系统环境变量或 appsettings.Production.json 设置）
#    详见"配置 → 管理员账户"章节

# 4. 配置 AI（可选，详见"配置 → AI 识别"章节）

# 5. 直接运行测试
$env:ASPNETCORE_ENVIRONMENT = "Production"
$env:ASPNETCORE_URLS = "http://+:5000"
$env:Admin__Username = "admin"
$env:Admin__Email = "admin@你的域名"
$env:Admin__Password = "你的强密码"
$env:ConnectionStrings__DefaultConnection = "Data Source=C:\BoxWise\data\boxwise.db"
$env:DataDirectory = "C:\BoxWise\data"
dotnet C:\BoxWise\BoxWise.Server.dll
```

**配置 IIS（推荐生产环境）：**

1. 安装 [ASP.NET Core Hosting Bundle](https://dotnet.microsoft.com/download/dotnet/10.0)（选择 Hosting Bundle 下载）
2. IIS 管理器 → 应用程序池 → 添加应用程序池 → 名称 `BoxWise`，.NET CLR 版本选"无托管代码"
3. 添加网站 → 物理路径 `C:\BoxWise\`，端口 `80`，应用程序池 `BoxWise`
4. IIS 自动通过 ASP.NET Core Module（ANCM，ASP.NET Core 模块）将请求转发到 Kestrel，无需额外配置反向代理

> **IIS 环境变量配置：** 可通过系统环境变量或 IIS 的 `web.config` 中的 `<environmentVariables>` 节设置 `Admin__Username`、`Admin__Email`、`Admin__Password`。

**注册为 Windows 服务（无 IIS 方案）：**

```powershell
# 先确认 dotnet 路径
where.exe dotnet
# 通常位于 C:\Program Files\dotnet\dotnet.exe

# 注意：sc.exe 要求等号后有空格（binPath= 而非 binPath=）
# binPath 内部引号不可省略 — 可执行文件路径含空格时需额外引号包裹
sc.exe create "BoxWise" binPath= '"C:\Program Files\dotnet\dotnet.exe" "C:\BoxWise\BoxWise.Server.dll"' start= auto
sc.exe description "BoxWise" "箱知 · BoxWise 家庭物品管理服务"
sc.exe start "BoxWise"
```

> **Windows 服务环境变量：** `sc.exe` 不支持直接传递环境变量。建议在 `C:\BoxWise\appsettings.Production.json` 中配置管理员信息（详见"配置 → 管理员账户"章节）。
>
> **路径差异：** Windows 版归档文件名为 `boxwise-win-x64.zip`，Linux 版为 `boxwise-linux-x64.tar.gz`。
>
> **自包含发布：** 如需在未安装 .NET Runtime 的机器上运行，可手动执行 `dotnet publish -c Release -r win-x64 --self-contained true`。

### Docker 部署

**前置条件：** Docker + Docker Compose、域名指向服务器 IP。

> **Docker Caddyfile 说明：** Docker 场景下 Caddy 和 boxwise 是独立容器，SPA 静态文件位于 boxwise 容器中。因此 Caddyfile 将所有请求（包括根路径）反向代理到 `boxwise:5000`，与二进制部署中 Caddy 直接提供静态文件的配置不同。

```bash
# 1. 配置 AI（可选，详见"配置 → AI 识别"章节）

# 2. 修改 Caddyfile 中的域名
# Linux
sed -i 's/boxwise.example.com/你的域名/' Caddyfile
# macOS（BSD sed）
# sed -i '' 's/boxwise.example.com/你的域名/' Caddyfile

# 3. 修改 docker-compose.yml 中的管理员密码
#    将 Admin__Password=请替换为强密码 改为实际密码

# 4. 拉取最新镜像并启动
docker compose pull
docker compose up -d

# 5. 查看状态
docker compose ps
docker compose logs -f
```

**默认 Caddyfile（Docker 场景）：**

```caddyfile
# 将 boxwise.example.com 替换为你的实际域名
boxwise.example.com {
    reverse_proxy boxwise:5000
    encode gzip
    header /api/images/* Cache-Control "public, max-age=86400"
}
```

**持久化目录：**

| 路径 | 内容 |
|------|------|
| `./data/boxwise.db` | SQLite 数据库 |
| `./data/images/{itemId}/` | 物品原图 + 缩略图 |
| `./data/caddy/` | Caddy TLS 证书 |

容器重启后数据完整保留。

#### 使用自有反向代理

如果你已有 Nginx、Caddy、Traefik 等反向代理，可使用独立部署文件，不打包 Caddy 容器：

```bash
# 1. 修改 docker-compose.standalone.yml 中的管理员密码和域名
#    - Admin__Password=请替换为强密码
#    - WebAuthn__Origin=https://你的域名
#    - WebAuthn__ServerDomain=你的域名
# 2. 启动（仅 boxwise 容器，监听 127.0.0.1:5000）
docker compose -f docker-compose.standalone.yml up -d
# 3. 配置你的反向代理将流量转发到 localhost:5000（示例见下方）
```

**Nginx 反代示例：**

```nginx
server {
    listen 80;
    server_name boxwise.example.com;
    return 301 https://$host$request_uri;
}

server {
    listen 443 ssl;
    server_name boxwise.example.com;

    ssl_certificate     /path/to/fullchain.pem;
    ssl_certificate_key /path/to/privkey.pem;

    client_max_body_size 10m;

    location / {
        proxy_pass http://127.0.0.1:5000;
        proxy_set_header Host $host;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
    }

    location /api/images/ {
        proxy_pass http://127.0.0.1:5000;
        proxy_set_header Host $host;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
        expires 1d;
        add_header Cache-Control "public, max-age=86400";
    }
}
```

**Caddy 反代示例（宿主机直接安装）：**

```caddyfile
boxwise.example.com {
    reverse_proxy localhost:5000 {
        header_up X-Forwarded-Proto {scheme}
    }
    encode gzip
    header /api/images/* Cache-Control "public, max-age=86400"
}
```

> **注意：** 独立部署模式下不包含 TLS 终止，你需要自行在反向代理层配置 HTTPS 证书。`docker-compose.standalone.yml` 中 boxwise 端口绑定到 `127.0.0.1:5000`，仅本地可访问，确保不直接暴露到公网。

> **Docker 版本号说明：** 因 `.dockerignore` 排除 `.git/` 目录，Docker 构建时 `git describe` 无法获取标签，版本号始终回退到 `v1.0.0`。如需嵌入真实版本，可在宿主机构建后 COPY 发布产物进镜像。

## 日常使用

**录入物品：** 登录 → 点击"录入物品" → 拍照（AI 识别）或跳过 → 填写名称 → 选位置 → 加标签 → 保存。连续收纳模式下位置自动继承。

**查找物品：** 首页搜索框输入关键词（模糊匹配名称/备注/标签），或点击"浏览物品"按位置树/标签筛选。

**查看详情 / 删除：** 点击物品卡片 → 详情页 → 查看完整信息或点击"删除物品"（需确认，不可撤销）。

**PWA 安装：** 支持的浏览器访问应用 → 地址栏"安装"按钮 → 桌面独立启动 → 离线浏览已缓存的物品信息。

## 项目结构

```
BoxWise/
├── src/
│   ├── BoxWise.Client/              # Blazor WASM (PWA)
│   │   ├── Pages/                   # 页面组件
│   │   ├── Components/              # 可复用组件
│   │   └── Services/                # HTTP 客户端服务
│   ├── BoxWise.Server/              # ASP.NET Core Web API
│   │   ├── Endpoints/               # Minimal API 路由
│   │   ├── Repositories/            # 数据访问层
│   │   ├── Services/                # 业务逻辑 + AI + 图片处理
│   │   │   ├── TwoFactorService.cs
│   │   │   ├── RecoveryCodeService.cs
│   │   │   ├── ImageStorageService.cs
│   │   │   ├── ThumbnailService.cs
│   │   │   └── IdentityEmailSender.cs
│   │   ├── Models/                  # EF Core 实体
│   │   ├── Data/                    # DbContext + Configurations
│   │   ├── Configuration/           # 配置选项类
│   │   ├── Areas/
│   │   │   └── Identity/
│   │   │       └── Pages/
│   │   │           └── Account/     # Identity 脚手架 Razor Pages
│   │   ├── Pages/
│   │   │   └── Admin/               # Admin Razor Pages 管理后台
│   │   ├── Migrations/              # EF Core 迁移
│   │   └── wwwroot/                 # 静态资源
│   ├── BoxWise.Shared/              # 共享 DTO
│   └── BoxWise.Server.Tests/        # xUnit 单元测试
├── docs/                            # 项目文档
├── .github/workflows/               # CI/CD 配置
├── Dockerfile                       # 多阶段构建
├── docker-compose.yml               # 双服务编排
├── Caddyfile                        # 反向代理配置（Docker 场景）
└── CLAUDE.md                        # AI 辅助开发上下文
```

## 相关文档

| 文档 | 说明 |
|------|------|
| [WebAuthn 通行密钥配置指南](docs/webauthn-setup-guide.md) | 通行密钥注册、登录、生产环境 origin 配置 |
| [Identity 脚手架修改记录](docs/identity-scaffold-modifications.md) | Areas/Identity/ 下所有文件的修改清单（持续维护） |
| [部署指南](docs/deployment-guide.md) | 二进制 / Docker / Windows 部署详细步骤 |
| [开发指南](docs/development-guide.md) | 本地开发环境搭建、调试、测试运行 |
| [API 合约](docs/api-contracts-server.md) | 服务端 Minimal API 端点定义与请求/响应格式 |
| [数据模型](docs/data-models-server.md) | 数据库实体（AppUser/Item/Location/Tag/RecoveryCode/WebAuthnCredential）与关系 |
| [服务端架构](docs/architecture-server.md) | 路由组、服务层、中间件、项目分层 |
| [客户端架构](docs/architecture-client.md) | Blazor WASM 组件树、服务层、状态管理 |
| [共享层架构](docs/architecture-shared.md) | Shared 项目 DTO 清单与分类 |
| [认证与安全](docs/auth-security.md) | Identity Cookie 认证、2FA（TOTP/WebAuthn）、速率限制、CSRF |
| [集成架构](docs/integration-architecture.md) | Client-Server 数据流、跨源通信、认证桥接 |
| [客户端组件清单](docs/component-inventory-client.md) | Blazor 组件与 MudBlazor 使用情况 |
| [源码树分析](docs/source-tree-analysis.md) | 完整项目文件清单与目录结构 |

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

> **CI/Docker 注意事项：** CI 构建前需 `git fetch --tags` 获取标签。

## CI/CD

- **平台：** GitHub Actions
- **触发条件：**
  - 推标签 `v*` → 构建发布包 + 创建 GitHub Release
  - 推 `main` 分支 / Pull Request → build + test
- **产物：**
  - `boxwise-linux-x64.tar.gz` — Linux 二进制发布包
  - `boxwise-win-x64.zip` — Windows 二进制发布包
- **配置：** [.github/workflows/release.yml](.github/workflows/release.yml)

## 已知限制

1. **.NET 10 `GetTwoFactorAuthenticationUserAsync()` Bug**（[dotnet/aspnetcore#66929](https://github.com/dotnet/aspnetcore/issues/66929)）— 影响 2FA 用户登录流程。Workaround 已就位（内联 `HttpContext.AuthenticateAsync` + `FindByIdAsync`）。待上游修复后移除。

2. **Docker 构建版本号始终回退 v1.0.0** — `.dockerignore` 排除了 `.git/` 目录。详见上方"版本管理"章节的 CI/Docker 注意事项。

3. **ConfiguredMethods 同步** — Identity 页面操作（如邮箱修改）不自动更新 `AppUser` 的自定义扩展字段，需手动同步（内部机制，供开发者参考）。

4. **Email 2FA 登录路径已退役** — 自 v0.11 起移除 Email 验证码双因素登录，当前仅支持 TOTP + WebAuthn。SMTP 配置仅用于 Identity 邮箱管理功能。

## 维护

```bash
# EF Core 迁移（开发环境）
cd src/BoxWise.Server
dotnet ef migrations add <MigrationName>
dotnet ef database update
```

> **自动迁移：** 新版本启动时自动执行 EF Core 迁移（`MigrateAsync()`），无需手动 `dotnet ef database update`。**升级前请先备份数据。**

### 数据备份

```bash
# 需备份的完整目录：
#   data/boxwise.db  — SQLite 数据库
#   data/images/     — 物品图片
#   data/keys/       — Data Protection 密钥环

# Linux 二进制部署
sudo cp -r /opt/boxwise/data /opt/boxwise/backup-$(date +%Y%m%d)/

# Docker 部署
sudo cp -r ./data ./backup-$(date +%Y%m%d)/
```

> **Data Protection 密钥警告：** `data/keys/` 目录存储 ASP.NET Core Data Protection 密钥环，用于加密 SMTP 密码等敏感数据。**密钥丢失后已存储的加密数据将无法解密**，需重新配置。确保该目录已纳入备份或持久化卷。

### 数据恢复

```bash
# 1. 停止服务
# Linux 二进制：sudo systemctl stop boxwise
# Docker：docker compose down

# 2. 还原 data/ 目录（将 <备份日期> 替换为实际日期，如 20260603）
# Linux 二进制：cp -r /opt/boxwise/backup-<备份日期>/data/* /opt/boxwise/data/
# Docker：cp -r ./backup-<备份日期>/data/* ./data/

# 3. 启动服务
# Linux 二进制：sudo systemctl start boxwise
# Docker：docker compose up -d
```

### 查看日志

```bash
# 查看日志（二进制 / systemd）
journalctl -u boxwise -f

# 查看日志（Docker）
docker compose logs -f
```

## 常见问题

### Kestrel 监听地址如何修改？

修改监听地址。开发环境在 `Properties/launchSettings.json` 中调整 `applicationUrl`；生产环境修改环境变量 `ASPNETCORE_URLS`（Unix socket 格式 `http://unix:/路径/boxwise.sock` 或 TCP 格式 `http://+:5001`），同时更新反向代理配置指向新地址。

### AI 识别没有响应？

检查以下项目：

1. Server 端 `LlmApi__ApiKey` 环境变量是否已配置（种子数据自动入库）
2. Admin 后台 `/admin/llm-config` 查看配置是否正确
3. 客户端浏览器是否能连通 API 端点（网络防火墙、代理等）
4. 30s 超时是否太短（部分模型首次推理较慢）

确认无误后重新部署。API 调用失败时自动降级为手动输入，不阻塞录入。

### PWA 安装按钮不出现？

PWA 安装需要满足以下条件：

- 通过 HTTPS 访问（`localhost` 除外）
- 浏览器支持 PWA（Chrome、Edge、Safari >= 16.4）
- `manifest.json` 和 Service Worker 正确加载（检查浏览器开发者工具 → Application）

首次访问可能需要几秒钟注册 Service Worker，刷新页面后安装按钮出现。

### Docker 部署后数据库文件在哪里？

数据库文件位于宿主机的 `./data/boxwise.db`（相对于 `docker-compose.yml` 所在目录）。该目录已通过卷挂载映射到容器内的 `/app/data/`。

### 图片上传失败怎么办？

常见原因及解决办法：

1. **目录权限问题：** 确保 `data/images/` 目录存在且应用进程有写入权限。Docker 场景确保卷挂载正确。
2. **磁盘空间不足：** 检查服务器磁盘剩余空间。
3. **文件大小超限：** 默认单文件最大 10MB（在 `Program.cs` 中可通过 `MultipartBodyLengthLimit` 调整）。
4. **图片格式不支持：** 支持 JPEG、PNG、WebP、BMP、GIF。

### 忘记管理员密码怎么办？

无需重置数据库。重新设置 `Admin__Password` 环境变量（或修改 `appsettings.Production.json` / `.env` 文件），重启应用后密码自动更新。原有账户 ID、数据、关联关系均不受影响。

## 许可证

[GNU General Public License v3.0](LICENSE)
