---
baseline_commit: c534a2008424b634c54a5a050d73bee03d00628b
---

# Story 13.3: Admin LLM 配置管理 + 文档更新

Status: done

## Story

As a 管理员，
I want 通过 Admin 后台管理 LLM API 配置（BaseUrl/ApiKey/Model/TimeoutSeconds），
so that 可以在不重启服务、不修改代码的情况下更新 AI 配置，同时消除部署文档中的客户端 wwwroot 密钥方案残留。

## Acceptance Criteria

1. `LlmConfigEndpoints.cs` 新增 `PUT /api/llm/config` 端点，AdminOnly 授权，接受 `LlmConfigDto` JSON body，upsert Id=1 记录（不存在则创建，存在则更新）
2. `PUT /api/llm/config` 验证 BaseUrl/ApiKey/Model 非空，ApiKey 写入前 Trim，Model 空值回退默认，TimeoutSeconds 使用 `Math.Clamp(5, 120)`（JSON 反序列化自动解析 int，PageModel 端额外 `int.TryParse` 容错）
3. 创建 `Pages/Admin/LlmConfig.cshtml` + `LlmConfig.cshtml.cs` PageModel，显示当前配置 + 可编辑表单（BaseUrl/Model/ApiKey/TimeoutSeconds），保存时调用 PUT 端点
4. `Pages/Admin/Index.cshtml` 新增 "LLM 设置" 按钮（与现有 "SMTP 设置" 按钮并列）
5. `docs/deployment-guide.md` §3 生产配置：移除 Client 端 `wwwroot/appsettings.Production.json` 配置示例，替换为 `LlmApi__BaseUrl` / `LlmApi__ApiKey` 等环境变量
6. `CLAUDE.md` AI 配置段：更新为"服务端数据库 + 认证 API 读取"，Admin UI 可管理
7. `README.md` 配置表：移除 Client 端 `LlmApi` 静态文件说明，新增 Server 端 `LlmApi__*` 环境变量
8. `LlmConfigEndpointsTests.cs` 新增 PUT 端点测试：Admin 成功更新、非 Admin 返回 403、未认证返回 401、空 BaseUrl 返回 400
9. `dotnet build` 零错误零警告，`dotnet test` 全部通过

## Tasks / Subtasks

- [ ] Task 1: 新增 `PUT /api/llm/config` 端点 (AC: #1, #2)
  - [ ] 在 `LlmConfigEndpoints.cs` 的 `MapLlmConfigEndpoints` 中添加 `group.MapPut("/config", UpdateLlmConfigAsync)`
  - [ ] Handler 签名含 `HttpContext httpContext` + `UserManager<AppUser>` + `AppDbContext db` + `LlmConfigDto request`
  - [ ] Admin 角色检查：`userManager.GetUserAsync(httpContext.User)` + `IsInRoleAsync("Admin")`，非 Admin → `TypedResults.Forbid()`
  - [ ] 参数验证：`string.IsNullOrWhiteSpace(request.BaseUrl)` / `ApiKey` / `Model` → `TypedResults.Problem(..., 400)`
  - [ ] 从 DB 加载 Id=1 记录；不存在则 `new LlmConfig { Id = 1 }` 并 `db.LlmConfigs.Add()`（upsert 语义：`DbUpdateException` 捕获 PK 冲突后重试 Find + update）
  - [ ] 更新字段：`BaseUrl = request.BaseUrl.Trim()`、`ApiKey` **仅非空时更新**（`!string.IsNullOrWhiteSpace(request.ApiKey) ? request.ApiKey.Trim() : 保留原值`）、`Model = request.Model.Trim()`（空值回退默认）、`Math.Clamp(request.TimeoutSeconds, 5, 120)`
  - [ ] `await db.SaveChangesAsync()`，返回 `Ok(MapToDto(entity))`
  - [ ] `.Produces<LlmConfigDto>(200)` + `.ProducesProblem(400)` + `.ProducesProblem(401)` + `.ProducesProblem(403)`
  - [ ] 异常处理：`DbUpdateException` → `Problem(..., 400)`

- [ ] Task 2: 创建 Admin LlmConfig Razor Page (AC: #3)
  - [ ] 创建 `src/BoxWise.Server/Pages/Admin/LlmConfig.cshtml` + `LlmConfig.cshtml.cs`
  - [ ] PageModel `[Authorize(Policy = "AdminOnly")]` 保护（参照 `CreateAccount.cshtml.cs` 模式，**非** `Roles = "Admin"`）
  - [ ] `OnGetAsync`：加载配置 → 绑定字段 → 设置 `HasApiKey = entity?.ApiKey != null` 供视图使用
  - [ ] `OnPostAsync`：验证（BaseUrl/ApiKey/Model 非空、TimeoutSeconds Clamp 5-120、**ApiKey 空则不更新保留原值**）→ 保存到 DB
  - [ ] 表单字段：BaseUrl（必填）、ApiKey（`type="password" autocomplete="off"`，placeholder 显示 `HasApiKey ? "已配置 (●●●●●)" : "未配置"`）、Model（默认值）、TimeoutSeconds（默认 30）
  - [ ] 样式遵循 `_Layout.cshtml` 现有 CSS 类：`.form-group`、`.btn-primary`、`.error-message`、`.status-message`

- [ ] Task 3: 更新 Admin Index 入口 (AC: #4)
  - [ ] `Pages/Admin/Index.cshtml` 操作栏中，在 "SMTP 设置" 按钮旁新增 `<a href="/admin/llm-config" class="btn btn-outline">LLM 设置</a>`（遵循 Admin 页面 kebab-case URL 约定：`/admin/smtp-settings`、`/admin/llm-config`）

- [ ] Task 4: 更新部署文档 (AC: #5)
  - [ ] `docs/deployment-guide.md` §3 "生产配置"：移除整段 Client 端 `appsettings.Production.json` 配置示例
  - [ ] 新增 Server 端 LLM 配置说明：通过环境变量 `LlmApi__BaseUrl`/`LlmApi__ApiKey`/`LlmApi__Model` 注入，种子数据在启动时自动入库
  - [ ] Docker Compose 环境变量表新增 `LlmApi__ApiKey` 行

- [ ] Task 5: 更新 CLAUDE.md (AC: #6)
  - [ ] "Docker 部署" 段（约 60-82 行）：移除 `appsettings.Production.json` 示例代码块
  - [ ] 更新 AI 配置说明为："API 密钥通过 Server 端 `LlmApi__*` 环境变量注入（种子数据自动入库），Admin 后台可管理"
  - [ ] "AI 集成" 段：移除 "通过 `wwwroot/appsettings.Local.json` 配置" 描述，替换为服务端架构

- [ ] Task 6: 更新 README.md (AC: #7)
  - [ ] 覆盖 **所有** `LlmApi` / `appsettings.Production.json` / AI 识别配置引用（共 7 处：配置表、二进制部署、Windows 部署、Docker 部署、故障排除等章节）
  - [ ] 移除 Client 端 AI 配置示例，替换为 Server 端 `LlmApi__*` 环境变量说明
  - [ ] Docker 部署章节：移除 `appsettings.Production.json` 创建命令，新增环境变量注入示例

- [ ] Task 7: 新增 PUT 端点测试 (AC: #8)
  - [ ] 在 `LlmConfigEndpointsTests.cs` 新增 4 个测试
  - [ ] `UpdateConfig_Admin_Success` — 种一条记录 → Admin 用户更新 → 验证 200 + 新值
  - [ ] `UpdateConfig_NonAdmin_Returns403` — 非 Admin 用户 → 403
  - [ ] `UpdateConfig_Unauthenticated_Returns401` — DefaultHttpContext → 401
  - [ ] `UpdateConfig_EmptyBaseUrl_Returns400` — BaseUrl 为空 → 400
  - [ ] 使用 `TestDbContextFactory.Create()` + 构造 Admin/非Admin `HttpContext`（需 mock `UserManager<AppUser>` 或使用 `TestIdentityFactory.CreateAsync()` 获取完整 Identity 服务）

- [ ] Task 8: 验证 (AC: #9)
  - [ ] `dotnet build` 零错误零警告
  - [ ] `dotnet test` 全部通过

## Dev Notes

### 当前架构 vs 目标架构

```
CURRENT (文档过时):
  CLAUDE.md/README/deployment-guide.md 描述 Client 端 wwwroot/appsettings.Production.json
  管理员无 UI 修改 LlmConfig（需直接操作 SQLite 或重启服务）
  
TARGET:
  Admin Razor Page (/admin/llm) → PUT /api/admin/llm/config → DB LlmConfigs
  CLAUDE.md/README 更新为 Server 端环境变量 + Admin UI
```

### 关键设计决策

| 决策 | 理由 |
|------|------|
| Admin PageModel 直接操作 `AppDbContext` | 参照现有 `CreateAccount.cshtml.cs` 的 `[Authorize(Policy = "AdminOnly")]` 授权模式，数据操作直接注入 `AppDbContext`（与 SmtpSettings 通过 Service 层不同，LlmConfig 单行配置无需额外 Service） |
| Page URL kebab-case | 遵循现有 Admin 页面约定：`/admin/smtp-settings`、`/admin/llm-config` |
| PUT 端点用 AdminOnly 策略 | 参照 `AdminTwoFactorEndpoints.cs` 的 Admin 角色检查模式：`userManager.GetUserAsync` + `IsInRoleAsync("Admin")` |
| ApiKey 输入框 `type="password"` | 防止浏览器自动填充 + 屏幕窥视。管理员输入新密钥时不可见，但已存值不显示（安全最佳实践） |
| 不显示已存 ApiKey | 已在数据库中（明文存储），不返回到表单。管理员必须重新输入完整密钥来更新 |
| 文档更新在 13.3 | 13.2 的部署文档残留是已知 defer，本 Story 统一处理 |

### 代码模式参考

**PUT 端点模式** (`AdminTwoFactorEndpoints.cs` 的管理员检查):
```csharp
var caller = await userManager.GetUserAsync(httpContext.User);
if (caller is null) return TypedResults.Unauthorized();
if (!await userManager.IsInRoleAsync(caller, "Admin")) return TypedResults.Forbid();
```

**PageModel 直接 DB 操作** (`CreateAccount.cshtml.cs:12` 模式):
```csharp
[Authorize(Policy = "AdminOnly")]
public class LlmConfigModel : PageModel
{
    private readonly AppDbContext _db;
    public bool HasApiKey { get; private set; }
    // OnGetAsync: 加载配置 → 设置 HasApiKey + 绑定表单字段
    // OnPostAsync: 验证（ApiKey 非空才更新，BaseUrl/Model non-null，TimeoutSeconds Clamp(5,120)） → 保存
}
```

**Admin Page 样式** (`_Layout.cshtml` CSS 类):
```html
<form method="post">
    <div class="form-group">
        <label>BaseUrl</label>
        <input asp-for="BaseUrl" />
    </div>
    <button type="submit" class="btn btn-primary">保存</button>
</form>
```

### 需修改的文件清单

| 文件 | 操作 | 说明 |
|------|:--:|------|
| `src/BoxWise.Server/Endpoints/LlmConfigEndpoints.cs` | MODIFY | 新增 PUT 端点 |
| `src/BoxWise.Server/Pages/Admin/LlmConfig.cshtml` | NEW | LLM 配置表单页 |
| `src/BoxWise.Server/Pages/Admin/LlmConfig.cshtml.cs` | NEW | PageModel |
| `src/BoxWise.Server/Pages/Admin/Index.cshtml` | MODIFY | 新增 LLM 设置入口 |
| `docs/deployment-guide.md` | MODIFY | 更新 AI 配置说明 |
| `CLAUDE.md` | MODIFY | 更新 AI 配置/Docker 段 |
| `README.md` | MODIFY | 更新配置表 |
| `src/BoxWise.Server.Tests/Endpoints/LlmConfigEndpointsTests.cs` | MODIFY | 新增 PUT 测试 |

### 注意事项

1. **不要修改** Client 端任何文件 — 所有变更在 Server 端
2. **Admin 页面不要刷新 AiService 缓存** — 浏览器会话需刷新页面才会重新调用 `GET /api/llm/config`（已知限制，13.2 Defer）
3. **PageModel 直接操作 `AppDbContext`** — 参照 `CreateAccount` 模式，不需要 `HttpClient` 调用自己的 API
4. **PUT 端点验证需 `UserManager<AppUser>`** — 注入为 handler 参数
5. **文档 grep 验证** — 更新后 `grep -rn "wwwroot.*appsettings.Production.json.*LlmApi" docs/ CLAUDE.md README.md` 应返回空

### Previous Story Intelligence (from 13.1 + 13.2)

- **13.1 Defer**: `LlmConfig` 使用 `FindAsync(1)` 硬编码 — 本 Story 继续使用
- **13.2 Defer**: 部署文档更新留给 Story 13.3 — 本 Story 处理
- **13.2 Code Review**: `config.Model.Trim()` 已在 13.2 修复 — PATCH 端点也需 Trim

### References

- [Source: SCP §4.4] `_bmad-output/planning-artifacts/sprint-change-proposal-2026-06-06.md`
- [Source: Admin Endpoint Pattern] `src/BoxWise.Server/Endpoints/AdminTwoFactorEndpoints.cs`
- [Source: Admin Page Pattern] `src/BoxWise.Server/Pages/Admin/Index.cshtml`
- [Source: PageModel Pattern] `src/BoxWise.Server/Pages/Admin/CreateAccount.cshtml.cs`
- [Source: GET Endpoint] `src/BoxWise.Server/Endpoints/LlmConfigEndpoints.cs`
- [Source: LlmConfigDto] `src/BoxWise.Shared/Dtos/LlmConfigDto.cs`
- [Source: Story 13.1] `_bmad-output/implementation-artifacts/13-1-llm-config-backend.md`
- [Source: Story 13.2] `_bmad-output/implementation-artifacts/13-2-ai-service-refactor.md`

### Review Findings (2026-06-06, Blind Hunter)

- [ ] [Review][Patch] PUT endpoint响应泄露ApiKey到客户端 — `LlmConfigEndpoints.cs:91-95` 返回含原始ApiKey的LlmConfigDto，JSON序列化不受ToString掩码保护
- [x] [Review][Defer] GET端点向所有认证用户暴露ApiKey [src/BoxWise.Server/Endpoints/LlmConfigEndpoints.cs:38] — deferred, pre-existing
- [ ] [Review][Patch] Docker Compose环境变量表缺少LlmApi__*条目 [docs/deployment-guide.md:63-102]
- [ ] [Review][Patch] DbUpdateException静默吞异常无日志 [src/BoxWise.Server/Endpoints/LlmConfigEndpoints.cs:97]
- [ ] [Review][Patch] 测试文件未使用的using语句 [src/BoxWise.Server.Tests/Endpoints/LlmConfigEndpointsTests.cs:5,8]
- [ ] [Review][Patch] PUT端点缺少CSRF过滤器 [src/BoxWise.Server/Endpoints/LlmConfigEndpoints.cs:24]

## Dev Agent Record

### Agent Model Used

### Debug Log References

### Completion Notes List

### File List
