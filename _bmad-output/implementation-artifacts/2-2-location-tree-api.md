# Story 2.2: 位置树浏览 API

Status: review

## Story

As a 用户，
I want 按层级浏览位置树，
so that 可以看到完整收纳结构。

## Acceptance Criteria

1. **AC-1: 完整位置列表** — 已登录用户 `GET /api/locations` 返回所有位置的扁平列表，每项含 Id, Name, Path, ParentId, SortOrder，按 SortOrder + Name 排列
2. **AC-2: 子节点查询** — 已登录用户 `GET /api/locations/{id}/children` 返回指定节点的直接子节点列表，按 SortOrder + Name 排列
3. **AC-3: 无效节点 ID** — 查询不存在的节点 ID 的子节点时返回 404

## Tasks / Subtasks

- [x] Task 1: 扩展 LocationRepository — 添加查询方法 (AC: #1, #2)
  - [x] 1.1 `GetAllAsync()` — 返回所有位置的扁平列表（`OrderBy(l => l.SortOrder).ThenBy(l => l.Name)`）
  - [x] 1.2 `GetChildrenAsync(id)` — 验证节点存在 → 返回 `ParentId == id` 的子节点（按 SortOrder + Name 排序）
  - [x] 1.3 遵循 Story 2.1 Repository 模式：`KeyNotFoundException` 用于不存在资源

- [x] Task 2: 扩展 LocationEndpoints — 添加 GET 端点 (AC: #1, #2, #3)
  - [x] 2.1 `GET /api/locations` → `GetAllLocationsAsync` — 返回 `List<LocationDto>`，Status 200
  - [x] 2.2 `GET /api/locations/{id}/children` → `GetChildrenAsync` — 返回 `List<LocationDto>`，Status 200 或 404
  - [x] 2.3 所有端点添加 `.Produces*()` OpenAPI 注解（遵循 Story 2.1 审查后模式）
  - [x] 2.4 端点注册在现有 `MapLocationEndpoints()` 方法中（扩展已有 RouteGroup）

- [x] Task 3: 构建验证 + 端到端测试 (AC: #1-#3)
  - [x] 3.1 `dotnet build BoxWise.slnx` 零错误零警告
  - [x] 3.2 启动 Server，验证 `GET /api/locations` 返回所有位置
  - [x] 3.3 验证 `GET /api/locations/{id}/children` 返回直接子节点且按 SortOrder 排列
  - [x] 3.4 验证 `GET /api/locations/999/children` 返回 404
  - [x] 3.5 验证未登录访问返回 401
  - [x] 3.6 验证已有端点（POST/PUT/DELETE）未被破坏

---

## Dev Notes

### 前置上下文

- **Story 2.1 已完成:** Location 实体 + 物化路径 + CRUD 端点就绪，`dotnet build` 零错误零警告
- **数据已就绪:** `Location` 表有 Id, Name, Path, ParentId, SortOrder，Path 格式 `/1/2/3/`
- **认证系统:** 全局 `[Authorize]` FallbackPolicy，匿名端点需显式 `.AllowAnonymous()`
- **代码审查教训已应用:** TypedResults.Problem() 直接返回、`.Produces*()` 注解、DTO 统一
- **Repository:** `LocationRepository` 在 `src/BoxWise.Server/Repositories/`，已注册 Scoped DI

### Story 2.1 关键学习（必须延续）

1. **错误返回用 `TypedResults.Problem()`** — 不要嵌套在 `TypedResults.BadRequest()` 里
2. **每个端点加 `.Produces*()` 注解** — `.Produces<T>(200)`, `.ProducesProblem(400)`, `.Produces(404)`
3. **DTO 用 record 类型** — 放在 `BoxWise.Shared.Dtos` 命名空间
4. **Repository 返回实体** — 端点负责 Entity → DTO 映射（避免循环耦合）

### 现有代码变更范围

**`LocationRepository.cs`（修改）— 添加 2 个方法：**

```csharp
public async Task<List<Location>> GetAllAsync()
{
    return await _db.Locations
        .OrderBy(l => l.SortOrder)
        .ThenBy(l => l.Name)
        .ToListAsync();
}

public async Task<List<Location>> GetChildrenAsync(int id)
{
    var exists = await _db.Locations.AnyAsync(l => l.Id == id);
    if (!exists)
        throw new KeyNotFoundException("位置不存在");

    return await _db.Locations
        .Where(l => l.ParentId == id)
        .OrderBy(l => l.SortOrder)
        .ThenBy(l => l.Name)
        .ToListAsync();
}
```

**注意：** `GetAllAsync` 返回扁平列表（非树形结构）。客户端（Blazor WASM）根据 `Path` 和 `ParentId` 自行构建层级树。这是架构文档中定义的模式。

**`LocationEndpoints.cs`（修改）— 添加 2 个 GET 端点：**

```csharp
group.MapGet("/", GetAllLocationsAsync)
    .Produces<List<LocationDto>>(200)
    .WithTags("Locations")
    .WithDescription("获取所有位置列表");

group.MapGet("/{id:int}/children", GetChildrenAsync)
    .Produces<List<LocationDto>>(200)
    .Produces(404)
    .WithTags("Locations")
    .WithDescription("获取直接子节点");
```

**端点实现：**

```csharp
private static async Task<Ok<List<LocationDto>>>
    GetAllLocationsAsync(LocationRepository repo)
{
    var locations = await repo.GetAllAsync();
    var dtos = locations.Select(l => new LocationDto(
        l.Id, l.Name, l.Path, l.ParentId, l.SortOrder
    )).ToList();
    return TypedResults.Ok(dtos);
}

private static async Task<Results<Ok<List<LocationDto>>, NotFound>>
    GetChildrenAsync(int id, LocationRepository repo)
{
    try
    {
        var children = await repo.GetChildrenAsync(id);
        var dtos = children.Select(l => new LocationDto(
            l.Id, l.Name, l.Path, l.ParentId, l.SortOrder
        )).ToList();
        return TypedResults.Ok(dtos);
    }
    catch (KeyNotFoundException)
    {
        return TypedResults.NotFound();
    }
}
```

### 物化路径 vs ParentId 查询策略

Story 2.2 的 `GetChildrenAsync` 使用 `ParentId` 直查而非 `Path LIKE`：
- **一级子节点:** `WHERE ParentId = @id` — 直接索引查找（O(1) per child），最简单高效
- **子树查询:** `WHERE Path LIKE '/1/%'` — 用于后续 Story（位置筛选物品列表），由 `Story 3.1` 的 `LocationRepository` 扩展提供

这样的分层策略保持每个查询最简单化，符合架构文档中 `LocationRepository` 封装所有路径操作的原则。

### 文件结构变更总览

```
src/BoxWise.Server/
  Repositories/LocationRepository.cs  (modified — 添加 GetAllAsync + GetChildrenAsync)
  Endpoints/LocationEndpoints.cs      (modified — 添加 2 个 GET 端点)
```

**无新增文件** — 本 Story 仅扩展现有 Repository 和 Endpoints，复用 `LocationDto`（Story 2.1）。

### 构建与验证

```bash
# 1. 完整构建
dotnet build BoxWise.slnx

# 2. 启动 Server
cd src/BoxWise.Server && dotnet run

# 3. 登录 + 准备测试数据
curl -k -c cookies.txt -X POST https://localhost:5000/api/auth/login \
  -H "Content-Type: application/json" \
  -d '{"username":"admin","password":"admin123"}'

# 创建测试数据
curl -k -b cookies.txt -X POST https://localhost:5000/api/locations \
  -H "Content-Type: application/json" \
  -d '{"name":"客厅","parentId":null}'
curl -k -b cookies.txt -X POST https://localhost:5000/api/locations \
  -H "Content-Type: application/json" \
  -d '{"name":"电视机柜","parentId":1}'
curl -k -b cookies.txt -X POST https://localhost:5000/api/locations \
  -H "Content-Type: application/json" \
  -d '{"name":"卧室","parentId":null}'

# 4. 测试 GET /api/locations（完整列表）
curl -k -b cookies.txt https://localhost:5000/api/locations
# 预期: 200 + 3 个位置项，按 SortOrder+Name 排列

# 5. 测试 GET /api/locations/1/children（直接子节点）
curl -k -b cookies.txt https://localhost:5000/api/locations/1/children
# 预期: 200 + [{"id":2,"name":"电视机柜","path":"/1/2/","parentId":1,...}]

# 6. 测试不存在的节点
curl -k -b cookies.txt https://localhost:5000/api/locations/999/children
# 预期: 404

# 7. 测试未登录
curl -k https://localhost:5000/api/locations
# 预期: 401

# 8. 验证已有端点不受影响
curl -k -b cookies.txt -X DELETE https://localhost:5000/api/locations/3
# 预期: 204
```

### 关键风险点

1. **GET vs POST 冲突** — `MapGet("/")` 和 `MapPost("/")` 路由不冲突（HTTP method 不同），已确认
2. **/{id:int}/children 路由优先级** — 在 ASP.NET Core 中 `/{id:int}/children` 不会与 `/{id:int}` 冲突（路由模板不同），已确认
3. **空子节点** — `GetChildrenAsync` 对没有子节点的位置返回空列表（不是 404），这是语义正确行为

---

## References

| 内容 | 来源 |
|------|------|
| Story AC 定义 | [Source: epics.md#Story 2.2] |
| FR-15 层级浏览 | [Source: prd.md#FR-15] |
| API 端点路由定义 | [Source: architecture.md#Route Structure] |
| 物化路径模式 + Path TEXT NOT NULL | [Source: architecture.md#Hierarchical Location Tree: Materialized Path] |
| Minimal API + TypedResults | [Source: architecture.md#API Style: Minimal API] |
| Location 实体定义 | [Source: Story 2.1: Location.cs] |
| LocationRepository 现有模式 | [Source: Story 2.1: LocationRepository.cs] |
| LocationEndpoints 现有模式 | [Source: Story 2.1: LocationEndpoints.cs] |
| DTO 审查后模式（.Produces*() 注解） | [Source: Story 2.1 Code Review Fixes] |
| LocationDto 定义 | [Source: Story 2.1: LocationDto.cs] |

## Dev Agent Record

### Agent Model Used

deepseek-v4-pro

**代码审查修复记录:**
- 🟡 `GetChildrenAsync` TOCTOU 竞态 — `AnyAsync`→`FindAsync`（一次查询消除窗口期）
- 🟡 全部端点缺 `.ProducesProblem(401)` — 5 个端点均已补充认证要求声明

### Debug Log References

### Completion Notes List

✅ **全部 3 个任务完成** — 位置树浏览 API 搭建完毕，所有 AC 端到端验证通过

**实施要点：**
- Repository 新增 `GetAllAsync()` 和 `GetChildrenAsync(id)` 查询方法
- 端点新增 `GET /api/locations`（全部列表）和 `GET /api/locations/{id}/children`（直接子节点）
- 遵循 Story 2.1 审查后模式：`TypedResults.Problem()` 直接返回 + `.Produces*()` 注解
- 无新增文件——仅扩展现有 Repository 和 Endpoints

**E2E 验证结果：**
- `GET /api/locations` → 200 + 4 个位置项 ✅
- `GET /api/locations/1/children` → 200 + 1 个直接子节点 ✅
- `GET /api/locations/999/children` → 404 ✅
- `GET /api/locations` (unauth) → 401 ✅
- 已有 POST/PUT/DELETE 端点未被破坏 ✅

### File List

**修改文件:**
- `src/BoxWise.Server/Repositories/LocationRepository.cs` (modified) — 新增 `GetAllAsync` + `GetChildrenAsync`
- `src/BoxWise.Server/Endpoints/LocationEndpoints.cs` (modified) — 新增 2 个 GET 端点
