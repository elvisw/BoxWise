# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## 构建与运行

```bash
# 构建整个解决方案
dotnet build

# 运行 Server（API + 托管 Blazor WASM 静态文件）
cd src/BoxWise.Server && dotnet run

# 运行 Client（Blazor WASM 开发服务器，带热重载）
cd src/BoxWise.Client && dotnet run

# EF Core 迁移（在 src/BoxWise.Server 目录下操作）
cd src/BoxWise.Server
dotnet ef migrations add <MigrationName>
dotnet ef database update
```

**端口与开发入口：**

| 地址 | 提供内容 | 热重载 | 推荐场景 |
|------|---------|--------|---------|
| `https://localhost:5001` | Blazor WASM 页面（Client 开发服务器） | 有 | **日常 UI 开发（推荐入口）** |
| `https://localhost:5000` | API + Admin 后台 + Blazor WASM 静态回退 | 无 | 测试 Admin / 完整集成测试 |

- **日常开发推荐 `https://localhost:5001`** — Blazor WASM 热重载，改 UI 秒级生效。API 请求通过 `CookieHandler` 跨源发送到 5000 端口，无需手动处理。
- **Admin 后台（`/admin`）是 Server 端 Razor Pages**，不走 Blazor WASM。在 5001 端口点击"管理后台"按钮自动跳转到 5000 端口。
- **仅需一个端口时**，只启动 Server（`dotnet run` in Server），访问 `https://localhost:5000` 即可同时使用页面 + API + Admin，代价是没有热重载。

## 测试

```bash
# 运行所有测试
dotnet test BoxWise.slnx

# 运行特定测试项目
dotnet test src/BoxWise.Server.Tests

# 运行特定测试类
dotnet test src/BoxWise.Server.Tests --filter "FullyQualifiedName~LocationRepositoryTests"
```

**测试框架:** xUnit + EF Core InMemory Database。测试项目 `src/BoxWise.Server.Tests/` 引用 `BoxWise.Server`，使用 `TestDbContextFactory.Create()` 创建隔离的 InMemory DbContext。

## 部署

### 二进制部署

```bash
dotnet publish src/BoxWise.Server -c Release -o publish
# 上传 publish/ 到服务器 /opt/boxwise/
# 反向代理: Caddy/Nginx → localhost:5000
# systemd 服务见 README.md
```

### Docker 部署

```bash
cat > src/BoxWise.Client/wwwroot/appsettings.Production.json << 'EOF'
{
  "LlmApi": {
    "BaseUrl": "https://ark.cn-beijing.volces.com/api/v3",
    "ApiKey": "ark-xxx",
    "Model": "doubao-seed-2-0-pro-260215",
    "TimeoutSeconds": 30
  }
}
EOF
docker compose up -d
```

**服务架构:** Caddy (443→80) → boxwise:5000（ASP.NET Core）<br>
**持久化:** `./data:/app/data`（SQLite + 图片），`./data/caddy:/data`（Caddy 证书）<br>
**环境变量注入:** `ASPNETCORE_URLS`、`DataDirectory`、`ConnectionStrings__DefaultConnection`<br>
**首次启动:** 通过 `Admin__Password` 环境变量创建管理员，登录后访问 `/admin` 创建家庭成员账户<br>
**AI 配置:** API 密钥通过 Client 端 `wwwroot/appsettings.Local.json`（开发，gitignored）或 `appsettings.Production.json`（生产，gitignored）配置。未配置时 AI 静默降级为手动输入。配置块键名 `LlmApi`（BaseUrl/ApiKey/Model/TimeoutSeconds），支持任意 OpenAI 兼容 API。<br>
**Admin UI:** Server 端独立 Razor Pages（`Pages/Admin/`），`AdminOnly` 策略保护，不走 Blazor WASM。<br>
**AppUser:** Identity 实体扩展，`IsInRoleAsync(user, "Admin")` 判断管理员

**测试模式:** Repository 层单元测试，每个测试独立创建 DbContext（GUID 命名），覆盖正常路径 + 边界条件（空值、超长、不存在 ID、重复创建、业务规则违反）。

## 项目架构

```
BoxWise.slnx                        # .NET 10 新格式 (.slnx = XML)
├── src/
│   ├── BoxWise.Client/             # Blazor WASM (PWA) - UI 层
│   │   ├── Pages/                  # Razor 页面组件（Home, Login, NotFound, ItemEntry, ItemDetail, Browse, Settings）
│   │   ├── Layout/                 # MainLayout.razor（4 Tab 底部导航：首页/录入/浏览/设置）
│   │   ├── Components/             # 可复用 Blazor 组件（LocationTree, TagFilter, ImageUploader, ContinuityBanner, LocationManageDialog, TagManageDialog）
│   │   └── Services/               # AuthService, AppState, LocationService, TagService, ItemEntryService, ItemService
│   ├── BoxWise.Server/             # ASP.NET Core Web API - 后端
│   │   ├── Areas/
│   │   │   └── Identity/
│   │   │       └── Pages/
│   │   │           └── Account/    # Identity 脚手架 Razor Pages（登录/2FA/账户管理）
│   │   ├── Endpoints/              # Minimal API 路由组（Auth, Item, Location, Tag, Image, WebAuthn, AdminTwoFactor）
│   │   ├── Data/                   # AppDbContext + EF Configurations
│   │   ├── Models/                 # Identity 实体（AppUser）+ Location, Tag, Item
│   │   ├── Repositories/           # LocationRepository, TagRepository, ItemRepository
│   │   ├── Services/               # IdentityEmailSender, ImageStorageService, ThumbnailService (SkiaSharp)
│   │   ├── Utilities/              # AuthConstants
│   │   └── Migrations/             # EF Core 迁移
│   └── BoxWise.Shared/             # 共享 DTO（record 类型）
│       └── Dtos/
├── Directory.Build.props           # 根级：net10.0, Nullable, ImplicitUsings, WarningsAsErrors
├── Directory.Build.targets         # 根级：git describe 自动版本号
├── Directory.Packages.props        # CPM 集中包版本管理
└── data/                           # SQLite 数据库文件
```

**引用关系：** Client → Shared, Server → Shared. Server 同时引用 Client 项目，用于 `.MapFallbackToFile("index.html")` SPA 回退。

## 关键技术决策

- **API 风格：** Minimal API + `RouteGroupBuilder` 静态扩展方法组织端点
- **返回类型：** `TypedResults`（`TypedResults.Ok()`、`TypedResults.Problem()`）+ `ProblemDetails`
- **错误返回：** 使用 `TypedResults.Problem()` 直接返回，**不要**嵌套在 `TypedResults.BadRequest()` 里
- **所有端点加 `.ProducesProblem(401)`** 注解
- **认证：** ASP.NET Core Identity + Cookie 认证 + Blazor WASM 侧自定义 `CookieAuthenticationStateProvider`
- **授权：** 全局 `FallbackPolicy` 要求认证，匿名端点显式标记 `.AllowAnonymous()`
- **UI 框架：** MudBlazor 9.5 — 见下方 [MudBlazor 9.x API 参考](#mudblazor-9x-api-参考)
- **数据库：** SQLite + EF Core，使用 CPM 管理包版本
- **Admin UI：** 独立的 Server 端 Razor Pages 区域（`Pages/Admin/`），不走 Blazor WASM
- **图片处理：** SkiaSharp 3.119.4（MIT 许可证），300px + 1200px 两级缩略图，后台异步生成
- **AI 集成：** 客户端浏览器直调火山 ARK API（OpenAI 兼容），通过 `IHttpClientFactory` 创建独立 HttpClient，30s 超时静默降级

## 认证流程

1. 浏览器首次加载 → `CookieAuthenticationStateProvider.GetAuthenticationStateAsync()` 调用 `GET /api/auth/me` 检查 Identity Cookie 中的登录会话
2. 登录 → Identity `Login.cshtml`（Server 端 Razor Page）→ Cookie 签发 → HTTP 302 重定向到 `/` → `AppState.SetUser()` 更新客户端状态
3. 通行密钥登录 → 用户访问 `/login`（Blazor WASM）→ 点击"使用通行密钥登录" → WebAuthn API → 验证成功 → `AppState.SetUser()` → 导航到 `/`
4. Server `Program.cs` 中 FallbackPolicy = `RequireAuthenticatedUser()`，所有端点默认受保护
5. `"Admin"` 角色通过 `userManager.IsInRoleAsync(user, "Admin")` 检查，结果通过 `AuthUserDto.IsAdmin` 传递到客户端

## Client DI 注册注意事项

**`HttpClient` 必须最先注册**（在所有依赖它的 Service 之前），否则 `WebAssemblyHostBuilder` 验证 DI 图时报 `CannotResolveService`。

**`CookieAuthenticationStateProvider` 同时需要两种注册方式：**
```csharp
// 1. 具体类型（AuthService 构造函数注入）
builder.Services.AddScoped<CookieAuthenticationStateProvider>();
// 2. 抽象→具体转发（AuthorizeRouteView 需要 AuthenticationStateProvider）
builder.Services.AddScoped<AuthenticationStateProvider>(sp => sp.GetRequiredService<CookieAuthenticationStateProvider>());
```

## 端口配置

| 组件 | 端口 | 说明 |
|------|------|------|
| Server HTTPS | `5000` | API + Admin Razor Pages + Blazor WASM 静态回退 |
| Client HTTPS | `5001` | Blazor WASM 开发服务器（热重载） |
| CORS 允许源 | `https://localhost:5001` | Server 允许 Client 开发端口跨源 |

**ApiBaseUrl 配置机制：**

| 环境 | ApiBaseUrl | Http.BaseAddress | API 请求目标 | Admin 链接 |
|------|-----------|-----------------|-------------|-----------|
| 开发 | `"https://localhost:5000/"` (appsettings.Development.json) | `https://localhost:5000/` | 跨源到 5000 | `https://localhost:5000/admin` |
| 生产 | 空（未配置） | null | 同源 | `/admin` |

- 开发环境通过 `src/BoxWise.Client/wwwroot/appsettings.Development.json` 配置
- 生产环境不配置 → `Program.cs` 默认空字符串 → `BaseAddress` 为 null → 所有请求走同源，无需跨端口
- 端口不匹配排查：如遇 `ERR_CONNECTION_REFUSED`，检查 `Properties/launchSettings.json` 与 `Program.cs` 中的端口是否一致

**Admin 跨端口访问：** Admin 后台是 Server 端 Razor Pages，仅在 5000 端口可用。Home.razor 的"管理后台"按钮通过 `Http.BaseAddress` 判断环境：有值时拼绝对路径指向 Server，无值时（生产同源）走根路径 `/admin`。

## 版本管理

版本号由 `Directory.Build.targets` 中的 MSBuild Target `SetVersionFromGit` 在构建时自动从 Git 标签获取。

**工作原理：**

1. `dotnet build` 触发 `SetVersionFromGit`（`BeforeTargets="BeforeBuild"`）
2. 执行 `git describe --tags --abbrev=7 --always` 获取版本描述
3. 解析结果，自动设置 `Version` 和 `InformationalVersion` 属性
4. 关于页面（`About.razor`）通过 `AssemblyInformationalVersionAttribute` 读取并显示

**版本号映射规则：**

| Git 状态 | git describe 输出 | Version | 关于页面显示 |
|----------|-------------------|---------|-------------|
| HEAD = v0.2.1 tag | `v0.2.1` | `0.2.1` | `v0.2.1` |
| v0.2.1 之后 4 个 commit | `v0.2.1-4-gabcdef1` | `0.2.1` | `v0.2.1-4-gabcdef1` |
| 无 tag | `abcdef1` | `1.0.0`（fallback） | `abcdef1` |

**发版流程：**

```bash
# 1. 打标签
git tag v0.3.0
# 2. 构建时自动生效——版本号 = 0.3.0
dotnet build
# 3. 推送标签到远程（CI/CD 需要 git fetch --tags）
git push --tags
```

**Docker 构建注意事项：** CI/CD 中需要在 `docker build` 前执行 `git fetch --tags`，否则 `git describe` 找不到标签，版本号会回退到 `v1.0.0`。

## MudBlazor 9.x API 参考

*基于 MudBlazor 9.5 源码验证（2026-05-24）。以下 API 与 MudBlazor 8.x 及常见文档有显著差异，务必使用以下正确 API。*

### MudTreeView<T>

| 错误（v8/文档旧版） | 正确（v9.x） | 说明 |
|------|------|------|
| `@bind-ActivatedValue` | `SelectedValue` + `SelectedValueChanged` | v9 重命名，SelectedValue 类型为 `T?`，不可跨类型绑定 |
| `T="TreeItem"` | `T="LocationDto"` 等数据模型 | 泛型参数应为 Value 的实际类型 |
| 未使用 `TreeItemData<T>` | `List<TreeItemData<T>>` | Items 必须为 `IReadOnlyCollection<TreeItemData<T>>` |
| `<Text>` 子元素 | `<BodyContent>` | 自定义内容放在 BodyContent 内 |

**正确用法：**
```razor
<MudTreeView T="MyModel" Items="TreeItems"
             SelectedValue="_selectedValue"
             SelectedValueChanged="OnSelectedValueChanged">
    <ItemTemplate Context="item">
        <MudTreeViewItem Value="@item.Value" Items="@item.Children">
            <BodyContent>
                <MudText>@(item.Value?.Name ?? "")</MudText>
            </BodyContent>
        </MudTreeViewItem>
    </ItemTemplate>
</MudTreeView>
```

**TreeItemData<T> 关键属性：**
- `T? Value` — 实际数据对象
- `string? Text` — 显示文本
- `IReadOnlyCollection<ITreeItemData<T>>? Children` — 子节点（递归）
- `bool Expanded` / `bool Selected` — 状态控制

### MudChipSet<T>

| 错误（v8） | 正确（v9.x） | 说明 |
|------|------|------|
| `Filter` / `Filter="true"` | （已移除） | v9 无此参数 |
| `MultiSelection` / `MultiSelection="true"` | `SelectionMode="SelectionMode.MultiSelection"` | 改用 enum |
| `SelectedValues="List<int>"` | `SelectedValues="IReadOnlyCollection<T>"` | 类型变更 |

**正确用法：**
```razor
<MudChipSet T="int" SelectionMode="SelectionMode.MultiSelection"
            @bind-SelectedValues="SelectedIds">
    @foreach (var tag in Tags)
    {
        <MudChip Value="@tag.Id" Color="Color.Primary" Variant="Variant.Outlined">
            @tag.Name
        </MudChip>
    }
</MudChipSet>
```

**SelectionMode 枚举值：** `SingleSelection`（默认）/ `MultiSelection` / `ToggleSelection`

### MUD0002 分析器

MudBlazor 9.x 内置命名风格分析器，违反时报错 `MUD0002`。**不要禁用它**——应该遵守分析器要求：
- 组件参数使用正确的 v9.x 参数名（如 `SelectedValue` 非 `ActivatedValue`）
- 已废弃/移除的参数会导致编译错误

### 获取 MudBlazor 源码验证 API

当 MudBlazor 官网文档不可用（SPA 渲染，fetch 返回空壳）时，直接从 GitHub 源码验证 API：
```bash
# MudTreeView 源码
https://raw.githubusercontent.com/MudBlazor/MudBlazor/dev/src/MudBlazor/Components/TreeView/MudTreeView.razor
https://raw.githubusercontent.com/MudBlazor/MudBlazor/dev/src/MudBlazor/Components/TreeView/MudTreeView.razor.cs
https://raw.githubusercontent.com/MudBlazor/MudBlazor/dev/src/MudBlazor/Components/TreeView/TreeItemData.cs

# MudChipSet 源码
https://raw.githubusercontent.com/MudBlazor/MudBlazor/dev/src/MudBlazor/Components/ChipSet/MudChipSet.razor.cs
```

## 端点开发规范

- **Repository 模式：** 返回 Entity，端点负责 Entity→DTO 映射。Scoped DI。
- **异常处理：** `ArgumentException` → `TypedResults.Problem(msg, 400)`；`KeyNotFoundException` → `TypedResults.NotFound()`
- **所有端点加 `.ProducesProblem(401)`** — 架构文档要求
- **DTO 用 positional record**，放在 `BoxWise.Shared.Dtos`
- **名称处理统一** — `Trim()` + `Length > N` 校验
- **并发安全** — `DbUpdateException` 捕获兜底

## 退役类 Story Definition of Done

涉及代码退役（删除文件/方法/端点/组件）的 Story，除常规 DoD 外必须执行以下检查：

```bash
# 退役后文档墓碑检测 — 确认无已退役标识符的残留引用
grep -rn "<已退役类名或端点路由>" docs/ CLAUDE.md _bmad-output/ --include="*.md"
```

- **零残留方为 close** — grep 必须返回空结果（或仅匹配自身退役说明）
- **PR 描述同步** — 每个 Story 完成后，PR 描述必须反映最新完成状态（不等到 Epic 结束才一次性更新）
- **`docs/identity-scaffold-modifications.md` 更新** — 任何对 `Areas/Identity/` 下文件的修改必须记录在此

> **来源：** Epic 11 回顾发现 CLAUDE.md 中的 `TwoFactorEndpoints.cs` 引用在退役后未清理，PR 描述在 Epic 执行期间停滞在 "2/4 Stories"。两条规则防空此类问题。

## Epic 2 技术债务清理记录 (2026-05-24)

| 债务 | 状态 | 修复 |
|------|------|------|
| HttpClient BaseAddress 硬编码 | ✅ 已清理 | `Program.cs` 从 `IConfiguration["ApiBaseUrl"]` 读取，默认 `https://localhost:5000/` |
| 缺少 CancellationToken | ✅ 已清理 | `LocationService.GetAllAsync` / `TagService.GetAllAsync` 添加 `CancellationToken` 参数 |
| SortOrder 未在 CreateAsync 赋值 | ✅ 已清理 | `LocationRepository.CreateAsync` 接受 `sortOrder` 参数并赋值 |
| 缺少单元测试框架 | ✅ 已清理 | `src/BoxWise.Server.Tests/` xUnit 项目，22 个测试全部通过 |

## .NET Framework 已知问题

### .NET 10 SignInManager.GetTwoFactorAuthenticationUserAsync() Bug (2026-05-30)

- **Issue:** [dotnet/aspnetcore#66929](https://github.com/dotnet/aspnetcore/issues/66929)
- **影响:** `SignInManager.GetTwoFactorAuthenticationUserAsync()` 在 .NET 10.0.8 中返回 null，即使 TwoFactorUserId Cookie 有效。内部 `UserManager.GetUserId(principal)` 返回 UserName 而非 UserId，导致 `FindByIdAsync` 用用户名查 GUID 列。
- **症状:** 2FA 用户登录时挑战端点返回 401 → 前端提示"无法获取可用的验证方式"。
- **Workaround:** 使用 `LoginWith2fa.cshtml.cs` / `LoginWithRecoveryCode.cshtml.cs` 中 PageModel 层的内联 workaround（`HttpContext.AuthenticateAsync` + `FindByIdAsync`）。`TwoFactorEndpoints.cs` 已在 Story 11.3 退役。**待上游修复后移除 workaround。**
- **详细调查:** `_bmad-output/implementation-artifacts/investigations/2fa-gettwofactoruserasync-null-investigation.md`
- **脚手架修改清单:** `docs/identity-scaffold-modifications.md` — 所有对 `Areas/Identity/` 下文件的修改必须记录在此。**每次涉及脚手架代码的改动，在修改代码前先查阅此文件。**

## BMad 工作流上下文

- **BMad 版本：** v6.7.1（bmm + core 模块）
- **规划工件：** `_bmad-output/planning-artifacts/`
- **实施工件：** `_bmad-output/implementation-artifacts/`
- **当前进度：** Epic 1 ✅ | Epic 2 ✅ | Epic 3 ✅ | Epic 4 ✅ | Epic 5 ✅ | Epic 6 ✅ | Epic 7 ✅ | Epic 8 ✅ | Epic 9 ✅ | Epic 10 ✅ | Epic 11 ✅ — 全部完成
- **设置页重构 ✅** — 4 Tab 导航 + 位置/标签管理弹窗，34 测试通过
