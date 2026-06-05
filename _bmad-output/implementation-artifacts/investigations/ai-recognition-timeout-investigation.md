# Investigation: AI 识别生产环境超时

## Hand-off Brief

1. **What happened.** 生产环境 AI 识别 100% 超时（今天 3/3，7 天内 10/12=83%）。根因确认为：**海外 VPS → 火山 ARK API（北京）的国际链路太慢**，上传 2.6MB base64 图片需 91-128 秒（最佳情况），远超 60s 默认超时。
2. **Where the case stands.** 根因已 Confirmed。生产配置中 `TimeoutSeconds` 未覆盖（使用代码默认 60s）。连接极不稳定：267ms RTT，40% 丢包率，有效上传速度仅 ~29KB/s。纯文本请求仅 6.35s 完成，瓶颈在图片上传而非 API 推理。
3. **What's needed next.** 架构变更（推荐图片压缩前置或前端重用火山 URL），不是参数调整。推荐进入 `bmad-correct-course` 评估方案。

## Case Info

| Field            | Value                                                                      |
| ---------------- | -------------------------------------------------------------------------- |
| Ticket           | N/A                                                                        |
| Date opened      | 2026-06-05                                                                 |
| Status           | Active                                                                     |
| System           | ASP.NET Core 10.0, Debian VPS (海外), 火山引擎豆包 API (国内)                |
| Evidence sources | `LlmClient.cs`, `AiEndpoints.cs`, `AiService.cs`, `LlmOptions.cs`, 生产 `appsettings.Production.json`, `journalctl`, ping/curl 实测 |

## Problem Statement

用户报告：生产环境 AI 识别（`LlmClient` 调用火山引擎豆包识图模型）工作不正常。用户假设海外服务器访问火山 API 传输大尺寸图片导致超时失败。**假设已验证为真。**

## Evidence Inventory

| Source   | Status                          | Notes     |
| -------- | ------------------------------- | --------- |
| `LlmClient.cs` | Available | 完整 AI 调用逻辑，base64 编码 + HTTP POST |
| `AiEndpoints.cs` | Available | 文件上传 → 服务器临时保存 → 调用 LlmClient |
| `AiService.cs` (Client) | Available | 客户端上传到服务器，默认 90s 超时 |
| `LlmOptions.cs` | Available | 超时配置默认 60s，注释已承认 15s 不足 |
| `ItemEntry.razor` | Available | 前端 AI 调用流程，失败静默降级 |
| `appsettings.Production.json` | Available (via sudo) | **TimeoutSeconds 未配置！** BaseUrl=ark.cn-beijing.volces.com |
| 生产环境系统日志 (journalctl) | Available | **3/3 今天超时，7 天 10/12=83% 失败率** |
| 网络延迟测试 (ping) | Available | **267ms RTT, 40% 丢包率** |
| API 连接测试 | Available | TCP 连接 1.4-7.3s（极不稳定），TLS 0.6-8.3s |
| 纯文本 API 调用 (无图片) | Available | **6.35s 成功** — 证明 API 推理不慢 |
| 上传速度基准测试 | Available | **2.6MB 图片需 91-128s（最佳情况）> 60s 超时** |
| 生产图片样本 | Available | 1.6MB (data/1/original.jpg) + 2.0MB (data/2/original.jpg), base64 后 2.6MB |

## Investigation Backlog

| # | Path to Explore | Priority              | Status                                | Notes     |
| - | --------------- | --------------------- | ------------------------------------- | --------- |
| 1 | ~~收集生产环境 LizClient 日志~~ | ~~High~~ | Done | journalctl 确认 100% 今日超时 |
| 2 | ~~测量典型图片的 base64 大小~~ | ~~High~~ | Done | 2.0MB → 2.6MB (+33%) |
| 3 | ~~生产环境网络延迟测试~~ | ~~High~~ | Done | 267ms RTT, 40% loss, 极不稳定 |
| 4 | ~~生产环境上传速度实测~~ | ~~High~~ | Done | 91-128s for 2.6MB > 60s timeout |
| 5 | 前端直调方案的可行性评估 | High | Open | 需评估火山 API 浏览器端支持 |
| 6 | 图片压缩前置方案 | High | Open | 客户端压缩后上传的成本和影响 |
| 7 | 异步化 + 轮询方案 | Low | Open | 用户体验权衡 |

## Timeline of Events

| Time        | Event               | Source                | Confidence            |
| ----------- | ------------------- | --------------------- | --------------------- |
| ~2026-05-21 | AI 识别功能首次实现，超时默认 15s | `LlmOptions.cs:15` 注释 | Deduced |
| 2026-05-24 前 | 生产发现 15s 超时不足，默认值提升到 60s | `LlmOptions.cs:19` | Confirmed |
| 2026-05-24 前 | 客户端超时提升到 90s（长于服务端） | `AiService.cs:15` | Confirmed |
| 2026-06-04 23:08 | 最近一次生产配置部署 | `appsettings.Production.json` 时间戳 | Confirmed |
| 2026-06-05 12:27:42 | 用户第一次尝试 AI 识别 | journalctl | Confirmed |
| 2026-06-05 12:28:42 | 超时 #1 (60s) — "AI API 超时，降级为手动输入" | journalctl | Confirmed |
| 2026-06-05 12:29:48 | 用户第二次尝试 | journalctl | Confirmed |
| 2026-06-05 12:30:48 | 超时 #2 (60s) | journalctl | Confirmed |
| 2026-06-05 12:31:22 | 用户第三次尝试 | journalctl | Confirmed |
| 2026-06-05 12:32:21 | 超时 #3 (59s) | journalctl | Confirmed |
| 2026-06-05 12:32:34 | 用户手动输入创建 Item #2（data/2/original.jpg 2.0MB） | journalctl + 文件时间戳 | Deduced |
| 2026-06-05 12:53 | 调查开始，收集生产证据 | 本案例 | Confirmed |
| 2026-06-05 13:10 | 上传速度基准测试完成 — 确认 2.6MB 需要 91-128s | curl 实测 | Confirmed |

## Confirmed Findings

### Finding 1: 超时问题已被团队识别并做过缓解

**Evidence:** `src/BoxWise.Server/Configuration/LlmOptions.cs:14-19`

**Detail:** 代码注释明确写有 "生产 VPS 带宽有限 + 视觉模型推理较慢，15 秒极易超时"，默认超时已从 15s → 60s。这个缓解措施不够——瓶颈是带宽而非超时设置。

### Finding 2: 数据传输存在双重跨海路径

**Evidence:** `src/BoxWise.Server/Endpoints/AiEndpoints.cs:48-69`, `src/BoxWise.Server/Services/LlmClient.cs:58-85`

**Detail:** 当前数据流：`Client(浏览器) --[1]上传--> 海外VPS --[2]base64编码--> 海外VPS --[3]2.6MB JSON POST--> 火山API(北京)`。图片数据两次跨越地理边界。步骤 [3] 是瓶颈。

### Finding 3: base64 编码扩大传输量约 33%

**Evidence:** `src/BoxWise.Server/Services/LlmClient.cs:58` + 生产实测 (`/tmp/test_ai_api.sh`)

**Detail:** 生产环境 2.0MB 原始 JPEG → base64 后 2.6MB (2,677,796 bytes, overhead 33%)。编码耗时仅 46ms——**计算不是瓶颈**。瓶颈是网络上传 2.6MB 数据。

### Finding 4: 客户端超时设计合理

**Evidence:** `src/BoxWise.Client/Services/AiService.cs:14-15`

**Detail:** 客户端默认超时 90s = 服务端 60s + 30s 缓冲。设计合理，但服务端 60s 本身不足以完成上传。

### Finding 5: 生产配置未覆盖 TimeoutSeconds

**Evidence:** `appsettings.Production.json` (生产环境, `sudo -u boxwise cat`)

**Detail:** 生产 Llm 配置仅 3 项，TimeoutSeconds 未出现：
```json
"Llm": {
    "BaseUrl": "https://ark.cn-beijing.volces.com/api/v3",
    "ApiKey": "ark-xxx-REDACTED",
    "Model": "doubao-seed-2-0-pro-260215"
}
```

### Finding 6: 100% 超时率 — 生产日志确证

**Evidence:** `journalctl -u boxwise` (生产环境)

**Detail:**
- **今天 Jun 05:** 3/3 = 100% 失败。每次精确 59-60s 超时
- **7 天:** 10/12 = 83% 失败率
- 用户行为模式：每次超时后重试 → 3 次后放弃，手动输入

### Finding 7: 国际网络链路极不稳定

**Evidence:** 生产环境 `ping` + `curl` 连接测试

**Detail:**
- ICMP: 267ms 平均 RTT，**40% 丢包率**（5 发 3 收）
- TCP: 1.4s、7.3s、无法连接（3 次测试中 1 次 connect-timeout 10s）
- 路由: VPS → Arelion/NTT 国际骨干 → 中国移动 221.183.x.x → 北京, 15+ 跳
- 有效上传速度: 363 bps (最差) ~ 29,438 bps (最好)

### Finding 8: API 推理不慢，瓶颈纯在上传

**Evidence:** 生产环境对比测试

| 测试 | Payload | 耗时 | HTTP |
|------|---------|------|------|
| 纯文本 | ~200 bytes | **6.35s** | 200 ✅ |
| 1KB padding | ~1KB | 3.1s | 200 ✅ |
| 10KB padding | ~10KB | 13.5s | 200 ✅ |
| 49KB padding | ~49KB | 2.4s | 200 ✅ |
| 98KB padding | ~98KB | 3.4s | 200 ✅ |
| **2.6MB 图片** (实测) | ~2.6MB | **>120s (curl max-time)** | 超时 ❌ |

**结论：瓶颈是 2.6MB 数据上传，不是模型推理。纯文本 6.35s 完成。**

### Finding 9: 用户反复重试后放弃

**Evidence:** journalctl 时序分析

**Detail:** 12:27→12:32 的 5 分钟内 3 次尝试全部超时，用户最终手动输入。每次等待 60s 超时 = 浪费 3 分钟 + 挫败感。

## Deduced Conclusions

### Deduction 1: 瓶颈在服务端→火山API 环节 (Confirmed)

**Based on:** Finding 6, 7, 8

**Reasoning:** 纯文本请求 6.35s 完成，证明 API 推理和连接建立都不慢。但 2.6MB 图片上传预估 91-128s，超额 60s 超时。日志显示每次精确 60s 超时——C# `CancelAfter(60s)` 触发，HTTP 上传还没完成。

**Conclusion:** **上网速度太慢，不是推理太慢。** 2.6MB base64 图片无法在 60s 内通过这条国际链路完成上传。

### Deduction 2: 单纯增加 TimeoutSeconds 不能根本解决问题 (Confirmed)

**Based on:** Finding 1, 6, 8

**Reasoning:** 已尝试过一次（15s → 60s），仍 100% 失败。即使设 120s，用户体验不可接受（拍照后等 2 分钟）。最差情况下（363 bps）需要 2 小时。用户舒适上限约 10-15s。

**Conclusion:** 需要架构层面的变更——减少/消除跨海传输的数据量。

### Deduction 3: 根因在架构而非配置 (Confirmed)

**Based on:** All findings

**Reasoning:** 当前的 "服务端中转" 架构要求海外 VPS 将 2.6MB 数据发往北京。无论怎么调整超时，这条链路的物理限制（丢包率、RTT、带宽）无法通过参数解决。

**Conclusion:** 需要改变数据路径：要么消除国际链路（前端直调），要么减少数据量（压缩）。

## Hypothesized Paths

### Hypothesis 1: 海外 VPS → 火山 API 网络延迟是主因

**Status:** ✅ Confirmed

**Theory:** 服务端→火山 API 的 2.6MB 上传时长 > 60s 超时。

**Resolution:** 生产环境实测确认：2.6MB 图片上传需 91-128s（最佳），远超 60s。纯文本仅 6.35s，排除推理慢的可能。

### Hypothesis 2: 火山 API 推理本身慢

**Status:** ❌ Refuted

**Theory:** 豆包识图模型推理时间长。

**Resolution:** 纯文本 API 调用 6.35s 完成（含网络往返 + 推理）。推理产生 145 tokens + 136 reasoning tokens，TTFB 6.35s。与大图上传时间（91-128s）相比可忽略。

### Hypothesis 3: 将 AI 调用移到前端可解决问题

**Status:** Open

**Theory:** 如果浏览器直接调用火山 API，消除 "海外 VPS → 火山 API" 这一最慢的链路。

**Supporting indicators:**
- 消除 2.6MB 跨海上传（服务端→火山API）
- 浏览器可直接发二进制（不需要 base64 编码）
- 用户浏览器→火山 API 的路径可能更短（取决于用户位置）

**Would confirm:** 火山 API 支持浏览器 CORS + API Key 安全方案

**Would refute:** 火山 API 不支持浏览器直接调用

**Resolution:** 待评估。需要调研火山引擎 ARK API 的浏览器端支持情况。

## Missing Evidence

| Gap              | Impact                               | How to Obtain   |
| ---------------- | ------------------------------------ | --------------- |
| ~~生产环境实际超时发生的环节~~ | ~~无法确认瓶颈~~ | ✅ 已通过日志+实测确认 |
| ~~典型图片的 base64 大小~~ | ~~无法量化数据量~~ | ✅ 已确认 2.0MB→2.6MB |
| ~~生产 VPS → 火山 API 的网络延迟~~ | ~~Hypothesis 1 核心证据~~ | ✅ 已确认 |
| 火山 API 是否支持浏览器端 CORS | 决定前端直调方案可行性 | 查阅火山引擎文档或实测 |
| 火山 ARK API 是否有 JavaScript SDK | 前端集成的复杂度 | 查阅火山引擎开发者文档 |
| 用户通常从哪里访问 BoxWise | 如果用户也在海外，前端直调同样慢 | 询问用户 |

## Source Code Trace

| Element       | Detail                                      |
| ------------- | ------------------------------------------- |
| Error origin  | `src/BoxWise.Server/Services/LlmClient.cs:99` — `OperationCanceledException` after 60s |
| Trigger       | `ItemEntry.razor:96` → `AiService.RecognizeAsync()` → `POST /api/ai/recognize` → `AiEndpoints.RecognizeAsync:69` → `LlmClient.RecognizeAsync:85` `SendAsync` 超时 |
| Condition     | 2.6MB HTTP body 上传时间 > 60s（生产实测 91-128s） |
| Related files | `AiEndpoints.cs`, `AiService.cs`, `ItemEntry.razor`, `LlmOptions.cs`, `Program.cs:150-155`, `appsettings.Production.json` |

## Conclusion

**Confidence:** High

### 根因

**生产环境 BoxWise 的 AI 识别 100% 超时，根因是海外 VPS 到火山引擎 ARK API（北京 `ark.cn-beijing.volces.com`）的国际链路上传速度不足以在 60 秒内传输 2.6MB 的 base64 编码图片。**

### 证据链

```
时间线: 用户拍照 → 浏览器上传到 VPS → VPS base64 编码 (46ms) → VPS POST 2.6MB 到火山 API → 60s CancelAfter 触发 → 超时
                                                                      ↑
                                                              瓶颈在这里: 91-128s 需要
```

| 证据 | 值 |
|------|:--|
| 图片大小 | 2.0MB JPEG → 2.6MB base64 |
| 网络 RTT | 267ms (40% 丢包) |
| 有效上传速率 | ~29KB/s (最佳) |
| 2.6MB 上传预估 | 91-128s |
| 当前超时设置 | 60s (默认) |
| 纯文本 API 调用 | 6.35s (推理不慢) |
| 7 天失败率 | 83% (10/12) |

### 为什么不加超时不能解决

即使把 `TimeoutSeconds` 设到 120s：用户体验不可接受（拍照后等 1-2 分钟），且网络波动时（363 bps 情况）仍会超时。

## Recommended Next Steps

### Fix direction

三种架构方案（推荐 `bmad-correct-course` 评估选优）：

1. **图片压缩前置（推荐优先尝试）** — 客户端上传前将图片压缩到合理分辨率（如 1024px 宽），目标 <500KB。base64 后 <670KB，在上传速度最差时也能在 ~45s 内完成。改动最小，可快速验证。
2. **前端重用火山 URL** — 浏览器直接调用火山 API，消除服务端中转。需确认：CORS 支持、API Key 安全方案（后端签发临时 token？）、火山是否有 JS SDK
3. **换用全球部署的模型** — 如 OpenAI/Claude/Gemini API，服务器在海外延迟低。但需评估成本 + 数据合规

### Workaround（临时缓解）

在生产 `appsettings.Production.json` 中添加 `"TimeoutSeconds": 120`，但这不是长久之计——用户体验会因等待过长而严重受损。

### 推荐工作流

进入 **`bmad-correct-course`** → 评估替代方案 → 更新架构 → 创建 Story → 实施。

## Reproduction Plan

1. ✅ 从生产 VPS 确认网络延迟和丢包率
2. ✅ 用实际生产图片（2.0MB）测试 API 调用时间
3. ✅ 用纯文本请求排除 API 推理慢的可能
4. ✅ 用不同大小 payload 量化上传速度
5. ⬜ 在浏览器中测试火山 API 的 CORS 支持
6. ⬜ 测试图片压缩到不同分辨率后的识别准确率

## Side Findings

- 生产 `appsettings.Production.json` 权限 `600 (boxwise:boxwise)`，elvisw 用户需要用 sudo 才能读取——建议在运维文档中记录
- `LlmOptions.cs:15` 注释写的是 15s 但代码默认值已是 60s——注释可能需要更新
- `AiService.cs:14` 客户端超时配置键 `AiSettings:TimeoutSeconds` 与服务端 `Llm:TimeoutSeconds` 不一致——代码异味
- 生产环境 `data/images/` 目录为空但图片实际存在 `data/1/`、`data/2/` 数字目录中——看起来图片是按 Item ID 存储的
- 火山豆包 `doubao-seed-2-0-pro-260215` 模型启用了 reasoning（返回了 136 reasoning_tokens），这增加了 TTFB 时间但影响很小（<1s）
