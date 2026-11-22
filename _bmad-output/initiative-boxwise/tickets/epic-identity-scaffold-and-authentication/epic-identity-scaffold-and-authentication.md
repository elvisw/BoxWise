---
id: 10
type: epic
title: "Server 端 Identity 脚手架 + 认证流程"
parent: "initiative-boxwise"
covers: []
sources: ["_bmad-output/initiative-boxwise/epics-identity-scaffold-migration/epics-identity-scaffold-migration.md"]
companions: ["_bmad-output/initiative-boxwise/architecture-boxwise/architecture-boxwise.md", "_bmad-output/initiative-boxwise/ux-design-specification/ux-design-specification.md"]
after: []
refined: true
status: done
risk: low
---
# Server 端 Identity 脚手架 + 认证流程

## Description

Server 端 Identity 脚手架、IEmailSender、Cookie 桥接和 2FA 登录验证就绪。

## Outcome

Server 端 Identity 脚手架、IEmailSender、Cookie 桥接和 2FA 登录验证就绪。

## Requirements

- R1 (source `epics-identity-scaffold-migration.md`): 脚手架 Identity 页面 + 构建验证 已完成。
- R2 (source `epics-identity-scaffold-migration.md`): IEmailSender 适配器注册 已完成。
- R3 (source `epics-identity-scaffold-migration.md`): Cookie 认证桥接 + LoginPath 配置 已完成。
- R4 (source `epics-identity-scaffold-migration.md`): 2FA 登录 + .NET 10 Bug 验证/workaround 已完成。

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

- Retrospective: epic-identity-scaffold-and-authentication-retrospective.md
