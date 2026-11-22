---
id: 8
type: epic
title: "账户安全加固"
parent: "initiative-boxwise"
covers: ["R22a", "R22b", "R22c", "R22d", "R22e", "R22f", "R22g"]
sources: ["_bmad-output/initiative-boxwise/sprint-change-proposal-2026-05-28-security/sprint-change-proposal-2026-05-28-security.md"]
companions: ["_bmad-output/initiative-boxwise/architecture-boxwise/architecture-boxwise.md", "_bmad-output/initiative-boxwise/ux-design-specification/ux-design-specification.md"]
after: []
refined: true
status: done
risk: low
---
# 账户安全加固

## Description

账户安全加固：密码规则、2FA、WebAuthn、恢复码、速率限制、管理员 2FA 与 CLI。

## Outcome

账户安全加固：密码规则、2FA、WebAuthn、恢复码、速率限制、管理员 2FA 与 CLI。

## Requirements

- R1 (parent R22a): 密码规则升级：系统必须强制执行以下密码规则：
- R2 (parent R22b): 邮箱验证码：系统提供邮箱验证码作为备选 2FA 方式。
- R3 (parent R22c): WebAuthn/Passkey 支持：WebAuthn 作为推荐的 2FA 方式（需要 HTTPS + 域名）。
- R4 (parent R22d): 恢复码：用户设置 2FA 后生成一次性恢复码，用于 2FA 设备丢失时的账户恢复。
- R5 (parent R22e): 速率限制与暴力破解防护：Consequences (testable):
- R6 (parent R22f): 管理员 2FA 管理：Consequences (testable):
- R7 (parent R22g): 管理员 2FA CLI 恢复：管理员丢失 2FA 设备时，可通过服务器本地 CLI 强制重置。

## Constraints

- 遵循主 PRD 的非目标与部署约束；历史实现不重新验收。

## Done when

- 本 Epic 的 Story 均有 tracking 记录，验收条件保留在各自 plan。
- 不新增超出本 Epic 范围的功能。

## Boundaries

- Must not change: 不重写历史验收结论；不扩展到其他 Epic 的范围。

## References

- parent — _bmad-output/initiative-boxwise/initiative-boxwise.md, Requirements
- source — _bmad-output/initiative-boxwise/sprint-change-proposal-2026-05-28-security/sprint-change-proposal-2026-05-28-security.md
- architecture — _bmad-output/initiative-boxwise/architecture-boxwise/architecture-boxwise.md

## Notes

- Retrospective: epic-account-security-retrospective.md
