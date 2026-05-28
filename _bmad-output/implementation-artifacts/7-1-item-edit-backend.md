# Story 7.1: 物品编辑 — 后端 DTO + Repository + 端点

Status: done

baseline_commit: 2b011bdb1ab8701a91bc7f88f8e9e2e6f0fef5b8

## Story

As a 用户，
I want 通过 API 编辑已有物品的名称、位置、标签和备注，
so that 可以在不删除重建的前提下修正录入错误或更新收纳位置。

## Acceptance Criteria

**AC-1: DTO 定义**
- 新建 `src/BoxWise.Shared/Dtos/UpdateItemRequest.cs`，positional record，字段：`string Name, int LocationId, List<int> TagIds, string? Note`
- 与 `CreateItemRequest` 字段一致

**AC-2: Repository UpdateAsync — 成功更新**
- `ItemRepository.UpdateAsync(int id, string name, int locationId, List<int> tagIds, string? note)` 返回 `Item?`
- 更新名称、位置、标签、备注后 `SaveChangesAsync`，返回含导航属性（Location、Tags、CreatedByUser）的 Item
- Note 空字符串统一存 null

**AC-3: Repository UpdateAsync — Tags 多对多安全更新**
- **必须** `.Include(i => i.Tags)` 加载现有集合
- **必须** 使用 `item.Tags.Clear()` + `item.Tags.AddRange(newTags)`
- **禁止** 直接赋值 `item.Tags = newList`（EF Core 隐式连接表陷阱，会产生僵尸 ItemTag 记录）

**AC-4: Repository UpdateAsync — 校验**
- 物品不存在 → 返回 null（端点映射为 404）
- 名称为空/空白 → `ArgumentException`
- 名称超过 200 字符 → `ArgumentException`
- 备注超过 2000 字符 → `ArgumentException`
- 位置不存在 → `ArgumentException`
- Tag 不存在（IDs 数量不匹配）→ `ArgumentException`（严格模式，与 CreateAsync 一致）

**AC-5: PUT 端点**
- `PUT /api/items/{id}` 接收 `UpdateItemRequest` body
- 成功 → `200 Ok<ItemDto>`（含完整 LocationPath、TagNames、CreatedByUserName）
- 物品不存在 → 404
- 校验失败 → 400 ProblemDetails
- **必须** 标注 `.ProducesProblem(401)`

**AC-6: 端点 handler 签名与模式**
- `private static async Task<Results<Ok<ItemDto>, NotFound, ProblemHttpResult>> UpdateItemAsync(...)`
- 遵循现有 `CreateItemAsync` / `GetItemByIdAsync` 的 DTO 映射模式
- 使用 `TypedResults.Ok()` / `TypedResults.NotFound()` / `TypedResults.Problem()`

**AC-7: 编译与现有测试**
- `dotnet build` 零警告（TreatWarningsAsErrors）
- 现有 34 个测试全部通过

## Tasks / Subtasks

- [x] Task 1: 创建 UpdateItemRequest DTO (AC: 1)
  - [x] 1.1 新建 `src/BoxWise.Shared/Dtos/UpdateItemRequest.cs`
  - [x] 1.2 Positional record: `string Name, int LocationId, List<int> TagIds, string? Note`

- [x] Task 2: 实现 ItemRepository.UpdateAsync (AC: 2, 3, 4)
  - [x] 2.1 空值防御：`tagIds ??= []; tagIds = tagIds.Distinct().ToList();`（与 CreateAsync 一致，防 NRE）
  - [x] 2.2 参数校验：name 空白 → ArgumentException, name.Length > 200 → ArgumentException
  - [x] 2.3 参数校验：note?.Length > 2000 → ArgumentException
  - [x] 2.4 校验 locationId 存在，不存在 → ArgumentException
  - [x] 2.5 校验 tagIds 全部存在（tags.Count != tagIds.Count → ArgumentException）
  - [x] 2.6 `.Include(i => i.Location).Include(i => i.CreatedByUser).Include(i => i.Tags)` 一次加载所有导航属性，不存在 → return null
  - [x] 2.7 更新字段：Name = name.Trim(), LocationId, Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim()
    （Note 空字符串统一存 null；同时修正 CreateAsync 的 Note 处理保持一致）
  - [x] 2.8 `item.Tags.Clear()` + `foreach (var tag in tags) item.Tags.Add(tag)` (ICollection<Tag> 不支持 AddRange), SaveChangesAsync 后 return item

- [x] Task 3: 添加 PUT 端点 + Client ItemService (AC: 5, 6)
  - [x] 3.1 `group.MapPut("/{id:int}", UpdateItemAsync)` + `.Produces*()` 注解
  - [x] 3.2 实现 `UpdateItemAsync` handler（private static, TypedResults 模式）
  - [x] 3.3 DTO 映射复用 GetItemByIdAsync 的映射模式（含 LocationPath 解析）
  - [x] 3.4 异常映射：ArgumentException → 400, null → 404
  - [x] 3.5 Client `ItemService.UpdateAsync` → `Task<ItemDto?>`（避免二次 GET）

### Review Findings

- [x] [Review][Decision] LocationId DTO 非空 vs 实体可空 — **保持现状**：位置必填是产品规则（FR-4），非空位置不能删除（已有校验），场景在正常操作中不会发生
- [x] [Review][Patch] UpdateAsync 缺少 CancellationToken — **已修复**：`UpdateAsync` 和 `UpdateItemAsync` 均已添加 `CancellationToken` 参数
- [x] [Review][Defer] 无并发控制（最后写入胜出） — `Item` 实体无并发令牌，多用户同时编辑同一物品会静默覆盖。v1：≤5 用户、编辑频率低，可接受
- [x] [Review][Defer] 无编辑人/修改时间追踪 — `Item` 实体无 `UpdatedByUserId`/`UpdatedAt` 字段。v1：PRD 未要求编辑人追踪，全员同权
- [x] [Review][Defer] 空 Tag 列表清空所有标签 — 与 `CreateAsync` 行为一致。若需强制标签应作为产品决策统一处理

## Dev Notes

### 现有模式 — 端点 handler（必须遵循）

当前 `ItemEndpoints.cs` 模式（来源：`src/BoxWise.Server/Endpoints/ItemEndpoints.cs`）：

```csharp
private static async Task<Results<Created<ItemDto>, ProblemHttpResult>>
    CreateItemAsync(CreateItemRequest request, ItemRepository repo,
        UserManager<AppUser> userManager, HttpContext httpContext,
        LocationRepository locationRepo)
{
    try
    {
        // ... 业务逻辑 ...
        return TypedResults.Created($"/api/items/{dto.Id}", dto);
    }
    catch (ArgumentException ex)
    {
        return TypedResults.Problem(ex.Message, statusCode: 400);
    }
}
```

**注意事项：**
- handler 为 `private static`，不使用实例方法
- 通过方法参数注入依赖（不是构造函数注入）
- DTO 映射在端点层完成（Entity → DTO），不在 Repository 层
- `LocationRepository.ResolvePathNamesAsync(path)` 解析物化路径为可读名称

### 现有模式 — Repository（必须遵循）

`ItemRepository.CreateAsync` 校验模式（来源：`src/BoxWise.Server/Repositories/ItemRepository.cs:16-53`）：
- `tagIds ??= []; tagIds = tagIds.Distinct().ToList();`
- 名称 `IsNullOrWhiteSpace` → `ArgumentException("物品名称不能为空")`
- `name = name.Trim(); name.Length > 200` → `ArgumentException("物品名称不能超过 200 个字符")`
- `note?.Length > 2000` → `ArgumentException("备注不能超过 2000 个字符")`
- 位置存在性：`await _db.Locations.AnyAsync(l => l.Id == locationId)`
- Tag 存在性：`await _db.Tags.Where(t => tagIds.Contains(t.Id)).ToListAsync()` + `tags.Count != tagIds.Count`
- 加载导航属性（返回前）：`_db.Entry(item).Reference(i => i.Location).LoadAsync()` 等

### Tags 多对多陷阱（🔥 关键）

EF Core many-to-many 通过隐式 `ItemTag` 连接表实现。更新 Tags 时：
```csharp
// ✅ 正确：一次 Include 加载所有导航属性，减少查询
var item = await _db.Items
    .Include(i => i.Location)
    .Include(i => i.CreatedByUser)
    .Include(i => i.Tags)
    .FirstOrDefaultAsync(i => i.Id == id);
item.Tags.Clear();
item.Tags.AddRange(newTags);
await _db.SaveChangesAsync();

// ❌ 错误 — 产生僵尸 ItemTag 记录
item.Tags = newTags;
```

### DTO 映射模式

编辑端点的 DTO 映射与 `GetItemByIdAsync` 完全一致（Item → ItemDto）。复制以下逻辑：

```csharp
var locationPath = item.Location?.Path is not null
    ? await locationRepo.ResolvePathNamesAsync(item.Location.Path)
    : null;

var dto = new ItemDto(
    item.Id, item.Name, item.Note,
    item.PhotoPath, item.ThumbPath, item.MediumPath,
    item.LocationId, item.Location?.Name, locationPath,
    item.Tags.Select(t => t.Name).ToList(),
    item.CreatedByUser?.UserName ?? "",
    item.CreatedAt);
```

### Client ItemService

Story 7-1 的 Client 端变更极小——仅在 `src/BoxWise.Client/Services/ItemService.cs` 新增 `UpdateAsync` 方法：

```csharp
public async Task<ItemDto?> UpdateAsync(int id, UpdateItemRequest request,
    CancellationToken cancellationToken = default)
{
    var response = await _http.PutAsJsonAsync($"api/items/{id}", request, cancellationToken);
    if (!response.IsSuccessStatusCode) return null;
    return await response.Content.ReadFromJsonAsync<ItemDto>(cancellationToken);
}
```

返回 `ItemDto?` 而非 `bool`，`ItemDetail.razor` 可直接使用避免二次 GET。

### 测试标准

- xUnit + EF Core InMemory + `TestDbContextFactory.Create()`（GUID 命名隔离）
- Repository 测试覆盖：happy path + not-found + 每个校验点
- 参照现有 `ItemRepositoryTests` 模式（`src/BoxWise.Server.Tests/Repositories/`）

### 照片

照片 **不在编辑范围内**。`UpdateAsync` 不涉及 PhotoPath/ThumbPath/MediumPath。照片替换通过删除→重新录入。

### 文件清单

| 操作 | 文件 |
|------|------|
| **新建** | `src/BoxWise.Shared/Dtos/UpdateItemRequest.cs` |
| **修改** | `src/BoxWise.Server/Repositories/ItemRepository.cs` |
| **修改** | `src/BoxWise.Server/Endpoints/ItemEndpoints.cs` |
| **修改** | `src/BoxWise.Client/Services/ItemService.cs` |

### 参考

- Sprint Change Proposal: `_bmad-output/planning-artifacts/sprint-change-proposal-2026-05-28.md`
- PRD §6.2（编辑功能已从 Out of Scope 移入 In Scope）
- Architecture: `_bmad-output/planning-artifacts/architecture.md` — API Pattern, Error Handling, EF Core Patterns
- Project Context: `_bmad-output/project-context.md` — Naming, DI, Testing, Minimal API 规范

## Dev Agent Record

### Agent Model Used

Claude (via Claude Code)

### Debug Log References

### Completion Notes List

- Task 1: 新建 `UpdateItemRequest` DTO（与 CreateItemRequest 字段一致）
- Task 2: `ItemRepository.UpdateAsync` — 完整校验 + Include 链式加载 + Tags foreach 添加 + 同步修正 CreateAsync Note 空字符串处理
- Task 3: `PUT /api/items/{id}` 端点 + Client `ItemService.UpdateAsync` → `Task<ItemDto?>`
- 实现细节：`ICollection<Tag>` 不支持 `AddRange`，改用 `foreach` 逐条添加
- 编译：0 警告 | 测试：190/190 通过

### File List

| 操作 | 文件 |
|------|------|
| **新建** | `src/BoxWise.Shared/Dtos/UpdateItemRequest.cs` |
| **修改** | `src/BoxWise.Server/Repositories/ItemRepository.cs` |
| **修改** | `src/BoxWise.Server/Endpoints/ItemEndpoints.cs` |
| **修改** | `src/BoxWise.Client/Services/ItemService.cs` |
