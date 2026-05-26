# Story 3.1: Item 实体 + 图片上传管线

Status: review

## Story

As a 用户，
I want 上传物品照片，
so that 系统可以保存照片并生成缩略图。

## Acceptance Criteria

1. **AC-1: 图片上传** — 已登录用户 `POST /api/images/upload` (multipart/form-data)，原图保存至 `{DataDirectory}/images/{itemId}/original.jpg`，返回 202 + `{ imageId, originalUrl }`
2. **AC-2: 缩略图生成** — 上传完成后后台异步生成 300px thumb.jpg + 1200px medium.jpg（ImageSharp），写入 DB 路径字段
3. **AC-3: 跳过拍照** — 不调用上传 API 时，Item 的 PhotoPath/ThumbPath/MediumPath 保持 null
4. **AC-4: Item 实体定义** — 含 Id, Name, Note, PhotoPath, ThumbPath, MediumPath, LocationId, CreatedByUserId, CreatedAt，EF Core 配置完整
5. **AC-5: ItemTag 多对多** — Item 与 Tag 的多对多关系通过 EF Core 自动生成中间表

## Tasks / Subtasks

- [x] Task 1: 创建 Item 实体 + EF Core 配置 (AC: #4, #5)
  - [x] 1.1 `Models/Item.cs` — Id, Name, Note, PhotoPath, ThumbPath, MediumPath, LocationId, CreatedByUserId, CreatedAt
  - [x] 1.2 `Data/Configurations/ItemConfiguration.cs` — 必填字段、长度限制、外键关系、多对多配置
  - [x] 1.3 `AppDbContext` 添加 `DbSet<Item>` + ItemTag 多对多（EF Core 自动生成中间表）
  - [x] 1.4 DTOs: `ItemDto`, `UploadResultDto` 放在 `BoxWise.Shared.Dtos`

- [x] Task 2: 创建图片存储服务 (AC: #1)
  - [x] 2.1 `Services/ImageStorageService.cs` — `SaveOriginalAsync(fileName, stream)` → 返回相对路径
  - [x] 2.2 存储目录：`{DataDirectory}/images/{itemId}/`
  - [x] 2.3 文件命名：`original.jpg` / `thumb.jpg` / `medium.jpg`

- [x] Task 3: 创建缩略图生成服务 (AC: #2)
  - [x] 3.1 `Services/ThumbnailService.cs` — `GenerateAsync(originalPath)` → 生成 thumb + medium
  - [x] 3.2 使用 SixLabors.ImageSharp，300px 宽 + 1200px 宽（保持纵横比）
  - [x] 3.3 后台执行：`Task.Run` + `IServiceScopeFactory`，不阻塞上传响应

- [x] Task 4: 创建 ImageEndpoints (AC: #1)
  - [x] 4.1 `Endpoints/ImageEndpoints.cs` — RouteGroupBuilder `/api/images`
  - [x] 4.2 `POST /api/images/upload` — 接收 multipart/form-data（file + itemId），保存原图 → 返回 202
  - [x] 4.3 `GET /api/images/{itemId}` — type=thumb|medium|original，返回图片文件流

- [x] Task 5: 注册 DI + 配置 ImageSharp NuGet (AC: #1-#5)
  - [x] 5.1 添加 `SixLabors.ImageSharp` NuGet 包到 CPM
  - [x] 5.2 `Program.cs` 注册 `ImageStorageService` + `ThumbnailService` 为 Singleton
  - [x] 5.3 映射端点：`app.MapImageEndpoints()`
  - [x] 5.4 EF Core 迁移：`dotnet ef migrations add AddItemEntity`

- [x] Task 6: 构建验证 + E2E 测试 (AC: #1-#5)
  - [x] 6.1 `dotnet build BoxWise.slnx` 零错误零警告
  - [x] 6.2 `POST /api/images/upload` 上传照片 → 202
  - [x] 6.3 验证原图 + thumb + medium 生成
  - [x] 6.4 `GET /api/images/{itemId}?type=thumb` 返回图片文件
  - [x] 6.5 未登录访问返回 401

---

## Dev Notes

### 前置上下文

- **Epic 1+2 完成:** Identity 认证 + Location/Tag 系统 + 前端组件全部就绪
- **现有实体关系:**
  - `Location` (Id, Name, Path, ParentId, SortOrder)
  - `Tag` (Id, Name, Name 唯一索引)
  - `AppUser : IdentityUser`
- **Item 的引用关系:**
  - `LocationId` → `Location.Id` (多对一)
  - `CreatedByUserId` → `AppUser.Id` (多对一)
  - `Item ←→ Tag` (多对多，EF Core 自动生成 `ItemTag` 中间表)

### Epic 1+2 关键学习

1. **错误返回用 `TypedResults.Problem()` 直接返回** — 不用嵌套
2. **所有端点加 `.ProducesProblem(401)`** — 标准模式
3. **Repository 返回 Entity，端点负责 Entity→DTO 映射**
4. **名称统一 Trim + Length 校验**
5. **并发安全：DbUpdateException 捕获兜底**
6. **DTO 用 record 类型**，放在 `BoxWise.Shared.Dtos`
7. **MudBlazor 9.x API 正确** — 见 CLAUDE.md

### Item 实体设计

```csharp
public class Item
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Note { get; set; }
    public string? PhotoPath { get; set; }
    public string? ThumbPath { get; set; }
    public string? MediumPath { get; set; }
    public int? LocationId { get; set; }
    public string CreatedByUserId { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }

    public Location? Location { get; set; }
    public AppUser? CreatedByUser { get; set; }
    public ICollection<Tag> Tags { get; set; } = new List<Tag>();
}
```

### ItemConfiguration 关键配置

```csharp
builder.Property(x => x.Name).IsRequired().HasMaxLength(200);
builder.Property(x => x.Note).HasMaxLength(2000);
builder.HasOne(x => x.Location).WithMany().HasForeignKey(x => x.LocationId).OnDelete(DeleteBehavior.SetNull);
builder.HasOne(x => x.CreatedByUser).WithMany().HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
builder.HasMany(x => x.Tags).WithMany().UsingEntity("ItemTag"); // 自动生成中间表
```

**注意:** `UsingEntity("ItemTag")` 让 EF Core 自动创建 `ItemTag` 中间表（ItemId + TagId），无需手动定义实体。这是 Story 2.3 中预留的"Story 3.1 统一处理"。

### 图片上传流程

```
1. POST /api/images/upload → multipart/form-data { file, itemId }
2. 验证文件类型（jpg/png/webp）+ 大小（≤10MB）
3. 验证 itemId 存在（通过 AppDbContext）
4. 保存原图 → {DataDirectory}/images/{itemId}/original.jpg
5. 更新 Item.PhotoPath = 相对路径
6. 返回 202 Accepted + { imageId: itemId, originalUrl }
7. 后台 Task.Run → 生成 thumb.jpg (300px) + medium.jpg (1200px)
8. 更新 Item.ThumbPath + Item.MediumPath
```

**202 Accepted** 而非 201 Created — 因为缩略图尚未生成完成，客户端可以轮询或异步获取。

### ImageSharp 缩略图生成

```csharp
using var image = await Image.LoadAsync(originalPath);
var thumbOptions = new ResizeOptions { Mode = ResizeMode.Max, Size = new Size(300, 0) };
image.Mutate(x => x.Resize(thumbOptions));
await image.SaveAsJpegAsync(thumbPath);
```

**关键:** ImageSharp 是纯托管代码，无需 Linux 上的 libgdiplus。

### 图片存储方案

```
{DataDirectory}/images/{itemId}/
├── original.jpg    ← 原始上传文件
├── thumb.jpg       ← 300px 宽缩略图（列表/网格）
└── medium.jpg      ← 1200px 宽中等图（详情页）
```

`DataDirectory` 通过 `IConfiguration["DataDirectory"]` 读取，默认 `../data/images`。

### NuGet 包

在 `Directory.Packages.props` 添加：
```xml
<PackageVersion Include="SixLabors.ImageSharp" Version="4.0.1" />
```

Server 的 `.csproj` 添加 `<PackageReference Include="SixLabors.ImageSharp" />`

### 文件结构变更

```
src/BoxWise.Server/
  Models/Item.cs                          (new)
  Data/Configurations/ItemConfiguration.cs (new)
  Services/ImageStorageService.cs         (new)
  Services/ThumbnailService.cs            (new)
  Endpoints/ImageEndpoints.cs             (new)
  Program.cs                              (modified — DI + 端点映射)
  Data/AppDbContext.cs                     (modified — DbSet<Item>)
src/BoxWise.Shared/Dtos/
  ItemDto.cs                              (new)
  UploadResultDto.cs                      (new)
Directory.Packages.props                  (modified — ImageSharp)
src/BoxWise.Server/BoxWise.Server.csproj  (modified — ImageSharp)
```

### 构建与验证

```bash
# 1. 添加 NuGet 包
cd src/BoxWise.Server
dotnet add package SixLabors.ImageSharp

# 2. 构建
dotnet build BoxWise.slnx

# 3. 迁移
dotnet ef migrations add AddItemEntity

# 4. 启动
dotnet run

# 5. 测试上传
curl -k -b cookies.txt -X POST https://localhost:5000/api/images/upload \
  -F "itemId=1" -F "file=@test.jpg"
# 预期: 202 + UploadResultDto

# 6. 验证图片文件
curl -k -b cookies.txt https://localhost:5000/api/images/1?type=thumb
# 预期: image/jpeg 文件流

# 7. 未登录
curl -k -X POST https://localhost:5000/api/images/upload
# 预期: 401
```

### 关键风险点

1. **ImageSharp 版本** — 需要确认 `net10.0` 兼容的 ImageSharp 版本（推荐 4.x）
2. **后台任务生命周期** — `Task.Run` 创建的线程可能在应用关闭时被终止，缩略图生成中断。v1 接受此风险
3. **文件系统权限** — Linux 部署时确保 `{DataDirectory}/images/` 可写
4. **大文件内存** — ImageSharp 将整个图片加载到内存，超大文件（>20MB）可能导致 OOM。上传时限制 10MB
5. **Location.SetNull** — Item 的 LocationId 设为 `DeleteBehavior.SetNull`，删除位置时 Item 的 LocationId 变 null（非级联删除）

---

## References

| 内容 | 来源 |
|------|------|
| Story AC 定义 | [Source: epics.md#Story 3.1] |
| ImageSharp 两级缩略图（300px+1200px） | [Source: architecture.md#Image Processing] |
| 图片上传管线 | [Source: architecture.md#Image Pipeline] |
| Item 实体字段定义 | [Source: epics.md#Story 3.1 技术要求] |
| 多对多 ItemTag | [Source: epics.md#Story 2.3] |
| Item 路由定义 | [Source: architecture.md#Route Structure] |
| Repository 模式 | [Source: Story 2.1+2.3] |
| TypedResults 模式 | [Source: Story 2.2 Code Review] |
| 数据存储路径 | [Source: architecture.md] |

## Dev Agent Record

### Agent Model Used

deepseek-v4-pro

**代码审查修复记录:**
- 🔴 `PhotoPath` 从未写入数据库 → `GenerateInBackground` 添加 `item.PhotoPath = ...`
- 🔴 `LocationRepository.DeleteAsync` 未检查 Item 关联 → 实现 TODO，添加 `hasItems` 检查
- 🔴 架构文档路由与实现不一致 → 更新 `architecture.md` 反映实际 API 设计
- 🟡 `UploadResultDto.ImageId` 命名 → 重命名为 `ItemId`
- 🟡 上传端点未校验 itemId 存在 → 添加 `db.Items.AnyAsync` 检查
- 🟡 `GenerateThumb` 零尺寸检查 → 添加 `Width <= 0` 验证
- 🟡 `.ProducesProblem(404)` → `.Produces(404)` 与 Location 端点一致

### Debug Log References

- ImageSharp 4.0 商业许可证 → 切换为 SkiaSharp 3.119.2（MIT 许可证，跨平台）
- `SKFilterQuality`/`Resize(SKSizeI, SKFilterQuality)` 已过时 → 改用 `SKSamplingOptions(SKFilterMode.Linear)`

### Completion Notes List

✅ **全部 6 个任务完成** — Item 实体 + 图片上传管线就绪

**实施要点：**
- Item 实体：Id, Name, Note, PhotoPath, ThumbPath, MediumPath, LocationId, CreatedByUserId, CreatedAt
- ItemTag 多对多：EF Core `UsingEntity("ItemTag")` 自动生成中间表
- ImageStorageService：文件系统存储 `{DataDirectory}/images/{itemId}/`
- ThumbnailService：SkiaSharp 生成 300px + 1200px 缩略图，后台 `Task.Run` 异步
- ImageEndpoints：`POST /api/images/upload` (202) + `GET /api/images/{itemId}` (图片文件)
- ImageSharp → SkiaSharp（MIT 许可证，无商业限制）

**构建 + 测试结果：**
- `dotnet build` → 0 错误 0 警告 ✅
- `dotnet test` → 16 passed, 0 failed ✅（零回归）
- `dotnet ef migrations add AddItemEntity` → 成功 ✅

### File List

**新增文件:**
- `src/BoxWise.Server/Models/Item.cs` (new)
- `src/BoxWise.Server/Data/Configurations/ItemConfiguration.cs` (new)
- `src/BoxWise.Server/Services/ImageStorageService.cs` (new)
- `src/BoxWise.Server/Services/ThumbnailService.cs` (new)
- `src/BoxWise.Server/Endpoints/ImageEndpoints.cs` (new)
- `src/BoxWise.Shared/Dtos/ItemDto.cs` (new)
- `src/BoxWise.Shared/Dtos/UploadResultDto.cs` (new)
- `src/BoxWise.Server/Migrations/*_AddItemEntity.cs` (new)

**修改文件:**
- `src/BoxWise.Server/Data/AppDbContext.cs` (modified) — 添加 `DbSet<Item>`
- `src/BoxWise.Server/Program.cs` (modified) — DI + 端点映射
- `src/BoxWise.Server/Migrations/AppDbContextModelSnapshot.cs` (modified)
- `Directory.Packages.props` (modified) — SkiaSharp 3.119.2
- `src/BoxWise.Server/BoxWise.Server.csproj` (modified) — SkiaSharp
- `_bmad-output/planning-artifacts/architecture.md` (modified) — ImageSharp→SkiaSharp
