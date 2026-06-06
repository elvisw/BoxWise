---
title: Epic 12 Retrospective — AI 识别架构修复
date: 2026-06-06
epic: 12
stories_completed: 3/3
status: done
---

# Epic 12 Retrospective: AI 识别架构修复

## Epic Summary

**目标:** 将 AI 识别从服务端中转改为前端直接调用火山 API，解决海外 VPS 到北京火山 ARK 的国际链路超时问题（91-128s > 60s）。

**交付:**

| Story | 标题 | 提交数 | 评审轮次 |
|:---|:---|:--:|:--:|
| 12.1 | 前端 AiService 直调火山 API | 4 | 1 轮 (P1-P3) |
| 12.2 | 退役服务端 AI 识别代码 | 1 | 2 轮 (P1-P4) |
| 12.3 | 更新测试 + 文档 | 4 | 3 轮 (10 处修复) |

**关键指标:**
- 总提交: 11 commits (474611c → be47eaa)
- 删除代码: ~470 行 (LlmClient.cs + AiEndpoints.cs + LlmOptions.cs)
- 新增代码: ~60 行 (AiService 重写 + ImageEndpoints 魔数验证)
- 文档更新: 14 个文件
- 测试: 273 通过 (241 Server + 32 Client)，零回归

## What Went Well

1. **架构决策正确** — Sprint Change Proposal 的方案选择（浏览器直调）经 Playwright CORS 实测验证后执行，避免了返工。前端直调火山 ARK API 从 >91s 降至 <10s。

2. **退役纪律严格** — Story 12.2 完全遵循 spec 操作顺序（先改 Program.cs 再删文件），确保中间状态可编译。魔数验证（IsValidMagic）正确移植到 ImageEndpoints.cs，图片上传校验不丢失。grep 零残留验证贯穿始终。

3. **评审质量高** — 三轮并行评审（Blind Hunter + Edge Case Hunter + Acceptance Auditor）累计发现 17 个 patch/defer 问题，全部修复或记录。12.3 的 10 处文档遗漏最终全部清理。

4. **Story 12.1 实现精准** — AiService 重写从 Server-proxy multipart 到 OpenAI-compatible JSON POST，base64 编码、IHttpClientFactory、Bearer 认证、TryParse JSON+正则回退，全部按 spec 实现。8 个测试覆盖成功/失败/超时/网络错误/空响应/配置缺失/载荷验证 7 个维度。

## What Could Be Improved

1. **Story 12.3 文档范围被低估** — 14 个文件需要更新，经历 3 轮评审才发现 10+ 处遗漏。纯文档 Story 容易被草率处理，应加强文档 Story 的 checklist 验证。

2. **多次修复模式** — Epic 12 有 5 个 `@ fix:` 提交。12.1 (3 patches)、12.2 (4 patches)、12.3 (10 patches)。虽然全部在评审中发现，但说明初始实现时对边界条件的考虑不足（配置完整性、异常分类、文档一致性）。

3. **Story 12.1 Task 未及时勾选** — 代码实现完成但 story 文件任务标记未更新，直到发现 git log 后才修正。应建立"代码提交即更新 story"的习惯。

4. **_baseUrl 字段反复增删** — 12.1 实现时添加 `_baseUrl` 字段，f0ad661 修复时作为"dead code"删除，12.3 AC 又要求检查 BaseUrl 完整性。最终通过 HttpClient.BaseAddress 间接处理。反映了配置管理设计的迭代成本。

## Lessons Learned

1. **Grepping 是退役的黄金标准** — Story 12.2 的 `grep -rn "LlmClient|LlmOptions|MapAiEndpoints" src/` 和 Story 12.3 的文档 grep 确认零残留，是退役 Story 的 DoD 必须项。

2. **纯文档 Story 需要多次评审** — 12.3 的 3 轮评审发现 PRD/Architecture/CLAUDE/README 等多个文件的遗漏。文档评审应分轮次、分文件组，避免一次性扫过。

3. **操作顺序约束在退役类 Story 中至关重要** — 12.2 的 Task 顺序（先修 Program.cs 再删文件）确保了中间状态可编译。这条规则应写入退役 Story 模板。

## Action Items

- [ ] 建立退役 Story checklist: grep 零残留 + build 零错误 + test 零回归 + 文档更新
- [ ] 纯文档 Story 增加文档一致性检查轮次（先 grep 找引用，再逐文件替换，最后 grep 验证）
- [ ] 完成后及时更新 story 文件 Task 勾选 + sprint-status

## Next Epic Preparation

无下一个 Epic。Epic 12 是当前规划的最后一个 Epic。项目已完成 12 个 Epic，83 个 Story。

如需继续开发，建议方向:
- 性能优化（图片缩略图批量生成、数据库查询优化）
- 多用户权限细化（只读用户、家庭组）
- 国际化（多语言支持）
