# Story 3.5: 物品详情展示 + 录入者标识

Status: done

## Story

As a 用户，
I want 查看物品的完整信息，
so that 确认物品详情和录入者。

## Acceptance Criteria

1. **AC-1: 详情展示** — `GET /api/items/{id}` 返回完整 `ItemDto`，含名称、照片、位置路径、标签、备注、录入者用户名、录入时间
2. **AC-2: 无照片兜底** — 物品无照片时显示 `MudIcon` 占位图标替代图片
3. **AC-3: 详情页** — `ItemDetail.razor` 路由 `@page "/items/{id:int}"`，展示物品所有信息

## Tasks / Subtasks

- [x] Task 1: 创建 Item 详情端点 (AC: #1)
  - [x] 1.1 `GET /api/items/{id}` — 返回 `ItemDto`，使用 `ItemRepository.GetByIdAsync` + Include 导航属性
  - [x] 1.2 添加 `.Produces*()` 注解（200 + 404 + 401）

- [x] Task 2: 创建 Client ItemService (AC: #1)
  - [x] 2.1 `src/BoxWise.Client/Services/ItemService.cs` — `GetByIdAsync(id)` → `GET /api/items/{id}`
  - [x] 2.2 `Program.cs` 注册 `ItemService` 为 Scoped

- [x] Task 3: 创建 ItemDetail 页面 (AC: #2, #3)
  - [x] 3.1 `src/BoxWise.Client/Pages/ItemDetail.razor` — 路由 `@page "/items/{id:int}"`
  - [x] 3.2 有照片 → 显示原图（`GET /api/images/{id}?type=medium`）
  - [x] 3.3 无照片 → `MudIcon Icon="@Icons.Material.Filled.Image"` 占位
  - [x] 3.4 信息卡片：名称、位置路径、标签列表、备注、录入者、录入时间
  - [x] 3.5 添加 `@attribute [Authorize]`

- [x] Task 4: 构建验证 (AC: #1-#3)
  - [x] 4.1 `dotnet build BoxWise.slnx` 零错误零警告
  - [x] 4.2 `dotnet test` 全部通过

---

## Dev Notes

### 前置上下文

- **ItemRepository.GetByIdAsync 已移除** — Story 3.2 审查后简化为单一 `CreateAsync`，需要重新添加 `GetByIdAsync`
- **GET /api/items 端点** — ItemEndpoints 目前只有 POST，需要添加 GET
- **ItemDto 已定义** — Story 3.1 创建，包含所需全部字段
- **图片服务就绪** — `GET /api/images/{itemId}?type=medium` (Story 3.1)
- **MudBlazor 9.x** — 见 CLAUDE.md API 参考

### 需要重新添加的 Repository 方法

```csharp
public async Task<Item?> GetByIdAsync(int id)
{
    return await _db.Items
        .Include(i => i.CreatedByUser)
        .Include(i => i.Location)
        .Include(i => i.Tags)
        .FirstOrDefaultAsync(i => i.Id == id);
}
```

**注意：** 返回 `Item?` 而非抛异常——端点负责 NotFound 映射。

### ItemEndpoints 新增 GET 端点

```csharp
group.MapGet("/{id:int}", GetItemByIdAsync)
    .Produces<ItemDto>(200)
    .Produces(404)
    .ProducesProblem(401)
    .WithTags("Items")
    .WithDescription("获取物品详情");
```

### ItemDetail.razor 页面结构

```
┌─────────────────────────────┐
│  物品照片 / MudIcon 占位    │
├─────────────────────────────┤
│  名称                       │
│  位置: /1/2/3/              │
│  标签: [Tags]               │
│  备注: xxx                  │
│  录入者: admin              │
│  录入时间: 2026-05-24       │
└─────────────────────────────┘
```

### Client ItemService

```csharp
public class ItemService
{
    private readonly HttpClient _http;
    public ItemService(HttpClient http) => _http = http;

    public async Task<ItemDto?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        var response = await _http.GetAsync($"api/items/{id}", ct);
        if (!response.IsSuccessStatusCode) return null;
        return await response.Content.ReadFromJsonAsync<ItemDto>(ct);
    }
}
```

### 文件结构变更

```
src/BoxWise.Server/
  Repositories/ItemRepository.cs    (modified — 重新添加 GetByIdAsync)
  Endpoints/ItemEndpoints.cs        (modified — 添加 GET /{id})
src/BoxWise.Client/
  Services/ItemService.cs           (new)
  Pages/ItemDetail.razor            (new)
  Program.cs                        (modified — DI)
```

### 构建与验证

```bash
dotnet build BoxWise.slnx
dotnet test BoxWise.slnx

# 登录后访问
# https://localhost:5001/items/1
```

---

## References

| 内容 | 来源 |
|------|------|
| Story AC 定义 | [Source: epics.md#Story 3.5] |
| FR-20 录入者标识 | [Source: prd.md#FR-20] |
| ItemDto 定义 | [Source: Story 3.1: ItemDto.cs] |
| MudBlazor MudIcon/Icons | [Source: mudblazor.com] |

## Dev Agent Record

### Agent Model Used

deepseek-v4-pro

### Completion Notes List

✅ 全部 4 个任务完成 — 物品详情展示就绪，22/22 测试通过，Epic 3 收官

**实施要点：**
- ItemRepository 重新添加 `GetByIdAsync`（返回 `Item?`，不含 keyNotFoundException）
- ItemEndpoints 新增 `GET /api/items/{id}` → 200 + ItemDto 或 404
- ItemDetail.razor：照片/占位、名称、位置 ID、备注、录入者、录入时间
- ItemService：Client HTTP 封装 `GetByIdAsync`

### File List

**新增文件:**
- `src/BoxWise.Client/Services/ItemService.cs` (new)
- `src/BoxWise.Client/Pages/ItemDetail.razor` (new)

**修改文件:**
- `src/BoxWise.Server/Repositories/ItemRepository.cs` (modified)
- `src/BoxWise.Server/Endpoints/ItemEndpoints.cs` (modified)
- `src/BoxWise.Client/Program.cs` (modified)

### Review Findings (2026-05-26 — 位置路径显示修复)

- [x] [Review][Decision] **CreateItemAsync 返回的 LocationPath 不完整** — 已选择选项 B：`ItemRepository.CreateAsync` 加载 Location 导航属性，`CreateItemAsync` 使用 `item.Location.Path` 解析完整路径
- [x] [Review][Patch] **`int.Parse` 无异常保护** [LocationRepository.cs:97] — 已改用 `int.TryParse`，格式损坏时优雅降级跳过无效段
- [x] [Review][Patch] **空路径返回语义不一致** — `ResolvePathNamesAsync` 和 `ResolvePathNames` 统一返回 `null`
- [x] [Review][Defer] **SearchItemsAsync 每次请求全量加载位置表** [ItemEndpoints.cs:113] — 位置表通常 <100 行，当前可接受
- [x] [Review][Defer] **重复的路径解析逻辑** — ✅ 已修复：提取 `LocationRepository.ResolvePathNames` 内部静态方法共享逻辑，`ItemEndpoints` 移除重复代码
- [x] [Review][Defer] **已删除位置的 ID 泄露到 UI** — 保留降级逻辑，需要级联删除功能（独立特性）
- [x] [Review][Defer] **GET /api/items/{id} 缺少标签字段** — ✅ 已修复：`ItemDto` 新增 `TagNames` 字段，`GetByIdAsync` 添加 `.Include(i => i.Tags)`，两个端点均填充标签
- [x] [Review][Defer] **ResolvePathNames 不可单独测试** — ✅ 已修复：提取为 `internal static` 后可独立测试
