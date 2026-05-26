# Story 4.2: 缩略图网格浏览

Status: done

## Story

As a 用户，
I want 以缩略图网格浏览所有物品，
So that 视觉化地概览家庭物品库。

## Acceptance Criteria

1. **AC-1: 浏览 API** — `GET /api/items`（无查询参数）返回所有物品按创建时间倒序，复用 `ItemSummaryDto[]` + `X-Total-Count`
2. **AC-2: 缩略图卡片** — ItemCard 展示 300px 缩略图（有照片）或 MudIcon 占位（无照片）+ 物品名称 + 位置概要
3. **AC-3: 响应式网格** — MudGrid + MudItem：移动端 2 列 / 平板 4 列 / 桌面 6 列
4. **AC-4: 加载状态** — API 未返回时显示 MudProgressCircular
5. **AC-5: 导航到详情** — 点击卡片跳转 `/items/{id}`
6. **AC-6: 入口更新** — Home.razor "浏览物品"按钮导航至 `/browse`
7. **AC-7: 认证保护** — 浏览页面和 API 均需登录

## Tasks / Subtasks

- [x] Task 1: 扩展 GET /api/items 端点支持浏览模式 (AC: #1, #7)
  - [x] 1.1 `ItemRepository.GetAllAsync()` — 返回所有物品，Include Location + Tags，按 CreatedAt 倒序，Take(100)
  - [x] 1.2 `GET /api/items` 端点：q 为空时调用 GetAllAsync 返回全部，q 非空时调用 SearchAsync
  - [x] 1.3 更新 `.Produces*()` 注解反映浏览+搜索双模式

- [x] Task 2: 创建 ItemCard 组件 (AC: #2, #5)
  - [x] 2.1 `src/BoxWise.Client/Components/ItemCard.razor` — MudPaper 卡片，Elevation=1，4dp 圆角
  - [x] 2.2 有照片 → 显示 300px 缩略图（`/api/images/{id}?type=thumb`）
  - [x] 2.3 无照片 → MudIcon 占位（`Icons.Material.Filled.Image`）
  - [x] 2.4 卡片底部：物品名称（Typo.body2）+ 位置概要（Typo.caption）
  - [x] 2.5 整卡可点击 → `Navigation.NavigateTo($"/items/{id}")`

- [x] Task 3: 创建 Browse.razor 页面 (AC: #3, #4, #7)
  - [x] 3.1 `src/BoxWise.Client/Pages/Browse.razor` — 路由 `@page "/browse"` + `@attribute [Authorize]`
  - [x] 3.2 MudGrid + MudItem：`xs="6" sm="4" md="3" lg="2"`（2/3/4/6 列）
  - [x] 3.3 加载中 → MudProgressCircular
  - [x] 3.4 空状态 → EmptyState "暂无物品，去录入第一个吧"
  - [x] 3.5 `@inject ItemService`，`OnInitializedAsync` 调用 `GetAllAsync`

- [x] Task 4: 更新 Client ItemService (AC: #1)
  - [x] 4.1 `ItemService.GetAllAsync(CancellationToken ct)` → `GET api/items`（无 q 参数）

- [x] Task 5: 更新首页入口 (AC: #6)
  - [x] 5.1 `Home.razor` "浏览物品"按钮 → `Navigation.NavigateTo("/browse")`，移除"即将推出"文字

- [x] Task 6: 构建验证 (AC: #1-#7)
  - [x] 6.1 `dotnet build BoxWise.slnx` 零错误零警告
  - [x] 6.2 `dotnet test BoxWise.slnx` 全部通过

---

## Dev Notes

### 前置上下文

- **Story 4.1 已完成** — ItemSummaryDto、SearchAsync、SearchBar、ItemService.SearchAsync 均已就绪
- **GET /api/items 端点** — 目前仅在 `?q=` 非空时返回搜索结果，需扩展为 `q` 为空时返回全部物品
- **ItemRepository** — 目前有 CreateAsync、GetByIdAsync、SearchAsync，需新增 GetAllAsync
- **ItemSummaryDto** — 已包含所需全部字段（Id、Name、ThumbPath、LocationPath、TagNames、CreatedAt）
- **MainLayout.razor** — 极简布局仅 `@Body`，无底部 Tab 栏
- **Home.razor** — "浏览物品"按钮当前为灰色占位（"即将推出"）
- **学习自 Story 4.1** — MudBlazor 9.x 泛型组件需显式 T 参数；使用 Value+ValueChanged 替代 @bind-Value 处理回调

### ItemRepository.GetAllAsync 实现

```csharp
public async Task<List<Item>> GetAllAsync()
{
    return await _db.Items
        .Include(i => i.Location)
        .Include(i => i.Tags)
        .OrderByDescending(i => i.CreatedAt)
        .Take(100)
        .AsSplitQuery()
        .ToListAsync();
}
```

### 端点扩展：GET /api/items 双模式

现有 `SearchItemsAsync` 需改为同时处理浏览和搜索：

```csharp
private static async Task<Ok<ItemSummaryDto[]>>
    SearchItemsAsync(string? q, ItemRepository repo, HttpContext httpContext)
{
    var items = string.IsNullOrWhiteSpace(q)
        ? await repo.GetAllAsync()
        : await repo.SearchAsync(q);

    var dtos = items.Select(i => new ItemSummaryDto(
        i.Id, i.Name, i.ThumbPath,
        i.Location?.Path,
        i.Tags.Select(t => t.Name).ToList(),
        i.CreatedAt)).ToArray();

    httpContext.Response.Headers["X-Total-Count"] = dtos.Length.ToString();
    return TypedResults.Ok(dtos);
}
```

**注意：** `X-Total-Count` 对于浏览模式返回的是截断后的数量（最多 100），未来分页时使用。

### ItemCard.razor 组件设计

```
┌──────────────────┐
│                  │
│   [缩略图/图标]   │  ← 150-180px 高，object-fit: cover
│                  │
├──────────────────┤
│  物品名称         │  ← Typo.body2, 单行截断
│  位置概要         │  ← Typo.caption, 路径格式化
└──────────────────┘
```

```razor
<MudPaper Elevation="1" Class="rounded pa-0" Style="overflow:hidden;cursor:pointer"
          OnClick='() => Navigation.NavigateTo($"/items/{Item.Id}")'>
    @if (!string.IsNullOrEmpty(Item.ThumbPath))
    {
        <img src="api/images/@Item.Id?type=thumb"
             alt="@Item.Name"
             style="width:100%;height:150px;object-fit:cover;" />
    }
    else
    {
        <div style="width:100%;height:150px;display:flex;align-items:center;justify-content:center;background:#F5F5F5">
            <MudIcon Icon="@Icons.Material.Filled.Image" Size="Size.Large" Color="Color.Default" />
        </div>
    }
    <MudStack Class="pa-2" Spacing="0">
        <MudText Typo="Typo.body2" Style="overflow:hidden;text-overflow:ellipsis;white-space:nowrap">
            @Item.Name
        </MudText>
        <MudText Typo="Typo.caption" Color="Color.Default" Style="overflow:hidden;text-overflow:ellipsis;white-space:nowrap">
            @FormatLocationPath(Item.LocationPath)
        </MudText>
    </MudStack>
</MudPaper>

@code {
    [Parameter, EditorRequired]
    public ItemSummaryDto Item { get; set; } = null!;

    [Inject]
    public NavigationManager Navigation { get; set; } = null!;

    private static string FormatLocationPath(string? path)
    {
        if (string.IsNullOrEmpty(path)) return "";
        return string.Join(" → ", path.Split('/', StringSplitOptions.RemoveEmptyEntries));
    }
}
```

### Browse.razor 页面结构

```razor
@page "/browse"
@attribute [Authorize]

<MudText Typo="Typo.h5" Class="mb-4">浏览物品</MudText>

@if (_loading)
{
    <div class="d-flex justify-center my-8">
        <MudProgressCircular Color="Color.Primary" Indeterminate="true" />
    </div>
}
else if (_items is { Count: 0 })
{
    <div class="d-flex flex-column align-center justify-center my-8" style="color:#9E9E9E">
        <MudIcon Icon="@Icons.Material.Filled.Inventory2" Size="Size.Large" />
        <MudText Typo="Typo.body1" Class="mt-2">暂无物品</MudText>
        <MudText Typo="Typo.caption">去录入第一个吧</MudText>
    </div>
}
else if (_items is not null)
{
    <MudGrid>
        @foreach (var item in _items)
        {
            <MudItem xs="6" sm="4" md="3" lg="2">
                <ItemCard Item="item" />
            </MudItem>
        }
    </MudGrid>
}
```

### 响应式断点

| 设备 | MudItem 属性 | 列数 | 卡片宽度 |
|------|-------------|------|---------|
| 手机 (<600px) | `xs="6"` | 2 列 | ~180px |
| 平板 (≥600px) | `sm="4"` | 3 列 | ~240px |
| 小桌面 (≥960px) | `md="3"` | 4 列 | ~280px |
| 大桌面 (≥1280px) | `lg="2"` | 6 列 | ~220px |

### 首屏性能策略

| 策略 | 实现 |
|------|------|
| 服务端限制 | `Take(100)` 硬限制 |
| 缩略图 | 300px thumb（~30-50KB/张），非原图 |
| AsSplitQuery | 避免 EF Core 生成巨大 JOIN |
| 懒加载 | 浏览器原生 `<img loading="lazy">` |
| 未来分页 | 数据超 100 条时添加分页参数（v2） |

### 文件结构变更

```
src/BoxWise.Server/
  Repositories/ItemRepository.cs          (modified — 添加 GetAllAsync)
  Endpoints/ItemEndpoints.cs              (modified — 扩展 GET / 为双模式)
src/BoxWise.Client/
  Services/ItemService.cs                 (modified — 添加 GetAllAsync)
  Components/ItemCard.razor               (new — 物品卡片组件)
  Pages/Browse.razor                      (new — 浏览页面)
  Pages/Home.razor                        (modified — "浏览物品"按钮导航)
```

### MudBlazor 9.x API 提醒

| 场景 | 正确 API |
|------|----------|
| MudPaper 圆角 | `Class="rounded"`（MudBlazor 内置类） |
| MudGrid/MudItem | `xs="6" sm="4" md="3" lg="2"` |
| MudIcon | `Icon="@Icons.Material.Filled.Image"` |
| 图片懒加载 | HTML `loading="lazy"` 属性 |
| MudProgressCircular | `Indeterminate="true"` |

### 构建与验证

```bash
dotnet build BoxWise.slnx
dotnet test BoxWise.slnx

# 手动测试
# 1. 登录 → 首页点击"浏览物品" → 进入 /browse
# 2. 有物品 → 网格展示缩略图卡片
# 3. 无物品 → 空状态提示
# 4. 点击卡片 → 跳转物品详情
# 5. 调整浏览器宽度 → 响应式列数变化
```

---

## References

| 内容 | 来源 |
|------|------|
| Story AC 定义 | [Source: epics.md#Story 4.2] |
| FR-11 缩略图网格 | [Source: prd.md#FR-11] |
| 响应式布局 | [Source: ux-design-specification.md#Spacing] |
| ItemSummaryDto | [Source: Story 4.1: ItemSummaryDto.cs] |
| MudBlazor 9.x API | [Source: CLAUDE.md#MudBlazor 9.x API 参考] |
| 前端组件架构 | [Source: architecture.md#Component Architecture] |

## Dev Agent Record

### Agent Model Used

deepseek-v4-pro

### Debug Log References

### Completion Notes List

✅ 全部 6 个 Task 完成 — 缩略图网格浏览就绪，22/22 测试通过

**实施要点：**
- ItemRepository.GetAllAsync：CreatedAt 倒序 + Include Location/Tags + Take(100) + AsSplitQuery
- GET /api/items 双模式：无 q → 浏览（全部），有 q → 搜索
- ItemCard.razor：MudPaper 卡片 + 300px 缩略图/占位图标 + 名称 + 位置路径，div 包裹 @onclick
- Browse.razor：MudGrid 响应式 2/3/4/6 列 + 加载态 + 空状态
- Home.razor "浏览物品"按钮：由灰色占位改为 Color.Secondary 导航按钮
- MUD0002 分析器合规：MudPaper 不支持 OnClick，用外层 div 处理点击导航

### File List

**新增文件:**
- `src/BoxWise.Client/Components/ItemCard.razor` (new)
- `src/BoxWise.Client/Pages/Browse.razor` (new)

**修改文件:**
- `src/BoxWise.Server/Repositories/ItemRepository.cs` (modified — 添加 GetAllAsync)
- `src/BoxWise.Server/Endpoints/ItemEndpoints.cs` (modified — GET / 端点双模式)
- `src/BoxWise.Client/Services/ItemService.cs` (modified — 添加 GetAllAsync)
- `src/BoxWise.Client/Pages/Home.razor` (modified — 浏览物品按钮导航)

### Review Findings (2026-05-26 — 位置路径显示修复)

- [x] [Review][Patch] **ItemCard 空位置路径显示空白** [ItemCard.razor:34] — 已修复：`string.IsNullOrWhiteSpace` 替代 `string.IsNullOrEmpty`，空值显示"未分配"
- [x] [Review][Defer] **位置名称路径批量解析性能** [ItemEndpoints.cs:113] — 浏览请求每次全量加载位置表，当前数据量可接受
- [x] [Review][Defer] **已删除位置的 ID 可能出现在卡片路径中** — 降级逻辑保留，需级联删除功能
