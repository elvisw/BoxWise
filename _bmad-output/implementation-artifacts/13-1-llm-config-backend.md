---
baseline_commit: 79d0bae44995241e2fadf5cfff38d749fd0a6cc2
---

# Story 13.1: 服务端 LlmConfig 实体与 API

Status: done

## Story

As a 开发者，
I want 创建 `LlmConfig` 实体、数据库迁移、`GET /api/llm/config` 认证端点，
so that AI 配置（BaseUrl/Model/ApiKey/TimeoutSeconds）安全地存储在服务端数据库中，不再暴露于 `wwwroot/` 匿名静态文件。

## Acceptance Criteria

1. `LlmConfig` 实体创建：Id (固定 1，单行配置)、BaseUrl (string?)、ApiKey (string?)、Model (string, 默认 "doubao-seed-2-0-pro-260215")、TimeoutSeconds (int, 默认 30)
2. `LlmConfigConfiguration : IEntityTypeConfiguration<LlmConfig>` 配置 Id 为主键、各属性列约束
3. `AppDbContext` 新增 `DbSet<LlmConfig> LlmConfigs`，`OnModelCreating` 通过 `ApplyConfigurationsFromAssembly` 自动发现配置
4. EF Core 迁移 `AddLlmConfigEntity` 生成 + 数据库更新
5. `LlmConfigDto` positional record 在 `BoxWise.Shared.Dtos/`（含 `override ToString()` 遮蔽 ApiKey）
6. `LlmConfigEndpoints.cs` — `GET /api/llm/config` 认证端点，返回 200 + `LlmConfigDto`；未配置时返回空配置 DTO（`ApiKey = null`），由客户端判断 ApiKey 是否为空来决定降级为手动输入。Handler 显式检查 `HttpContext.User.Identity?.IsAuthenticated`，无认证返回 401
7. `Program.cs` 注册 `MapLlmConfigEndpoints()`（放在 `MapAdminTwoFactorEndpoints()` 之后、`MapRazorPages()` 之前）
8. `Program.cs` 启动种子数据：从 **Server 端** `IConfiguration` 读取 `LlmApi:*` 配置（来源：`appsettings.json` 的 `LlmApi` 块 + 环境变量 `LlmApi__*`）。仅当 `BaseUrl` 和 `ApiKey` 均非空时自动插入单行；`SaveChangesAsync` 异常兜底不阻断启动
9. `LlmConfigEndpointsTests.cs` 覆盖：已配置返回 200 + 完整 DTO、未配置返回 200 + 空配置 DTO（ApiKey=null）、未认证返回 401、ApiKey 遮蔽验证
10. `dotnet build` 零错误零警告，`dotnet test` 全部通过

## Tasks / Subtasks

- [x] Task 1: 创建 `LlmConfig` 实体 (AC: #1)
  - [x] 创建 `src/BoxWise.Server/Models/LlmConfig.cs`
  - [x] Id = 1 作为业务约定（单行配置），API 层面映射到 `private const int ConfigId = 1`
  - [x] ApiKey 字段为 `string?`（本地部署可选，未配置时 AI 降级为手动输入）

- [x] Task 2: 创建 `LlmConfigConfiguration` (AC: #2)
  - [x] 创建 `src/BoxWise.Server/Data/Configurations/LlmConfigConfiguration.cs`
  - [x] 实现 `IEntityTypeConfiguration<LlmConfig>`，参照 `LocationConfiguration.cs` 模式
  - [x] 各 Property 约束：BaseUrl MaxLength(500)、ApiKey MaxLength(200)、Model IsRequired MaxLength(100)、TimeoutSeconds 默认 30

- [x] Task 3: 更新 `AppDbContext` + EF 迁移 (AC: #3, #4)
  - [x] `public DbSet<LlmConfig> LlmConfigs => Set<LlmConfig>();`
  - [x] 运行 `dotnet ef migrations add AddLlmConfigEntity`（在 `src/BoxWise.Server` 目录）
  - [x] 运行 `dotnet ef database update`

- [x] Task 4: 创建 `LlmConfigDto` (AC: #5)
  - [x] 创建 `src/BoxWise.Shared/Dtos/LlmConfigDto.cs`
  - [x] positional `sealed record`: `(string? BaseUrl, string? ApiKey, string Model, int TimeoutSeconds)`
  - [x] `override ToString()` 遮蔽 ApiKey（参照 `SmtpConfigDto.cs` 模式）

- [x] Task 5: 创建 `LlmConfigEndpoints.cs` (AC: #6)
  - [x] 创建 `src/BoxWise.Server/Endpoints/LlmConfigEndpoints.cs`
  - [x] 添加 using 语句：`using Microsoft.AspNetCore.Http.HttpResults;`、`using Microsoft.EntityFrameworkCore;`、`using BoxWise.Server.Data;`、`using BoxWise.Shared.Dtos;`
  - [x] `static class` + `MapLlmConfigEndpoints(this IEndpointRouteBuilder app)` 扩展方法
  - [x] `GET /api/llm/config` — 直接查询 `AppDbContext.LlmConfigs.FindAsync(1)`（单行配置无需 Repository）
  - [x] Handler 签名含 `HttpContext httpContext`，显式检查 `httpContext.User.Identity?.IsAuthenticated == true`，未认证 → `TypedResults.Unauthorized()`
  - [x] 返回：存在 → `Ok(MapToDto(entity))`，不存在 → `Ok(new LlmConfigDto(null, null, "doubao-seed-2-0-pro-260215", 30))`
  - [x] Entity→DTO 映射：`entity.Model ?? "doubao-seed-2-0-pro-260215"` 兜底 null safety
  - [x] `.Produces<LlmConfigDto>(200)` + `.ProducesProblem(401)` + `.WithTags("LlmConfig")`

- [x] Task 6: 注册端点 (AC: #7)
  - [x] `app.MapLlmConfigEndpoints();` 放在 `MapAdminTwoFactorEndpoints()` 之后

- [x] Task 7: 实现种子数据 (AC: #8)
  - [x] **先决条件：** 在 `src/BoxWise.Server/appsettings.json` 中，`"TwoFactor"` 块闭合后新增 `"LlmApi"` 配置块：`{ "BaseUrl": "", "Model": "doubao-seed-2-0-pro-260215", "TimeoutSeconds": 30 }`（BaseUrl 默认为空字符串——种子仅在实际部署配置提供 BaseUrl+ApiKey 时触发）。ApiKey 通过 Server 的 `appsettings.Production.json`（gitignored）或环境变量 `LlmApi__ApiKey` 注入
  - [x] 在 `Program.cs` 启动迁移块中，`MigrateAsync()` 之后、Admin 种子数据之前，检查 `await db.LlmConfigs.FindAsync(1)`
  - [x] 若不存在，从 `scope.ServiceProvider.GetRequiredService<IConfiguration>()` 读取 `LlmApi:*`
  - [x] **仅当 `!string.IsNullOrWhiteSpace(baseUrl) && !string.IsNullOrWhiteSpace(apiKey)` 时** 才创建记录（与 Admin 种子 `Program.cs:285` 的 `IsNullOrWhiteSpace` 风格一致）
  - [x] `TimeoutSeconds` 使用 `int.TryParse` 容错，解析失败回退默认值 30
  - [x] `try { db.LlmConfigs.Add(config); await db.SaveChangesAsync(); } catch (DbUpdateException ex) { app.Logger.LogWarning(ex, "Failed to seed LlmConfig — continuing"); }`
  - [x] 日志：种子成功 → `LogInformation`，配置不完整 → `LogWarning("LlmConfig not seeded — LlmApi:BaseUrl or LlmApi:ApiKey not configured")`，异常 → `LogWarning`

- [x] Task 8: 创建端点测试 (AC: #9)
  - [x] 创建 `src/BoxWise.Server.Tests/Endpoints/LlmConfigEndpointsTests.cs`
  - [x] 使用 `TestDbContextFactory.Create()` 创建 InMemory 上下文
  - [x] `GetConfig_WhenConfigured_ReturnsOkWithDto` — 种子一条记录，调用 handler，验证 200 + DTO 字段完整
  - [x] `GetConfig_WhenNotConfigured_ReturnsOkWithEmptyDto` — 空表，验证 200 + DTO 中 ApiKey 为 null
  - [x] `GetConfig_WhenUnauthenticated_Returns401` — 传入 `DefaultHttpContext`（`User` 未认证），验证 handler 返回 `Unauthorized()`
  - [x] `GetConfig_ApiKeyMaskedInToString` — 验证 `LlmConfigDto.ToString()` 不包含明文 ApiKey
  - [x] **401 测试实现说明：** Handler 含 `HttpContext httpContext` 参数并显式检查 `httpContext.User.Identity?.IsAuthenticated`（纯认证门控）。通过反射调用时传入 `new DefaultHttpContext()`（未认证 User），handler 内部返回 401，无需中间件。

- [x] Task 9: 验证 (AC: #10)
  - [x] `dotnet build` 零错误零警告
  - [x] `dotnet test` 全部通过

## Dev Notes

### 背景与动机

**问题：** Epic 12 将 AI 识别改为客户端直调火山 ARK API，ApiKey 存放在 `wwwroot/appsettings.Local.json`。但 `MapStaticAssets().AllowAnonymous()`（`Program.cs:392`）使所有 wwwroot 文件匿名可访问，未经认证的用户可通过猜测 URL 直接读取 ApiKey。

**解决方案：** 将 ApiKey 从客户端静态文件迁移至服务端数据库，通过认证 API 安全读取。本 Story 创建后端基础设施；Story 13.2 重构 `AiService` 改为 API 获取；Story 13.3 添加 Admin 管理 UI。

### 当前架构 vs 目标架构

```
CURRENT (安全漏洞):
  浏览器 --[1] HTTP GET appsettings.Local.json (匿名可读)--> MapStaticAssets
  浏览器 --[2] ApiKey 在内存中--> 火山 ARK API

TARGET (本 Story 建立后端):
  浏览器 --[1] 登录认证--> Server (Cookie)
  浏览器 --[2] GET /api/llm/config (需 Cookie auth)--> DB: LlmConfigs
  浏览器 --[3] ApiKey 在内存中--> 火山 ARK API
```

### 关键设计决策

| 决策 | 理由 |
|------|------|
| 单行配置（Id=1） | 应用级配置，非每用户配置。简单直接，避免复杂的配置范围管理 |
| 无需 Repository | 单表单行 `FindAsync(1)` 无需额外抽象层。端点直接依赖 `AppDbContext`。**此例外仅适用于单行配置表（Id=1）——具有多行或 CRUD 操作的表仍需 Repository** |
| ApiKey 明文存储 | 防御性决策：本地 SQLite 文件，与 SMTP DataProtection 加密模式不同。ApiKey 仅通过认证端点返回，会话 Cookie 保证传输安全 |
| 种子数据从 Server 端 `IConfiguration` 读取 | 需在 Server 的 `appsettings.json` 新增 `LlmApi` 配置块（默认值），ApiKey 通过 `appsettings.Production.json`（gitignored）或环境变量 `LlmApi__ApiKey` 注入。**不能从 Client 的 `wwwroot/appsettings.json` 读取——那是 Client 的静态文件，Server 的 `IConfiguration` 访问不到** |
| Handler 显式认证检查 | 参照 `AdminTwoFactorEndpoints.cs` 模式，handler 含 `HttpContext httpContext` 参数并检查 `IsAuthenticated`。不是替代 `FallbackPolicy`，而是防御性编码——外层中间件 + 内层 handler 双重防线，同时确保 401 行为在现有 `TestDbContextFactory` 反射测试框架中可验证 |
| GET 端点无需 Admin 角色 | 所有已认证用户都应能使用 AI 识别功能。Admin 管理接口（PUT/POST）留给 Story 13.3 |
| 未配置时返回空配置 DTO | 替代 `null` 返回，避免 `.Produces<LlmConfigDto>(200)` 与 null body 的 OpenAPI 元数据不一致。客户端通过 `ApiKey == null` 判断降级 |

### 代码模式参考

**实体模式** (`Location.cs:1-13`):
```csharp
namespace BoxWise.Server.Models;

public class LlmConfig
{
    public int Id { get; set; }
    public string? BaseUrl { get; set; }
    public string? ApiKey { get; set; }
    public string Model { get; set; } = "doubao-seed-2-0-pro-260215";
    public int TimeoutSeconds { get; set; } = 30;
}
```

**EF 配置模式** (`LocationConfiguration.cs:1-28`):
- 实现 `IEntityTypeConfiguration<T>`，放在 `Data/Configurations/`
- `HasKey(x => x.Id)` — Id=1 由种子数据保证，非数据库约束
- `Property(x => x.Model).IsRequired().HasMaxLength(100)`
- `Property(x => x.BaseUrl).HasMaxLength(500)`
- `Property(x => x.ApiKey).HasMaxLength(200)`

**端点模式** (`TagEndpoints.cs:1-44`):
- `public static class LlmConfigEndpoints`
- `MapLlmConfigEndpoints(this IEndpointRouteBuilder app)` → 返回 `RouteGroupBuilder`
- handler 为 `private static async`，参数含 `HttpContext httpContext`（用于显式认证检查）+ `AppDbContext db`
- 使用 `TypedResults.Ok()` / `TypedResults.Unauthorized()`
- 认证检查：`httpContext.User.Identity?.IsAuthenticated == true`，否则返回 `TypedResults.Unauthorized()`
- Entity→DTO 映射：`new LlmConfigDto(entity.BaseUrl, entity.ApiKey, entity.Model ?? "doubao-seed-2-0-pro-260215", entity.TimeoutSeconds)` — `Model` 用 `??` 兜底避免 nullable→non-null 的 `CS8600` 警告

**DTO 模式** (`SmtpConfigDto.cs:1-19`):
- positional record with `{ get; }` properties
- `override ToString()` 遮蔽敏感字段

**种子数据模式** (`Program.cs:267-389`):
- 在 `using var scope = app.Services.CreateScope()` 块中
- `db.Database.MigrateAsync()` 之后
- `scope.ServiceProvider.GetRequiredService<IConfiguration>()` 获取配置（**Server 端** ICconfig，非 Client 端）
- 检查是否存在 → 不存在 + 配置完整（`BaseUrl` 和 `ApiKey` 均非空）→ `try { ... SaveChangesAsync } catch (DbUpdateException) { logger.LogWarning }`
- 参照 Admin 种子数据的 `catch (DbUpdateException)` 模式避免启动崩溃

### 需修改的文件清单

| 文件 | 操作 | 说明 |
|------|:--:|------|
| `src/BoxWise.Server/Models/LlmConfig.cs` | NEW | 实体类 |
| `src/BoxWise.Server/Data/Configurations/LlmConfigConfiguration.cs` | NEW | EF 配置 |
| `src/BoxWise.Server/Data/AppDbContext.cs` | MODIFY | 新增 `DbSet<LlmConfig>` |
| `src/BoxWise.Server/Endpoints/LlmConfigEndpoints.cs` | NEW | GET 端点 |
| `src/BoxWise.Server/Program.cs` | MODIFY | 注册端点 + 种子数据 |
| `src/BoxWise.Server/appsettings.json` | MODIFY | 新增 `LlmApi` 配置块（默认值，不含 ApiKey） |
| `src/BoxWise.Shared/Dtos/LlmConfigDto.cs` | NEW | DTO |
| `src/BoxWise.Server/Migrations/*.cs` | NEW (auto) | EF 迁移文件 |
| `src/BoxWise.Server.Tests/Endpoints/LlmConfigEndpointsTests.cs` | NEW | 端点测试 |

### 注意事项

1. **不要修改** `AiService.cs` 或 `Client/Program.cs` — 那是 Story 13.2 的范围
2. **不要修改** `wwwroot/appsettings*.json` — Story 13.2 移除 Client 端的 `LlmApi` 配置块
3. **不要创建** Repository 类 — 单行配置直接通过 `AppDbContext.LlmConfigs.FindAsync(1)` 访问
4. **不要添加** PUT/POST 端点 — Admin 管理接口在 Story 13.3
5. **种子数据幂等** — `FindAsync(1)` 非 null 时跳过，`catch (DbUpdateException)` 兜底并发/锁定，确保重复启动或并发实例不覆盖已有配置、不崩溃
6. **迁移名称** — 使用 `AddLlmConfigEntity`（遵循项目 13 个迁移的 `Add{Entity}Entity` 命名惯例）
7. **测试惯例** — 使用 `TestDbContextFactory.Create()` + InMemory Database。Handler 含 `HttpContext httpContext` 参数，401 通过传入未认证 `DefaultHttpContext` 在 handler 内部验证（无需 `WebApplicationFactory`）
8. **Server 配置源** — 种子数据读的是 **Server 端** `IConfiguration`（来自 Server 的 `appsettings.json` + 环境变量），不是 Client 的 `wwwroot/appsettings.json`。需在 Server 的 `appsettings.json` 中新增 `"LlmApi"` 块（BaseUrl 默认空字符串——种子仅在实际部署提供配置时触发），ApiKey 通过 `appsettings.Production.json` 或 `LlmApi__ApiKey` 环境变量注入
9. **Entity→DTO null safety** — `Model` 在 DB 中为 `IsRequired`，但映射时仍需 `entity.Model ?? "default"` 兜底，避免 `TreatWarningsAsErrors` 下 `CS8600` 编译失败

### Previous Story Intelligence (from Epic 12)

- **退役纪律重要** — Story 12.2 的 `grep -rn` 零残留检查是退役 Story 的 DoD 必须项（本 Story 无退役，但需注意不要遗留 `LlmApi` 配置引用）
- **Epic 12 多次修复模式** — 3 个 Story 共 5 次 `@ fix:` 提交。实施时注意边界条件：配置完整性检查、异常分类、文档一致性
- **Story Task 及时勾选** — Epic 12 教训：代码实现完成后需及时更新 Task 勾选状态

### Git Intelligence

```
79d0bae chore: 批准 Epic 13 Sprint Change Proposal + 记录评审发现 (7 implementor notes)
0171d70 Sprint Change Proposal: Epic 13 — LLM 配置安全迁移至服务端数据库
33d51f9 @ fix: Blazor WASM 改用 HTTP fetch 加载 appsettings.Local.json
fd4d47d refactor: VolcEngine → LlmApi 配置键名（通用化）
```

**7 implementor notes 需在实施中关注（详见 Sprint Change Proposal §5 Review Findings）：**
1. `LlmConfigDto` 需在 `BoxWise.Shared.Dtos` 中新建 positional record
2. AiService 的 `Authorization: Bearer` header 需从构造函数移至 `RecognizeAsync` 懒加载 → **Story 13.2**
3. `LlmApi` HttpClient 的 `BaseAddress` 需在获取服务端配置后动态设置 → **Story 13.2**
4. 配置缓存需线程安全初始化（`SemaphoreSlim` 或 `Lazy<Task<T>>` 模式） → **Story 13.2**
5. `appsettings.Development.json` 中 `LlmApi` 配置块需同步移除 → **Story 13.2**
6. `Program.cs` 中 `AddJsonStream("appsettings.Local.json")` 代码块需移除 → **Story 13.2**
7. **ApiKey 以明文存储在 SQLite 中** — 防御性决策，本 Story 实施

### References

- [Source: Sprint Change Proposal 2026-06-06] `_bmad-output/planning-artifacts/sprint-change-proposal-2026-06-06.md`
- [Source: Architecture §Authentication & Security] `_bmad-output/planning-artifacts/architecture.md#authentication--security`
- [Source: Project Context §关键避坑规则] `_bmad-output/project-context.md`
- [Source: Epic 12 Retro] `_bmad-output/implementation-artifacts/epic-12-retro-2026-06-06.md`
- [Source: Entity Pattern] `src/BoxWise.Server/Models/Location.cs`
- [Source: Configuration Pattern] `src/BoxWise.Server/Data/Configurations/LocationConfiguration.cs`
- [Source: Endpoint Pattern] `src/BoxWise.Server/Endpoints/TagEndpoints.cs`
- [Source: Admin Endpoint Pattern] `src/BoxWise.Server/Endpoints/AdminTwoFactorEndpoints.cs`
- [Source: DTO Pattern] `src/BoxWise.Shared/Dtos/SmtpConfigDto.cs`
- [Source: Seed Data Pattern] `src/BoxWise.Server/Program.cs:267-389`
- [Source: Test Pattern] `src/BoxWise.Server.Tests/Endpoints/TagEndpointsTests.cs`

## Dev Agent Record

### Agent Model Used

deepseek-v4-pro

### Debug Log References

### Review Findings

- [x] [Review][Defer] Missing CancellationToken parameter in GetLlmConfigAsync — pre-existing pattern across all endpoint handlers (TagEndpoints, AdminTwoFactorEndpoints also skip CancellationToken)
- [x] [Review][Defer] ApiKey MaxLength(200) may truncate keys >200 chars — low risk, most providers use <200 char keys. Increase to 500 in future if needed
- [x] [Review][Defer] Default model name hardcoded in 4 places (entity, config, endpoint fallback, DTO fallback) — minor maintainability concern, consolidate in future refactor
- [x] [Review][Defer] TimeoutSeconds zero/negative not validated server-side — client AiService Clamp(5,120) in Story 13.2 handles this. Add server-side guard in Story 13.3 Admin UI

### Completion Notes List

### File List

| 文件 | 操作 |
|------|:--:|
| `src/BoxWise.Server/Models/LlmConfig.cs` | NEW |
| `src/BoxWise.Server/Data/Configurations/LlmConfigConfiguration.cs` | NEW |
| `src/BoxWise.Server/Data/AppDbContext.cs` | MODIFY |
| `src/BoxWise.Server/Endpoints/LlmConfigEndpoints.cs` | NEW |
| `src/BoxWise.Server/Program.cs` | MODIFY |
| `src/BoxWise.Server/appsettings.json` | MODIFY |
| `src/BoxWise.Shared/Dtos/LlmConfigDto.cs` | NEW |
| `src/BoxWise.Server/Migrations/` | NEW (auto) |
| `src/BoxWise.Server.Tests/Endpoints/LlmConfigEndpointsTests.cs` | NEW |

### Completion Notes List

- LlmConfig 实体 + EF Configuration 遵循现有 `Location`/`Item` 模式
- EF 迁移 `AddLlmConfigEntity` 命名遵循项目 13 个迁移惯例
- Handler 显式检查 `IsAuthenticated`（防御性编码），测试通过反射传入 `DefaultHttpContext` 验证
- 种子数据读 Server 端 `IConfiguration`（非 Client wwwroot），仅 `BaseUrl`+`ApiKey` 均非空时插入，`DbUpdateException` 兜底
- `appsettings.json` 新增 `LlmApi` 块（BaseUrl 默认空，种子仅在实际部署提供配置时触发）
- `dotnet build` 零错误零警告，`dotnet test` 277 全部通过（245 Server + 32 Client）
