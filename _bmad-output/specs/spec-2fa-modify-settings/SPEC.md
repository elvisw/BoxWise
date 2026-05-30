---
id: SPEC-2fa-modify-settings
companions:
  - modify-flow.md
sources:
  - _bmad-output/specs/spec-2fa-multi-method-login/SPEC.md
---

> **Canonical contract.** This SPEC and the files in `companions:` are the complete, preservation-validated contract for what to build, test, and validate. Source documents listed in frontmatter are for traceability only — consult them only if you need narrative rationale or prose color this contract intentionally omits.

# 2FA 设置管理

## Why

**Pain to solve.** 用户配置 2FA 后无法自行管理设置。三个具体缺口：(1) 无法修改邮箱地址，(2) 无法重置 TOTP 密钥（换手机/密钥泄露），(3) 无法提前重新生成恢复码。当前唯一路径是管理员在 `/admin` 后台重置全部 2FA→重新走完整设置流程，平均耗时 5+ 分钟且需管理员在线。

恢复码缺口尤为突出：`POST /api/auth/2fa/recovery/regenerate` API 端点已存在但前端无调用入口，用户即使能正常登录也无法更新恢复码。用户只能在恢复码用完后以核选项自毁重建 2FA 来获得新码。

受影响用户：所有已配置 2FA 的用户。

## Capabilities

- id: CAP-1
  intent: 用户可以使用已配置的任一 2FA 方法（TOTP 验证码、邮箱验证码、恢复码）完成身份验证，进入 2FA 设置修改模式。恢复码用于修改验证时仅校验不消耗——与登录时使用恢复码的"核选项"行为不同。
  success: 单元测试：用户有 TOTP+Email 双方法，用 TOTP 验证通过后获得 modify session token；用 Email 验证通过同样获得；用恢复码验证通过同样获得且恢复码数量不变。错误验证码被拒绝。

- id: CAP-2
  intent: 用户在修改模式下可以更改 Email 2FA 的邮箱地址：输入新邮箱→发送验证码到新邮箱→验证新邮箱→更新 `EmailForTwoFactor`。
  success: E2E 测试：用户原邮箱为 a@test.com，通过 2FA 验证后修改为 b@test.com，数据库 `EmailForTwoFactor` 更新为 b@test.com，`ConfiguredMethods` 保持 Email 标志位不变。后续登录时验证码发送到新邮箱。

- id: CAP-3
  intent: 用户在修改模式下可以重置 TOTP 密钥：重新生成密钥+二维码→用户扫描新二维码→验证新 TOTP 码→更新 `TotpSecretKey`→旧密钥立即失效。
  success: 单元测试：用户原 `TotpSecretKey` 为 K1，重置后更新为 K2（不等于 K1）。用旧密钥生成的 TOTP 码验证失败，用新密钥生成的 TOTP 码验证成功。

- id: CAP-4
  intent: 修改操作完成后不改变 2FA 启用状态和恢复码。修改邮箱或重置 TOTP 后 `TwoFactorEnabled` 保持 true，已有恢复码继续有效。
  success: 回归测试：修改邮箱后 `TwoFactorEnabled`=true，`HasRecoveryCodes`=true，已有恢复码仍可登录。

- id: CAP-5
  intent: 用户在修改模式下可以重新生成恢复码，新恢复码生成后旧码立即失效。
  success: E2E 测试：用户通过 modify/authenticate 验证后调用 regenerate，获得 8 个新恢复码。旧码验证返回 false，新码验证返回 true。无 modify session token 时调用 regenerate 返回 401。

## Constraints

- **身份验证门控**：所有管理操作（修改邮箱、重置 TOTP、重新生成恢复码）必须先通过 2FA 验证（TOTP/Email/RecoveryCode 三选一）。验证通过后颁发 modify session token（复用现有 Data Protection SessionToken 机制，purpose 改为 `"2fa-modify"`），有效期 15 分钟。
- **方法隔离原则延续**：修改 Email 仅操作 `EmailForTwoFactor`，不读写 `TotpSecretKey`。重置 TOTP 仅操作 `TotpSecretKey`，不读写 `EmailForTwoFactor`。任一修改流程不改变 `ConfiguredMethods` 标志位。
- **仅修改已配置的方法**：用户只能修改已配置的 2FA 方法。未配置 Email 时不能修改邮箱地址；未配置 TOTP 时不能重置 TOTP。如需添加新方法，走现有 setup 端点（`/api/auth/2fa/setup-email`、`/api/auth/2fa/setup-totp`）。
- **速率限制**：修改流程中的验证端点复用现有 `2fa-totp`、`2fa-recovery` 限流策略。新增 `2fa-modify` 限流策略保护身份验证端点。
- **向后兼容**：修改端点不影响现有 setup/verify/challenge 端点。`TwoFactorStatusDto` 不因本次修改而变更字段（`ConfiguredMethods` 和 `AvailableMethods` 语义不变）。
- **恢复码不受影响**：修改邮箱或重置 TOTP 不清除已有恢复码，不触发重新生成。用户如需新恢复码，通过现有 `regenerate` 端点主动操作。
- **修改验证用恢复码不消耗**：`modify/authenticate` 端点使用恢复码时仅校验哈希，不删除恢复码、不清除 2FA 设置。这与 `VerifyRecoveryCodeDuringLoginAsync`（登录时使用恢复码=核选项：清除所有恢复码+禁用 2FA+24h 宽限期）行为不同。需在 `RecoveryCodeService` 中新增 `ValidateRecoveryCodeAsync` 方法（仅校验不销毁），或给现有方法增加 `consume: bool` 参数。

## Non-goals

- 不新增"移除单个 2FA 方法"功能（仍为 Known Limitation，需管理员重置）
- 不新增 2FA 方法类型
- 不修改登录流程（challenge/verify 端点逻辑不变）
- 不修改恢复码生成/验证逻辑
- 不新增"修改 2FA 设置"的独立页面——修改/重新生成入口集成到现有 `TwoFactorSetup` 对话框中
- 不修改恢复码的生成算法和验证逻辑（仅增加 modify session token 门控）

## Success signal

- 已配置 Email 2FA 的用户可以自行更改邮箱地址，更改后登录验证码发送到新邮箱。
- 已配置 TOTP 的用户可以自行重置 TOTP 密钥，重置后旧密钥生成的验证码失效。
- 修改任一设置后 2FA 保持启用状态，已有恢复码仍然可用。
- 已登录用户可以一键重新生成恢复码，旧码立即失效。
- 所有现有 2FA 测试继续通过。

## Assumptions

- 假设 modify session token 可复用现有 `TwoFactorService.GenerateSessionToken` 机制，仅需新增 purpose 参数支持 `"2fa-modify"`。
- 假设修改邮箱的验证码发送可复用现有 `EmailTwoFactorService.GenerateCode` / `SendVerificationEmailAsync`。
- 假设重置 TOTP 的密钥生成可复用现有 `TwoFactorService.GenerateTotpSecretAsync`，但需新增 `PendingTotpSecretKey` 字段实现双密钥窗口（旧密钥在 verify 确认前保持有效）。
- 假设 `RecoveryCodeService` 需新增 `ValidateRecoveryCodeAsync` 方法（仅校验哈希，不删除恢复码），供 `modify/authenticate` 的 RecoveryCode 路由使用。
- 假设前端管理入口集成到 `TwoFactorSetup.razor` 的 `ChooseMethod` 步骤中：已配置的方法显示"修改"按钮；2FA 已启用时在对话框底部显示"重新生成恢复码"按钮。

## Open Questions

- 重置 TOTP 后是否需要强制重新生成恢复码？当前假设不需要——恢复码独立于具体 2FA 方法，重置一种方法不影响恢复码的有效性。
