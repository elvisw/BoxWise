# 源码树分析

> BoxWise — 完整项目目录结构与关键路径

## 仓库结构

```
BoxWise/                              # 解决方案根目录
├── BoxWise.slnx                      # .NET 10 解决方案（.slnx XML 格式）
├── Directory.Build.props             # 根级 MSBuild 属性（net10.0, Nullable, ImplicitUsings, WarningsAsErrors）
├── Directory.Build.targets           # 根级 MSBuild 目标
├── Directory.Packages.props          # CPM 集中包版本管理
├── LICENSE                           # GPLv3
├── README.md                         # 项目介绍
├── Dockerfile                        # 多阶段 Docker 构建
├── docker-compose.yml                # Docker 编排（Caddy + App）
├── data/                             # SQLite 数据库文件（boxwise.db）
│
├── src/                              # 源代码
│   ├── Directory.Build.props         # src 级 MSBuild 属性
│   │
│   ├── BoxWise.Client/               # [Part: client] Blazor WASM PWA 前端
│   │   ├── BoxWise.Client.csproj     # SDK: BlazorWebAssembly
│   │   ├── Program.cs                # 入口: DI 注册, HttpClient 配置
│   │   ├── Pages/                    # 路由页面 (7 个)
│   │   │   ├── Home.razor            # /        首页搜索+物品网格
│   │   │   ├── Browse.razor          # /browse  位置/标签筛选浏览
│   │   │   ├── ItemEntry.razor       # /entry   物品录入（拍照+AI+位置+标签）
│   │   │   ├── ItemDetail.razor      # /items/{id} 物品详情+图片+删除
│   │   │   ├── Login.razor           # /login   登录表单（含通行密钥登录）
│   │   │   ├── Settings.razor        # /settings 设置页（位置/标签/通行密钥管理入口）
│   │   │   └── NotFound.razor        # /not-found 404
│   │   ├── Layout/                   # 布局
│   │   │   └── MainLayout.razor      # 主题+顶栏+4Tab底栏+对话框/提示容器
│   │   ├── Components/               # 可复用组件 (12 个)
│   │   │   ├── ContinuityBanner.razor    # 连续收纳绿色提示
│   │   │   ├── SearchBar.razor          # 搜索文本框（去抖，未实际使用）
│   │   │   ├── ConfirmDeleteDialog.razor # 删除确认弹窗
│   │   │   ├── TagFilter.razor          # 多选标签芯片组
│   │   │   ├── ItemCard.razor           # 物品网格卡片
│   │   │   ├── LocationTree.razor       # 位置树选择器
│   │   │   ├── LocationManageDialog.razor # 位置 CRUD 弹窗
│   │   │   ├── TagManageDialog.razor    # 标签 CRUD 弹窗
│   │   │   ├── ImageUploader.razor      # 拍照/文件选择+预览
│   │   │   ├── WebAuthnSetup.razor      # 通行密钥注册（Epic 10）
│   │   │   ├── WebAuthnCredentialList.razor # 通行密钥凭据列表（Epic 10）
│   │   │   └── PasskeyManageDialog.razor # 通行密钥管理弹窗（Epic 10）
│   │   ├── Services/                 # 服务层 (9 个 .cs)
│   │   │   ├── AppState.cs           # 全局状态（Singleton, 事件驱动）
│   │   │   ├── AuthService.cs        # 登录/登出
│   │   │   ├── CookieAuthenticationStateProvider.cs # Cookie→Blazor 认证桥接
│   │   │   ├── CookieHandler.cs      # HttpClientHandler (跨源 Cookie)
│   │   │   ├── ItemService.cs        # 物品 CRUD + 搜索
│   │   │   ├── ItemEntryService.cs   # 物品创建
│   │   │   ├── LocationService.cs    # 位置 CRUD
│   │   │   ├── TagService.cs         # 标签 CRUD
│   │   │   └── AiService.cs          # AI 识别 (30s 超时, 客户端直调火山 ARK)
│   │   ├── Models/                   # 客户端模型
│   │   │   └── PhotoCapture.cs       # 照片数据载体 record
│   │   └── wwwroot/                  # 静态资源
│   │       ├── index.html            # SPA 入口
│   │       ├── manifest.webmanifest  # PWA Manifest
│   │       ├── service-worker.js     # PWA Service Worker (开发)
│   │       ├── service-worker.published.js # PWA Service Worker (生产)
│   │       ├── icon-192.png          # PWA 图标 192px
│   │       ├── icon-512.png          # PWA 图标 512px
│   │       ├── appsettings.Development.json # 开发配置 (ApiBaseUrl)
│   │       ├── css/app.css           # 自定义样式
│   │       └── js/
│   │           ├── camera-capture.js  # 原生相机调用 (ES Module)
│   │           ├── webauthn.js        # WebAuthn API 封装
│   │           └── utils.js           # 浏览器下载辅助函数
│   │
│   ├── BoxWise.Server/               # [Part: server] ASP.NET Core API
│   │   ├── BoxWise.Server.csproj     # SDK: Web
│   │   ├── Program.cs                # 入口: 服务注册, 中间件, 端点映射, 种子数据
│   │   ├── appsettings.json          # 基础配置（连接字符串）
│   │   ├── appsettings.Development.json # 开发配置
│   │   ├── Endpoints/                # Minimal API 路由组 (8 个文件)
│   │   │   ├── AuthEndpoints.cs          # /api/auth (me)
│   │   │   ├── WebAuthnEndpoints.cs      # /api/auth/webauthn (通行密钥注册与登录)
│   │   │   ├── AdminTwoFactorEndpoints.cs # /api/admin/users/{userId}/two-factor
│   │   │   ├── LocationEndpoints.cs      # /api/locations (CRUD + children)
│   │   │   ├── ItemEndpoints.cs          # /api/items (CRUD + search)
│   │   │   ├── ImageEndpoints.cs         # /api/images (upload + serve)
│   │   │   ├── TagEndpoints.cs           # /api/tags (CRUD)

│   │   ├── Data/                     # EF Core 数据层
│   │   │   ├── AppDbContext.cs       # IdentityDbContext<AppUser>
│   │   │   └── Configurations/       # Fluent API 实体配置
│   │   │       ├── AppUserConfiguration.cs
│   │   │       ├── LocationConfiguration.cs
│   │   │       ├── ItemConfiguration.cs
│   │   │       └── TagConfiguration.cs
│   │   ├── Models/                   # 领域实体
│   │   │   ├── AppUser.cs            # IdentityUser 扩展
│   │   │   ├── Location.cs           # 自引用树形位置
│   │   │   ├── Item.cs               # 物品（含图片路径）
│   │   │   └── Tag.cs                # 标签（M:N → Item）
│   │   ├── Repositories/             # Repository 层 (Scoped)
│   │   │   ├── LocationRepository.cs
│   │   │   ├── ItemRepository.cs
│   │   │   └── TagRepository.cs
│   │   ├── Services/                 # 业务服务 (11 个 .cs)
│   │   │   ├── ImageStorageService.cs       # Singleton - 图片文件管理
│   │   │   ├── ThumbnailService.cs          # Singleton - SkiaSharp 缩略图生成

│   │   │   ├── CsrfValidationFilter.cs      # CSRF 验证过滤器
│   │   │   ├── TwoFactorService.cs          # 双因素认证核心服务
│   │   │   ├── EmailTwoFactorService.cs     # 邮箱 2FA 验证码服务
│   │   │   ├── WebAuthnService.cs           # FIDO2 WebAuthn 通行密钥服务
│   │   │   ├── RecoveryCodeService.cs       # 恢复码生成与验证
│   │   │   ├── SmtpConfigurationService.cs  # SMTP 配置管理服务
│   │   │   ├── ISmtpConfigurationService.cs # SMTP 配置服务接口
│   │   │   └── IdentityEmailSender.cs       # Identity 邮箱发送（MailKit）
│   │   ├── Configuration/            # 选项配置

│   │   ├── Dtos/                     # Server 端 DTO (空，共用 Shared)
│   │   ├── Pages/Admin/              # Admin Razor Pages (Server 端)
│   │   │   ├── Index.cshtml          # 管理后台首页
│   │   │   ├── CreateAccount.cshtml  # 创建用户
│   │   │   ├── EditAccount.cshtml     # 编辑用户
│   │   │   ├── ChangeUserPassword.cshtml # 修改用户密码
│   │   │   ├── ResetTwoFactor.cshtml  # 重置用户 2FA
│   │   │   └── SmtpSettings.cshtml    # SMTP 配置管理
│   │   ├── Migrations/               # EF Core 迁移文件
│   │   └── certs/                    # 开发证书
│   │
│   ├── BoxWise.Shared/               # [Part: shared] 共享 DTO
│   │   ├── BoxWise.Shared.csproj     # SDK: Microsoft.NET.Sdk (纯类库)
│   │   └── Dtos/                     # 共享 record 类型 (30 个)
│   │       ├── LoginRequest.cs
│   │       ├── AuthUserDto.cs
│   │       ├── UserListItemDto.cs
│   │       ├── CreateAccountRequest.cs
│   │       ├── ChangePasswordRequest.cs
│   │       ├── UpdateProfileRequest.cs
│   │       ├── ReAuthenticateRequest.cs
│   │       ├── LocationDto.cs
│   │       ├── CreateLocationRequest.cs
│   │       ├── RenameLocationRequest.cs
│   │       ├── TagDto.cs
│   │       ├── CreateTagRequest.cs
│   │       ├── RenameTagRequest.cs
│   │       ├── CreateItemRequest.cs
│   │       ├── UpdateItemRequest.cs
│   │       ├── ItemDto.cs
│   │       ├── ItemSummaryDto.cs
│   │       ├── UploadResultDto.cs
│   │       ├── RecognitionResultDto.cs
│   │       ├── TwoFactorStatusDto.cs
│   │       ├── VerifyTwoFactorRequest.cs
│   │       ├── SetupEmailTwoFactorRequest.cs
│   │       ├── SwitchMethodRequest.cs
│   │       ├── RecoveryCodesResponse.cs
│   │       ├── WebAuthnChallengeResponse.cs
│   │       ├── WebAuthnAvailableResponse.cs
│   │       ├── WebAuthnCredentialDto.cs
│   │       ├── AdminTwoFactorStatusResponse.cs
│   │       ├── SmtpConfigDto.cs
│   │       └── SmtpTestResult.cs
│   │
│   └── BoxWise.Server.Tests/         # [Part: tests] xUnit 测试
│       ├── BoxWise.Server.Tests.csproj
│       ├── TestDbContextFactory.cs         # InMemory DbContext 工厂
│       ├── TestIdentityFactory.cs          # Identity 测试工厂
│       ├── AdminUserManagementTests.cs     # Admin 用户管理测试
│       ├── AuthEndpointsTests.cs           # 认证端点集成测试
│       ├── Repositories/                   # Repository 层单元测试
│       │   ├── LocationRepositoryTests.cs
│       │   ├── ItemRepositoryTests.cs
│       │   └── TagRepositoryTests.cs
│       ├── Services/                       # 服务层单元测试

│       │   ├── ImageStorageServiceTests.cs
│       │   ├── ThumbnailServiceTests.cs
│       │   ├── CsrfValidationFilterTests.cs
│       │   ├── PasswordValidatorTests.cs
│       │   ├── RecoveryCodeServiceTests.cs
│       │   ├── TwoFactorServiceTests.cs
│       │   ├── EmailTwoFactorServiceTests.cs
│       │   └── SmtpConfigurationServiceTests.cs
│       └── Endpoints/                      # 端点集成测试
│           ├── AuthEndpointsTests.cs
│           ├── ItemEndpointsTests.cs
│           ├── LocationEndpointsTests.cs
│           ├── TagEndpointsTests.cs
│           └── TwoFactorFlowE2ETests.cs
│
├── docs/                             # 项目文档（本次生成）
│   ├── index.md                      # 主索引入口
│   ├── project-scan-report.json      # 扫描状态
│   └── superpowers/                  # Superpowers 产出
│       ├── specs/                    # 设计规格
│       └── plans/                    # 实现计划
│
├── _bmad/                            # BMad 工作流配置
├── _bmad-output/                     # BMad 工作流产出
│   ├── planning-artifacts/           # PRD, UX, Architecture, Epics
│   └── implementation-artifacts/     # Story 文档, Sprint Status, Retro
│
└── .github/workflows/                # GitHub Actions
    └── release.yml                   # 发布流水线
```

## 关键路径速查

| 关注点 | 入口文件 |
|--------|---------|
| 解决方案 | `BoxWise.slnx` |
| Server 入口 | `src/BoxWise.Server/Program.cs` |
| Client 入口 | `src/BoxWise.Client/Program.cs` |
| 数据库配置 | `src/BoxWise.Server/Data/AppDbContext.cs` |
| 认证配置 | `src/BoxWise.Server/Program.cs` (L25-64) |
| API 端点 | `src/BoxWise.Server/Endpoints/*.cs` |
| 数据模型 | `src/BoxWise.Server/Models/*.cs` |
| 共享 DTO | `src/BoxWise.Shared/Dtos/*.cs` |
| 核心页面 | `src/BoxWise.Client/Pages/*.razor` |
| 可复用组件 | `src/BoxWise.Client/Components/*.razor` |
| 客户端服务 | `src/BoxWise.Client/Services/*.cs` |
| PWA 配置 | `src/BoxWise.Client/wwwroot/manifest.webmanifest` |
| JS 互操作 | `src/BoxWise.Client/wwwroot/js/camera-capture.js` |
| Repository 测试 | `src/BoxWise.Server.Tests/Repositories/*.cs` |
| Services 测试 | `src/BoxWise.Server.Tests/Services/*.cs` |
| Endpoints 测试 | `src/BoxWise.Server.Tests/Endpoints/*.cs` |
| CI/CD | `.github/workflows/release.yml` |
| Docker | `Dockerfile`, `docker-compose.yml` |
