---
id: 11
type: epic
title: "前端适配 + 退役 + 文档更新"
parent: "initiative-boxwise"
covers: []
sources: ["_bmad-output/initiative-boxwise/epics-identity-scaffold-migration/epics-identity-scaffold-migration.md"]
companions: ["_bmad-output/initiative-boxwise/architecture-boxwise/architecture-boxwise.md", "_bmad-output/initiative-boxwise/ux-design-specification/ux-design-specification.md"]
after: []
refined: true
status: done
risk: low
---
# 前端适配 + 退役 + 文档更新

## Description

前端适配 Identity、保留通行密钥、退役旧认证代码并更新文档。

## Outcome

前端适配 Identity、保留通行密钥、退役旧认证代码并更新文档。

## Requirements

- R1 (source `epics-identity-scaffold-migration.md`): Settings.razor 替换为跳转链接 已完成。
- R2 (source `epics-identity-scaffold-migration.md`): Login.razor 保留通行密钥 + 适配 Identity 登录 已完成。
- R3 (source `epics-identity-scaffold-migration.md`): 退役旧代码 + 测试更新 已完成。
- R4 (source `epics-identity-scaffold-migration.md`): SameSite 策略 + 更新 Architecture/UX 文档 已完成。

## Constraints

- 遵循主 PRD 的非目标与部署约束；历史实现不重新验收。

## Done when

- 本 Epic 的 Story 均有 tracking 记录，验收条件保留在各自 plan。
- 不新增超出本 Epic 范围的功能。

## Boundaries

- Must not change: 不重写历史验收结论；不扩展到其他 Epic 的范围。

## References

- parent — _bmad-output/initiative-boxwise/initiative-boxwise.md, Requirements
- source — _bmad-output/initiative-boxwise/epics-identity-scaffold-migration/epics-identity-scaffold-migration.md
- architecture — _bmad-output/initiative-boxwise/architecture-boxwise/architecture-boxwise.md

## Notes

- Retrospective: epic-identity-adaptation-and-retirement-retrospective.md
