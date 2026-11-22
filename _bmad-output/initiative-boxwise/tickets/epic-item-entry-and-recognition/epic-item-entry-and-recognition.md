---
id: 3
type: epic
title: "物品录入与智能识别"
parent: "initiative-boxwise"
covers: ["R1", "R2", "R3", "R4", "R5", "R6", "R7", "R8"]
sources: ["_bmad-output/initiative-boxwise/archive-v6/_bmad-output/initiative-boxwise/archive-v6/planning-artifacts/epics.md"]
companions: ["_bmad-output/initiative-boxwise/architecture-boxwise/architecture-boxwise.md", "_bmad-output/initiative-boxwise/ux-design-specification/ux-design-specification.md"]
after: []
refined: true
status: done
risk: low
---
# 物品录入与智能识别

## Description

用户可以拍照或手动录入物品，AI 识别预填，选择位置和标签后入库。

## Outcome

用户可以拍照或手动录入物品，AI 识别预填，选择位置和标签后入库。

## Requirements

- R1 (parent R1): 拍照采集（可选）：用户可以选择拍摄物品照片或跳过拍照直接录入。照片为物品记录的可选字段。
- R2 (parent R2): AI 识别预填：系统将物品照片 base64 编码后，通过客户端浏览器直接发送至火山引擎 ARK API（OpenAI 兼容），自动识别物品名称并生成简要描述，预填至备注字段。服务端不参与 AI 调用。v1 仅支持火山 ARK（doubao-seed-2-0-pro-260215）。
- R3 (parent R3): 手动输入兜底：当 AI API 调用失败、超时或不可用时，系统自动切换为纯手动输入模式。
- R4 (parent R4): 位置分配：用户为物品分配一个层级位置（房间 → 柜子 → 箱子）。
- R5 (parent R5): 标签附加：用户可以为物品添加多个自定义标签。
- R6 (parent R6): 入库保存：用户确认所有信息后保存物品记录。
- R7 (parent R7): 位置继承：保存一件物品后，下一件物品录入时位置字段默认填充为上一件的位置。
- R8 (parent R8): 连续模式提示：UI 清晰标识当前处于"连续收纳"状态及继承的位置。

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

- Retrospective: epic-item-entry-and-recognition-retrospective.md
