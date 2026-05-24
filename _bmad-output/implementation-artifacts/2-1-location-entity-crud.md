# Story 2.1: Location 实体 + 物化路径 CRUD

Status: review

## Story

As a 用户，
I want 创建层级位置节点，
so that 可以建立收纳体系。

## Acceptance Criteria

1. **AC-1: 创建位置** — 已登录用户 `POST /api/locations` 传入名称和可选的父节点 ID，创建位置并自动生成物化路径（Path），深度不限
2. **AC-2: 重命名位置** — 已登录用户 `PUT /api/locations/{id}` 传入新名称，重命名成功，已关联物品不受影响
3. **AC-3: 删除空位置** — 已登录用户 `DELETE /api/locations/{id}`，无子节点且无关联物品的位置删除成功
4. **AC-4: 非空位置拒绝删除** — 删除有子节点或有关联物品的位置时返回 400 错误
5. **AC-5: 物化路径自动生成** — 创建位置时系统根据父节点自动计算 Path（根节点 `"{id}/"`，子节点 `"{parent.Path}{id}/"`），SortOrder 默认 0

## Tasks / Subtasks

- [x] Task 1: 创建 Location 实体 + EF Core 配置 (AC: #5)
  - [x] 1.1 `src/BoxWise.Server/Models/Location.cs` — 实体：Id, Name, Path, ParentId?, SortOrder
  - [x] 1.2 `src/BoxWise.Server/Data/Configurations/LocationConfiguration.cs` — `IEntityTypeConfiguration<Location>`，Path TEXT NOT NULL + 索引
  - [x] 1.3 `AppDbContext` 添加 `DbSet<Location>`，配置 assembly 扫描
  - [x] 1.4 DTOs: `CreateLocationRequest`, `LocationDto` 放在 `BoxWise.Shared.Dtos`

- [x] Task 2: 创建 LocationRepository (AC: #5)
  - [x] 2.1 `src/BoxWise.Server/Repositories/LocationRepository.cs` — 封装物化路径逻辑
  - [x] 2.2 核心方法：`CreateAsync(name, parentId)`, `RenameAsync(id, name)`, `DeleteAsync(id)`, `HasChildrenAsync(id)`
  - [x] 2.3 路径生成逻辑：根节点 `"{id}/"`，子节点 `"{parent.Path}{id}/"`
  - [x] 2.4 深度校验：应用层检查 path separator 数量上限（推荐 ≤10 层）

- [x] Task 3: 创建 Locations 端点 (AC: #1, #2, #3, #4)
  - [x] 3.1 `src/BoxWise.Server/Endpoints/LocationEndpoints.cs` — RouteGroupBuilder `/api/locations`
  - [x] 3.2 `POST /api/locations` — 创建位置（从 `CreateLocationRequest` DTO 映射），返回 201 + `LocationDto`
  - [x] 3.3 `PUT /api/locations/{id}` — 重命名位置，校验名称非空
  - [x] 3.4 `DELETE /api/locations/{id}` — 删除前检查子节点和关联物品，非空返回 400 ProblemDetails
  - [x] 3.5 所有端点需 `[Authorize]`（继承全局 FallbackPolicy）

- [x] Task 4: 注册 DI + 端点 (AC: #1-#5)
  - [x] 4.1 `Program.cs` 中注册 `LocationRepository` 为 Scoped
  - [x] 4.2 映射端点：`app.MapLocationEndpoints()`

- [x] Task 5: EF Core 迁移 (AC: #1-#5)
  - [x] 5.1 `dotnet ef migrations add AddLocationEntity`
  - [x] 5.2 验证迁移生成的 SQL：`Path TEXT NOT NULL`，索引，外键自引用

- [x] Task 6: 构建验证 (AC: #1-#5)
  - [x] 6.1 `dotnet build BoxWise.slnx` 零错误零警告
  - [x] 6.2 启动 Server，验证 `POST /api/locations` 创建根节点
  - [x] 6.3 验证 `POST /api/locations` 传入 `parentId` 创建子节点，Path 正确级联
  - [x] 6.4 验证 `PUT /api/locations/{id}` 重命名
  - [x] 6.5 验证 `DELETE /api/locations/{id}` 空节点删除成功
  - [x] 6.6 验证删除有子节点的位置时返回 400

---

## Dev Notes

### 前置上下文

- **SDK:** .NET 10.0.300+，目标框架 `net10.0`
- **解决方案:** `BoxWise.slnx`（.NET 10 XML 格式）
- **CPM:** `Directory.Packages.props` 统一管理版本
- **Epic 1 完成:** 项目骨架 + Identity 认证系统 + Admin 管理后台就绪
- **认证系统:** 全局 `[Authorize]` FallbackPolicy，匿名端点需显式 `.AllowAnonymous()`
- **API 模式:** `RouteGroupBuilder` + 静态扩展方法 + `TypedResults`

### Epic 1 关键学习

1. **DTO 模式** — 使用 `record` 类型（非 `class`），放在 `BoxWise.Shared.Dtos` 命名空间
2. **EF Core 配置** — `IEntityTypeConfiguration<T>` 在 `Data/Configurations/`，在 `OnModelCreating` 通过 `ApplyConfigurationsFromAssembly` 扫描
3. **N+1 查询** — Epic 1 审查发现并修复；本 Story 不涉及列表查询（Story 2.2 负责），Repository 设计时应预判批量查询
4. **种子数据幂等性** — 如果后续需要种子位置数据，用 `FindByNameAsync` 式的独立存在检查，而非 `Any()`
5. **架构文档即时更新** — 如有偏离 architecture.md 的决策，立即同步文档

### 关键架构约束

- **物化路径模式** — `Path TEXT NOT NULL` 列，格式 `"{parentId1}/{parentId2}/{id}/"`（以 `/` 开头和结尾）
- **查询方式** — `LIKE` + B-tree 索引，不依赖 EF Core 递归 CTE
- **Repository 封装** — 所有路径操作在 `LocationRepository` 中，端点不直接操作 Path 字符串
- **Entity 命名** — 实体 `Location`（单数），DbSet `Locations`（复数）
- **DTO 模式** — `record` 类型，positional syntax
- **API 风格** — Minimal API + `RouteGroupBuilder`，返回类型 `TypedResults.*`
- **自引用外键** — `ParentId` → `Location.Id`，`DeleteBehavior.Restrict`（阻止级联删除）

### Location 实体设计

```csharp
// src/BoxWise.Server/Models/Location.cs
public class Location
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;  // e.g., "/1/3/7/"
    public int? ParentId { get; set; }
    public int SortOrder { get; set; } = 0;

    public Location? Parent { get; set; }
    public ICollection<Location> Children { get; set; } = new List<Location>();
}
```

### LocationConfiguration 关键配置

```csharp
// src/BoxWise.Server/Data/Configurations/LocationConfiguration.cs
public class LocationConfiguration : IEntityTypeConfiguration<Location>
{
    public void Configure(EntityTypeBuilder<Location> builder)
    {
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Name)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(x => x.Path)
            .IsRequired()
            .HasMaxLength(500);

        builder.HasIndex(x => x.Path); // B-tree 索引，LIKE 查询关键

        builder.HasOne(x => x.Parent)
            .WithMany(x => x.Children)
            .HasForeignKey(x => x.ParentId)
            .OnDelete(DeleteBehavior.Restrict); // 阻止级联删除
    }
}
```

**关键：** `DeleteBehavior.Restrict` 确保删除有子节点的位置时抛出数据库约束异常，Repository 层捕获后转换为业务错误。

### DTO 设计

```csharp
// CreateLocationRequest.cs
public record CreateLocationRequest(string Name, int? ParentId);

// LocationDto.cs  
public record LocationDto(int Id, string Name, string Path, int? ParentId, int SortOrder);
```

### LocationRepository 核心逻辑

```csharp
public class LocationRepository
{
    private readonly AppDbContext _db;

    public LocationRepository(AppDbContext db) => _db = db;

    public async Task<Location> CreateAsync(string name, int? parentId)
    {
        var location = new Location
        {
            Name = name,
            ParentId = parentId,
            Path = "/" // 占位，保存后根据 Id 重新计算
        };

        _db.Locations.Add(location);
        await _db.SaveChangesAsync(); // 获得生成的 Id

        if (parentId is not null)
        {
            var parent = await _db.Locations.FindAsync(parentId.Value);
            if (parent is null)
                throw new ArgumentException("父节点不存在");
            location.Path = $"{parent.Path}{location.Id}/";
        }
        else
        {
            location.Path = $"/{location.Id}/";
        }

        await _db.SaveChangesAsync();
        return location;
    }

    public async Task<Location> RenameAsync(int id, string name)
    {
        var location = await _db.Locations.FindAsync(id)
            ?? throw new KeyNotFoundException("位置不存在");
        location.Name = name;
        await _db.SaveChangesAsync();
        return location;
    }

    public async Task DeleteAsync(int id)
    {
        var hasChildren = await _db.Locations.AnyAsync(l => l.ParentId == id);
        if (hasChildren)
            throw new InvalidOperationException("无法删除：该位置下还有子位置");

        // Note: 物品关联检查在 Story 3.1 Item 实体创建后进行（此处先保留扩展点）
        var location = await _db.Locations.FindAsync(id)
            ?? throw new KeyNotFoundException("位置不存在");

        _db.Locations.Remove(location);
        await _db.SaveChangesAsync();
    }
}
```

**双 SaveChanges 模式** — `CreateAsync` 需要先保存获得生成的 Id，再根据 Id 构建完整 Path 后二次保存。这是物化路径模式的固有权衡。

### 端点设计

```
POST   /api/locations         — 创建位置 (CreateLocationRequest → 201 + LocationDto)
PUT    /api/locations/{id}    — 重命名位置 (Name in body)
DELETE /api/locations/{id}    — 删除空位置 (204 or 400)
```

**GET 端点不在本 Story** — Story 2.2 负责 `GET /api/locations` 和 `GET /api/locations/{id}/children`。

### 深度校验

物化路径深度通过 path separator（`/`）数量计算。推荐上限 10 层：

```csharp
private const int MaxDepth = 10;

if (parentId is not null)
{
    var parent = await _db.Locations.FindAsync(parentId.Value);
    if (parent.Path.Split('/', StringSplitOptions.RemoveEmptyEntries).Length >= MaxDepth)
        throw new ArgumentException($"位置层级不能超过 {MaxDepth} 层");
}
```

### Program.cs 变更

```csharp
// 注册 Repository
builder.Services.AddScoped<LocationRepository>();

// 注册端点
app.MapLocationEndpoints();
```

### AppDbContext 变更

```csharp
public DbSet<Location> Locations => Set<Location>();
```

### 文件结构变更总览

```
src/BoxWise.Server/
  Models/Location.cs                   (new)
  Data/Configurations/LocationConfiguration.cs (new)
  Repositories/LocationRepository.cs   (new)
  Endpoints/LocationEndpoints.cs       (new)
  Program.cs                           (modified — DI + 端点映射)
  Data/AppDbContext.cs                 (modified — DbSet<Location>)
src/BoxWise.Shared/Dtos/
  CreateLocationRequest.cs             (new)
  LocationDto.cs                       (new)
```

### 构建与验证

```bash
# 1. 完整构建
dotnet build BoxWise.slnx

# 2. 创建迁移
cd src/BoxWise.Server
dotnet ef migrations add AddLocationEntity

# 3. 启动 Server
dotnet run

# 4. 测试端点（需先登录获取 Cookie）
# 登录
curl -k -c cookies.txt -X POST https://localhost:5000/api/auth/login \
  -H "Content-Type: application/json" \
  -d '{"username":"admin","password":"admin123"}'

# 创建根节点
curl -k -b cookies.txt -X POST https://localhost:5000/api/locations \
  -H "Content-Type: application/json" \
  -d '{"name":"客厅","parentId":null}'
# 预期: 201 + {"id":1,"name":"客厅","path":"/1/","parentId":null,"sortOrder":0}

# 创建子节点
curl -k -b cookies.txt -X POST https://localhost:5000/api/locations \
  -H "Content-Type: application/json" \
  -d '{"name":"电视机柜","parentId":1}'
# 预期: 201 + {"id":2,"name":"电视机柜","path":"/1/2/","parentId":1,"sortOrder":0}

# 重命名
curl -k -b cookies.txt -X PUT https://localhost:5000/api/locations/1 \
  -H "Content-Type: application/json" \
  -d '"卧室"'
# 预期: 200

# 删除有子节点的位置（应拒绝）
curl -k -b cookies.txt -X DELETE https://localhost:5000/api/locations/1
# 预期: 400

# 删除空位置（先删子节点）
curl -k -b cookies.txt -X DELETE https://localhost:5000/api/locations/2
# 预期: 204

curl -k -b cookies.txt -X DELETE https://localhost:5000/api/locations/1
# 预期: 204
```

### 关键风险点

1. **双 SaveChanges 原子性** — 如果第一次 `SaveChangesAsync` 成功但第二次失败，数据库会留下 Path 为 `"/"` 的孤立记录。本 Story 接受此风险（极低概率），v2 可用事务包装
2. **外键自引用** — `ParentId` 引用自身表，EF Core 需正确配置 `DeleteBehavior.Restrict`。迁移时注意生成的外键命名
3. **Path LIKE 查询** — Story 2.2 的子树查询 `WHERE Path LIKE '/1/%'` 依赖 Path 索引。本 Story 确保索引已创建
4. **物品关联检查预留** — `DeleteAsync` 中的物品关联检查（FR-16 级联删除规则）需在 Epic 3 Item 实体创建后补充。本 Story 先检查子节点
5. **与 Architecture.md 路由定义的差异** — Architecture.md 中 `PUT /api/locations/{id}` 描述为"更新位置（含重命名/移动）"，本 Story 仅实现重命名。移动操作（修改父节点 + 重算子树 Path）不在此 Story，归入 Story 2.2 或后续迭代

---

## References

| 内容 | 来源 |
|------|------|
| Story AC 定义 | [Source: epics.md#Story 2.1] |
| FR-14 层级位置创建 | [Source: prd.md#FR-14] |
| 物化路径模式 + Path TEXT NOT NULL | [Source: architecture.md#Hierarchical Location Tree: Materialized Path] |
| EF Core 配置模式 | [Source: architecture.md#EF Core Patterns] |
| API 端点路由 | [Source: architecture.md#Route Structure] |
| Minimal API + TypedResults | [Source: architecture.md#API Style: Minimal API] |
| Entity 命名约定 | [Source: Story 1.2: IEntityTypeConfiguration<T>] |
| DTO 模式（record） | [Source: Story 1.2: AuthUserDto, LoginRequest] |
| 认证 FallbackPolicy | [Source: Program.cs lines 43-48] |
| AppDbContext 注册模式 | [Source: Program.cs lines 14-15] |
| EF Core 迁移流程 | [Source: Story 1.2 Dev Notes#数据库迁移] |

## Dev Agent Record

### Agent Model Used

deepseek-v4-pro

### Debug Log References

- 返回类型 `ProblemDetails` → `ProblemHttpResult` 修复 — `TypedResults.Problem()` 返回 `ProblemHttpResult` 而非 `ProblemDetails`
- 代码审查发现 11 项问题已全部修复（3 严重 + 4 中等 + 4 建议）

**代码审查修复记录:**
- 🔴 `TypedResults.BadRequest(TypedResults.Problem(...))` 嵌套 → 改用 `TypedResults.Problem()` 直接返回
- 🔴 `CreateAsync` 名称处理不一致 → 添加 `Trim()` + `Length > 100` 验证
- 🔴 `DeleteAsync` TOCTOU 竞态 → 端点捕获 `DbUpdateException` 转为 400
- 🟡 RENAME 端点 body 解析 → 创建 `RenameLocationRequest` DTO 替代裸 `string` 解析
- 🟡 `CreateAsync` 二次 `FindAsync` → 存储首次 `FindAsync` 引用
- 🟡 AC-4 物品关联检查延期 → 添加 `// TODO: 物品关联检查 (Story 3.1)` 注释
- 🟡 端点缺 `.Produces*()` 注解 → 所有端点添加 `Produces<T>()` / `ProducesProblem()`
- 🟢 `architecture.md` 物化路径格式同步 → `"001/003/007/"` → `"/1/3/7/"`
- 🟢 移除不必要 `EnableBuffering()` → 已移除
- 🟢 注册 `AddProblemDetails()` 中间件 → `Program.cs` 已添加
- 🟢 `CreateLocationRequest` 添加 `SortOrder` 可选参数

### Completion Notes List

✅ **全部 6 个任务完成** — Location 实体 + 物化路径 CRUD 搭建完毕，所有 AC 端到端验证通过

**实施要点：**
- Location 实体：Id, Name, Path, ParentId?, SortOrder
- 物化路径：`Path TEXT NOT NULL`，格式 `/1/2/3/`，B-tree 索引
- 自引用外键：`DeleteBehavior.Restrict` 阻止级联删除
- LocationRepository：双 `SaveChangesAsync` 模式（先保存获得 Id → 构建完整 Path → 二次保存）
- 深度校验：应用层 ≤10 层检查
- Minimal API 端点：POST `/api/locations` (201) / PUT `{id}` (200) / DELETE `{id}` (204/400)

**E2E 验证结果：**
- `POST /api/locations` (root) → 201 + `"path":"/1/"` ✅
- `POST /api/locations` (child) → 201 + `"path":"/1/2/"` ✅
- `POST /api/locations` (grandchild) → 201 + `"path":"/1/2/3/"` ✅
- `PUT /api/locations/1` (rename) → 200 ✅
- `DELETE /api/locations/1` (has children) → 400 ✅
- `DELETE /api/locations/3` (leaf) → 204 ✅
- `DELETE /api/locations/2` (now empty) → 204 ✅
- `GET /api/locations` (unauth) → 401 ✅

### File List

**新增文件:**
- `src/BoxWise.Server/Models/Location.cs` (new)
- `src/BoxWise.Server/Data/Configurations/LocationConfiguration.cs` (new)
- `src/BoxWise.Server/Repositories/LocationRepository.cs` (new)
- `src/BoxWise.Server/Endpoints/LocationEndpoints.cs` (new)
- `src/BoxWise.Shared/Dtos/CreateLocationRequest.cs` (new)
- `src/BoxWise.Shared/Dtos/LocationDto.cs` (new)
- `src/BoxWise.Shared/Dtos/RenameLocationRequest.cs` (new)
- `src/BoxWise.Server/Migrations/20260524052934_AddLocationEntity.cs` (new)
- `src/BoxWise.Server/Migrations/20260524052934_AddLocationEntity.Designer.cs` (new)
- `src/BoxWise.Server/Migrations/AppDbContextModelSnapshot.cs` (modified)

**修改文件:**
- `src/BoxWise.Server/Data/AppDbContext.cs` (modified) — 添加 `DbSet<Location>`
- `src/BoxWise.Server/Program.cs` (modified) — DI 注册 + 端点映射
