# Story 12.3: 更新测试 + 文档

Status: done

baseline_commit: 58342e8

## Story

As a 开发者,
I want 更新 AI 识别架构变更后的所有文档和测试引用,
so that 文档准确反映新架构，无残留死代码/死文档引用。

## Acceptance Criteria

1. `docs/` 下 8 个文件移除所有 `LlmClient`、`AiEndpoints`、`/api/ai/recognize`、`LlmOptions` 引用
2. `docs/architecture-server.md` 更新 AI 集成小节 — 移除 LlmClient 行、AI 端点行
3. `docs/api-contracts-server.md` 移除 §7 AI 识别 API 文档
4. `docs/deployment-guide.md` 移除 `Llm__*` 环境变量、Llm 配置块示例，新增 Client `VolcEngine` 配置说明
5. PRD (`_bmad-output/planning-artifacts/*prd*.md`) §3 Glossary / §8.4 NFR-4 / §11 Assumptions 更新
6. Architecture (`_bmad-output/planning-artifacts/architecture.md`) §1 Technical Constraints / §3 Cross-Component 更新
7. CLAUDE.md 更新 AI 集成架构、Docker 部署配置、服务列表
8. README.md 清理全部 Llm 引用（配置表、user-secrets、Docker 部署、文件树、排错指南）
9. `_bmad-output/project-context.md` 清理 LlmClient 引用
10. `dotnet build` 零错误，`dotnet test` 全部通过
11. `grep -rn "LlmClient\|LlmOptions\|MapAiEndpoints\|/api/ai/" docs/ CLAUDE.md README.md _bmad-output/project-context.md` 返回空（零残留）

## Tasks / Subtasks

- [x] Task 1: 更新 PRD 文档 (AC: #5)
  - [x] §3 Glossary "AI 识别" — 更新调用方式描述为"客户端浏览器直接调用火山引擎豆包识图 API"，移除多提供商支持描述
  - [x] §8.4 NFR-4 — 更新超时：15s→30s（浏览器端），API 密钥通过客户端 `wwwroot/appsettings.Production.json` 管理
  - [x] §11 Assumptions Index — 3 条旧假设替换：服务端调用→浏览器直调，多提供商→仅火山 ARK，15s→30s

- [x] Task 2: 更新 Architecture 文档 (AC: #6)
  - [x] §1 Technical Constraints — "OpenAI-compatible LLM API" → "火山引擎 ARK API（OpenAI 兼容），浏览器端直调，CORS 已确认"
  - [x] §3 Cross-Component Dependencies 表 — AI recognition 行：`Server→External` → `Browser (Client)→火山 ARK`

- [x] Task 3: 更新 `docs/architecture-server.md` (AC: #2)
  - [x] 移除 LlmClient 服务描述行
  - [x] 移除 AiEndpoints / `/api/ai` 端点行
  - [x] 更新 AI 集成小节为"AI 识别已迁移至客户端直调火山 ARK API"

- [x] Task 4: 更新 `docs/api-contracts-server.md` (AC: #3)
  - [x] 移除 §7 AI 识别 API 文档（`POST /api/ai/recognize` 及其请求/响应说明）

- [x] Task 5: 更新 `docs/architecture-client.md` (AC: #1)
  - [x] 移除旧 AiService 代理描述，更新为直调火山 ARK API + `IHttpClientFactory` 模式

- [x] Task 6: 更新 `docs/architecture-shared.md` (AC: #1)
  - [x] `RecognitionResultDto` 方向 `Server→Client` → Client 端内部使用

- [x] Task 7: 更新 `docs/source-tree-analysis.md` (AC: #1)
  - [x] 移除 `AiEndpoints.cs`、`LlmClient.cs`、`LlmOptions.cs`、`LlmClientTests.cs` 文件引用
  - [x] 更新 AiService 描述为新的直调模式

- [x] Task 8: 更新 `docs/deployment-guide.md` (AC: #4)
  - [x] 移除 `Llm__*` 环境变量说明
  - [x] 移除 Server 端 Llm 配置块示例（`LlmClient:BaseUrl` 等）
  - [x] 新增 Client 端 `VolcEngine` 配置说明：`wwwroot/appsettings.Production.json` 格式 + gitignore 策略

- [x] Task 9: 更新 `docs/integration-architecture.md` (AC: #1)
  - [x] 移除 `LlmClient` 组件引用
  - [x] 更新 `AiService.RecognizeAsync()` 调用链为浏览器→火山 ARK

- [x] Task 10: 更新 `docs/component-inventory-client.md` (AC: #1)
  - [x] 更新 AiService 超时描述：90s → 30s（Clamp 5-120s）

- [x] Task 11: 更新 CLAUDE.md (AC: #7)
  - [x] LLM 集成描述 — "OpenAI 兼容" → "火山 ARK 客户端直调"
  - [x] Docker 部署 — 移除 Server 端 `appsettings.Production.json` 中 `LlmClient` 配置块生成示例，新增 Client 端 `VolcEngine` 配置生成
  - [x] 服务/组件列表 — 移除 `LlmClient`、`LlmOptions`、`AiEndpoints`，更新 `AiService` 描述
  - [x] AI 集成说明 — "通过 `AddHttpClient<T>()` 注册" → "客户端通过 `IHttpClientFactory` 直调"

- [x] Task 12: 更新 README.md (AC: #8)
  - [x] 配置表 — 移除 `Llm__BaseUrl`、`Llm__ApiKey`、`Llm__Model` 环境变量行
  - [x] user-secrets 初始化命令 — 移除 `dotnet user-secrets set "Llm:..."` 示例
  - [x] Docker 部署 — 移除 Server 端 Llm 配置块生成，新增 Client 端 `VolcEngine` 配置
  - [x] 文件树 — 移除 `LlmClient.cs`、`LlmOptions.cs`、`AiEndpoints.cs` 文件引用
  - [x] 排错指南 — 移除 AI 超时/配置相关排查条目

- [x] Task 13: 更新 `_bmad-output/project-context.md` (AC: #9)
  - [x] 清理 LlmClient 引用，更新 AI 集成描述

- [x] Task 14: 验证 (AC: #10, #11)
  - [x] `dotnet build` 零错误零警告
  - [x] `dotnet test` 全部通过
  - [x] `rg -n "LlmClient|LlmOptions|MapAiEndpoints|/api/ai/" -g "*.md" docs/ CLAUDE.md README.md _bmad-output/project-context.md` 零匹配

## Dev Notes

### 文档变更范围

本 Story 为纯文档更新 + 零残留验证。所有代码变更已在 Story 12.1（前端重写）和 Story 12.2（服务端退役）中完成。

| 文件 | 操作 | 关键内容 |
|------|:--:|------|
| `_bmad-output/planning-artifacts/*prd*.md` | MODIFY | Glossary + NFR-4 + Assumptions |
| `_bmad-output/planning-artifacts/architecture.md` | MODIFY | Tech Constraints + Cross-Component |
| `docs/architecture-server.md` | MODIFY | 移除 LlmClient/AI 端点，新增说明 |
| `docs/api-contracts-server.md` | MODIFY | 移除 §7 AI API |
| `docs/architecture-client.md` | MODIFY | 更新 AiService 描述 |
| `docs/architecture-shared.md` | MODIFY | RecognitionResultDto 方向 |
| `docs/source-tree-analysis.md` | MODIFY | 移除 4 个退役文件引用 |
| `docs/deployment-guide.md` | MODIFY | 移除 Llm 配置，新增 VolcEngine |
| `docs/integration-architecture.md` | MODIFY | 移除 LlmClient，更新调用链 |
| `docs/component-inventory-client.md` | MODIFY | 更新 AiService 超时 |
| CLAUDE.md | MODIFY | AI 架构/Docker/服务列表 |
| README.md | MODIFY | 配置/部署/文件树/排错 |
| `_bmad-output/project-context.md` | MODIFY | LlmClient 清理 |

### PRD 修改（Section 4.2 精确替换文本）

**Glossary "AI 识别"（§3）：**
```
OLD:
AI 识别（AI Recognition）—— 调用 OpenAI 兼容的多模态 LLM API，对物品照片
进行分析，自动识别物品名称并生成备注描述。支持通过配置（base URL、模型名称、
自定义字段）适配不同 OpenAI 兼容提供商。

NEW:
AI 识别（AI Recognition）—— 客户端浏览器直接调用火山引擎豆包识图 API
（OpenAI 兼容接口），对物品照片进行 base64 编码后发送，自动识别物品名称
并生成备注描述。服务端不参与 AI 调用。API 地址和密钥在客户端配置文件中管理。
v1 仅支持火山 ARK。
```

**NFR-4（§8.4）：**
```
OLD: AI API 15s 超时，静默降级为手动输入
NEW: AI API 30s 超时（浏览器端），静默降级为手动输入。API 密钥通过客户端
     wwwroot/appsettings.Production.json 管理（gitignored）。
```

**Assumptions Index（§11）：**
```
OLD (3 条不再成立的假设):
- LLM API 调用在后端完成，前端不直接持有 API key
- v1 支持所有 OpenAI 兼容提供商，通过配置文件切换
- AI API 超时阈值设为 15 秒

NEW:
- LLM API 由浏览器前端直接调用（CORS 已确认支持）
- v1 仅支持火山引擎 ARK API（doubao-seed-2-0-pro-260215）
- AI API 超时阈值 30 秒（浏览器端）
```

### Architecture 修改（Section 4.3 精确替换文本）

**Technical Constraints（§1）：**
```
OLD: OpenAI-compatible LLM API — configurable via base URL + model name
NEW: 火山引擎 ARK API（OpenAI 兼容）— 浏览器端直接调用，CORS 已确认
```

**Cross-Component（§3）：**
```
OLD: | AI recognition | Server | External LLM API | HttpClient + configurable base URL |
NEW: | AI recognition | Browser (Client) | 火山 ARK API (北京) | fetch + CORS |
```

### GREP 零残留验证

```bash
grep -rn "LlmClient\|LlmOptions\|MapAiEndpoints\|/api/ai/" docs/ CLAUDE.md README.md _bmad-output/project-context.md
```
预期：仅返回本文档自身（或零结果）。

### References

- Sprint Change Proposal: `_bmad-output/planning-artifacts/sprint-change-proposal-2026-06-05.md`
- PRD 精确替换: Section 4.2
- Architecture 精确替换: Section 4.3
- Story 12.1: `_bmad-output/implementation-artifacts/12-1-frontend-direct-api.md`
- Story 12.2: `_bmad-output/implementation-artifacts/12-2-decommission-server-ai.md`

## Dev Agent Record

### Agent Model Used

### Debug Log References

### Completion Notes List

### File List
