---
id: 1
type: epic
title: "项目搭建与账户认证"
parent: "initiative-boxwise"
covers: ["R17", "R18", "R19"]
sources: ["_bmad-output/initiative-boxwise/archive-v6/_bmad-output/initiative-boxwise/archive-v6/planning-artifacts/epics.md"]
companions: ["_bmad-output/initiative-boxwise/architecture-boxwise/architecture-boxwise.md", "_bmad-output/initiative-boxwise/ux-design-specification/ux-design-specification.md"]
after: []
refined: true
status: done
risk: low
---
# 项目搭建与账户认证

## Description

项目骨架就绪，家庭成员可以创建账户并登录使用。

## Outcome

项目骨架就绪，家庭成员可以创建账户并登录使用。

## Requirements

- R1 (parent R17): 用户注册：管理员通过后台管理界面创建家庭成员账户。不提供面向最终用户的自助注册功能（后续版本也不做）。
- R2 (parent R18): 用户登录：已注册用户可以通过用户名和密码登录系统。
- R3 (parent R19): 登录保护：所有物品相关页面（浏览、搜索、录入、详情）需要登录后才能访问。

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

- Retrospective: epic-project-setup-and-authentication-retrospective.md
