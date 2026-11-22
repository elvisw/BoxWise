---
id: 2
type: epic
title: "位置体系与标签管理"
parent: "initiative-boxwise"
covers: ["R5", "R14", "R15"]
sources: ["_bmad-output/initiative-boxwise/archive-v6/_bmad-output/initiative-boxwise/archive-v6/planning-artifacts/epics.md"]
companions: ["_bmad-output/initiative-boxwise/architecture-boxwise/architecture-boxwise.md", "_bmad-output/initiative-boxwise/ux-design-specification/ux-design-specification.md"]
after: []
refined: true
status: done
risk: low
---
# 位置体系与标签管理

## Description

用户可以创建层级收纳位置体系，并用标签分类物品。

## Outcome

用户可以创建层级收纳位置体系，并用标签分类物品。

## Requirements

- R1 (parent R5): 标签附加：用户可以为物品添加多个自定义标签。
- R2 (parent R14): 层级位置创建：用户可以创建、命名、编辑、删除层级位置节点，层级深度由用户自定义（如房间 → 柜子 → 箱子，或房间 → 柜子 → 抽屉 → 分隔盒等更深层级）。
- R3 (parent R15): 层级浏览：用户可以在浏览视图中按位置层级导航查看物品。

## Constraints

- 遵循主 PRD 的非目标与部署约束；历史实现不重新验收。

## Done when

- 本 Epic 的 Story 均有 tracking 记录，验收条件保留在各自 plan。
- 不新增超出本 Epic 范围的功能。

## Boundaries

- Must not change: 不重写历史验收结论；不扩展到其他 Epic 的范围。

## References

- parent — _bmad-output/initiative-boxwise/initiative-boxwise.md, Requirements
- source — _bmad-output/initiative-boxwise/archive-v6/_bmad-output/initiative-boxwise/archive-v6/planning-artifacts/epics.md
- architecture — _bmad-output/initiative-boxwise/architecture-boxwise/architecture-boxwise.md

## Notes

- Retrospective: epic-locations-and-tags-retrospective.md
