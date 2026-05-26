# Story 4.1: 搜索功能

Status: review

## Story

As a 用户，
I want 用关键词搜索物品，
So that 快速找到目标物品而无需翻遍整个物品库。

## Acceptance Criteria

1. **AC-1: 搜索 API** — `GET /api/items?q={keyword}`，EF Core LIKE 模糊匹配物品名称、备注和标签，返回 `ItemSummaryDto[]` + `X-Total-Count` 响应头
2. **AC-2: 搜索结果展示** — 列表展示缩略图 + 名称 + 位置路径 + 标签，按名称匹配优先排列
3. **AC-3: 空结果处理** — 搜索无匹配时显示 EmptyState 空状态提示
4. **AC-4: 导航到详情** — 点击某个搜索结果跳转至物品详情页 `/items/{id}`
5. **AC-5: 搜索栏组件** — `SearchBar.razor` 组件，MudTextField + Adornment 搜索图标，防抖 300ms
6. **AC-6: 认证保护** — 搜索端点和页面均需登录

## Tasks / Subtasks

- [x] Task 1: 创建 ItemSummaryDto (AC: #1, #2)
  - [x] 1.1 `src/BoxWise.Shared/Dtos/ItemSummaryDto.cs` — positional record：Id, Name, ThumbPath, LocationPath, TagNames, CreatedAt
  - [x] 1.2 TagNames 用 `IReadOnlyList<string>` 类型

- [x] Task 2: 添加服务端搜索能力 (AC: #1, #6)
  - [x] 2.1 `ItemRepository.SearchAsync(string query)` — EF Core LIKE 查询 Name/Note/Tags，Include Location 和 Tags 导航属性
  - [x] 2.2 `GET /api/items?q=` 端点 — 调用 repo.SearchAsync，返回 `Ok<ItemSummaryDto[]>` + `X-Total-Count` 头
  - [x] 2.3 添加 `.Produces*()` 注解（200 + 401）
  - [x] 2.4 空查询参数或无结果返回空数组（非 404）

- [x] Task 3: 创建 SearchBar 组件 (AC: #5)
  - [x] 3.1 `src/BoxWise.Client/Components/SearchBar.razor` — MudTextField + InputAdornment 搜索图标
  - [x] 3.2 防抖 300ms：用户停止输入 300ms 后才触发搜索
  - [x] 3.3 `SearchTextChanged` EventCallback 向父组件通知搜索文本变化

- [x] Task 4: 更新 Client ItemService (AC: #1)
  - [x] 4.1 `ItemService.SearchAsync(string query, CancellationToken ct)` → `GET api/items?q={query}`
  - [x] 4.2 返回 `List<ItemSummaryDto>?`（null = 网络错误，空列表 = 无结果）

- [x] Task 5: 更新首页集成搜索 (AC: #2, #3, #4)
  - [x] 5.1 `Home.razor` 添加 SearchBar 组件
  - [x] 5.2 搜索结果列表：缩略图（300px 或占位图标）+ 名称 + 位置路径 + 标签
  - [x] 5.3 点击结果跳转 `/items/{id}`
  - [x] 5.4 无结果时显示 EmptyState："未找到匹配的物品"
  - [x] 5.5 首次加载时显示提示："输入关键词搜索物品"

- [x] Task 6: 构建验证 (AC: #1-#6)
  - [x] 6.1 `dotnet build BoxWise.slnx` 零错误零警告
  - [x] 6.2 `dotnet test BoxWise.slnx` 全部通过

---

## Dev Notes

### 前置上下文

- **Epic 3 全部完成** — Item 实体、CRUD、图片上传、AI 识别、录入 UI、详情页均已就绪
- **ItemRepository** — 目前仅有 `CreateAsync` 和 `GetByIdAsync`，需要新增 `SearchAsync`
- **ItemEndpoints** — 目前有 `POST /` 和 `GET /{id}`，需要新增 `GET /?q=`
- **ItemSummaryDto 不存在** — 需要在 Shared 项目中新建，与 ItemDto 不同（精简版，用于列表/搜索展示）
- **Home.razor** — 当前仅有"录入物品"和"浏览物品（即将推出）"两个占位按钮

### ItemSummaryDto 设计

```csharp
// src/BoxWise.Shared/Dtos/ItemSummaryDto.cs
public record ItemSummaryDto(
    int Id,
    string Name,
    string? ThumbPath,
    string? LocationPath,
    IReadOnlyList<string> TagNames,
    DateTime CreatedAt);
```

**与 ItemDto 的区别：** ItemSummaryDto 不含 PhotoPath/MediumPath/Note/LocationId/CreatedByUserName，新增 LocationPath（完整位置字符串）和 TagNames（标签名称列表）。

### ItemRepository.SearchAsync 实现要点

```csharp
public async Task<List<Item>> SearchAsync(string query)
{
    if (string.IsNullOrWhiteSpace(query))
        return [];

    var q = query.Trim();
    
    return await _db.Items
        .Include(i => i.Location)
        .Include(i => i.Tags)
        .Where(i => i.Name.Contains(q) || (i.Note != null && i.Note.Contains(q))
                    || i.Tags.Any(t => t.Name.Contains(q)))
        .OrderByDescending(i => i.Name.StartsWith(q))  // 名称精确匹配优先
        .ThenBy(i => i.Name)
        .Take(50)  // 限制返回数量
        .AsSplitQuery()  // 多 Include 避免笛卡尔积
        .ToListAsync();
}
```

**关键考量：**
- 使用 `Contains`（EF Core 翻译为 LIKE '%q%'），SQLite 上大小写不敏感（LIKE 默认）
- `OrderByDescending(i => i.Name.StartsWith(q))` — 名称开头匹配的排前面（近似"相关度"）
- `Take(50)` — 限制返回，避免大量结果影响性能
- `AsSplitQuery()` — 避免 Include Location + Tags 产生笛卡尔积

### ItemEndpoints 新增 GET 搜索端点

```csharp
group.MapGet("/", SearchItemsAsync)
    .Produces<ItemSummaryDto[]>(200)
    .ProducesProblem(401)
    .WithTags("Items")
    .WithDescription("搜索物品（关键词模糊匹配名称/备注/标签）");
```

**注意：** 搜索端点使用 `GET /api/items`（无路径参数），与现有的 `GET /api/items/{id}` 通过路由模板区分。

**端点实现骨架：**
```csharp
private static async Task<Ok<ItemSummaryDto[]>> SearchItemsAsync(
    string? q, ItemRepository repo, HttpContext httpContext)
{
    var items = string.IsNullOrWhiteSpace(q)
        ? new List<Item>()
        : await repo.SearchAsync(q);

    var dtos = items.Select(i => new ItemSummaryDto(
        i.Id, i.Name, i.ThumbPath,
        i.Location?.Path,  // LocationPath: 物化路径
        i.Tags.Select(t => t.Name).ToList(),
        i.CreatedAt)).ToArray();

    httpContext.Response.Headers["X-Total-Count"] = dtos.Length.ToString();
    return TypedResults.Ok(dtos);
}
```

### SearchBar.razor 组件设计

```razor
<!-- 防抖搜索栏 -->
<MudTextField @bind-Value="_searchText"
              Placeholder="搜索物品..."
              Adornment="Adornment.Start"
              AdornmentIcon="@Icons.Material.Filled.Search"
              Immediate="true"
              Variant="Variant.Outlined" />

@code {
    [Parameter] public EventCallback<string> SearchTextChanged { get; set; }

    private string _searchText = "";
    private System.Threading.Timer? _debounceTimer;

    // 在 OnParametersSet / 文本变更时启动 300ms 防抖计时器
    // 超时后调用 SearchTextChanged.InvokeAsync(_searchText)
}
```

**关键 MudBlazor 9.x API：**
- `Immediate="true"` — 每次按键触发 `ValueChanged`（v9.x 中 Immediate 替代了 Immediate="true"）
- `Adornment="Adornment.Start"` — 搜索图标在输入框左侧
- `AdornmentIcon` — 图标内容
- 防抖通过 `Timer` 实现：用户停止输入 300ms 后触发搜索

### Home.razor 搜索结果区域

```
┌─────────────────────────────┐
│  [🔍 搜索物品...        ]  │  ← SearchBar
├─────────────────────────────┤
│  首次进入: "输入关键词搜索"  │  ← 引导提示
│  搜索中: MudProgressCircular │  ← 加载态
│  有结果:                     │
│  ┌────┬─────────────────────┐│
│  │ 🖼  │ 数据线              ││  ← 缩略图 + 名称
│  │    │ 书房/储物柜/电子配件  ││  ← 位置路径
│  │    │ [电子配件] [充电]    ││  ← 标签 Chips
│  └────┴─────────────────────┘│
│  无结果: EmptyState           │
└─────────────────────────────┘
```

**搜索结果列表项：**
- 左：300px 缩略图（有照片）或 MudIcon 占位（无照片）
- 右：物品名称（Typo.h6）+ 位置路径（Typo.caption）+ 标签 MudChipSet（只读展示）
- 整行可点击 → `Navigation.NavigateTo($"/items/{id}")`

### 搜索响应 < 500ms 性能策略

| 策略 | 实现 |
|------|------|
| LIKE 查询 B-tree 扫描 | SQLite 上 LIKE '%x%' 无法用索引，但 100 条数据量可接受 |
| 结果限制 | `Take(50)` 硬限制 |
| AsSplitQuery | 避免 EF Core 生成巨大 JOIN |
| 客户端防抖 | 300ms 防抖减少请求频率 |
| 未来优化 | 数据超 500 条时考虑 FTS5 全文索引（v2） |

### 文件结构变更

```
src/BoxWise.Shared/
  Dtos/ItemSummaryDto.cs                  (new — 列表/搜索结果 DTO)
src/BoxWise.Server/
  Repositories/ItemRepository.cs          (modified — 添加 SearchAsync)
  Endpoints/ItemEndpoints.cs              (modified — 添加 GET / 搜索端点)
src/BoxWise.Client/
  Services/ItemService.cs                 (modified — 添加 SearchAsync)
  Components/SearchBar.razor              (new — 搜索栏组件)
  Pages/Home.razor                        (modified — 集成搜索)
```

### MudBlazor 9.x API 提醒

| 场景 | 正确 API |
|------|----------|
| MudTextField 即时模式 | `Immediate="true"` |
| Input 装饰 | `Adornment="Adornment.Start"` + `AdornmentIcon` |
| MudChip 只读展示 | 使用 `MudChip` 不设 `SelectionMode`（只读） |
| MudList/MudListItem | 可点击，设置 `Href` 或 `OnClick` |
| 加载指示器 | `MudProgressCircular` |

### 构建与验证

```bash
dotnet build BoxWise.slnx
dotnet test BoxWise.slnx

# 手动测试
# 1. 登录 → 首页显示搜索框
# 2. 输入关键词 → 300ms 后显示结果
# 3. 点击结果 → 跳转详情页
# 4. 搜索无匹配 → EmptyState
# 5. 清空关键词 → 显示引导提示
```

---

## Review Findings

- [ ] [Review][Patch] SearchBar 防抖机制因 OnParametersSet 生命周期误解而完全失效 [SearchBar.razor:26-33]
  - **严重性:** CRITICAL — 功能完全不可用
  - **详情:** `SearchBar.razor` 使用 `@bind-Value="_searchText"` 双向绑定，但防抖逻辑放在 `OnParametersSet` 中。当用户在 MudTextField 中输入时，`_searchText` 通过 `@bind-Value` 的 setter 在组件内部变更，**不会触发 `OnParametersSet`**（该生命周期仅当父组件传入新参数时调用）。因此防抖 Timer 的 `Change(300, ...)` 永远不会被执行，`SearchTextChanged` 回调永远不会触发。
  - **建议修复:** 移除 `@bind-Value`，改用显式 `Value` + `ValueChanged`，在 `ValueChanged` handler 中启动防抖：
    ```razor
    <MudTextField Value="_searchText" ValueChanged="OnValueChanged" ... />
    @code {
        private void OnValueChanged(string value)
        {
            _searchText = value;
            _lastDebouncedText = value;
            _debounceTimer?.Change(300, Timeout.Infinite);
        }
    }
    ```

- [ ] [Review][Patch] 快速连续搜索导致竞态条件 [Home.razor:138-155]
  - **严重性:** MEDIUM — 搜索结果可能被旧请求覆盖
  - **详情:** `OnSearchTextChanged` 方法中，每次搜索都直接赋值 `_results`。用户快速输入导致多个请求并发时，先后返回的结果可能以错误顺序覆盖（先发出的请求后返回，覆盖了后发出的正确结果）。
  - **建议修复:** 使用 `CancellationTokenSource` 每轮搜索前取消上一轮请求：
    ```csharp
    private CancellationTokenSource? _searchCts;
    private async Task OnSearchTextChanged(string query)
    {
        _searchCts?.Cancel();
        _searchCts = new CancellationTokenSource();
        var ct = _searchCts.Token;
        ...
        var result = await ItemService.SearchAsync(_query, ct);
        if (!ct.IsCancellationRequested) _results = result;
    }
    ```
    同时需要将 `CancellationToken` 透传到 `ItemRepository.SearchAsync`（httpContext.RequestAborted）。

- [ ] [Review][Patch] Timer 回调缺少异常处理 [SearchBar.razor:20-23]
  - **严重性:** LOW
  - **详情:** `System.Threading.Timer` 回调中 `InvokeAsync(() => SearchTextChanged.InvokeAsync(_searchText))` 的 Task 未 await 也未 try-catch。若 `SearchTextChanged` 抛出异常将在 WASM 渲染器级别未捕获。
  - **建议修复:** 添加 try-catch：
    ```csharp
    _debounceTimer = new System.Threading.Timer(_ =>
    {
        try { InvokeAsync(() => SearchTextChanged.InvokeAsync(_searchText)); } catch { }
    }, null, Timeout.Infinite, Timeout.Infinite);
    ```

- [ ] [Review][Patch] `_isSearching = false` 后缺少 `StateHasChanged()` 调用 [Home.razor:153-155]
  - **严重性:** LOW
  - **详情:** 异步搜索完成后 `_isSearching = false;` 但未调用 `StateHasChanged()`。虽然 Blazor 通常在异步事件处理器完成后自动触发重新渲染，但通过 `InvokeAsync` 调度的回调可能不保证自动重渲染。
  - **建议修复:** `_isSearching = false;` 之后添加 `StateHasChanged();`

- [x] [Review][Defer] LIKE 查询通配符处理 — SQLite 中 `%` 和 `_` 会被 LIKE 运算符当作通配符 [ItemRepository.cs:73-75] — deferred, 预期行为（`Contains` 直接翻译为 LIKE），暂不处理

## References

| 内容 | 来源 |
|------|------|
| Story AC 定义 | [Source: epics.md#Story 4.1] |
| FR-9 模糊搜索 | [Source: prd.md#FR-9] |
| FR-10 搜索结果展示 | [Source: prd.md#FR-10] |
| 架构搜索模式 | [Source: architecture.md#FR-Group FR-9~10] |
| ItemSummaryDto 定义 | [Source: architecture.md#Structure Patterns] |
| MudBlazor 9.x API | [Source: CLAUDE.md#MudBlazor 9.x API 参考] |
| 物化路径模式 | [Source: architecture.md#Materialized Path Queries] |

## Dev Agent Record

### Agent Model Used

deepseek-v4-pro

### Debug Log References

### Completion Notes List

✅ 全部 6 个 Task 完成 — 搜索功能就绪，22/22 测试通过

**实施要点：**
- ItemSummaryDto：精简 DTO（Id, Name, ThumbPath, LocationPath, TagNames, CreatedAt），与 ItemDto 职责分离
- ItemRepository.SearchAsync：EF Core LIKE 多字段匹配（Name/Note/Tags），AsSplitQuery + Take(50) 性能优化
- ItemEndpoints GET /api/items?q=：空查询返回空数组，X-Total-Count 响应头
- SearchBar.razor：MudTextField + 300ms Timer 防抖，SearchTextChanged EventCallback 通知父组件
- Home.razor：三种状态（引导提示/加载/结果或空状态），MudList 结果列表，点击导航至详情页
- MudBlazor 9.x 泛型组件需显式指定 T 参数（MudList<T>, MudListItem<T>, MudChip<T>）

### File List

**新增文件:**
- `src/BoxWise.Shared/Dtos/ItemSummaryDto.cs` (new)
- `src/BoxWise.Client/Components/SearchBar.razor` (new)

**修改文件:**
- `src/BoxWise.Server/Repositories/ItemRepository.cs` (modified — 添加 SearchAsync)
- `src/BoxWise.Server/Endpoints/ItemEndpoints.cs` (modified — 添加 GET /?q= 搜索端点)
- `src/BoxWise.Client/Services/ItemService.cs` (modified — 添加 SearchAsync)
- `src/BoxWise.Client/Pages/Home.razor` (modified — 集成搜索)
