---
title: Sprint Change Proposal — AI 识别架构修复
date: 2026-06-05
status: approved
trigger: 生产环境 AI 识别 100% 超时（调查案例 ai-recognition-timeout）
scope: Moderate — 新增 Epic + 代码变更
reviewed: 2026-06-05 (子代理全面审核，7 个维度通过)
---

# Sprint Change Proposal: AI 识别架构修复

## 1. Issue Summary

### 问题陈述

**生产环境 BoxWise AI 识别功能事实不可用。** 服务端中转模式要求海外 VPS 将 2.6MB base64 图片上传到北京火山 ARK API，但国际链路（267ms RTT, 40% 丢包率）有效上传速度仅 ~29KB/s，需要 91-128 秒，远超 60 秒默认超时。功能退化为"永远降级到手动输入"（7 天失败率 83%）。

### 发现背景

- 用户在生产环境使用 AI 识别时发现每次超时
- 调查案例 `ai-recognition-timeout-investigation.md` 通过生产环境实测确认根因
- 问题不是 bug，而是架构假设（"服务端代理 AI 调用"）在海外部署场景下不可行

### 证据

| 指标 | 数值 |
|------|:--|
| 今日失败率 | 3/3 = 100% |
| 7 天失败率 | 10/12 = 83% |
| 网络 RTT | 267ms (40% 丢包) |
| 2.6MB 上传预估 | 91-128s |
| 当前超时 | 60s |
| 纯文本 API 调用 | 6.35s ✅ |

## 2. Impact Analysis

### Epic Impact

| Epic | 状态 | 影响 |
|------|:--:|------|
| Epic 3 "物品录入与智能识别" (Story 3.3) | Done | AI 识别功能需重新实现 |
| **Epic 12 (NEW)** "AI 识别架构修复" | Backlog | 新增 |

### Story Impact

| Story | 影响 |
|------|------|
| Story 3.3 "AI 识别集成 + 降级策略" | ⚠️ 现有实现需退役，改为新架构 |
| Story 12.1 | 前端 `AiService.cs` 重写为直接调用火山 API，含 HttpClient 策略 + base64 编码 + 配置 |
| Story 12.2 | 退役服务端 `LlmClient` + `AiEndpoints` 识别端点 + `LlmOptions` |
| Story 12.3 | 测试更新（删除/重写）+ 文档更新（Architecture/CLAUDE.md/docs/PRD/README） |

### Artifact Conflicts

| 文档 | 需更新 | 说明 |
|------|:--:|------|
| PRD §3 Glossary "AI 识别" | ✅ | 更新调用方式描述 |
| PRD §8.4 NFR-4 | ✅ | 15s→30s 超时，客户端直调 |
| PRD §11 Assumptions Index | ✅ | 3 条假设不再成立（服务端调用/15s超时/多提供商） |
| Architecture §1 Technical Constraints | ✅ | "OpenAI-compatible LLM API" → "浏览器直调火山 ARK API" |
| Architecture §3 Cross-Component | ✅ | 第 307 行 "AI recognition: Server→External" → "Browser→External" |
| `docs/architecture-server.md` | ✅ | 移除 LlmClient 行、AI 端点行，更新 AI 集成小节 |
| `docs/api-contracts-server.md` | ✅ | 移除 §7 AI 识别 API 文档 |
| `docs/architecture-shared.md` | ✅ | `RecognitionResultDto` 方向 Server→Client → Client-internal |
| `docs/architecture-client.md` | ✅ | 移除 AiService 引用 |
| `docs/source-tree-analysis.md` | ✅ | 多处 AI 引用更新/移除 |
| `docs/deployment-guide.md` | ✅ | 移除 Llm__* 环境变量、Llm 配置块示例 |
| `docs/integration-architecture.md` | ✅ | 移除 LlmClient 引用，更新 AiService.RecognizeAsync 调用链 |
| `docs/component-inventory-client.md` | ✅ | 更新 AiService 超时描述 |
| `_bmad-output/project-context.md` | ✅ | LlmClient 引用清理 |
| CLAUDE.md | ✅ | 更新 AI 集成架构、Docker 部署配置、服务列表（4 处） |
| README.md | ✅ | 清理全部 Llm 引用：配置表、user-secrets、Docker 部署、文件树、排错指南（8 处） |
| `sprint-status.yaml` | ✅ | 新增 epic-12 |

### Technical Impact

| 组件 | 变更 |
|------|------|
| `AiService.cs` (Client) | **重写** — 直接调用火山 API，使用 `IHttpClientFactory` 创建独立 HttpClient |
| `LlmClient.cs` (Server) | **退役** — 不再需要服务端代理 |
| `AiEndpoints.cs` — `/api/ai/recognize` | **退役** — 整个文件删除；`IsValidMagic` 方法及魔数字节常量移植到 `ImageEndpoints.cs` |
| `LlmOptions.cs` (Server) | **退役** |
| `Program.cs` (Server) | 移除 `AddOptions<LlmOptions>()` 和 `AddHttpClient<LlmClient>()` |
| `wwwroot/appsettings.json` (Client) | `AiSettings` → `VolcEngine` 配置块（ApiKey, BaseUrl, Model, TimeoutSeconds） |
| `wwwroot/appsettings.Production.json` (Client) | **新增** — 生产火山 API 凭证（gitignored） |
| `appsettings.Production.json` (Server) | 移除 Llm 配置块 |
| `ItemEntry.razor` | 微调 — `photo.OpenReadStream()` → 字节数组传递 |
| `AiServiceTests.cs` (Client) | **重写** — 5 个测试改为验证直调火山 API 行为 |
| `LlmClientTests.cs` (Server) | **退役** — 6 个测试随 LlmClient 删除 |

## 3. Recommended Approach

### 选定方案：前端直接调用火山 ARK API

**方案描述：** 浏览器从 `ItemEntry.razor` 直接调用火山 ARK API，消除服务端中转。

**数据流变化：**

```
Before (❌ 83% 失败率):
  浏览器 --[1]上传图片--> 海外VPS --[2]base64 46ms--> 海外VPS --[3]2.6MB POST--> 北京火山API
  总延迟: [1] + [2] + [3:91-128s] = 超过60s超时

After (✅):
  浏览器 --[1]base64编码 + 直发--> 北京火山API
  总延迟: [1: 国内网络 <10s] = 远低于30s超时
```

**可行性验证：**

| 前置条件 | 状态 | 证据 |
|------|:--:|------|
| 火山 API CORS 支持 | ✅ | Playwright 实测：从 `about:blank` 页面 `fetch` POST 到 `ark.cn-beijing.volces.com/api/v3/chat/completions`，返回 `type: "cors"`, HTTP 200, 响应正文正常 |
| API Key 安全 | ✅ | 5 人家庭场景可接受（见下方安全说明） |
| 用户地理位置 | ✅ | 国内用户 → 北京火山 API，延迟低 |
| API 功能正常 | ✅ | 文本 + 图片 base64 请求均正常返回 |

**安全说明：**
- API Key 存储在 `wwwroot/appsettings.Production.json`（gitignored），浏览器可读取
- 5 人家庭场景，Key 泄露风险低且可更换
- **建议：** 在火山 ARK 控制台为该 Key 设置最低权限（仅限 `doubao-seed-2-0-pro-260215` 模型）和消费上限告警
- CORS 已实测通过，火山 ARK 返回有效的跨域响应头

**理由：**
1. 唯一可行的方案（方案 1 链路不可靠，方案 3 成本高+封禁风险）
2. 消除跨国链路，国内用户响应时间 <10s
3. 代码简化 — 删除 ~160 行服务端 AI 代理代码
4. 无额外成本

**风险评估：**
- Low — 所有前置条件已验证通过
- 实施回退策略：Story 12.1 完成后先部署验证，确认功能正常后再执行 Story 12.2 退役

**时间线影响：**
- 新增 Epic 12，3 个 Story
- 不影响其他已完成功能

## 4. Detailed Change Proposals

### 4.1 New Epic: Epic 12 — AI 识别架构修复

**Goal:** 将 AI 识别从服务端中转改为前端直接调用火山 API，确保国内用户可用。

**Stories:**

| Story | 标题 | 范围 |
|:------|------|------|
| 12.1 | 前端 AiService 直调火山 API | 重写 `AiService.cs`（`IHttpClientFactory` 创建独立 HttpClient，base64 编码，JSON body 构造）；新增 `wwwroot/appsettings.json` 中 `VolcEngine` 配置块（ApiKey/BaseUrl/Model/TimeoutSeconds=30）；调整 `ItemEntry.razor` 流→字节数组传递；撤销 `Program.cs` 中 `AiService` 对 Server HttpClient 的依赖 |
| 12.2 | 退役服务端 AI 识别代码 | 删除 `LlmClient.cs` + `LlmOptions.cs` + `AiEndpoints.cs`（整个文件）；将 `IsValidMagic` 方法及魔数字节常量（JPEG/PNG/WebP）从 `AiEndpoints.cs` **移植到 `ImageEndpoints.cs`**，确保图片上传不丢失文件字节校验；移除 `Program.cs` 中 `AddOptions<LlmOptions>()`、`AddHttpClient<LlmClient>()`、`MapAiEndpoints()` 三处调用；移除 `appsettings.Production.json` 中 Llm 配置块 |
| 12.3 | 更新测试 + 文档 | 删除 `LlmClientTests.cs`（6 个测试）；重写 `AiServiceTests.cs`（5 个测试）；更新 Architecture §1/§3；更新 `docs/` 下 8 个文件（architecture-server、architecture-client、architecture-shared、api-contracts-server、source-tree-analysis、deployment-guide、integration-architecture、component-inventory-client）；更新 PRD §3/§8.4/§11；更新 CLAUDE.md（AI 架构/Docker/服务列表 4 处）；更新 README.md（配置表/user-secrets/Docker/文件树/排错 8 处 Llm 引用）；更新 project-context.md；更新 sprint-status.yaml |

**实施顺序：** 12.1 → 部署验证 → 12.2 → 12.3（12.1 完成后先确认功能正常，再执行退役）

### 4.2 PRD Modifications

**Glossary "AI 识别"（§3）：**

```
OLD:
AI 识别（AI Recognition）—— 调用 OpenAI 兼容的多模态 LLM API，对物品照片
进行分析，自动识别物品名称并生成备注描述。支持通过配置（base URL、模型名称、
自定义字段）适配不同 OpenAI 兼容提供商（如 OpenAI 官方、火山方舟、Kimi、
Qwen 等）。非 OpenAI 兼容的原生 API（如 Claude API、Gemini API）为后续版本。

NEW:
AI 识别（AI Recognition）—— 客户端浏览器直接调用火山引擎豆包识图 API
（OpenAI 兼容接口），对物品照片进行 base64 编码后发送，自动识别物品名称
并生成备注描述。服务端不参与 AI 调用。API 地址和密钥在客户端配置文件中管理。
v1 仅支持火山 ARK，后续版本可扩展至其他兼容提供商。
```

**NFR-4（§8.4）：**

```
OLD:
AI API 15s 超时，静默降级为手动输入

NEW:
AI API 30s 超时（浏览器端），静默降级为手动输入。API 密钥通过客户端
wwwroot/appsettings.Production.json 管理（gitignored）。
```

**Assumptions Index（§11）— 新增更新项：**

```
OLD (3 条不再成立的假设):
- §4.1 FR-2: LLM API 调用在后端完成，前端不直接持有 API key
- §4.1 FR-2: v1 支持所有 OpenAI 兼容提供商，通过配置文件切换
- §4.1 FR-3: AI API 超时阈值设为 15 秒

NEW:
- §4.1 FR-2: LLM API 由浏览器前端直接调用（CORS 已确认支持）
- §4.1 FR-2: v1 仅支持火山引擎 ARK API（doubao-seed-2-0-pro-260215）
- §4.1 FR-3: AI API 超时阈值 30 秒（浏览器端）
```

### 4.3 Architecture Modifications

**Cross-Component Dependencies 表（§3）：**

```
OLD:
| AI recognition | Server | External LLM API | HttpClient + configurable base URL |

NEW:
| AI recognition | Browser (Client) | 火山 ARK API (北京) | fetch + CORS |
```

**Technical Constraints（§1）：**

```
OLD:
- OpenAI-compatible LLM API — configurable via base URL + model name + custom fields

NEW:
- 火山引擎 ARK API（OpenAI 兼容）— 浏览器端直接调用，CORS 已通过 Playwright 实测确认
```

### 4.4 sprint-status.yaml Addition

```yaml
epic-12: backlog
  12-1-frontend-direct-api: backlog
  12-2-decommission-server-ai: backlog
  12-3-update-tests-docs: backlog
```

### 4.5 Docker 部署影响

生产部署需新增 Client 端配置文件挂载：

```bash
# 新增：Client 端火山 API 配置（含 ApiKey）
cat > src/BoxWise.Client/wwwroot/appsettings.Production.json << 'EOF'
{
  "VolcEngine": {
    "BaseUrl": "https://ark.cn-beijing.volces.com/api/v3",
    "ApiKey": "ark-xxx",
    "Model": "doubao-seed-2-0-pro-260215",
    "TimeoutSeconds": 30
  }
}
EOF
```

Server 端 `appsettings.Production.json` 移除 Llm 配置块，Docker compose 无需额外挂载（Client 静态文件随 `dotnet publish` 输出）。

## 5. Implementation Handoff

### Scope Classification: Moderate

需要前后端代码变更 + 测试重写 + 多文档更新 + 退役旧代码。由 Developer agent 顺序执行。

### Handoff Plan

| 步骤 | 负责人 | 技能 | 说明 |
|------|--------|------|------|
| 1. 创建 Story 12.1 | Developer | `bmad-create-story` | 前端直调实现 |
| 2. 实施 Story 12.1 | Developer | `bmad-dev-story` | 含部署验证 |
| 3. 创建 Story 12.2 | Developer | `bmad-create-story` | 服务端退役（确认 12.1 正常后） |
| 4. 实施 Story 12.2 | Developer | `bmad-dev-story` | |
| 5. 创建 Story 12.3 | Developer | `bmad-create-story` | 测试 + 文档 |
| 6. 实施 Story 12.3 | Developer | `bmad-dev-story` | |
| 7. Epic 回顾 | Developer | `bmad-retrospective` | |

### Success Criteria

- [ ] 国内用户浏览器可直接调用火山 API 完成 AI 识别（CORS 已确认 ✅）
- [ ] AI 识别成功率达到生产可用水平（>90%）
- [ ] 服务端 `LlmClient.cs` + `LlmOptions.cs` + `AiEndpoints` 识别端点退役
- [ ] 所有现有测试通过 + `AiServiceTests` 5 个新测试通过，`LlmClientTests` 6 个已删除
- [ ] Architecture + CLAUDE.md + docs/ + PRD + README 文档更新
- [ ] Server 端 `appsettings.Production.json` 清理 Llm 配置；Client 端新增 `VolcEngine` 配置
- [ ] Docker 部署文档更新（新增 Client wwwroot 配置文件生成步骤）
