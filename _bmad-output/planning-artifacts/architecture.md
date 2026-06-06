---
stepsCompleted: [1, 2, 3, 4, 5, 6, 7, 8]
inputDocuments:
  - _bmad-output/planning-artifacts/prds/prd-BoxWise-2026-05-21/prd.md
workflowType: 'architecture'
project_name: '箱知 · BoxWise'
user_name: 'Developer'
date: '2026-05-21'
lastStep: 8
status: 'complete'
completedAt: '2026-05-21'
---

# Architecture Decision Document

_This document builds collaboratively through step-by-step discovery. Sections are appended as we work through each architectural decision together._

## Project Context Analysis

### Requirements Overview

**Functional Requirements (20 FRs across 7 features):**

| Feature | FRs | Architectural Impact |
|---------|-----|---------------------|
| Item Entry + AI (§4.1) | FR-1~6 | Image upload pipeline, OpenAI-compatible API abstraction, graceful degradation |
| Continuous Storage (§4.2) | FR-7~8 | Session-scoped state management (location inheritance) |
| Search (§4.3) | FR-9~10 | Server-side fuzzy search, thumbnail serving |
| Browse (§4.4) | FR-11~13 | Responsive grid, hierarchical filter, tag filter |
| Location Management (§4.5) | FR-14~15 | Tree data model (user-defined depth), recursive queries |
| Item Deletion (§4.6) | FR-16 | Cascade delete (DB record + photo file) |
| Authentication & Accounts (§4.7) | FR-17~20 | ASP.NET Core Identity, Cookie auth, admin backend UI |

**Non-Functional Requirements:**

- **Security:** ASP.NET Core Identity, full endpoint authentication, HTTPS via Caddy/Nginx, HttpOnly/Secure cookies
- **Performance:** 1C1G Linux VPS, ≤5 users, <2s first screen (100 items), <500ms search, lazy-loaded thumbnails
- **PWA / Offline:** Service Worker with Stale-While-Revalidate, offline read-only mode
- **AI Reliability:** 30s timeout (browser-side), single OpenAI-compatible model, silent fallback to manual entry
- **Data:** SQLite single file, file-system image storage, no hard size limits, persistent volume

### Scale & Complexity

- **Primary domain:** Full-stack Web (Blazor WASM + ASP.NET Core)
- **Complexity level:** Medium — full-stack with AI integration and auth, but minimal user scale (≤5)
- **Estimated architectural components:** 7 (Frontend PWA, Backend API, Auth Layer, AI Abstraction, File Service, Database, Admin UI)

### Technical Constraints & Dependencies

- C# full-stack (Blazor WASM + ASP.NET Core) — non-negotiable per tech stack decision
- SQLite + EF Core — single-file, zero-config database
- ASP.NET Core Identity — chosen authentication framework
- 火山引擎 ARK API（OpenAI 兼容）— 浏览器端直接调用，CORS 已通过 Playwright 实测确认
- Linux VPS deployment — single machine, Caddy/Nginx reverse proxy
- PWA — no native app, browser-based with install capability

### Cross-Cutting Concerns Identified

1. **Authentication boundary** — Every API endpoint except login requires auth; Blazor WASM needs token/cookie propagation
2. **Image pipeline** — Capture (client) → Upload (API) → Store (file system) → Serve (static files or API) → Thumbnail generation
3. **AI abstraction** — Single configurable provider interface, must fail gracefully without blocking the entry flow
4. **Hierarchical data** — Location tree of arbitrary depth requires recursive query strategy in EF Core + SQLite
5. **PWA offline** — Service worker caching strategy distinguishes read (cached) from write (online-only)
6. **Admin backend** — Account management UI separated from main app, access control considerations

---

## Starter Template Evaluation

### Primary Technology Domain

.NET 10 全栈 Web（Blazor WASM + ASP.NET Core Web API），基于 SDK 10.0.300。

### Starter Options Considered

| 方案 | 评估 |
|------|------|
| `blazor` (Blazor Web App 统一模板) | 适合 SSR+CSR 混合场景，额外复杂度（RenderMode、`.Client` 子项目）对纯 SPA 架构无益 |
| `blazorwasm` + `webapi` 分离 | 前后端独立部署，职责清晰，适合 PWA + API 架构 |
| 含 `--auth Individual` | 使用 Azure AD/OIDC，**不适合**本地 Identity + Cookie 认证需求 |

### Selected Starter: 独立模板组合 + 手动集成 Identity

**Rationale for Selection:**
BoxWise 是前后端分离的 SPA + API 架构，`blazorwasm`（独立 WebAssembly）和 `webapi`（Web API）模板分离是最佳匹配。两个模板的 `--auth` 选项均不支持本地 Cookie 认证，以 `--auth None` 创建后手动集成 ASP.NET Core Identity。

**Initialization Command:**

```bash
dotnet new sln -n BoxWise
dotnet new blazorwasm --pwa --empty -n BoxWise.Client -o src/BoxWise.Client --framework net10.0
dotnet new webapi -n BoxWise.Server -o src/BoxWise.Server --framework net10.0
dotnet new classlib -n BoxWise.Shared -o src/BoxWise.Shared --framework net10.0
dotnet sln add src/BoxWise.Client src/BoxWise.Server src/BoxWise.Shared
dotnet add src/BoxWise.Server reference src/BoxWise.Shared
dotnet add src/BoxWise.Client reference src/BoxWise.Shared
```

**构建基础设施（Directory.Build 体系）：**

```
BoxWise/
├── Directory.Build.props          ← 根级：Nullable/ImplicitUsings/代码分析
├── Directory.Build.targets        ← 根级：自定义构建目标
├── Directory.Packages.props       ← CPM 集中包版本管理
├── BoxWise.sln
├── src/
│   ├── Directory.Build.props      ← src 级（链式导入父级）
│   ├── BoxWise.Client/
│   ├── BoxWise.Server/
│   └── BoxWise.Shared/
```

**Note:** 项目初始化使用上述命令应为实现的第一个 story。

---

## Core Architectural Decisions

### Decision Priority Analysis

**Critical Decisions (Block Implementation):**
- Data architecture: Minimal API, Materialized Path, ImageSharp thumbnails
- Authentication: ASP.NET Core Identity + Cookie + Blazor WASM CookieAuthenticationStateProvider
- Deployment: Docker + Caddy + Linux VPS (1C1G)

**Important Decisions (Shape Architecture):**
- Frontend: Scoped AppState, component tree, PWA cache strategy
- API: TypedResults + ProblemDetails, route hierarchy, strict OpenAPI
- Admin: Independent Razor Pages area with Admin role (IdentityRole)

**Deferred Decisions (Post-MVP):**
- CI/CD pipeline
- Automated backup strategy
- Image editing/cropping

---

### Data Architecture

#### API Style: Minimal API

**Decision:** ASP.NET Core Minimal API with `RouteGroupBuilder` for endpoint organization.

**Rationale:** Controller-based introduces unnecessary abstraction (BaseController, Action Filter pipeline, ControllerBase IL) for a ≤5 user project. Minimal API's `IResult` + `TypedResults` are natively testable. Swagger/OpenAPI generation is mature in .NET 10.

**Route groups:**
- `/api/auth` — Authentication
- `/api/items` — Item CRUD + search + filter
- `/api/locations` — Location hierarchy
- `/api/tags` — Tag listing
- `/api/images` — Image serving (thumbnails + originals)

**OpenAPI Discipline:** Every endpoint must declare:
```csharp
group.MapPost("/items", CreateItem)
    .Produces<ItemDto>(201)
    .ProducesProblem(400)
    .ProducesProblem(401)
    .WithTags("Items")
    .WithDescription("录入一件物品");
```

#### Error Handling: TypedResults + ProblemDetails

**Decision:** Success responses use `TypedResults.Ok()` / `TypedResults.Created()` etc. Validation failures and exceptions use `AddProblemDetails()` middleware producing RFC 7807-compliant error responses.

#### Hierarchical Location Tree: Materialized Path

**Decision:** Materialized path pattern stored as `Path TEXT NOT NULL` column (e.g., `"/1/3/7/"`), with `SortOrder INT` for same-level ordering.

**Rationale:** EF Core recursive CTE in SQLite requires raw SQL (bypasses change tracker) and has O(depth) per query. Materialized path uses B-tree indexed `LIKE` for subtree queries (O(log N)) and single-`UPDATE` subtree moves.

**Implementation notes:**
- Subtree query: `WHERE Path LIKE '/1/3/%'`
- Subtree move: `UPDATE Location SET Path = REPLACE(Path, '/1/3/', '/1/5/') WHERE Path LIKE '/1/3/%'`
- Depth validation: Application-layer check on path separator count
- All operations encapsulated in `LocationRepository`

#### Image Processing: SkiaSharp + Two-Level Thumbnails

**Decision:** SkiaSharp generates two thumbnail sizes on upload: 300px wide (list/grid view) and 1200px wide (detail view). Thumbnail generation runs asynchronously in background via `Task.Run` with scoped `IServiceScopeFactory`. Upload endpoint returns 202 Accepted immediately.

**Rationale:** 1C1G VPS cannot serve 4-5MB original images in grid view. Thumbnails (50-100KB) enable responsive browsing. SkiaSharp (MIT license) is cross-platform — no libgdiplus dependency on Linux, no commercial license required.

**Pipeline:**
```
Upload (multipart/form-data) → Validate → Save original
  → Return 202 { imageId }
  → Background: Generate thumb (300px) + medium (1200px)
  → Update DB record
```

**Caching:** Reverse proxy (Caddy) sets `Cache-Control: public, max-age=86400` on `/images/thumb/` and `/images/medium/` paths.

---

### Authentication & Security

#### Identity Integration: Cookie + Blazor WASM

**Decision:** ASP.NET Core Identity with Cookie authentication. Login, logout, 2FA verification, and account management are handled by Identity scaffold Razor Pages (`Areas/Identity/Pages/Account/`). Blazor WASM uses a custom `CookieAuthenticationStateProvider` that calls `/api/auth/me` on startup to retrieve the current authenticated user. WebAuthn/Passkey login is retained in Blazor WASM (`Login.razor`).

**Rationale:** Cookie-based auth is the standard pattern for Blazor WASM standalone with Identity (per Microsoft Learn: `standalone-with-identity`). No JWT token management needed — the browser automatically sends cookies with every API request.

**Cross-domain:** Server and client are deployed behind the same reverse proxy (same origin), eliminating CORS cookie configuration.

#### Admin UI: Independent Razor Pages Area

**Decision:** Admin functionality (account management) lives in a separate Razor Pages area at `/admin` on the server project. Protected by `AdminOnly` policy (`RequireRole("Admin")`).

**Rationale:** Separating admin from the Blazor WASM client avoids mixing admin UI into the main PWA bundle and provides a natural route-level protection boundary.

#### Admin Identification: Admin Role

**Decision:** ASP.NET Core Identity `IdentityRole` ("Admin" role) distinguishes administrators. The seed admin user is assigned the Admin role. `IsInRoleAsync(user, "Admin")` is used server-side, and the `IsAdmin` boolean is propagated to the client via `AuthUserDto.IsAdmin`.

#### API Authorization: Authenticated-Only

**Decision:** `[Authorize]` attribute = authenticated user only. No role-based authorization in v1 — all authenticated members have equal permissions per PRD §3 Glossary.

**Anonymous endpoints (only):** `/api/auth/login`, `/api/auth/logout`.

**Affects:** FR-17, FR-18, FR-19, FR-20

#### Admin UI Expansion: Continue on Identity + Razor Pages

**Decision (2026-05-27):** 用户管理功能扩展（编辑用户、修改密码、删除用户、角色分配、用户自助改密/改信息）继续基于 ASP.NET Core Identity 内置 API + 自建 Razor Pages Admin UI 实现。不引入 Microsoft Identity UI (`AddDefaultUI`)、Auth0/Clerk、ABP Framework 或其他第三方管理层。

**Rationale:**
- 所有缺失功能的 API 已由 `UserManager<T>` 内置提供，当前缺失的仅是调用这些 API 的 UI 页面
- Microsoft Identity UI 设计为自助注册场景，不提供 Admin CRUD/角色管理页面，且 UI 风格（Bootstrap）与项目（MudBlazor）不统一
- 云托管方案（Auth0/Clerk）对 ≤5 人家用场景过重
- .NET 生态中不存在仅提供 "Identity Admin 管理 UI" 的独立轻量库
- 自建 Admin UI 工作量小（2-3 个 Razor Page），架构一致，无新依赖

**Admin Razor Pages (updated structure):**
```
Pages/Admin/
├── Index.cshtml              ← 账户列表（含操作列：编辑/删除/角色切换）
├── CreateAccount.cshtml      ← 创建账户
├── EditAccount.cshtml        ← [新增] 编辑用户信息
├── ChangeUserPassword.cshtml ← [新增] 修改用户密码
├── _Layout.cshtml
├── _ViewImports.cshtml
└── _ViewStart.cshtml
```

**New FRs:** FR-21~FR-26 (see Sprint Change Proposal 2026-05-27)
**New Epic:** Epic 5 — 用户管理增强 (proposed)

#### Identity 脚手架混合模式迁移 (Epic 10-11, 2026-06-02)

**决策：** 用 ASP.NET Core Identity 脚手架 Razor Pages 替换手写认证 UI 和 2FA 设置管理，退役 ~1600 行代码。

**迁移范围：**
- 登录/登出：`Areas/Identity/Pages/Account/Login.cshtml` + `Logout.cshtml`（替代 `AuthEndpoints.LoginAsync/LogoutAsync` + `Login.razor` 用户名密码表单）
- 2FA 登录验证：`LoginWith2fa.cshtml` + `LoginWithRecoveryCode.cshtml`（替代 `TwoFactorEndpoints.VerifyAsync/VerifyRecoveryCodeDuringLoginAsync`）
- 账户管理：`Account.Manage.*` 系列页面（替代 `TwoFactorModifyEndpoints.cs` + `TwoFactorManage.razor`）
- 邮箱验证：`Account.Manage.Email`（替代 `EmailVerificationEndpoints.cs`）
- 密码修改：`Account.Manage.ChangePassword`（替代 `ChangePasswordDialog.razor`）

**保留的手写代码：**
- WebAuthn/Passkey 端点（`WebAuthnEndpoints.cs`）——通行密钥登录不可替代
- `CookieAuthenticationStateProvider` —— WASM 感知服务器 Cookie 的核心桥接
- `GET /api/auth/me` —— 认证状态同步端点
- `RecoveryCodeService` —— WebAuthn 注册后恢复码生成 + Admin 后台依赖

**关键架构决策：**
- 通行密钥验证成功后 `WebAuthnEndpoints.LoginCompleteAsync` 直接 `SignInAsync`，不经过 2FA 验证——通行密钥本身作为硬件令牌已满足第二因子要求
- Identity 页面使用 Bootstrap 默认样式，不与 MudBlazor 做样式桥接——双 UI 风格并存是已接受的权衡
- .NET 10 `GetTwoFactorAuthenticationUserAsync()` Bug (#66929)：在 `LoginWith2fa.cshtml.cs` / `LoginWithRecoveryCode.cshtml.cs` PageModel 中应用 workaround，待上游修复后移除

---

### API & Communication Patterns

#### Route Structure

```
/api/auth/me            GET    认证  当前用户信息
/api/auth/webauthn/*    POST   匿名/认证  通行密钥登录/注册
/api/items              GET    认证  搜索 + 筛选（query params: q, locationId, tagId）
/api/items              POST   认证  创建物品（multipart: JSON + image file）
/api/items/{id}         GET    认证  物品详情
/api/items/{id}         DELETE 认证  删除物品（FR-16）
/api/locations          GET    认证  位置树
/api/locations          POST   认证  创建位置
/api/locations/{id}     PUT    认证  更新位置（含重命名/移动）
/api/locations/{id}     DELETE 认证  删除空位置
/api/locations/{id}/children  GET  认证  子节点
/api/tags               GET    认证  标签列表 + 物品计数
/api/tags               POST   认证  创建标签
/api/images/upload      POST   认证  上传物品照片（multipart/form-data）
/api/images/{itemId}    GET    认证  图片文件（type=thumb|medium|original）
/api/admin/accounts     GET    Admin 账户列表
/api/admin/accounts     POST   Admin 创建账户
```

#### Cross-Component Dependencies

| Dependency | From | To | Mechanism |
|------------|------|-----|-----------|
| Auth state | Blazor WASM | Server API | Cookie + `/api/auth/me` |
| Image upload | Blazor WASM | Server API | `multipart/form-data` POST |
| Location tree | Server | SQLite | EF Core + Materialized Path |
| AI recognition | Browser (Client) | 火山 ARK API (北京) | fetch + CORS |
| Thumbnail gen | Server (background) | File system | ImageSharp resize |
| Static files | Browser | Caddy | File server + reverse proxy |

---

### Frontend Architecture

#### State Management: Scoped DI AppState

**Decision:** A single `AppState` class registered as `AddScoped<AppState>()` in DI. Properties: `CurrentUser`, `ContinuousStorageLocation`, `IsLoggedIn`. Components inject via `@inject AppState`.

**Rationale:** Simple enough to avoid Fluxor/Blazor-State. Scoped lifetime ensures state isolation per browser session.

#### Component Architecture

```
BoxWise.Client/
├── Layout/
│   └── MainLayout.razor          ← 导航栏 + 登录状态
├── Pages/
│   ├── Index.razor               ← 首页（搜索 + 最近录入）
│   ├── Login.razor               ← 登录页
│   ├── Browse.razor              ← 网格浏览 + 筛选
│   ├── ItemDetail.razor          ← 物品详情 + 删除
│   ├── ItemEntry.razor           ← 录入页（拍照 + AI 预填）
│   └── Admin/Accounts.razor      ← 管理端：账户列表/创建（Razor Pages 侧）
├── Components/
│   ├── ItemCard.razor            ← 网格卡片
│   ├── LocationTree.razor        ← 层级位置树
│   ├── TagFilter.razor           ← 标签筛选器
│   ├── ImageUploader.razor       ← 拍照/上传组件
│   └── SearchBar.razor           ← 搜索栏
└── Services/
    ├── ApiClient.cs              ← HttpClient 封装
    ├── AuthService.cs            ← 登录/状态管理
    └── AppState.cs               ← 全局状态
```

#### PWA Cache Strategy

| Resource | Strategy |
|----------|----------|
| `_framework/*.dll`, `*.wasm` | Cache-First (immutable per deploy) |
| `/images/thumb/*`, `/images/medium/*` | Stale-While-Revalidate |
| `/api/*` | Network-Only |
| `index.html`, `manifest.webmanifest`, icons | Cache-First |

**Rationale:** Framework files are immutable per deployment. Images have long cache lifetime but may update. API responses must always be fresh.

---

### Infrastructure & Deployment

#### Containerization: Docker Multi-Stage Build

**Decision:** Dockerfile with SDK build stage + ASP.NET Runtime stage. Base image: `mcr.microsoft.com/dotnet/aspnet:10.0`.

**Persistence:** Docker volume mounts for data directory (`./data:/app/data`) containing SQLite database and uploaded images. Reverse proxy config (`./caddy:/etc/caddy`).

#### Reverse Proxy: Caddy

**Decision:** Caddy as reverse proxy with automatic Let's Encrypt TLS.

**Rationale:** Single binary, auto TLS, Caddyfile ~10 lines. Nginx requires certbot + cron for the same functionality. Caddy's simplicity is ideal for a 1C1G single-admin VPS.

**Caddyfile:**
```
boxwise.example.com {
    reverse_proxy /api/* localhost:5000
    reverse_proxy /admin/* localhost:5000
    root * /var/www/boxwise
    file_server
    encode gzip
    header /images/* Cache-Control "public, max-age=86400"
}
```

#### Environment Configuration

**Decision:** `appsettings.json` holds defaults (connection string template, LLM base URL default). `appsettings.Production.json` (gitignored) holds production secrets. Sensitive values (LLM API key, Identity signing key) injected via environment variables in `docker-compose.yml`.

#### Deployment Flow

```bash
dotnet publish -c Release
docker build -t boxwise:latest .
docker compose up -d
```

**docker-compose.yml components:**
- `boxwise` service: Server + static file serving
- Volume mount: `./data:/app/data` (SQLite + images persistence)
- Volume mount: `./caddy:/etc/caddy` (Caddyfile)
- Port: 443 → Caddy → boxwise:5000

---

### Decision Impact Analysis

**Implementation Sequence (ordered by dependency):**

1. Solution + project scaffolding (`dotnet new`)
2. Directory.Build infrastructure (props, targets, CPM)
3. Database schema: Identity tables + Location (materialized path) + Item + Tag + ItemTag
4. Authentication layer: Identity setup, Cookie config, `/api/auth/*` endpoints
5. Core CRUD: Items + Locations + Tags API endpoints
6. Image pipeline: Upload endpoint + ImageSharp thumbnail generation
7. AI abstraction: OpenAI-compatible API client + configurable base URL
8. Frontend shell: Layout, routing, AppState, AuthService
9. Frontend pages: Login → ItemEntry → Browse → ItemDetail
10. Admin area: Razor Pages accounts management
11. PWA: Service Worker config, manifest, icons
12. Deployment: Dockerfile, Caddyfile, docker-compose.yml

---

## Implementation Patterns & Consistency Rules

### Critical Conflict Points Identified

7 areas where AI agents could make different choices: naming conventions, API response format, EF Core patterns, DI registration, image paths, error handling, Blazor component structure.

### Naming Patterns

**Database Naming:**
- Tables: Plural PascalCase — `Items`, `Locations`, `Tags`, `ItemTags` (EF Core default convention)
- Columns: PascalCase — `Id`, `Name`, `PhotoPath`, `CreatedAt`
- Foreign keys: Singular PascalCase + `Id` — `LocationId`, `CreatedByUserId`
- Identity tables: Use ASP.NET Core Identity defaults — `AspNetUsers`, `AspNetRoles`, etc.

**API Naming:**
- Routes: lowercase, resource-based plural — `/api/items`, `/api/locations`
- Query parameters: camelCase — `?locationId=3&tagId=5&q=数据线`
- JSON fields: camelCase — `{ "itemName": "...", "photoPath": "..." }` (System.Text.Json default)

**Code Naming:**
- C# standard: PascalCase public, camelCase parameters/variables, `_camelCase` private fields
- DTO classes: Suffix `Dto`/`Request`/`Response` — `ItemDto`, `CreateItemRequest`
- Blazor components: PascalCase `.razor` files — `ItemCard.razor`, `LocationTree.razor`
- Service classes: Suffix `Service`/`Repository` — `ItemService`, `LocationRepository`

### Structure Patterns

**Server Project Organization:**
```
BoxWise.Server/
├── Program.cs
├── Areas/
│   └── Identity/
│       └── Pages/
│           └── Account/          ← Identity 脚手架 Razor Pages
├── Endpoints/
│   ├── AuthEndpoints.cs
│   ├── ItemEndpoints.cs
│   ├── LocationEndpoints.cs
│   ├── TagEndpoints.cs
│   ├── ImageEndpoints.cs
│   ├── WebAuthnEndpoints.cs
│   └── AdminTwoFactorEndpoints.cs
├── Data/
│   ├── AppDbContext.cs
│   └── Migrations/
├── Models/
│   ├── Item.cs
│   ├── Location.cs
│   ├── Tag.cs
│   └── AppUser.cs
├── Services/
│   ├── ItemService.cs
│   ├── LocationRepository.cs
│   ├── ImageProcessor.cs
│   └── IdentityEmailSender.cs    ← IEmailSender 适配器
├── Utilities/
│   └── AuthConstants.cs
├── Dtos/
│   ├── ItemDto.cs
│   ├── CreateItemRequest.cs
│   └── LoginRequest.cs
├── Pages/Admin/
│   ├── Index.cshtml           ← 账户列表
│   └── CreateAccount.cshtml   ← 创建账户
└── appsettings.json
```

**Client Project Organization:**
```
BoxWise.Client/
├── Layout/MainLayout.razor
├── Pages/ (as defined in §Frontend Architecture)
├── Components/ (as defined in §Frontend Architecture)
└── Services/ (as defined in §Frontend Architecture)
```

### Format Patterns

**API Response Format:**
- Success (single): `TypedResults.Ok(dto)` → `200 { ...dto fields... }`
- Success (list): `TypedResults.Ok(list)` → `200 [item1, item2, ...]` — no wrapper object
- Created: `TypedResults.Created($"/api/items/{item.Id}", dto)` → `201` + `Location` header
- Deleted: `TypedResults.NoContent()` → `204`
- Error: `TypedResults.Problem(detail, statusCode: 400)` → RFC 7807 `{ type, title, status, detail }`
- List metadata: `X-Total-Count` response header on search/filter results

**Date/Time Format:**
- API: ISO 8601 strings — `"2026-05-21T15:30:00Z"` (UTC)
- Database: SQLite `TEXT` column ISO 8601 (no native datetime type)

### EF Core Patterns

**Entity Configuration:**
```csharp
// Use IEntityTypeConfiguration<T> — no attributes on entity classes
public class ItemConfiguration : IEntityTypeConfiguration<Item> {
    public void Configure(EntityTypeBuilder<Item> builder) {
        builder.ToTable("Items");
        builder.HasKey(i => i.Id);
        builder.Property(i => i.Name).IsRequired().HasMaxLength(200);
        builder.Property(i => i.PhotoPath).HasMaxLength(500);
        builder.HasOne(i => i.Location)
            .WithMany(l => l.Items)
            .HasForeignKey(i => i.LocationId);
    }
}
```

**Materialized Path Queries:**
```csharp
// All agents MUST use this pattern for hierarchical location queries
public async Task<List<Location>> GetChildren(string parentPath) =>
    await context.Locations
        .Where(l => l.Path.StartsWith(parentPath) && l.Path != parentPath)
        .OrderBy(l => l.SortOrder)
        .ToListAsync();

// Subtree move
public async Task MoveSubtree(string oldPath, string newPath) {
    await context.Locations
        .Where(l => l.Path.StartsWith(oldPath))
        .ExecuteUpdateAsync(s => s
            .SetProperty(l => l.Path, l => l.Path.Replace(oldPath, newPath)));
    await context.SaveChangesAsync();
}
```

**Cascade Delete (application-level):**
```csharp
public async Task DeleteItem(int id) {
    var item = await context.Items.FindAsync(id)
        ?? throw new NotFoundException($"Item {id} not found");
    // Delete physical files
    File.Delete(Path.Combine(imageRoot, item.PhotoPath));
    File.Delete(Path.Combine(imageRoot, item.ThumbPath));
    File.Delete(Path.Combine(imageRoot, item.MediumPath));
    context.Items.Remove(item);
    await context.SaveChangesAsync();
}
```

### DI Registration Patterns

```csharp
// Server Program.cs — lifetime rules:
builder.Services.AddScoped<ItemService>();          // Per-request business logic
builder.Services.AddScoped<LocationRepository>();    // Per-request data access

builder.Services.AddScoped<ImageProcessor>();        // May need IServiceScopeFactory for background work

// Client Program.cs — all scoped:
builder.Services.AddScoped<ApiClient>();
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<AppState>();
```

### Image Path Conventions

```
Data directory root (configurable via appsettings):
  └── images/
      └── {itemId}/
          ├── original.jpg     ← uploaded file (JPEG/PNG)
          ├── thumb.jpg        ← 300px wide (ImageSharp resize)
          └── medium.jpg       ← 1200px wide (ImageSharp resize)
```

**Rules:**
- Physical root: `{DataDirectory}/images/` (default: `./data/images/`)
- DB `PhotoPath`: relative path — `images/{itemId}/original.jpg`
- DB `ThumbPath`: relative path — `images/{itemId}/thumb.jpg`
- DB `MediumPath`: relative path — `images/{itemId}/medium.jpg`
- API response: map relative path to URL — `/images/{itemId}/thumb.jpg`
- File naming: always JPEG output regardless of input — `original.jpg`, `thumb.jpg`, `medium.jpg`

### Enforcement Guidelines

**All AI Agents MUST:**
- Use `IEntityTypeConfiguration<T>` over data annotations for EF Core configuration
- Use `TypedResults.*` static methods — never `Results.*` instance methods
- Return arrays for lists, not wrapper objects — `[item1, item2]` not `{ data: [item1, item2] }`
- Register services with correct DI lifetime (Scoped for per-request, Singleton for stateless)
- Follow the materialized path query pattern exactly — no raw SQL for location queries
- Use `ProblemDetails` for all error responses — no custom error DTOs
- Write relative paths to DB, not absolute paths
- Place endpoint definitions in `Endpoints/` folder, one file per resource group

### Anti-Patterns (DO NOT USE)

- Mixing Minimal API `MapGet` with Controller `[HttpGet]` — pick Minimal API, stick with it
- Writing raw SQL for location queries — use the materialized path pattern
- `async void` — always `async Task` or `async Task<T>`
- Direct `DbContext` usage in endpoints — go through Service/Repository layer
- `File.Exists()` then `File.Delete()` without exception handling — use try/catch around file I/O
- Hardcoding directory paths — read from `IConfiguration` or `IOptions<T>`

---

## Project Structure & Boundaries

### Complete Project Directory Structure

```
BoxWise/
├── BoxWise.sln
├── Directory.Build.props
├── Directory.Build.targets
├── Directory.Packages.props
├── .gitignore
├── .dockerignore
├── docker-compose.yml
├── Dockerfile
├── Caddyfile
├── README.md
│
├── src/
│   ├── Directory.Build.props
│   │
│   ├── BoxWise.Server/                    # ASP.NET Core Web API + Identity
│   │   ├── BoxWise.Server.csproj
│   │   ├── Program.cs
│   │   ├── appsettings.json
│   │   ├── appsettings.Development.json
│   │   ├── Endpoints/
│   │   │   ├── AuthEndpoints.cs           # /api/auth/*
│   │   │   ├── ItemEndpoints.cs           # /api/items/*
│   │   │   ├── LocationEndpoints.cs       # /api/locations/*
│   │   │   ├── TagEndpoints.cs            # /api/tags
│   │   │   ├── ImageEndpoints.cs          # /api/images/*
│   │   │   └── AdminEndpoints.cs          # Admin API backing
│   │   ├── Data/
│   │   │   ├── AppDbContext.cs
│   │   │   └── Configurations/
│   │   │       ├── ItemConfiguration.cs
│   │   │       ├── LocationConfiguration.cs
│   │   │       ├── TagConfiguration.cs
│   │   │       └── AppUserConfiguration.cs
│   │   ├── Models/
│   │   │   ├── Item.cs
│   │   │   ├── Location.cs
│   │   │   ├── Tag.cs
│   │   │   └── AppUser.cs
│   │   ├── Dtos/
│   │   │   ├── ItemDto.cs
│   │   │   ├── CreateItemRequest.cs
│   │   │   ├── LocationDto.cs
│   │   │   ├── LoginRequest.cs
│   │   │   └── AccountCreateRequest.cs
│   │   ├── Services/
│   │   │   ├── ItemService.cs
│   │   │   ├── LocationRepository.cs
│   │   │   └── ImageProcessor.cs
│   │   └── Pages/
│   │       └── Admin/
│   │           ├── Index.cshtml           # 账户列表
│   │           ├── CreateAccount.cshtml   # 创建账户
│   │           └── _Layout.cshtml
│   │
│   ├── BoxWise.Client/                    # Blazor WASM PWA
│   │   ├── BoxWise.Client.csproj
│   │   ├── Program.cs
│   │   ├── App.razor
│   │   ├── _Imports.razor
│   │   ├── Layout/
│   │   │   └── MainLayout.razor
│   │   ├── Pages/
│   │   │   ├── Index.razor                # FR-9/11 首页搜索+浏览
│   │   │   ├── Login.razor                # FR-18 登录
│   │   │   ├── Browse.razor               # FR-11/12/13 网格+筛选
│   │   │   ├── ItemDetail.razor           # FR-10/16 详情+删除
│   │   │   └── ItemEntry.razor            # FR-1~6 录入+AI
│   │   ├── Components/
│   │   │   ├── ItemCard.razor
│   │   │   ├── LocationTree.razor
│   │   │   ├── TagFilter.razor
│   │   │   ├── ImageUploader.razor
│   │   │   └── SearchBar.razor
│   │   ├── Services/
│   │   │   ├── ApiClient.cs
│   │   │   ├── AuthService.cs
│   │   │   └── AppState.cs
│   │   └── wwwroot/
│   │       ├── index.html
│   │       ├── manifest.webmanifest
│   │       ├── service-worker.js
│   │       ├── service-worker.published.js
│   │       ├── icon-192.png
│   │       ├── icon-512.png
│   │       └── css/
│   │           └── app.css
│   │
│   └── BoxWise.Shared/                    # 共享合约
│       ├── BoxWise.Shared.csproj
│       └── Dtos/
│           ├── ItemSummaryDto.cs
│           └── LocationTreeNode.cs
│
└── tests/
    └── BoxWise.Server.Tests/
        ├── BoxWise.Server.Tests.csproj
        ├── Services/
        │   ├── ItemServiceTests.cs
        │   └── LocationRepositoryTests.cs
        └── Endpoints/
            ├── ItemEndpointsTests.cs
            └── AuthEndpointsTests.cs
```

### Requirements to Structure Mapping

| FR Group | Primary Files |
|----------|--------------|
| FR-1~6 Item Entry + AI | `ItemEntry.razor` → `ItemEndpoints.cs` → `ItemService.cs` → `ImageProcessor.cs` |
| FR-7~8 Continuous Storage | `AppState.cs` (client-state) + `ItemEndpoints.cs` |
| FR-9~10 Search | `SearchBar.razor` → `ItemEndpoints.cs` → `ItemService.cs` → EF Core `LIKE` query |
| FR-11~13 Browse + Filter | `Browse.razor` + `ItemCard.razor` + `LocationTree.razor` + `TagFilter.razor` → `ItemEndpoints.cs` |
| FR-14~15 Location Management | `LocationTree.razor` → `LocationEndpoints.cs` → `LocationRepository.cs` |
| FR-16 Item Deletion | `ItemDetail.razor` → `ItemEndpoints.cs` → `ItemService.cs` |
| FR-17~20 Auth + Accounts | `Login.razor` + `Pages/Admin/` → `AuthEndpoints.cs` + ASP.NET Core Identity |

### Architectural Boundaries

**API Boundaries:**
- `/api/auth/*` — Anonymous endpoints: login, logout. Authenticated: `/me`.
- `/api/items/*` — Authenticated. CRUD + search + filter via query params.
- `/api/locations/*` — Authenticated. Tree operations via materialized path.
- `/api/tags` — Authenticated. Read-only tag listing with item counts.
- `/api/images/*` — Authenticated. Serve thumb/medium/original via `?type=` query param.
- `/api/admin/*` — `AdminOnly` policy (`RequireRole("Admin")`). Account CRUD.

**Component Boundaries:**
- Client ↔ Server: HTTP REST, Cookie-based auth, same-origin via Caddy reverse proxy
- Server ↔ SQLite: EF Core via `AppDbContext`, synchronous calls
- Browser ↔ 火山 ARK API: 客户端直调，30s 超时（CORS），静默降级
- Server ↔ File System: `ImageProcessor` reads/writes `{DataDirectory}/images/`
- Client ↔ Browser Cache: Service Worker with resource-type-differentiated strategies

**Data Flow:**
```
User Action → Blazor Component → ApiClient (HttpClient + Cookie)
  → Caddy (HTTPS) → ASP.NET Core Endpoint → Service/Repository
  → EF Core → SQLite
  → Response → JSON → Component re-render
```

### Development Workflow Integration

**Local Development:**
```bash
cd src/BoxWise.Server && dotnet run     # API on localhost:5000
cd src/BoxWise.Client && dotnet run     # WASM on localhost:5001
```

**Build:**
```bash
dotnet build BoxWise.sln
dotnet test tests/BoxWise.Server.Tests
```

**Production Deployment:**
```bash
dotnet publish src/BoxWise.Server -c Release -o /app
docker build -t boxwise:latest .
docker compose up -d
```

---

## Architecture Validation Results

### Coherence Validation

| Check | Status | Notes |
|-------|--------|-------|
| .NET 10 + Blazor WASM + ASP.NET Core | ✅ | Unified framework — no version conflicts |
| Minimal API + Identity Cookie Auth | ✅ | Mature support in .NET 10, per Microsoft Learn `standalone-with-identity` |
| Materialized Path + SQLite | ✅ | String `LIKE` B-tree indexed, efficient for ≤5 user scale |
| ImageSharp + Linux VPS | ✅ | Pure managed code — no libgdiplus dependency |
| Docker + Caddy + HTTPS | ✅ | Caddy auto Let's Encrypt TLS |
| Blazor WASM PWA + Offline | ✅ | `--pwa` flag generates Service Worker scaffold |

**Decision Compatibility:** All technology choices are compatible. No contradictions found.

**Pattern Consistency:** Implementation patterns align with Minimal API, EF Core, and Blazor WASM conventions. Naming conventions follow C#/.NET standards consistently.

**Structure Alignment:** Project structure directly supports all 7 feature groups with clear component-to-endpoint mapping.

### Requirements Coverage Validation

**Functional Requirements Coverage (20/20 FRs):**

| FR Group | Coverage | Primary Implementation Path |
|----------|----------|---------------------------|
| FR-1~6 Item Entry + AI | ✅ | `ItemEntry.razor` → `ItemEndpoints.cs` → `ItemService` → `ImageProcessor` |
| FR-7~8 Continuous Storage | ✅ | `AppState.ContinuousStorageLocation` + `ItemEndpoints.cs` |
| FR-9~10 Search | ✅ | `SearchBar.razor` → `ItemEndpoints.cs` → EF Core `LIKE` query |
| FR-11~13 Browse + Filter | ✅ | `Browse.razor` + `ItemCard` + `LocationTree` + `TagFilter` → Materialized Path |
| FR-14~15 Location Management | ✅ | `LocationEndpoints.cs` → `LocationRepository` + Materialized Path CRUD |
| FR-16 Item Deletion | ✅ | `ItemDetail.razor` → `ItemService.DeleteItem` with cascade file cleanup |
| FR-17~20 Auth + Accounts | ✅ | ASP.NET Core Identity + Cookie + `Pages/Admin/` Razor Pages |

**Non-Functional Requirements Coverage:**

| NFR Category | Coverage | Implementation |
|-------------|----------|---------------|
| Security (§8.2) | ✅ | ASP.NET Core Identity, Cookie HttpOnly/Secure, HTTPS, [Authorize] on all endpoints |
| Performance (§8.1) | ✅ | 1C1G target, thumbnails, lazy loading, materialized path B-tree queries |
| PWA/Offline (§8.3) | ✅ | Service Worker with resource-type-differentiated cache strategies |
| AI Reliability (§8.4) | ✅ | 30s timeout (browser-side), silent fallback to manual entry |
| Data (§8.5) | ✅ | SQLite single file, file-system images, persistent Docker volume |

### Gap Analysis

| Gap | Severity | Resolution |
|-----|----------|------------|
| Database migration strategy not explicitly documented | Low | EF Core Migrations is the default approach — applied at implementation |
| Client-side test project missing | Low | Deferred: v1 service-level test coverage sufficient; Blazor WASM UI tests are high-effort/low-ROI for ≤5 users |
| CI/CD pipeline | Deferred | Post-MVP per PRD decision |
| Automated backup | Deferred | Post-MVP per PRD §8.5 |
| Logging/monitoring strategy | Low | `ILogger<T>` + `docker logs` + Caddy access logs sufficient for single-VPS deployment |

**No critical gaps.** Architecture is complete for implementation.

### Architecture Completeness Checklist

**Requirements Analysis:**
- [x] Project context thoroughly analyzed
- [x] Scale and complexity assessed (Medium, 7 components)
- [x] Technical constraints identified (C# full-stack, SQLite, 1C1G VPS)
- [x] Cross-cutting concerns mapped (6 concerns)

**Architectural Decisions:**
- [x] Critical decisions documented with versions (.NET 10, SDK 10.0.300)
- [x] Technology stack fully specified
- [x] Integration patterns defined (Cookie auth, REST, materialized path, Caddy reverse proxy)
- [x] Performance considerations addressed (thumbnails, lazy loading, cache strategies)

**Implementation Patterns:**
- [x] Naming conventions established
- [x] Structure patterns defined
- [x] Communication patterns specified
- [x] Process patterns documented (error handling, loading states, cascade delete)

**Project Structure:**
- [x] Complete directory structure defined (56 files across 4 projects)
- [x] Component boundaries established
- [x] Integration points mapped
- [x] Requirements to structure mapping complete

### Architecture Readiness Assessment

**Overall Status:** READY FOR IMPLEMENTATION

**Confidence Level:** High — all 16 checklist items confirmed, all 20 FRs mapped, all NFRs addressed, no critical gaps.

**Key Strengths:**
- Full PRD coverage with explicit file-to-FR traceability
- Technology choices calibrated to project scale (family use, ≤5 users, 1C1G VPS)
- Implementation patterns prevent agent conflicts across naming, API format, EF Core, DI, and file I/O
- Materialized path chosen over recursive CTE for pragmatic SQLite compatibility
- Image pipeline designed for low-resource VPS (async thumbnails, reverse proxy caching)

**Areas for Future Enhancement:**
- CI/CD pipeline (when deployment frequency warrants it)
- Automated database backup (when item count grows beyond manual comfort)
- E2E testing with Playwright or bUnit for Blazor components
- Monitoring/alerting for the VPS (when reliability becomes critical)

### Implementation Handoff

**AI Agent Guidelines:**
- Follow all architectural decisions exactly as documented — this document is the single source of truth
- Use implementation patterns consistently across all components — no ad-hoc deviations
- Respect project structure and boundaries — `Endpoints/` for API, `Services/` for business logic, `Components/` for UI
- Materialized Path queries must use the documented `StartsWith` / `ExecuteUpdateAsync` pattern — no raw SQL
- API responses must use `TypedResults.*` with full `.Produces*()` OpenAPI annotations
- File I/O must go through `ImageProcessor` — no direct `File.*` calls in endpoints

**First Implementation Priority:**
```bash
# Story 1: Project scaffolding
dotnet new sln -n BoxWise
dotnet new blazorwasm --pwa --empty -n BoxWise.Client -o src/BoxWise.Client --framework net10.0
dotnet new webapi -n BoxWise.Server -o src/BoxWise.Server --framework net10.0
dotnet new classlib -n BoxWise.Shared -o src/BoxWise.Shared --framework net10.0
dotnet sln add src/BoxWise.Client src/BoxWise.Server src/BoxWise.Shared
dotnet add src/BoxWise.Server reference src/BoxWise.Shared
dotnet add src/BoxWise.Client reference src/BoxWise.Shared
```

