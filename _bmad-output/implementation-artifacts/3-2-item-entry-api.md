# Story 3.2: 物品录入 API + 位置分配 + 入库保存

Status: review

## Story

As a 用户，
I want 填写物品信息并保存，
so that 物品记录生成，进入家庭物品库。

## Acceptance Criteria

1. **AC-1: 创建物品** — 已登录用户 `POST /api/items` 传入 name, locationId, tagIds, note，返回 201 + `ItemDto`
2. **AC-2: 名称为空** — name 为空时返回 400 ProblemDetails
3. **AC-3: 位置无效** — locationId 无效或为空时返回 400
4. **AC-4: 录入者标识** — `CreatedByUserId` 自动设为当前登录用户 ID，`CreatedAt` 设为 UTC 时间
5. **AC-5: 标签关联** — 传入的 tagIds 中的标签自动关联到物品，不存在的 tagId 返回 400

## Tasks / Subtasks

- [x] Task 1: 创建 ItemRepository (AC: #1-#5)
  - [x] 1.1 `src/BoxWise.Server/Repositories/ItemRepository.cs` — `CreateAsync(CreateItemRequest, userId)`
  - [x] 1.2 校验 name 非空 + Trim + Length ≤ 200
  - [x] 1.3 校验 locationId 非 null 且 Location 存在
  - [x] 1.4 校验 tagIds 全部存在（不可自动创建——自动创建在 Story 3.4 通过 GetOrCreateAsync）
  - [x] 1.5 设置 CreatedByUserId = userId, CreatedAt = DateTime.UtcNow

- [x] Task 2: 创建 ItemEndpoints (AC: #1)
  - [x] 2.1 `src/BoxWise.Server/Endpoints/ItemEndpoints.cs` — RouteGroupBuilder `/api/items`
  - [x] 2.2 `POST /api/items` → 返回 201 + `ItemDto`
  - [x] 2.3 从 `HttpContext.User` 获取当前用户 ID（`userManager.GetUserId`）
  - [x] 2.4 所有端点添加 `.Produces*()` + `.ProducesProblem(401)` 注解

- [x] Task 3: 创建 CreateItemRequest DTO (AC: #1)
  - [x] 3.1 `src/BoxWise.Shared/Dtos/CreateItemRequest.cs` — record: `string Name, int LocationId, List<int> TagIds, string? Note`

- [x] Task 4: 注册 DI + 端点 (AC: #1-#5)
  - [x] 4.1 `Program.cs` 注册 `ItemRepository` 为 Scoped
  - [x] 4.2 映射端点：`app.MapItemEndpoints()`

- [x] Task 5: 单元测试 (AC: #1-#5)
  - [x] 5.1 ItemRepository 测试：正常创建、空名、无效 locationId、标签关联、CreatedByUserId 赋值
  - [x] 5.2 `dotnet test` 全部通过

- [x] Task 6: 构建验证 + E2E 测试 (AC: #1-#5)
  - [x] 6.1 `dotnet build BoxWise.slnx` 零错误零警告
  - [x] 6.2 `POST /api/items` 创建物品 → 201 + ItemDto
  - [x] 6.3 空 name → 400
  - [x] 6.4 无效 locationId → 400
  - [x] 6.5 不存在的 tagId → 400

---

## Dev Notes

### 前置上下文

- **Story 3.1 完成:** Item 实体 + ItemTag 多对多 + 图片上传管线就绪
- **Item 实体:** Id, Name, Note, PhotoPath, ThumbPath, MediumPath, LocationId, CreatedByUserId, CreatedAt
- **ItemTag 中间表:** EF Core `UsingEntity("ItemTag")` 自动生成
- **Tag 系统:** `TagRepository.GetOrCreateAsync` 已有（Story 2.3）
- **Location 系统:** Location CRUD + 浏览 API 已有（Story 2.1+2.2）

### Story 3.1 关键学习

1. **Item.Location.DeleteBehavior = SetNull** — 删除 Location 时 Item 的 LocationId 变 null
2. **Item.CreatedByUser.DeleteBehavior = Restrict** — 不允许删除有物品的用户
3. **ItemTag 多对多** — 中间表名 "ItemTag"，列 ItemId + TagsId
4. **PhotoPath/ThumbPath/MediumPath** — 由图片上传管线填充，Item 创建时不设置

### 关键架构约束

- **Repository 模式** — 遵循 LocationRepository/TagRepository 约定
- **错误返回用 `TypedResults.Problem()`** — 直接返回，不嵌套
- **所有端点加 `.ProducesProblem(401)`** — 标准模式
- **DTO 用 record 类型** — 放在 `BoxWise.Shared.Dtos`
- **Entity → DTO 映射在端点层** — Repository 返回 Entity

### ItemRepository 核心逻辑

```csharp
public class ItemRepository
{
    private readonly AppDbContext _db;

    public ItemRepository(AppDbContext db) => _db = db;

    public async Task<Item> CreateAsync(string name, int locationId, List<int> tagIds, string? note, string userId)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("物品名称不能为空");
        name = name.Trim();
        if (name.Length > 200)
            throw new ArgumentException("物品名称不能超过 200 个字符");

        var locationExists = await _db.Locations.AnyAsync(l => l.Id == locationId);
        if (!locationExists)
            throw new ArgumentException("位置不存在");

        var tags = await _db.Tags.Where(t => tagIds.Contains(t.Id)).ToListAsync();
        if (tags.Count != tagIds.Count)
            throw new ArgumentException("部分标签不存在");

        var item = new Item
        {
            Name = name,
            LocationId = locationId,
            Note = note?.Trim(),
            CreatedByUserId = userId,
            CreatedAt = DateTime.UtcNow,
            Tags = tags
        };

        _db.Items.Add(item);
        await _db.SaveChangesAsync();
        return item;
    }
}
```

### ItemEndpoints 设计

```csharp
group.MapPost("/", CreateItemAsync)
    .Produces<ItemDto>(201)
    .ProducesProblem(400)
    .ProducesProblem(401)
    .WithTags("Items")
    .WithDescription("创建物品");
```

**获取当前用户 ID:**
```csharp
var userId = userManager.GetUserId(httpContext.User)
    ?? throw new InvalidOperationException("无法获取当前用户");
```

### DTO

```csharp
public record CreateItemRequest(string Name, int LocationId, List<int> TagIds, string? Note);
```

### 文件结构变更

```
src/BoxWise.Server/
  Repositories/ItemRepository.cs    (new)
  Endpoints/ItemEndpoints.cs        (new)
  Program.cs                        (modified — DI + 端点映射)
src/BoxWise.Shared/Dtos/
  CreateItemRequest.cs              (new)
src/BoxWise.Server.Tests/
  Repositories/ItemRepositoryTests.cs (new)
```

**无迁移** — Item 表已在 Story 3.1 创建，本 Story 仅添加业务逻辑。

### 构建与验证

```bash
# 1. 构建
dotnet build BoxWise.slnx

# 2. 测试
dotnet test BoxWise.slnx

# 3. E2E
dotnet run
curl -k -b cookies.txt -X POST https://localhost:5000/api/items \
  -H "Content-Type: application/json" \
  -d '{"name":"螺丝刀套装","locationId":1,"tagIds":[1,2],"note":"蓝色手柄"}'
# 预期: 201 + ItemDto (含 CreatedByUserName, CreatedAt)

# 空名称
curl -k -b cookies.txt -X POST https://localhost:5000/api/items \
  -H "Content-Type: application/json" \
  -d '{"name":"","locationId":1,"tagIds":[],"note":""}'
# 预期: 400
```

### 关键风险点

1. **标签验证批量查询** — `Where(t => tagIds.Contains(t.Id))` 对空 tagIds 返回空列表，正常
2. **CreatedByUserId 类型** — `AppUser.Id` 是 `string`（IdentityUser 默认 GUID），从 `UserManager.GetUserId()` 获取
3. **ItemDto.CreatedByUserName** — 需要加载 `CreatedByUser` 导航属性（`.Include(i => i.CreatedByUser)`）
4. **ItemDto 已有** — Story 3.1 已定义，无需新建（确认字段覆盖：Id, Name, Note, PhotoPath, ThumbPath, MediumPath, LocationId, CreatedByUserName, CreatedAt）

---

## References

| 内容 | 来源 |
|------|------|
| Story AC 定义 | [Source: epics.md#Story 3.2] |
| FR-6 入库保存 | [Source: prd.md#FR-6] |
| FR-4 位置分配、FR-20 录入者标识 | [Source: prd.md#FR-4, FR-20] |
| Item 实体定义 | [Source: Story 3.1: Item.cs] |
| ItemDto 定义 | [Source: Story 3.1: ItemDto.cs] |
| TagRepository.GetOrCreateAsync | [Source: Story 2.3: TagRepository.cs] |
| LocationRepository 模式 | [Source: Story 2.1: LocationRepository.cs] |
| TypedResults 模式 | [Source: Story 2.2 Code Review] |

## Dev Agent Record

### Agent Model Used

deepseek-v4-pro

### Debug Log References

- InMemory 不支持 `Include(i => i.CreatedByUser)` 的多对多导航属性加载 → 移除 GetByIdAsync，改为 `Reference().LoadAsync()`

**代码审查修复记录:**
- 🔴 TagIds null → 500 — 添加 `tagIds ??= []` + `Distinct()` 去重
- 🟡 创建流程两次查询 — `CreateAsync` 使用 `Reference(i => i.CreatedByUser).LoadAsync()`，端点直接使用返回的 item
- 🟡 不必要 `Include(Location/Tags)` — 移除 GetByIdAsync，简化 Repository
- 🟡 Note 长度无校验 — 添加 `note.Length > 2000` 验证

### Completion Notes List

✅ **全部 6 个任务完成** — 物品录入 API 就绪，23/23 测试通过

**实施要点：**
- ItemRepository：CreateAsync + GetByIdAsync（含 Include 导航属性）
- ItemEndpoints：POST /api/items → 201 + ItemDto
- CreatedByUserId 从 UserManager.GetUserId 自动获取
- 标签关联批量验证：所有 tagIds 必须存在

### File List

**新增文件:**
- `src/BoxWise.Server/Repositories/ItemRepository.cs` (new)
- `src/BoxWise.Server/Endpoints/ItemEndpoints.cs` (new)
- `src/BoxWise.Shared/Dtos/CreateItemRequest.cs` (new)
- `src/BoxWise.Server.Tests/Repositories/ItemRepositoryTests.cs` (new)

**修改文件:**
- `src/BoxWise.Server/Program.cs` (modified) — DI + 端点映射
### Debug Log References

### Completion Notes List

### File List
