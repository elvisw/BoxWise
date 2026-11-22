---
id: 12
type: epic
title: "AI 识别架构修复"
parent: "initiative-boxwise"
covers: []
sources: ["_bmad-output/initiative-boxwise/sprint-change-proposal-2026-06-05/sprint-change-proposal-2026-06-05.md"]
companions: ["_bmad-output/initiative-boxwise/architecture-boxwise/architecture-boxwise.md", "_bmad-output/initiative-boxwise/ux-design-specification/ux-design-specification.md"]
after: []
refined: true
status: done
risk: low
---
# AI 识别架构修复

## Description

AI 识别改为前端直调，服务端 AI 识别代码退役。

## Outcome

AI 识别改为前端直调，服务端 AI 识别代码退役。

## Requirements

- R1 (source `sprint-change-proposal-2026-06-05.md`): 前端 AiService 直调火山 API 已完成。
- R2 (source `sprint-change-proposal-2026-06-05.md`): 退役服务端 AI 识别代码 已完成。
- R3 (source `sprint-change-proposal-2026-06-05.md`): 更新测试 + 文档 已完成。

## Constraints

- 遵循主 PRD 的非目标与部署约束；历史实现不重新验收。

## Done when

- 本 Epic 的 Story 均有 tracking 记录，验收条件保留在各自 plan。
- 不新增超出本 Epic 范围的功能。

## Boundaries

- Must not change: 不重写历史验收结论；不扩展到其他 Epic 的范围。

## References

- parent — _bmad-output/initiative-boxwise/initiative-boxwise.md, Requirements
- source — _bmad-output/initiative-boxwise/sprint-change-proposal-2026-06-05/sprint-change-proposal-2026-06-05.md
- architecture — _bmad-output/initiative-boxwise/architecture-boxwise/architecture-boxwise.md

## Notes

- Retrospective: epic-ai-recognition-architecture-retrospective.md
