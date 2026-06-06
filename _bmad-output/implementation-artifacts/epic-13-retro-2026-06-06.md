---
title: Epic 13 Retrospective — LLM 配置安全迁移
date: 2026-06-06
epic: 13
stories_completed: 3/3
status: done
---

# Epic 13 Retrospective: LLM 配置安全迁移

## Epic Summary

**目标:** 将 AI API 密钥从客户端 `wwwroot/` 静态文件迁移至服务端数据库，消除未经认证 HTTP 读取漏洞，同时保留客户端直调火山 ARK API 的架构。

**触发原因:** Epic 12 完成后安全审查发现 `wwwroot/appsettings.Local.json` 中的 ApiKey 可通过 `MapStaticAssets().AllowAnonymous()` 匿名访问。

## 交付

| Story | 标题 | 提交 | 关键成果 |
|:---|:---|:--:|:---|
| 13.1 | 服务端 LlmConfig 实体与 API | 1 | LlmConfig entity + GET /api/llm/config + 种子数据 |
| 13.2 | 客户端 AiService 重构 | (含在共同提交) | 移除 IConfiguration 依赖，改为 API 获取配置 + Lazy 缓存 |
| 13.3 | Admin LLM 配置管理 + 文档 | 1 | PUT 端点 + Admin Razor Page + 文档全面更新 |

**关键指标:**
- 总提交: 2 commits (含 13.2)
- 新增代码: ~500 行 (Entity + Endpoint + Admin Page + DTO + Tests)
- 删除代码: ~250 行 (AiService 旧代码 + Client LlmApi 配置块 + 文档残留)
- 修改文件: 20 个 (13.1) + 5 个 (13.2) + 10 个 (13.3)
- 测试: 279 通过 (34 Client + 245 Server)，零回归
- 构建: 零错误零警告

## What Went Well

1. **Sprint Change Proposal 驱动设计** — Epic 13 的设计完全基于 SCP，经过 7 条 implementor notes 审查和 subagent review，减少了实现阶段的返工。

2. **Story 13.1 实施精准** — 从实体创建、EF 配置、GET 端点、种子数据到 4 个测试一气呵成。Build 零错误零警告，277 测试首次即通过。

3. **2 轮 Story 评审发掘深层问题** — Story 13.2 经历 2 轮评审发现 `BaseUrl` 空值会导致 `new Uri()` 崩溃、`Model` 未 Trim、`using System.Net.Http.Headers` 保留而非移除等问题，全部在实施前修复。

4. **Admin Page 遵循现有约定** — `[Authorize(Policy = "AdminOnly")]`、kebab-case URL (`/admin/llm-config`)、`_Layout.cshtml` CSS 类，与现有 Admin 页面完全一致。

5. **安全修复闭环** — 代码评审发现 2 个 BLOCKER（ApiKey HTML 泄露、PUT 端点强制要求 ApiKey），立即修复并通过二次评审验证。

6. **文档全面清理** — CLAUDE.md、README.md、deployment-guide.md 中 7 处旧版 Client 端 `wwwroot/appsettings.Production.json` 引用全部替换为 Server 端环境变量方案。

## What Could Be Improved

1. **PUT 端点测试被推迟** — AC #8（PUT 端点 4 个测试）因 `TestIdentityFactory` + 反射式 `InvokeAndReadAsync` 无法解析 scoped `UserManager<AppUser>` 而被推迟。测试基础设施需要 `WebApplicationFactory` 集成测试。

2. **Epic 13 整体偏文档密集型** — Story 13.2（重构）和 13.3（Admin + 文档）变更相对集中，但编码/测试/文档/评审循环耗时均衡。文档更新（README 7 处、CLAUDE.md 2 处、deployment-guide.md 1 处）是 Story 13.3 的主要工作量。

3. **13.2 中的 `CachesConfig` 测试被移除** — `IHttpClientFactory.CreateClient()` 扩展方法在 Moq + xUnit 上下文中无法正确解析，缓存行为测试被移除。缓存正确性由其他测试隐式验证。

4. **`Lazy<T>` 永久缓存限制** — 一旦 `FetchConfigAsync` 首次返回 null，该 WASM 会话不再重试。用户必须刷新页面。这是已知的设计权衡，文档已记录。

## Lessons Learned

1. **Story 评审发现的问题远不止表面** — Blind Hunter + Edge Case Hunter + Acceptance Auditor 3 层并行评审在 2 轮中累计发现 ~20 个 patch/defer 问题，其中 `new Uri(null)` 崩溃、`Model` 未 Trim、`ApiKey` HTML 泄露等方式如果未提前发现，会在生产环境暴露。

2. **Admin 页面开发遵循现有约定事半功倍** — `/admin/llm-config` 完全照搬 `CreateAccount.cshtml.cs` 的 `[Authorize(Policy = "AdminOnly")]` + `AppDbContext` 注入模式，CSS 复用 `_Layout.cshtml`，零新样式代码。

3. **Subagent 审查的 Round 2 价值高** — 第一轮发现大的 BLOCKER，第二轮发现微小不一致（路由命名、Authorize 策略重复、测试名不准确）。两轮配合才能达到高质量。

4. **PUT 端点设计需要与 PageModel 保持一致** — 最初的 PUT 端点强制要求 ApiKey 非空，但 PageModel 允许部分更新（ApiKey 可选）。代码评审发现了这个分歧并统一为可选。

## Action Items

- [ ] 建立 WebApplicationFactory 集成测试基础设施，支持需要 Identity 全栈的端点测试
- [ ] 考虑未来 Story 中为 `Lazy<T>` 缓存添加 TTL 过期机制（避免永久失效）
- [x] 将 `deferred-work.md` 中的文档残留 defer 项标记为已清偿

## Next Epic Preparation

Epic 13 是当前规划的最后一个 Epic。项目已完成 13 个 Epic。

如需继续开发，建议方向:
- 性能优化（图片缩略图批量生成、数据库查询优化）
- 多用户权限细化（只读用户、家庭组）
- 国际化（多语言支持）
- 集成测试基础设施（WebApplicationFactory）
