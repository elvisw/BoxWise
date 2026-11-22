---
id: 14
type: epic
title: "技术债务清偿（第三轮）"
parent: "initiative-boxwise"
covers: []
sources: ["_bmad-output/initiative-boxwise/sprint-change-proposal-2026-06-07-tech-debt-3/sprint-change-proposal-2026-06-07-tech-debt-3.md"]
companions: ["_bmad-output/initiative-boxwise/architecture-boxwise/architecture-boxwise.md", "_bmad-output/initiative-boxwise/ux-design-specification/ux-design-specification.md"]
after: []
refined: true
status: done
risk: low
---
# 技术债务清偿（第三轮）

## Description

第三轮技术债清偿：管理员 2FA 保护、HasFlag、SW 缓存、CI 包验证、grep 与 i18n。

## Outcome

第三轮技术债清偿：管理员 2FA 保护、HasFlag、SW 缓存、CI 包验证、grep 与 i18n。

## Requirements

- R1 (source `sprint-change-proposal-2026-06-07-tech-debt-3.md`): #29 ResetTwoFactor 管理员自保护 已完成。
- R2 (source `sprint-change-proposal-2026-06-07-tech-debt-3.md`): #30 HasFlag switch 精确值匹配 已完成。
- R3 (source `sprint-change-proposal-2026-06-07-tech-debt-3.md`): #27 SW 缓存键不匹配 — 关闭 Issue 已完成。
- R4 (source `sprint-change-proposal-2026-06-07-tech-debt-3.md`): #28 CI tar/ZIP 包安全验证 已完成。
- R5 (source `sprint-change-proposal-2026-06-07-tech-debt-3.md`): #31 grep 验证命令通用化 已完成。
- R6 (source `sprint-change-proposal-2026-06-07-tech-debt-3.md`): #32 i18n 硬编码 — 关闭 Issue 已完成。

## Constraints

- 遵循主 PRD 的非目标与部署约束；历史实现不重新验收。

## Done when

- 本 Epic 的 Story 均有 tracking 记录，验收条件保留在各自 plan。
- 不新增超出本 Epic 范围的功能。

## Boundaries

- Must not change: 不重写历史验收结论；不扩展到其他 Epic 的范围。

## References

- parent — _bmad-output/initiative-boxwise/initiative-boxwise.md, Requirements
- source — _bmad-output/initiative-boxwise/sprint-change-proposal-2026-06-07-tech-debt-3/sprint-change-proposal-2026-06-07-tech-debt-3.md
- architecture — _bmad-output/initiative-boxwise/architecture-boxwise/architecture-boxwise.md

## Notes

- 无额外未决事项。
