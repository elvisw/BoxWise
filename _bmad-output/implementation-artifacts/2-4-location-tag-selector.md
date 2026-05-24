# Story 2.4: 前端 — 位置树选择器 + 标签选择器

Status: review

## Story

As a 用户，
I want 在 UI 中浏览位置树和选择标签，
so that 录入时可快速选位置，浏览时可筛选。

## Acceptance Criteria

1. **AC-1: 位置树展示** — `LocationTree.razor` 使用 MudTreeView 展示层级位置树，叶子节点可选中高亮
2. **AC-2: 位置选择事件** — 选中节点时触发 `SelectedLocationChanged` EventCallback，传递被选中的 LocationId
3. **AC-3: 标签 ChipSet** — `TagFilter.razor` 使用 MudChipSet 展示所有标签，支持多选，选中/取消时触发 `SelectedTagsChanged`
4. **AC-4: 数据加载** — 组件 `OnInitializedAsync` 时自动从 `/api/locations` 和 `/api/tags` 加载数据
5. **AC-5: 空状态** — 无位置或无标签时显示友好的空状态提示

## Tasks / Subtasks

- [x] Task 1: 创建 Client HTTP 服务层 (AC: #4)
  - [x] 1.1 `src/BoxWise.Client/Services/LocationService.cs` — `GetAllAsync()` → `GET /api/locations` → `List<LocationDto>`
  - [x] 1.2 `src/BoxWise.Client/Services/TagService.cs` — `GetAllAsync()` → `GET /api/tags` → `List<TagDto>`
  - [x] 1.3 `Program.cs` 注册两个 Service 为 Scoped

- [x] Task 2: 创建 LocationTree 组件 (AC: #1, #2, #5)
  - [x] 2.1 `src/BoxWise.Client/Components/LocationTree.razor` — MudTreeView + 层级数据绑定
  - [x] 2.2 提供参数：`SelectedLocationId` (int?，双向绑定) + `SelectedLocationChanged` (EventCallback<int?>)
  - [x] 2.3 `OnInitializedAsync` 加载位置数据 → 根据 ParentId 构建树节点层级 → 绑定到 MudTreeView
  - [x] 2.4 无数据时显示空状态提示："暂无位置，请先在管理后台创建"

- [x] Task 3: 创建 TagFilter 组件 (AC: #3, #5)
  - [x] 3.1 `src/BoxWise.Client/Components/TagFilter.razor` — MudChipSet + MultiSelect
  - [x] 3.2 提供参数：`SelectedTags` (List<int>，双向绑定) + `SelectedTagsChanged` (EventCallback<List<int>>)
  - [x] 3.3 `OnInitializedAsync` 加载标签数据 → 渲染 MudChip
  - [x] 3.4 无数据时显示空状态提示："暂无标签，录入物品时可自动创建"

- [x] Task 4: 构建验证 (AC: #1-#5)
  - [x] 4.1 `dotnet build BoxWise.slnx` 零错误零警告
  - [x] 4.2 启动 Server + Client，验证位置树组件渲染
  - [x] 4.3 验证标签 ChipSet 渲染并支持多选
  - [x] 4.4 验证空数据状态提示

---

## Dev Notes

### 前置上下文

- **Epic 2 API 就绪:** `GET /api/locations` (Story 2.2) + `GET /api/tags` (Story 2.3) 均已实现
- **MudBlazor 9.4 已集成:** `index.html` 含 MudBlazor CSS/JS，`_Imports.razor` 含 MudBlazor 命名空间
- **Client 认证:** `CookieAuthenticationStateProvider` 启动时通过 `/api/auth/me` 恢复登录态，Cookie 自动携带
- **HttpClient BaseAddress:** `https://localhost:5000/`（Client Program.cs 第 22 行）
- **Client DI:** `AddScoped` + `AddMudServices()` 已在 Program.cs

### Epic 2 前序学习（应用于前端代码）

1. **DTO 共享** — Client 引用 `BoxWise.Shared` 项目，直接使用 `LocationDto`、`TagDto` record 类型
2. **异常处理** — 前端 HTTP 调用应 try-catch 防止网络错误崩溃整个组件
3. **异步加载** — `OnInitializedAsync` 中加载数据，`StateHasChanged()` 自动触发
4. **组件参数** — 使用 `[Parameter]` + `EventCallback<T>` 模式通知父组件

### 现有 Client 项目结构

```
src/BoxWise.Client/
├── Program.cs                    ← 注册 DI（HttpClient, Auth, MudBlazor）
├── App.razor                     ← CascadingAuthenticationState + AuthorizeRouteView
├── _Imports.razor                ← MudBlazor + Auth + Shared 命名空间
├── Layout/MainLayout.razor       ← 当前：仅 @Body
├── Pages/
│   ├── Home.razor                ← "/" 路由
│   ├── Login.razor               ← "/login" 路由
│   └── NotFound.razor            ← 404 页面
├── Services/
│   ├── AuthService.cs
│   ├── AppState.cs
│   └── CookieAuthenticationStateProvider.cs
└── wwwroot/
```

### 新增 Component 目录结构

```
src/BoxWise.Client/
└── Components/
    ├── LocationTree.razor        ← MudTreeView 层级位置树
    └── TagFilter.razor           ← MudChipSet 多选标签
```

### LocationTree 设计

```razor
@using BoxWise.Shared.Dtos
@inject LocationService LocationService

<MudTreeView @bind-ActivatedValue="SelectedLocationId"
             Items="TreeItems"
             ItemsSelector="@(item => item.Children)">
    <ItemTemplate>
        <MudTreeViewItem @key="@context.Id" Value="@context.Location.Id">
            <Text>@context.Location.Name</Text>
        </MudTreeViewItem>
    </ItemTemplate>
</MudTreeView>

@if (!_hasData)
{
    <MudText Typo="Typo.caption" Color="Color.Default">暂无位置，请先在管理后台创建</MudText>
}

@code {
    [Parameter] public int? SelectedLocationId { get; set; }
    [Parameter] public EventCallback<int?> SelectedLocationIdChanged { get; set; }

    private List<TreeItem> TreeItems = [];
    private bool _hasData;

    protected override async Task OnInitializedAsync()
    {
        try
        {
            var locations = await LocationService.GetAllAsync();
            _hasData = locations.Count > 0;
            TreeItems = BuildTree(locations, null);
        }
        catch { }
    }

    private List<TreeItem> BuildTree(List<LocationDto> locations, int? parentId)
    {
        return locations
            .Where(l => l.ParentId == parentId)
            .OrderBy(l => l.SortOrder)
            .ThenBy(l => l.Name)
            .Select(l => new TreeItem
            {
                Location = l,
                Children = BuildTree(locations, l.Id)
            }).ToList();
    }

    private class TreeItem
    {
        public LocationDto Location { get; set; } = null!;
        public List<TreeItem> Children { get; set; } = [];
    }
}
```

### TagFilter 设计

```razor
@using BoxWise.Shared.Dtos
@inject TagService TagService

<MudChipSet Filter MultiSelection
            @bind-SelectedValues="SelectedTagIds"
            SelectedValuesChanged="OnSelectedTagsChanged">
    @foreach (var tag in _tags)
    {
        <MudChip Value="@tag.Id" Color="Color.Primary" Variant="Variant.Outlined">
            @tag.Name
        </MudChip>
    }
</MudChipSet>

@if (!_hasData)
{
    <MudText Typo="Typo.caption" Color="Color.Default">暂无标签，录入物品时可自动创建</MudText>
}

@code {
    [Parameter] public List<int> SelectedTagIds { get; set; } = [];
    [Parameter] public EventCallback<List<int>> SelectedTagIdsChanged { get; set; }

    private List<TagDto> _tags = [];
    private bool _hasData;

    protected override async Task OnInitializedAsync()
    {
        try
        {
            _tags = await TagService.GetAllAsync();
            _hasData = _tags.Count > 0;
        }
        catch { }
    }

    private async Task OnSelectedTagsChanged(IEnumerable<object> values)
    {
        SelectedTagIds = values.Cast<int>().ToList();
        await SelectedTagIdsChanged.InvokeAsync(SelectedTagIds);
    }
}
```

### HTTP 服务设计

```csharp
// LocationService.cs
public class LocationService
{
    private readonly HttpClient _http;
    public LocationService(HttpClient http) => _http = http;

    public async Task<List<LocationDto>> GetAllAsync()
    {
        var response = await _http.GetAsync("api/locations");
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<List<LocationDto>>() ?? [];
    }
}

// TagService.cs — 同理
```

### 构建与验证

```bash
# 构建
dotnet build BoxWise.slnx

# 启动 Server + Client
# 终端1: cd src/BoxWise.Server && dotnet run
# 终端2: cd src/BoxWise.Client && dotnet run

# 浏览器访问 https://localhost:5001
# 验证：登录后访问首页，确认组件渲染
```

### 关键风险点

1. **MudTreeView 层级绑定** — `ItemsSelector` 需要正确返回子节点列表，确保 `BuildTree` 递归逻辑正确
2. **MudChipSet.Filter + MultiSelection** — MudChipSet 的 `Filter` 模式影响选中样式，确保配合 `Color.Primary` 和 `Variant.Outlined`
3. **CORS** — Client (5001) 调用 Server (5000) API，开发环境已配置 CORS "Dev" 策略
4. **认证** — 用户需先登录才能加载数据（`OnInitializedAsync` 发起 API 调用时 Cookie 自动携带）

---

## References

| 内容 | 来源 |
|------|------|
| Story AC 定义 | [Source: epics.md#Story 2.4] |
| FR-14 层级位置创建、FR-15 层级浏览、FR-5 标签附加 | [Source: prd.md] |
| UX-2 底部 3 Tab 导航 | [Source: ux-design-specification.md] |
| MudBlazor MudTreeView | [Source: mudblazor.com] |
| MudBlazor MudChipSet | [Source: mudblazor.com] |
| Client 现有结构 | [Source: src/BoxWise.Client/] |
| LocationDto 定义 | [Source: Story 2.1: LocationDto.cs] |
| TagDto 定义 | [Source: Story 2.3: TagDto.cs] |
| Client HttpClient 配置 | [Source: Program.cs#BaseAddress] |

## Dev Agent Record

### Agent Model Used

deepseek-v4-pro

### Debug Log References

- MudBlazor 9.x API 适配：`ActivatedValue`→`SelectedValue`, `Filter`/`MultiSelection`→`SelectionMode`, `List<int>`→`IReadOnlyCollection<int>`
- `TreeItemData<T>` 使用 `Text`+`Value`+`Children` 构建层级树

### Completion Notes List

✅ **全部 4 个任务完成** — 位置树选择器 + 标签选择器组件就绪

**实施要点：**
- LocationTree.razor：MudTreeView + TreeItemData<LocationDto> 递归构建
- TagFilter.razor：MudChipSet + SelectionMode.MultiSelection + IReadOnlyCollection<int>
- LocationService / TagService：HttpClient 封装 /api/locations 和 /api/tags
- MudBlazor 9.x 源码验证：SelectedValue（非 ActivatedValue）、SelectionMode（非 Filter/MultiSelection）

**构建结果：**
- `dotnet build BoxWise.slnx` → 0 错误 0 警告 ✅

### File List

**新增文件:**
- `src/BoxWise.Client/Services/LocationService.cs` (new)
- `src/BoxWise.Client/Services/TagService.cs` (new)
- `src/BoxWise.Client/Components/LocationTree.razor` (new)
- `src/BoxWise.Client/Components/TagFilter.razor` (new)

**修改文件:**
- `src/BoxWise.Client/Program.cs` (modified) — 注册 LocationService + TagService
