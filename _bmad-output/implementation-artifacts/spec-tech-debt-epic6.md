---
title: 'Epic 6 技术债务清理'
type: 'chore'
created: '2026-05-27'
status: 'done'
baseline_commit: 'f22d233'
context: []
---

<frozen-after-approval>

## Intent

**Problem:** Epic 6 回顾记录了 3 项技术债务：Auth Endpoint 的 Login/Logout 测试缺失（SignInManager 无法解析）、ItemRepository.GetByIdAsync 的 200 成功路径测试因 EF Core InMemory 限制无法正确断言、ThumbnailService 完全无自动化测试仅靠手动验证。

**Approach:** 三个独立子目标并行解决 — (1) TestIdentityFactory 从 AddIdentityCore 切换为 AddIdentity 以注册 SignInManager 和认证服务，同时设置 IHttpContextAccessor.HttpContext 满足 SignInManager 运行时依赖，补全 Login/Logout 端点测试；(2) 在 ItemRepository 测试 Arrange 阶段于同一 DbContext 中植入 AppUser 实体，绕过 InMemory Include 限制，补全 200 路径断言和端点层测试；(3) 遵循项目现有 Service 测试模式（临时目录 + 真实依赖），为 ThumbnailService 建立自动化测试，通过提取 internal async 方法解决 fire-and-forget 不可等待问题。

## Boundaries & Constraints

**Always:**
- 所有现有 124 测试必须继续通过
- 遵循项目测试模式：xUnit + 临时目录 + 真实依赖（不引入不必要的抽象层）
- 测试命名遵循项目惯例：`MethodName_StateUnderTest_ExpectedBehavior`
- 使用 `TestIdentityFactory` 和 `TestDbContextFactory` 创建测试上下文
- 变更 TestIdentityFactory 后、添加新测试前，必须先运行全量测试验证无回归

**Ask First:**
- 任何对生产代码（非测试代码）的签名或行为变更 — 包括提取 internal 方法
- 新增 NuGet 包依赖

**Never:**
- 修改生产代码的 public API 签名
- 引入新的抽象层（IFileSystem、ISkiaSharpService 等）— 过度工程
- 将 InMemory 数据库切换为 SQLite — 影响范围过大
- 删除或弱化现有测试

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Login 有效凭据 | 正确用户名+密码，用户已存在 | SignInManager.PasswordSignInAsync 返回 Success，handler 返回 Ok(200) | N/A |
| Login 错误密码 | 密码不匹配 | handler 返回 ValidationProblem(400)，errors["credentials"] = "用户名或密码错误" | 400 Bad Request |
| Login 空用户名 | 用户名为 "" 或空白 | handler 返回 ValidationProblem(400) | 400 Bad Request |
| Login 不存在用户 | 用户名在 DB 中不存在 | FindByNameAsync 返回 null → handler 返回 ValidationProblem(400) | 400 Bad Request |
| Logout 已认证 | 有效认证状态 | SignInManager.SignOutAsync 被调用，返回 Ok(200) | N/A |
| GetByIdAsync Item 存在且有 CreatedByUser | Item+AppUser 在同一 DbContext 中已持久化 | 返回 Item，CreatedByUser/Location/Tags 全部非 null | N/A |
| GetByIdAsync Item 不存在 | ID=999 | 返回 null（Repository）/ 404（Endpoint） | NotFound |
| GetByIdAsync CreatedByUser 外键指向不存在的 User | Item.CreatedByUserId 无匹配 AppUser | 返回 Item，CreatedByUser = null | N/A |
| GenerateThumb 正常图片 | 600x800 JPG 源文件 | 生成 300px 宽 JPEG 缩略图，输出文件存在且尺寸正确 | N/A |
| GenerateThumb 源文件不存在 | sourcePath 指向不存在的文件 | SKBitmap.Decode 抛异常，被 catch 静默捕获 | 静默返回，记录 Warning 日志 |
| GenerateThumb 损坏图片 | 空字节或非图片文件 | SKBitmap.Decode 返回 null 或抛异常 | 静默返回，记录 Warning 日志 |
| GenerateInBackground Item 不存在 | 无效 Item ID | 不抛异常，不创建文件 | 静默返回 |
| GenerateInBackground 正常流程 | 有效 Item ID，源图存在 | Item.PhotoPath/ThumbPath/MediumPath 被更新，缩略图文件创建 | N/A |

</frozen-after-approval>

## Code Map

- `src/BoxWise.Server.Tests/TestIdentityFactory.cs` -- 测试 Identity DI 注册（AddIdentityCore→AddIdentity + IHttpContextAccessor 设置）
- `src/BoxWise.Server.Tests/Endpoints/AuthEndpointsTests.cs` -- Auth 端点测试（补全 Login/Logout 测试，使用反射 InvokeAsync 模式）
- `src/BoxWise.Server/Endpoints/AuthEndpoints.cs` -- Login/Logout handler 实现参考
- `src/BoxWise.Server.Tests/Repositories/ItemRepositoryTests.cs` -- Item Repository 测试（修复 GetByIdAsync 200 路径）
- `src/BoxWise.Server.Tests/Endpoints/ItemEndpointsTests.cs` -- Item 端点测试（补全 GetByIdAsync 200 端点测试）
- `src/BoxWise.Server/Repositories/ItemRepository.cs` -- GetByIdAsync 实现参考
- `src/BoxWise.Server.Tests/Services/ThumbnailServiceTests.cs` -- [新建] ThumbnailService 测试
- `src/BoxWise.Server/Services/ThumbnailService.cs` -- ThumbnailService 实现参考（可能需要提取 internal async 方法）
- `src/BoxWise.Server.Tests/Services/ImageStorageServiceTests.cs` -- Service 测试模式参考
- `src/BoxWise.Server.Tests/TestDbContextFactory.cs` -- 可能需要支持命名数据库的重载方法

## Tasks & Acceptance

**Execution:**

### 目标 1: Login/Logout 测试补全（预计 +5 测试）

- [x] `src/BoxWise.Server.Tests/TestIdentityFactory.cs` -- (a) 添加 `services.AddHttpContextAccessor()` 注册 `IHttpContextAccessor`（`AddIdentity` 不会自动注册此服务）；(b) 将 `AddIdentityCore<AppUser>()` 替换为 `AddIdentity<AppUser, IdentityRole>()`，统一密码配置；(c) 添加 `ConfigureApplicationCookie` 设置 `OnRedirectToLogin`/`OnRedirectToAccessDenied` 返回 401 而非 302；(d) 在 `CreateAsync` 中 `provider` 构建后设置 `IHttpContextAccessor.HttpContext = new DefaultHttpContext { RequestServices = provider, Response = { Body = new MemoryStream() } }`；(e) 在 `TestIdentityContext` 中暴露 `SignInManager<AppUser>` 属性 — 使 SignInManager 及其所有运行时依赖可在测试中解析
- [x] **验证门**: 运行 `dotnet test` 确认全部 124 现有测试通过（AddIdentity 变更影响 32 个使用 TestIdentityFactory 的测试）
- [x] `src/BoxWise.Server.Tests/Endpoints/AuthEndpointsTests.cs` -- 添加 `LoginAsync_ValidCredentials_ReturnsOk`、`LoginAsync_InvalidCredentials_ReturnsValidationProblem`、`LoginAsync_EmptyUsername_ReturnsValidationProblem`、`LoginAsync_NonexistentUser_ReturnsValidationProblem`、`LogoutAsync_Authenticated_ReturnsOk` 测试。注意：修改的文件是 `Endpoints/AuthEndpointsTests.cs`（使用反射 InvokeAsync 模式），不是根级 `AuthEndpointsTests.cs`

### 目标 2: GetByIdAsync 200 路径修复（预计 +1 端点测试，修复 2 个现有测试）

- [x] `src/BoxWise.Server.Tests/Repositories/ItemRepositoryTests.cs` -- 在 `GetByIdAsync_Exists_ReturnsItem` 和 `GetByIdAsync_WithMultipleTags_IncludesAllTags` 的 Arrange 阶段通过 `db.Users.Add(new AppUser { Id = "user-1", UserName = "test" })` 植入 AppUser 实体（与 Item 在同一 DbContext），在 `SaveChangesAsync` 后调用 `db.ChangeTracker.Clear()`，移除 if/else 回退代码，直接断言 CreatedByUser 非 null
- [x] `src/BoxWise.Server.Tests/Endpoints/ItemEndpointsTests.cs` -- 添加 `GetItemByIdAsync_Exists_ReturnsOk` 测试：使用 `TestDbContextFactory.Create()` 创建 DbContext，在同一 DbContext 中植入 AppUser + Item + Location，将 ItemRepository（使用该 DbContext）传入 handler，验证返回 200 + ItemDto 完整

### 目标 3: ThumbnailService 测试建立（预计 +4 测试）

- [x] `src/BoxWise.Server/Services/ThumbnailService.cs` -- **[Ask First]** 将 `GenerateInBackground` 的核心逻辑提取为 `internal async Task GenerateAsync(int itemId, IServiceScopeFactory scopeFactory)` 方法，原有 fire-and-forget 方法改为调用 `_ = Task.Run(() => GenerateAsync(itemId, scopeFactory))`。通过 `[InternalsVisibleTo]` 暴露给测试项目。如用户拒绝此变更，则改用轮询等待方案（SpinWait.SpinUntil + 超时断言）
- [x] `src/BoxWise.Server.Tests/TestDbContextFactory.cs` -- 添加 `Create(string databaseName)` 重载以支持命名 InMemory 数据库 — ThumbnailService 内部通过 `IServiceScopeFactory` 创建新 DbContext，需与测试共享同一数据库名称
- [x] `src/BoxWise.Server.Tests/Services/ThumbnailServiceTests.cs` -- 新建测试文件：(a) `GenerateThumb_ValidImage_CreatesResizedFile` — 用 SkiaSharp 动态生成 600x800 测试图片，调用 GenerateThumb，断言输出文件存在且宽度为 300px；(b) `GenerateThumb_SourceNotExists_LogsWarning` — 提供不存在的源路径，验证不抛异常；(c) `GenerateAsync_ValidItem_UpdatesDbPaths` — 使用命名 InMemory 数据库 + 真实 ImageStorageService，完整流程验证 Item 路径被更新；(d) `GenerateAsync_ItemNotFound_NoOp` — 无效 Item ID 静默返回

**Acceptance Criteria:**
- Given 有效用户名+密码，when 调用 LoginAsync，then 返回 200 OK
- Given 错误密码/空用户名/不存在用户，when 调用 LoginAsync，then 返回 400 ValidationProblem
- Given 已认证用户，when 调用 LogoutAsync，then 返回 200 OK
- Given Item+AppUser 在同一 DbContext 中已持久化，when 调用 GetByIdAsync，then 返回 Item（CreatedByUser 非 null）
- Given Item+AppUser 在同一 DbContext 中已持久化，when 调用 GET /api/items/{id}，then 返回 200 + 完整 ItemDto
- Given 600x800 JPG 源图，when 调用 GenerateThumb，then 生成 300px 宽 JPEG 缩略图且文件存在
- Given 全部变更完成，when 运行 `dotnet test`，then 全部测试通过，测试数量从 124 增长到 134（+10）

## Design Notes

### 目标 1: IHttpContextAccessor 设置链

`SignInManager.PasswordSignInAsync` 成功时内部调用 `Context.SignInAsync()`，需要 `IAuthenticationService`（通过 `HttpContext.RequestServices` 解析）和 `IHttpContextAccessor.HttpContext`（用于写入 Cookie）。测试中需确保两者一致：

```
TestIdentityFactory.CreateAsync()
  → services.AddHttpContextAccessor()  // AddIdentity 不会自动注册此服务
  → services.AddIdentity<AppUser, IdentityRole>() ...
  → provider = services.BuildServiceProvider()
  → IHttpContextAccessor.HttpContext = new DefaultHttpContext {
        RequestServices = provider,     // SignInManager 内部通过此解析 IAuthenticationService
        Response.Body = new MemoryStream()  // Cookie 写入目标
    }
```

`AuthEndpointsTests.InvokeAsync` 中的 `HttpContext` 仅用于 `IResult.ExecuteAsync` 写入响应体 — 与 SignInManager 内部使用的 `IHttpContextAccessor.HttpContext` 是两个独立对象。关键是将 `IHttpContextAccessor` 的 HttpContext 设置好，InvokeAsync 只负责执行 handler 返回的 IResult。

### 目标 2: InMemory Include 限制的根因

EF Core InMemory provider 不是关系型数据库，不执行 LEFT JOIN。当 `.Include(i => i.CreatedByUser)` 物化时，如果 `CreatedByUserId` 外键引用的 AppUser 在 InMemory 存储中缺失，整个查询返回 null。解决方案：在 Arrange 阶段将 AppUser 实体植入同一 DbContext，并在 SaveChanges 后调用 `ChangeTracker.Clear()` 确保查询走存储而非跟踪缓存。

### 目标 3: Fire-and-Forget 可测试化

`GenerateInBackground` 使用 `Task.Run` 不返回 Task。推荐提取 internal async 方法：

```csharp
// 原有 public 方法保持不变
public void GenerateInBackground(int itemId, IServiceScopeFactory scopeFactory)
    => _ = Task.Run(() => GenerateAsync(itemId, scopeFactory));

// 新增 internal 方法供测试使用
internal async Task GenerateAsync(int itemId, IServiceScopeFactory scopeFactory)
{
    // 原有核心逻辑
}
```

此变更不改变 public API，仅增加 internal 入口供测试直接 await。需在 `.csproj` 中添加 `[InternalsVisibleTo]`（项目可能已有此配置）。

### 目标 3: 命名 InMemory 数据库共享

ThumbnailService 内部通过 `scopeFactory.CreateScope()` → `scope.ServiceProvider.GetRequiredService<AppDbContext>()` 获取 DbContext。测试需使用**真实 DI 容器**（非 mock）配置 `IServiceScopeFactory`：

```csharp
var dbName = Guid.NewGuid().ToString();
// Arrange 的 DbContext
using var db = TestDbContextFactory.Create(dbName);

// 配置 scope 内的 DI 容器，使用相同的 InMemory 数据库名称
var services = new ServiceCollection();
services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(dbName));
services.AddLogging();
services.AddSingleton<IConfiguration>(
    new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
        { "DataDirectory", tempDir }
    }).Build());
var scopeFactory = services.BuildServiceProvider()
    .GetRequiredService<IServiceScopeFactory>();
```

这样 ThumbnailService 在 scope 内解析的 DbContext 与 Arrange 阶段共享同一 InMemory 实例，Item 实体对两者均可见。

`TestDbContextFactory.Create(string databaseName)` 重载实现简单：将 `Guid.NewGuid().ToString()` 替换为传入参数。

## Verification

**Commands:**
- `dotnet test src/BoxWise.Server.Tests` -- expected: 全部 134 测试通过，0 失败

**验证顺序（目标 1）：**
1. 仅修改 TestIdentityFactory（添加 AddHttpContextAccessor + AddIdentity + HttpContext 设置），运行 `dotnet test` → 124 通过
2. 添加新 Auth 测试，运行 `dotnet test` → 129 通过

## Suggested Review Order

**入口：Identity 测试基础设施重构**

- 核心变更 — AddIdentityCore→AddIdentity + AddHttpContextAccessor + IHttpContextAccessor.HttpContext 设置，使 SignInManager 可解析
  [`TestIdentityFactory.cs:20`](../../src/BoxWise.Server.Tests/TestIdentityFactory.cs#L20)

- SignInManager 上下文双重绑定（IHttpContextAccessor + signInManager.Context），绕过 AsyncLocal 在测试中的限制
  [`TestIdentityFactory.cs:52`](../../src/BoxWise.Server.Tests/TestIdentityFactory.cs#L52)

- TestIdentityContext 新增 SignInManager 属性，保持 backward-compatible 构造函数扩展
  [`TestIdentityContext.cs:82`](../../src/BoxWise.Server.Tests/TestIdentityFactory.cs#L82)

**关注点：Login/Logout 端点测试**

- 5 个新测试覆盖 Login 有效/无效/空用户名/不存在用户 + Logout
  [`AuthEndpointsTests.cs:50`](../../src/BoxWise.Server.Tests/Endpoints/AuthEndpointsTests.cs#L50)

**关注点：ItemRepository Include 限制绕过**

- 移除 if/else 回退，植入 AppUser + ChangeTracker.Clear() 后直接断言 CreatedByUser 非 null
  [`ItemRepositoryTests.cs:193`](../../src/BoxWise.Server.Tests/Repositories/ItemRepositoryTests.cs#L193)

- 端点层 200 路径 — 同一 DbContext 植入 AppUser+Location+Item，验证完整 ItemDto
  [`ItemEndpointsTests.cs:84`](../../src/BoxWise.Server.Tests/Endpoints/ItemEndpointsTests.cs#L84)

**关注点：ThumbnailService 可测试化**

- 提取 internal async GenerateAsync 方法，原 fire-and-forget 退化为 wrapper；GenerateThumb 改为 internal static
  [`ThumbnailService.cs:16`](../../src/BoxWise.Server/Services/ThumbnailService.cs#L16)

- InternalsVisibleTo 暴露 internal 成员给测试项目
  [`BoxWise.Server.csproj:8`](../../src/BoxWise.Server/BoxWise.Server.csproj#L8)

**外围：测试支持与新建**

- TestDbContextFactory.Create(string) 重载支持命名 InMemory 数据库共享
  [`TestDbContextFactory.cs:9`](../../src/BoxWise.Server.Tests/TestDbContextFactory.cs#L9)

- 4 个 ThumbnailService 测试：缩略图生成、源文件不存在、完整流程验证、Item 不存在静默返回
  [`ThumbnailServiceTests.cs:1`](../../src/BoxWise.Server.Tests/Services/ThumbnailServiceTests.cs#L1)
