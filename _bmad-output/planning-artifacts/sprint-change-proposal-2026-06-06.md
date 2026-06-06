---
title: Sprint Change Proposal — LLM 配置安全迁移至服务端数据库
date: 2026-06-06
status: approved
trigger: Epic 12 完成后安全审查 — wwwroot appsettings.Local.json ApiKey 存在未认证 HTTP 访问风险
scope: Moderate — 新增 Epic 13，前后端代码变更 + DB 迁移
reviewed: 2026-06-06 (subagent review — PASS, 3 minor implementor notes recorded)
---

# Sprint Change Proposal: LLM 配置安全迁移

## 1. Issue Summary

### 问题陈述

**Epic 12 将 AI 识别改为客户端直调火山 ARK API，ApiKey 存放在 `wwwroot/appsettings.Local.json`（gitignored）。** 但 `wwwroot/` 下所有文件通过 `MapStaticAssets().AllowAnonymous()` 服务，未经认证的用户可通过猜测 URL 直接 HTTP 读取 ApiKey，构成安全漏洞。

### 发现背景

- Epic 12 已全部完成（3/3 Stories 交付，12 次提交）
- 用户本地测试 AI 识别功能时发现配置加载机制问题
- 进一步审查确认 `appsettings.Local.json` 可通过匿名 HTTP 直接访问
- Sprint Change Proposal 中默认"5 人家庭场景，Key 泄露风险低并可通过消费上限告警缓解"，但**服务部署在公网上，无法避免匿名访问的攻击面**——攻击者扫描 `wwwroot/` 静态文件即可获取 ApiKey

### 证据

| 证据 | 说明 |
|------|------|
| `MapStaticAssets().AllowAnonymous()` | `Program.cs:392` — 所有 wwwroot 文件匿名可访问 |
| `appsettings.Local.json` API Key | 可被 `curl https://host/appsettings.Local.json` 直接读取 |
| Epic 12 架构 | 客户端直调火山 ARK 是解决国际链路超时的唯一方案，不可回退 |

## 2. Impact Analysis

### Epic Impact

| Epic | 状态 | 影响 |
|------|:--:|------|
| Epic 12 "AI 识别架构修复" | Done | 不修改，客户端直调架构保留 |
| **Epic 13 (NEW)** "LLM 配置安全迁移" | Backlog | 新增 — 将 ApiKey 从 wwwroot 迁移至服务端数据库 |

### Story Impact

| Story | 影响 |
|------|------|
| **13.1** | 服务端：创建 `LlmConfig` 实体 + DB 迁移 + `GET /api/llm/config` 认证端点 + 种子数据 |
| **13.2** | 客户端：`AiService` 改为通过 API 获取配置（含缓存），移除对 `IConfiguration["LlmApi:*"]` 的依赖 |
| **13.3** | Admin 后台：新增 LLM 配置管理页面（BaseUrl/Model/ApiKey/TimeoutSeconds） |

### Artifact Conflicts

| 文档 | 需更新 | 说明 |
|------|:--:|------|
| Architecture §1 Technical Constraints | ✅ | AI 配置从"客户端 wwwroot JSON" → "服务端数据库，认证 API 读取" |
| Architecture §3 Cross-Component | ✅ | 新增 AI config 数据流：Browser → Server API → DB |
| `docs/architecture-server.md` | ✅ | 新增 `LlmConfig` 实体和端点 |
| `docs/architecture-client.md` | ✅ | 更新 `AiService` 配置获取方式 |
| `docs/api-contracts-server.md` | ✅ | 新增 `GET /api/llm/config` 端点文档 |
| `docs/deployment-guide.md` | ✅ | 更新部署配置说明（移除 wwwroot 密钥文件，改为数据库种子） |
| CLAUDE.md | ✅ | 更新 AI 配置说明 + 服务列表 |
| README.md | ✅ | 更新配置章节 |
| `sprint-status.yaml` | ✅ | 新增 epic-13 |

### Technical Impact

| 组件 | 变更 |
|------|------|
| `Models/LlmConfig.cs` (Server) | **NEW** — 实体 ID=1 单行配置 |
| `Data/AppDbContext.cs` (Server) | 新增 `DbSet<LlmConfig>` |
| `Endpoints/LlmConfigEndpoints.cs` (Server) | **NEW** — `GET /api/llm/config` 认证端点 |
| `Program.cs` (Server) | 注册 `MapLlmConfigEndpoints()` |
| `Migrations/` (Server) | **NEW** — EF Core 迁移 |
| `AiService.cs` (Client) | 移除 `IConfiguration` 依赖，改为 API 获取 + 缓存 |
| `Program.cs` (Client) | 移除 `AddHttpClient("LlmApi", ...)` 中的 BaseAddress 配置读取 |
| `appsettings.json` / `appsettings.Development.json` (Client) | 移除 `LlmApi` 配置块 |
| `appsettings.Local.json` (Client) | 无需再使用，可删除 |
| `Admin/` (Server) | **NEW** — LLM 配置管理 Razor Page |
| `AiServiceTests.cs` (Client) | 适配新的配置获取方式 |

## 3. Recommended Approach

### 选定方案：服务端数据库存储 + 认证 API 读取

**数据流变化：**

```
Before (安全漏洞):
  浏览器 --[1]加载 appsettings.Local.json (匿名可读)--> 静态文件
  浏览器 --[2]ApiKey 在内存中--> 火山 ARK API

After (安全):
  浏览器 --[1]登录认证--> Server
  浏览器 --[2]GET /api/llm/config (需 Cookie)--> Server → DB → 返回配置(不含 ApiKey 时降级)
  浏览器 --[3]ApiKey 在内存中--> 火山 ARK API
```

**理由：**
1. ApiKey 永不出现在静态文件中，消除未经认证访问风险
2. 客户端直调架构保留（解决国际链路超时问题）
3. DB 存储便于将来通过 Admin UI 管理
4. 若 ApiKey 未配置（null），AiService 静默降级为手动输入

**风险评估：** Low — 新增端点遵循现有 Minimal API + Repository 模式，无需引入新技术栈。

### 替代方案评估

| 方案 | 评估 |
|------|------|
| 回退服务端代理 | ❌ 国际链路超时问题重现（Epic 12 的整个目的） |
| StaticFileOptions 排除特定文件 | ❌ Blazor WASM 内部 HTTP fetch 也被阻断 |
| 接受现状（家庭自托管） | ❌ 用户明确拒绝此风险承受 |

## 4. Detailed Change Proposals

### 4.1 New Epic: Epic 13 — LLM 配置安全迁移

**Goal:** 将 AI API 密钥从客户端 wwwroot 静态文件迁移至服务端数据库，通过认证 API 安全读取。

**Stories:**

| Story | 标题 | 范围 |
|:------|------|------|
| 13.1 | 服务端 LlmConfig 实体与 API | 创建 `LlmConfig` 实体（BaseUrl/Model/ApiKey/TimeoutSeconds）、`DbSet<LlmConfig>`、EF 迁移、`GET /api/llm/config` 认证端点、从 `appsettings.json` 种子数据 |
| 13.2 | 客户端 AiService 重构 | 移除 `IConfiguration` 依赖，改为通过服务端 API 获取配置（首次调用时 fetch + 缓存），移除 `wwwroot/appsettings.json` 中 `LlmApi` 配置块，`Program.cs` 简化 |
| 13.3 | 测试 + Admin UI + 文档 | `LlmConfigEndpointsTests` + `AiServiceTests` 适配 + Admin LLM 配置管理页面 + docs/CLAUDE/README 更新 |

**实施顺序：** 13.1 → 13.2 → 13.3

### 4.2 AiService 新架构

```csharp
// OLD: 直接从 IConfiguration 读取（ApiKey 在 wwwroot JSON）
public AiService(IHttpClientFactory httpFactory, IConfiguration configuration)
{
    _apiKey = configuration["LlmApi:ApiKey"];
}

// NEW: 通过服务端 API 获取（首次调用时 fetch，内存缓存）
public AiService(IHttpClientFactory httpFactory, HttpClient serverHttp)
{
    _http = httpFactory.CreateClient("LlmApi");
    _serverHttp = serverHttp;  // 带 CookieHandler，可访问认证 API
}

private async Task<LlmConfigDto?> GetConfigAsync()
{
    if (_configCached) return _cachedConfig;
    var response = await _serverHttp.GetAsync("/api/llm/config");
    if (response.IsSuccessStatusCode)
    {
        _cachedConfig = await response.Content.ReadFromJsonAsync<LlmConfigDto>();
        _configCached = true;
    }
    return _cachedConfig;
}
```

### 4.3 数据库模型

```csharp
public class LlmConfig
{
    public int Id { get; set; }  // 固定为 1（单行配置）
    public string? BaseUrl { get; set; }
    public string? ApiKey { get; set; }
    public string Model { get; set; } = "doubao-seed-2-0-pro-260215";
    public int TimeoutSeconds { get; set; } = 30;
}
```

### 4.4 Admin UI

在 `/admin` 后台新增 "LLM 配置" 页面，允许管理员：
- 查看当前配置
- 更新 BaseUrl / Model / ApiKey / TimeoutSeconds
- 测试连接（可选）

## 5. Implementation Handoff

### Scope Classification: Moderate

需要前后端代码变更 + DB 迁移 + 测试更新 + Admin UI + 文档更新。

### Handoff Plan

| 步骤 | 负责人 | 技能 | 说明 |
|------|--------|------|------|
| 1. 创建 Story 13.1 | Developer | `bmad-create-story` | 服务端实体与 API |
| 2. 实施 Story 13.1 | Developer | `bmad-dev-story` | |
| 3. 创建 Story 13.2 | Developer | `bmad-create-story` | 客户端重构 |
| 4. 实施 Story 13.2 | Developer | `bmad-dev-story` | |
| 5. 创建 Story 13.3 | Developer | `bmad-create-story` | 测试 + Admin + 文档 |
| 6. 实施 Story 13.3 | Developer | `bmad-dev-story` | |
| 7. Epic 回顾 | Developer | `bmad-retrospective` | |

### Success Criteria

- [ ] ApiKey 不在任何 `wwwroot/` 文件中
- [ ] `GET /api/llm/config` 需认证（未登录 → 401）
- [ ] Admin 可配置 LLM 参数
- [ ] 客户端首次调用 AI 时自动获取配置并缓存
- [ ] ApiKey 未配置时 AI 静默降级为手动输入
- [ ] 所有现有测试通过 + 新增测试覆盖
- [ ] `dotnet build` 零错误零警告

### Review Findings (2026-06-06)

**Review outcome:** APPROVED — all assumptions verified, minor implementor notes recorded.

**Implementor Notes (handle during Story 13.2):**

1. `LlmConfigDto` 需在 `BoxWise.Shared.Dtos` 中新建 positional record
2. AiService 的 `Authorization: Bearer` header 需从构造函数移至 `RecognizeAsync` 懒加载（因为 ApiKey 不再在构造函数中可用）
3. `LlmApi` HttpClient 的 `BaseAddress` 需在获取服务端配置后动态设置，或改用绝对 URL
4. 配置缓存需线程安全初始化（`SemaphoreSlim` 或 `Lazy<Task<T>>` 模式）
5. `appsettings.Development.json:3-7` 中的 `LlmApi` 配置块需同步移除
6. `Program.cs:14-23` 中 `AddJsonStream("appsettings.Local.json")` 代码块需移除
7. ApiKey 以明文存储在 SQLite 中（防御性决策：本地文件，与 SMTP 加密模式不同）
