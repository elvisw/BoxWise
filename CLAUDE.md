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

Server 开发环境监听 `https://localhost:5000`，Client 开发服务器监听 `https://localhost:5001`。开发时需**同时运行两个项目**（或仅启动 Server，它同时提供 API 和 Client 静态文件回退）。

## 项目架构

```
BoxWise.slnx                        # .NET 10 新格式 (.slnx = XML)
├── src/
│   ├── BoxWise.Client/             # Blazor WASM (PWA) - UI 层
│   │   ├── Pages/                  # Razor 页面组件（Home, Login, NotFound）
│   │   ├── Layout/                 # MainLayout.razor
│   │   ├── Components/             # 可复用 Blazor 组件（LocationTree, TagFilter）
│   │   └── Services/               # AuthService, AppState, LocationService, TagService
│   ├── BoxWise.Server/             # ASP.NET Core Web API - 后端
│   │   ├── Endpoints/              # Minimal API 路由组（RouteGroupBuilder 模式）
│   │   ├── Data/                   # AppDbContext + EF Configurations
│   │   ├── Models/                 # Identity 实体（AppUser）
│   │   └── Migrations/             # EF Core 迁移
│   └── BoxWise.Shared/             # 共享 DTO（record 类型）
│       └── Dtos/
├── Directory.Build.props           # 根级：net10.0, Nullable, ImplicitUsings, WarningsAsErrors
├── Directory.Packages.props        # CPM 集中包版本管理
└── data/                           # SQLite 数据库文件
```

**引用关系：** Client → Shared, Server → Shared. Server 同时引用 Client 项目，用于 `.MapFallbackToFile("index.html")` SPA 回退。

## 关键技术决策

- **API 风格：** Minimal API + `RouteGroupBuilder` 静态扩展方法组织端点（参见 `AuthEndpoints.cs` 模式）
- **返回类型：** `TypedResults`（`TypedResults.Ok()`、`TypedResults.ValidationProblem()`）+ `ProblemDetails`
- **认证：** ASP.NET Core Identity + Cookie 认证 + Blazor WASM 侧自定义 `CookieAuthenticationStateProvider`
- **授权：** 全局 `FallbackPolicy` 要求认证，匿名端点显式标记 `.AllowAnonymous()`
- **UI 框架：** MudBlazor 9.4 — 见下方 [MudBlazor 9.x API 参考](#mudblazor-9x-api-参考)
- **数据库：** SQLite + EF Core，使用 CPM 管理包版本
- **Admin UI：** 独立的 Server 端 Razor Pages 区域（`Pages/Admin/`），不走 Blazor WASM

## 认证流程

1. 浏览器首次加载 → `CookieAuthenticationStateProvider.GetAuthenticationStateAsync()` 调用 `GET /api/auth/me` 检查 Cookie 中的登录会话
2. 登录 → `POST /api/auth/login` → Cookie 签发 → `AppState.SetUser()` 更新客户端状态
3. Server `Program.cs` 中 FallbackPolicy = `RequireAuthenticatedUser()`，所有端点默认受保护
4. `"Admin"` 角色通过 `userManager.IsInRoleAsync(user, "Admin")` 检查，结果通过 `AuthUserDto.IsAdmin` 传递到客户端

## MudBlazor 9.x API 参考

*基于 MudBlazor 9.4 源码验证（2026-05-24）。以下 API 与 MudBlazor 8.x 及常见文档有显著差异，务必使用以下正确 API。*

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

- **种子数据缺陷：** `Program.cs` 中的种子数据创建了 `admin` 用户但**未创建 `"Admin"` 角色也未分配**，导致 `IsInRoleAsync` 始终返回 `false`。Story 1.3 需要修复此问题。
- **AuthService.cs 中的重复 DTO：** `AuthService.cs` 内部定义了 `private record AuthUserDto`，与 `BoxWise.Shared.Dtos.AuthUserDto` 重复但字段相同——这是客户端私有副本，不影响功能。

## Epic 2 技术债务清理记录 (2026-05-24)

| 债务 | 状态 | 修复 |
|------|------|------|
| HttpClient BaseAddress 硬编码 | ✅ 已清理 | `Program.cs` 从 `IConfiguration["ApiBaseUrl"]` 读取，默认 `https://localhost:5000/` |
| 缺少 CancellationToken | ✅ 已清理 | `LocationService.GetAllAsync` / `TagService.GetAllAsync` 添加 `CancellationToken` 参数 |
| SortOrder 未在 CreateAsync 赋值 | ✅ 已清理 | `LocationRepository.CreateAsync` 接受 `sortOrder` 参数并赋值 |
| 缺少单元测试框架 | ⏳ Epic 3 规划 | 需新建测试项目 + 配置 |

## BMad 工作流上下文

- **BMad 版本：** v6.7.1（bmm + core 模块）
- **规划工件：** `_bmad-output/planning-artifacts/`
- **实施工件：** `_bmad-output/implementation-artifacts/`
- **当前进度：** Epic 1 ✅ | Epic 2 ✅（2.1/2.2/2.3/2.4 全部完成）| Epic 3 backlog | Epic 4 backlog
