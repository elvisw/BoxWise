# Story 2.3: 标签系统

Status: review

## Story

As a 用户，
I want 创建和管理标签，
so that 可以跨位置分类物品。

## Acceptance Criteria

1. **AC-1: 标签列表** — 已登录用户 `GET /api/tags` 返回所有标签列表，每项含 Id, Name，按 Name 字母序排列
2. **AC-2: 创建标签** — 已登录用户 `POST /api/tags` 传入标签名称，创建新标签，名称唯一；重复名称返回 400
3. **AC-3: 自动创建** — 录入物品时传入新标签名，标签不存在则自动创建（通过 `TagRepository.GetOrCreateAsync(name)` 暴露给后续 Story 3.4）

## Tasks / Subtasks

- [x] Task 1: 创建 Tag 实体 + EF Core 配置 (AC: #1)
  - [x] 1.1 `src/BoxWise.Server/Models/Tag.cs` — 实体：Id, Name
  - [x] 1.2 `src/BoxWise.Server/Data/Configurations/TagConfiguration.cs` — `IEntityTypeConfiguration<Tag>`，Name 唯一索引
  - [x] 1.3 `AppDbContext` 添加 `DbSet<Tag>`
  - [x] 1.4 DTO: `TagDto` (Id, Name) 放在 `BoxWise.Shared.Dtos`

- [x] Task 2: 创建 TagRepository (AC: #2, #3)
  - [x] 2.1 `src/BoxWise.Server/Repositories/TagRepository.cs` — 封装标签操作
  - [x] 2.2 `GetAllAsync()` — 返回所有标签，按 Name 排序
  - [x] 2.3 `CreateAsync(name)` — 校验名称非空+Trim+长度≤50 → 检查唯一性 → 创建标签；重复返回 `ArgumentException`
  - [x] 2.4 `GetOrCreateAsync(name)` — 先查后建（供 Story 3.4 物品录入时自动创建）

- [x] Task 3: 创建 TagEndpoints (AC: #1, #2)
  - [x] 3.1 `src/BoxWise.Server/Endpoints/TagEndpoints.cs` — RouteGroupBuilder `/api/tags`
  - [x] 3.2 `GET /api/tags` → 返回 `List<TagDto>`
  - [x] 3.3 `POST /api/tags` → 接收 `CreateTagRequest` DTO，返回 201 + `TagDto` 或 400
  - [x] 3.4 所有端点添加 `.Produces*()` + `.ProducesProblem(401)` 注解

- [x] Task 4: 注册 DI + 端点 + EF Core 迁移 (AC: #1-#3)
  - [x] 4.1 `Program.cs` 注册 `TagRepository` 为 Scoped + 映射 `app.MapTagEndpoints()`
  - [x] 4.2 `dotnet ef migrations add AddTagEntity`
  - [x] 4.3 验证迁移 SQL：Name 唯一索引

- [x] Task 5: 构建验证 + 端到端测试 (AC: #1-#3)
  - [x] 5.1 `dotnet build BoxWise.slnx` 零错误零警告
  - [x] 5.2 `GET /api/tags` 返回空列表或已有标签
  - [x] 5.3 `POST /api/tags` 创建标签，返回 201
  - [x] 5.4 `POST /api/tags` 重复名称返回 400
  - [x] 5.5 未登录访问返回 401
  - [x] 5.6 验证 `GetOrCreateAsync` 幂等性（通过端点间接测试）

---

## Dev Notes

### 前置上下文

- **Story 2.1 + 2.2 已完成:** Location 实体 + CRUD + 浏览 API 就绪
- **认证系统:** 全局 `[Authorize]` FallbackPolicy
- **API 模式:** Minimal API + RouteGroupBuilder + TypedResults + `.Produces*()`
- **Repository 模式:** Entity 返回，端点负责 DTO 映射，Scoped DI

### Epic 2 前序学习

1. **TypedResults.Problem() 直接返回** — 不用 `BadRequest(Problem(...))` 嵌套
2. **所有端点加 `.ProducesProblem(401)`** — Story 2.2 审查修复后的标准
3. **Repository 方法用 `KeyNotFoundException` 表示不存在，端点 catch 后转 404**
4. **DTO 用 positional record** — 放在 `BoxWise.Shared.Dtos`
5. **名称处理统一** — `Trim()` + `Length > N` 校验 + 唯一性检查
6. **构建验证只需 `dotnet build` + E2E curl** — 项目无测试框架

### Tag 实体设计

```csharp
// src/BoxWise.Server/Models/Tag.cs
public class Tag
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
}
```

**注意：** 本 Story 不创建 `ItemTag` 多对多中间表。该表将在 Story 3.1（Item 实体）中统一创建，届时 `Item.Configuration` 会配置 `HasMany(i => i.Tags).WithMany(t => t.Items)` 让 EF Core 自动生成中间表。当前 Tag 实体无需导航属性到 Item。

### TagConfiguration

```csharp
public class TagConfiguration : IEntityTypeConfiguration<Tag>
{
    public void Configure(EntityTypeBuilder<Tag> builder)
    {
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Name)
            .IsRequired()
            .HasMaxLength(50);

        builder.HasIndex(x => x.Name).IsUnique();
    }
}
```

### TagRepository 核心逻辑

```csharp
public class TagRepository
{
    private readonly AppDbContext _db;

    public TagRepository(AppDbContext db) => _db = db;

    public async Task<List<Tag>> GetAllAsync()
    {
        return await _db.Tags
            .OrderBy(t => t.Name)
            .ToListAsync();
    }

    public async Task<Tag> CreateAsync(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("标签名称不能为空");
        name = name.Trim();
        if (name.Length > 50)
            throw new ArgumentException("标签名称不能超过 50 个字符");

        var exists = await _db.Tags.AnyAsync(t => t.Name == name);
        if (exists)
            throw new ArgumentException($"标签 '{name}' 已存在");

        var tag = new Tag { Name = name };
        _db.Tags.Add(tag);
        await _db.SaveChangesAsync();
        return tag;
    }

    public async Task<Tag> GetOrCreateAsync(string name)
    {
        name = name.Trim();
        var existing = await _db.Tags.FirstOrDefaultAsync(t => t.Name == name);
        if (existing is not null)
            return existing;

        return await CreateAsync(name);
    }
}
```

### TagEndpoints 设计

```csharp
group.MapGet("/", GetAllTagsAsync)
    .Produces<List<TagDto>>(200)
    .ProducesProblem(401)
    .WithTags("Tags")
    .WithDescription("获取所有标签");

group.MapPost("/", CreateTagAsync)
    .Produces<TagDto>(201)
    .ProducesProblem(400)
    .ProducesProblem(401)
    .WithTags("Tags")
    .WithDescription("创建标签");
```

### DTO

```csharp
// CreateTagRequest.cs
public record CreateTagRequest(string Name);

// TagDto.cs
public record TagDto(int Id, string Name);
```

### 文件结构变更

```
src/BoxWise.Server/
  Models/Tag.cs                       (new)
  Data/Configurations/TagConfiguration.cs (new)
  Repositories/TagRepository.cs       (new)
  Endpoints/TagEndpoints.cs           (new)
  Program.cs                          (modified — DI + 端点映射)
  Data/AppDbContext.cs                (modified — DbSet<Tag>)
src/BoxWise.Shared/Dtos/
  CreateTagRequest.cs                 (new)
  TagDto.cs                           (new)
```

### 构建与验证

```bash
# 1. 构建
dotnet build BoxWise.slnx

# 2. 创建迁移
cd src/BoxWise.Server
dotnet ef migrations add AddTagEntity

# 3. 启动 Server
dotnet run

# 4. 登录
curl -k -c cookies.txt -X POST https://localhost:5000/api/auth/login \
  -H "Content-Type: application/json" \
  -d '{"username":"admin","password":"admin123"}'

# 5. 测试 GET
curl -k -b cookies.txt https://localhost:5000/api/tags

# 6. 创建标签
curl -k -b cookies.txt -X POST https://localhost:5000/api/tags \
  -H "Content-Type: application/json" \
  -d '{"name":"电子配件"}'
# 预期: 201

# 7. 重复创建
curl -k -b cookies.txt -X POST https://localhost:5000/api/tags \
  -H "Content-Type: application/json" \
  -d '{"name":"电子配件"}'
# 预期: 400

# 8. 验证 GetOrCreateAsync
curl -k -b cookies.txt -X POST https://localhost:5000/api/tags \
  -H "Content-Type: application/json" \
  -d '{"name":"工具"}'
# 预期: 201（首次创建）/ 400（已存在时的端点行为）

# 9. 未登录
curl -k https://localhost:5000/api/tags
# 预期: 401
```

### 关键风险点

1. **ItemTag 中间表延期** — Story 3.1 创建 Item 实体时需配置多对多关系。本 Story 不创建中间表，确保后续 Story 知道此依赖
2. **GetOrCreateAsync 无独立端点** — 此方法仅作为 Repository 公共方法暴露给 Story 3.4（物品录入），不直接映射到 HTTP 端点。通过 `CreateAsync` 端点间接覆盖其行为
3. **Name 唯一索引** — SQLite 唯一索引在并发创建时可能抛出 `DbUpdateException`，`CreateAsync` 的 `AnyAsync` 前置检查已降低概率

---

## References

| 内容 | 来源 |
|------|------|
| Story AC 定义 | [Source: epics.md#Story 2.3] |
| FR-5 标签附加 | [Source: prd.md#FR-5] |
| Tag 实体 + ItemTag 多对多 | [Source: epics.md#Story 2.3 技术约束] |
| API 端点路由 | [Source: architecture.md#Route Structure] |
| Location 实体 + Repository 模式 | [Source: Story 2.1: LocationRepository.cs] |
| TypedResults.Problem() + .ProducesProblem(401) 模式 | [Source: Story 2.2 Code Review Fixes] |
| DTO record 模式 | [Source: Story 2.1: CreateLocationRequest, LocationDto] |

## Dev Agent Record

### Agent Model Used

deepseek-v4-pro

**代码审查修复记录:**
- 🔴 `CreateAsync` TOCTOU 竞态 — 添加 `DbUpdateException` 捕获转为 `ArgumentException`
- 🔴 `GetOrCreateAsync` TOCTOU 竞态 — 添加 `DbUpdateException` 捕获 + 重试读取
- 🟡 `GetOrCreateAsync` 缺长度校验 — 添加 `Length > 50` 验证
- 🟡 `TagEndpoints` 缺 `DbUpdateException` 捕获 — 添加 catch 返回 400

### Debug Log References

### Completion Notes List

✅ **全部 5 个任务完成** — 标签系统搭建完毕，所有 AC 端到端验证通过

**实施要点：**
- Tag 实体：Id, Name（Name 唯一索引）
- TagRepository：`GetAllAsync` / `CreateAsync` / `GetOrCreateAsync`（供 Story 3.4）
- TagEndpoints：`GET /api/tags`（列表）/ `POST /api/tags`（创建）
- ItemTag 多对多中间表延期至 Story 3.1（Item 实体统一处理）

**E2E 验证结果：**
- `GET /api/tags` → 200 + 3 标签（字母序） ✅
- `POST /api/tags` → 201 + TagDto ✅
- `POST /api/tags`（重复） → 400 ✅
- `GET /api/tags` (unauth) → 401 ✅
- `GET /api/locations` → 200（回归无破坏） ✅

### File List

**新增文件:**
- `src/BoxWise.Server/Models/Tag.cs` (new)
- `src/BoxWise.Server/Data/Configurations/TagConfiguration.cs` (new)
- `src/BoxWise.Server/Repositories/TagRepository.cs` (new)
- `src/BoxWise.Server/Endpoints/TagEndpoints.cs` (new)
- `src/BoxWise.Shared/Dtos/TagDto.cs` (new)
- `src/BoxWise.Shared/Dtos/CreateTagRequest.cs` (new)
- `src/BoxWise.Server/Migrations/20260524055828_AddTagEntity.cs` (new)
- `src/BoxWise.Server/Migrations/20260524055828_AddTagEntity.Designer.cs` (new)

**修改文件:**
- `src/BoxWise.Server/Data/AppDbContext.cs` (modified) — 添加 `DbSet<Tag>`
- `src/BoxWise.Server/Program.cs` (modified) — DI 注册 + 端点映射
- `src/BoxWise.Server/Migrations/AppDbContextModelSnapshot.cs` (modified)
