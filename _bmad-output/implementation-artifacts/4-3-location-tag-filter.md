# Story 4.3: 位置与标签筛选

Status: review

## Story

As a 用户，
I want 按位置和标签筛选物品，
So that 按收纳结构或分类快速缩小查找范围。

## Acceptance Criteria

1. **AC-1: 位置筛选** — `GET /api/items?locationId={id}` 返回该位置及其所有子位置下的物品（物化路径子树查询）
2. **AC-2: 标签筛选** — `GET /api/items?tagId=3&tagId=5` 返回具有**全部**选中标签的物品（AND 逻辑）
3. **AC-3: 组合筛选** — 同时设置位置和标签时，返回同时满足两个条件的物品
4. **AC-4: 无结果空状态** — 筛选无匹配时显示 EmptyState
5. **AC-5: 浏览页集成** — Browse.razor 顶部显示 LocationTree + TagFilter 筛选控件
6. **AC-6: 筛选计数** — 网格上方显示当前筛选条件下的物品总数

## Tasks / Subtasks

- [x] Task 1: 扩展 API 筛选参数 (AC: #1, #2, #3)
  - [x] 1.1 `ItemRepository.GetFilteredAsync(locationId?, tagIds, query?)` — 组合筛选方法，物化路径子树 + 标签 AND + 关键词 LIKE
  - [x] 1.2 `GET /api/items` 端点新增 `locationId` 和 `tagId` 查询参数
  - [x] 1.3 空参数 = 返回全部（保持现有浏览模式），任一参数非空 = 调用 GetFilteredAsync

- [x] Task 2: 更新 Client ItemService (AC: #1, #2, #3)
  - [x] 2.1 `GetFilteredAsync(locationId?, tagIds, query?, ct)` — 构建含 locationId/tagId/q 的查询字符串

- [x] Task 3: 更新 Browse.razor 集成筛选 (AC: #4, #5, #6)
  - [x] 3.1 顶部添加 LocationTree 组件（`SelectedLocationId` + `SelectedLocationIdChanged`）
  - [x] 3.2 顶部添加 TagFilter 组件（`SelectedTagIds` + `SelectedTagIdsChanged`）
  - [x] 3.3 筛选参数变化时重新调用 API
  - [x] 3.4 显示当前筛选结果计数
  - [x] 3.5 无结果时显示 EmptyState

- [x] Task 4: 构建验证 (AC: #1-#6)
  - [x] 4.1 `dotnet build BoxWise.slnx` 零错误零警告
  - [x] 4.2 `dotnet test BoxWise.slnx` 全部通过

---

## Dev Notes

### 前置上下文

- **LocationTree.razor 已就绪** — 双向绑定 `SelectedLocationId`/`SelectedLocationIdChanged`，MudTreeView + TreeItemData
- **TagFilter.razor 已就绪** — 双向绑定 `SelectedTagIds`/`SelectedTagIdsChanged`，MudChipSet MultiSelection
- **Browse.razor 已就绪** — Story 4.2 创建，MudGrid 响应式布局 + ItemCard
- **LocationRepository** — 使用物化路径 `Path TEXT` 列，子节点查询通过 `Path.StartsWith(parentPath)`
- **现有 GET /api/items** — 当前支持 `?q=`（搜索），需扩展支持 `?locationId=` 和 `?tagId=`
- **ItemRepository.SearchAsync** — 仅处理关键词搜索，需新增综合筛选方法

### ItemRepository.GetFilteredAsync 实现

```csharp
public async Task<List<Item>> GetFilteredAsync(int? locationId, List<int>? tagIds, string? query)
{
    IQueryable<Item> q = _db.Items
        .Include(i => i.Location)
        .Include(i => i.Tags);

    // 位置筛选：子树查询
    if (locationId.HasValue)
    {
        var location = await _db.Locations.FindAsync(locationId.Value);
        if (location is not null)
        {
            q = q.Where(i => i.Location != null && i.Location.Path.StartsWith(location.Path));
        }
    }

    // 标签筛选：AND 逻辑（物品必须具有所有选中标签）
    if (tagIds is { Count: > 0 })
    {
        foreach (var tagId in tagIds)
        {
            var id = tagId; // 闭包捕获
            q = q.Where(i => i.Tags.Any(t => t.Id == id));
        }
    }

    // 关键词搜索
    if (!string.IsNullOrWhiteSpace(query))
    {
        var keyword = query.Trim();
        q = q.Where(i => i.Name.Contains(keyword)
                      || (i.Note != null && i.Note.Contains(keyword))
                      || i.Tags.Any(t => t.Name.Contains(keyword)));
    }

    return await q
        .OrderByDescending(i => i.CreatedAt)
        .Take(100)
        .AsSplitQuery()
        .ToListAsync();
}
```

**关键设计：**
- `IQueryable<T>` 逐步构建，EF Core 自动组合 WHERE 条件为 AND
- 位置筛选先查 Location 获取 Path，再 `StartsWith()` 子树匹配
- 标签筛选使用 `foreach` 叠加 `Where`，EF Core 翻译为多个 EXISTS 子查询（AND 逻辑）
- 原 `SearchAsync` 和 `GetAllAsync` 可保留（或内部调用此方法），但端点层直接使用此方法统一处理

### ItemEndpoints 扩展

```csharp
group.MapGet("/", SearchItemsAsync)
    .Produces<ItemSummaryDto[]>(200)
    .ProducesProblem(401)
    .WithTags("Items")
    .WithDescription("搜索/筛选/浏览物品（可选 q/locationId/tagId）");

// 端点方法签名扩展：
private static async Task<Ok<ItemSummaryDto[]>>
    SearchItemsAsync(string? q, int? locationId, [FromQuery] List<int>? tagId,
        ItemRepository repo, HttpContext httpContext)
{
    var items = await repo.GetFilteredAsync(locationId, tagId, q);
    // DTO 映射不变...
}
```

**注意：** `tagId` 用 `[FromQuery] List<int>?` 绑定多个 `?tagId=3&tagId=5`

### Browse.razor 集成设计

```
┌─────────────────────────────┐
│  浏览物品                    │
├─────────────────────────────┤
│  [LocationTree]  [TagFilter] │  ← 筛选控件
├─────────────────────────────┤
│  找到 X 件物品               │  ← 计数
├─────────────────────────────┤
│  MudGrid (ItemCard...)       │  ← 筛选结果
└─────────────────────────────┘
```

```razor
@page "/browse"
@attribute [Authorize]

<!-- 筛选控件 -->
<MudGrid Class="mb-2">
    <MudItem xs="12" sm="6">
        <MudText Typo="Typo.subtitle2">按位置</MudText>
        <LocationTree @bind-SelectedLocationId="_locationId" />
    </MudItem>
    <MudItem xs="12" sm="6">
        <MudText Typo="Typo.subtitle2">按标签</MudText>
        <TagFilter @bind-SelectedTagIds="_tagIds" />
    </MudItem>
</MudGrid>

<!-- 计数 -->
@if (_items is not null)
{
    <MudText Typo="Typo.caption" Class="mb-2">找到 @_items.Count 件物品</MudText>
}

<!-- 原有三态（加载/空/网格）保持不变 -->
```

**筛选触发：** `_locationId` 或 `_tagIds` 变更时 → `OnParametersSet` 检测变化 → 重新调用 API。

### 双向绑定与刷新策略

LocationTree 和 TagFilter 已有双向绑定，Browse 页面需要：

```csharp
private int? _locationId;
private IReadOnlyCollection<int> _tagIds = Array.Empty<int>();
private int? _prevLocationId;
private IReadOnlyCollection<int> _prevTagIds = Array.Empty<int>();

protected override async Task OnParametersSetAsync()
{
    if (_locationId != _prevLocationId || !_tagIds.SequenceEqual(_prevTagIds))
    {
        _prevLocationId = _locationId;
        _prevTagIds = _tagIds.ToList();
        await LoadAsync();
    }
}
```

**或更简洁的方式：** 直接在回调中调用 LoadAsync()（利用 EventCallback 触发搜索）。

### 文件结构变更

```
src/BoxWise.Server/
  Repositories/ItemRepository.cs          (modified — 新增 GetFilteredAsync，重构 GetAllAsync/SearchAsync)
  Endpoints/ItemEndpoints.cs              (modified — 扩展查询参数)
src/BoxWise.Client/
  Services/ItemService.cs                 (modified — 新增 GetFilteredAsync)
  Pages/Browse.razor                      (modified — 集成筛选控件)
```

### MudBlazor 9.x API 提醒

| 场景 | 正确 API |
|------|----------|
| MudTreeView | `SelectedValue` + `SelectedValueChanged`（非 ActivatedValue） |
| MudChipSet 多选 | `SelectionMode="SelectionMode.MultiSelection"` + `SelectedValues` |
| MudItem 断点 | `xs="12" sm="6"` |

### 构建与验证

```bash
dotnet build BoxWise.slnx
dotnet test BoxWise.slnx
```

---

## References

| 内容 | 来源 |
|------|------|
| Story AC 定义 | [Source: epics.md#Story 4.3] |
| FR-12 位置筛选 | [Source: prd.md#FR-12] |
| FR-13 标签筛选 | [Source: prd.md#FR-13] |
| 物化路径模式 | [Source: architecture.md#Materialized Path Queries] |
| LocationTree 组件 | [Source: Story 2.4] |
| TagFilter 组件 | [Source: Story 2.4] |
| MudBlazor 9.x API | [Source: CLAUDE.md#MudBlazor 9.x API 参考] |

## Dev Agent Record

### Agent Model Used

deepseek-v4-pro

### Debug Log References

### Completion Notes List

✅ 全部 4 个 Task 完成 — 位置与标签筛选就绪，22/22 测试通过

**实施要点：**
- ItemRepository.GetFilteredAsync：IQueryable 逐步构建 — 物化路径子树 (Path.StartsWith) + 标签 AND (foreach Where) + 关键词 LIKE，统一方法替代 GetAllAsync/SearchAsync
- ItemEndpoints GET /api/items：新增 locationId 和 tagId 参数（string[] 绑定多值），空参数=全部，任一非空=筛选
- 标签 AND 逻辑：多次 `.Where(i => i.Tags.Any(t => t.Id == id))` 迭代 → EF Core 翻译为多个 EXISTS 子查询
- ItemService.GetFilteredAsync：手动拼接查询字符串（locationId/tagId/q），GetAllAsync 和 SearchAsync 改为委托
- Browse.razor：顶部 LocationTree + TagFilter 双向绑定，OnParametersSetAsync 检测参数变化自动重新加载
- 筛选空结果 vs 初始空状态：_hasFilter 区分"筛选无匹配"与"暂无物品"两种文案

### File List

**修改文件:**
- `src/BoxWise.Server/Repositories/ItemRepository.cs` (modified — 新增 GetFilteredAsync)
- `src/BoxWise.Server/Endpoints/ItemEndpoints.cs` (modified — 扩展查询参数)
- `src/BoxWise.Client/Services/ItemService.cs` (modified — 新增 GetFilteredAsync，重构 GetAllAsync/SearchAsync)
- `src/BoxWise.Client/Pages/Browse.razor` (modified — 集成筛选控件)
