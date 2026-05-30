---
id: SPEC-2fa-multi-method-login
companions:
  - data-model.md
  - login-flow.md
sources:
  - _bmad-output/implementation-artifacts/investigations/2fa-code-invalid-investigation.md
---

> **Canonical contract.** This SPEC and the files in `companions:` are the complete, preservation-validated contract for what to build, test, and validate. Source documents listed in frontmatter are for traceability only — consult them only if you need narrative rationale or prose color this contract intentionally omits.

# 2FA 多方法并存与登录选择

## Why

**Pain to solve.** 当前 `AppUser.TwoFactorMethod` 是单值枚举，用户只能激活一种 2FA 方法。`VerifyEmailAsync` 在切换到 Email 方法时清除 `TotpSecretKey`（`TwoFactorEndpoints.cs:461-463`），导致已配置的 TOTP 密钥永久丢失。登录路由（`ChallengeAsync`、`VerifyAsync`）按单一方法分发，前端 `Login.razor` 不支持方法选择和 Email 验证码传递。用户当前被完全锁在登录外：TOTP 密钥已删除，Email 验证前端不支持。数据库已确认：`TwoFactorMethod=2(Email)`, `TotpSecretKey=NULL`。

受影响用户：所有需要同时配置多种 2FA 方法的用户；所有从 TOTP 切换到 Email 的用户（密钥被清除）。

## Capabilities

- id: CAP-1
  intent: 用户可以同时配置 TOTP 生成器和邮箱两种 2FA 方法，互不覆盖。
  success: 数据库验证：用户先后设置 TOTP 和 Email 后，`TotpSecretKey` 非空且 `EmailForTwoFactor` 非空，`ConfiguredMethods` 包含 TOTP 和 Email。

- id: CAP-2
  intent: 登录时，2FA 挑战响应返回用户已配置的所有验证方法，用户可从中选择一种完成验证。
  success: E2E 测试：同时配置 TOTP 和 Email 的用户登录时看到两个方法选项，选择任一方法输入正确验证码后成功登录。

- id: CAP-3
  intent: 验证端点根据请求中指定的方法路由到对应的验证器（TOTP 走 `VerifyTotpChallengeAsync`，Email 走 `VerifyCode`），各方法验证逻辑独立运行。
  success: 单元测试：指定 method=TOTP 时仅校验 TOTP，不检查 emailToken；指定 method=Email 时要求 emailToken 并校验邮箱验证码。交叉验证（TOTP 码发到 Email 端点）返回 false。

- id: CAP-4
  intent: 前端登录页在 2FA 阶段展示方法选择器（TOTP/Email）和恢复码入口，TOTP 方法显示验证码输入框，Email 方法显示"验证码已发送至邮箱"提示、验证码输入框和重新发送链接，并正确传递 emailToken。
  success: 手动验证：登录页 2FA 阶段看到 TOTP/Email 两个选项和"使用恢复码"入口，切换 Email 后显示邮箱提示和重新发送链接，输入验证码验证成功；Token 过期后可点击重新发送获取新令牌。

- id: CAP-5
  intent: 现有单方法用户（仅 TOTP 或仅 Email）在升级后不受影响，登录流程与升级前一致。
  success: 回归测试：升级前仅配置 TOTP 的用户登录时仍然走 TOTP 验证且成功；仅配置 Email 的用户同理。

## Constraints

- **方法隔离原则**：每种 2FA 方法的配置数据独立存储，互不越界。TOTP 仅操作 `TotpSecretKey` + `ConfiguredMethods` 的 TOTP 位；Email 仅操作 `EmailForTwoFactor` + `ConfiguredMethods` 的 Email 位。Email 2FA 无服务端存储密钥（自包含 Data Protection 令牌，验证后即焚）。任一方法的设置/验证/禁用流程不得读写另一方法的存储字段。
- **向后兼容 DB 迁移**：`TwoFactorMethod` INT 列改为 `[Flags]` 枚举（None=0, TOTP=1, Email=2, WebAuthn=4），现有值（0/1/2）无需转换直接对应。新列名 `ConfiguredMethods`。需在迁移中确认生产数据库无 `TwoFactorMethod=3` 的记录（WebAuthn 从未实现，3 在 flags 下会被解释为 TOTP|Email）。
- **API 兼容**：`VerifyTwoFactorRequest` 新增可选 `Method` 字段（默认 null），null 时回退到旧行为——若仅配置单一方法则走该方法，若多方法则降级到 TOTP（保证旧客户端不返回 400 错误）。
- **恢复码不受影响**：恢复码验证路径独立于方法选择，`VerifyRecoveryCodeDuringLoginAsync` 不做修改。前端在 2FA 阶段增加"使用恢复码登录"入口。
- **Session Token 安全边界不变**：设置端点（setup/verify-totp, setup-email/verify-email）的 SessionToken 校验逻辑不修改。
- **速率限制维持**：`2fa-totp`、`2fa-recovery` 限流策略保持不变。
- **SMTP 依赖不变**：Email 2FA 是否可用仍由 `EmailTwoFactorService.IsSmtpConfigured()` 决定。
- **废弃 `SwitchMethodAsync`**：在 `[Flags]` 多方法模型下，"切换到某方法"的语义不明确（是添加方法？替换所有其他方法？）。该端点不再使用，由独立的 setup/verify 端点通过 `|=` 添加方法替代。旧端点的硬编码 `return false` for Email 一并移除。

## Non-goals

- 不新增 2FA 方法类型（WebAuthn 已预留，不在本次范围）
- 不修改 2FA 设置流程的 UX（Settings 页面不变，仅改后端逻辑）
- 不实现"默认方法"概念——每次登录用户主动选择
- 不修改 SMTP 发送的错误处理（fire-and-forget 优化留给后续专项）
- 不修改恢复码生成/验证逻辑
- 不新增"移除单个 2FA 方法"端点（已知限制：用户配置两种方法后无法单独移除一种，需管理员重置或后续专项解决）

## Success signal

- 同时配置 TOTP 和 Email 的用户可以在登录页选择任一方法完成验证，登录成功。
- 设置 Email 2FA 后，已有的 TOTP 密钥不被清除（数据库 `TotpSecretKey` 保持非空）。
- 所有现有测试通过，新增测试覆盖多方法路由和交叉验证拒绝场景。

## Assumptions

- 假设 `[Flags]` 枚举在 EF Core + SQLite 中以 INT 存储，现有值（0/1/2）直接映射到 flags 语义。生产数据库中不存在 `TwoFactorMethod=3(WebAuthn)` 的记录（WebAuthn 从未实现），值 3 在 flags 下会被解释为 TOTP|Email。
- 假设前端可以接收 `ChallengeResponse.AllowedMethods` 包含多项并渲染选择器，当前 MudBlazor 组件库支持所需 UI。
- 假设用户至少配置了一种 2FA 方法才会进入 2FA 登录流程（`TwoFactorEnabled=true` 的前提不变）。
- 假设并发 VerifyTotp + VerifyEmail 场景极低概率（需用户同时在两个 Tab 操作且交替提交），不做专门的乐观并发保护。如果发生，后果是 `ConfiguredMethods` 可能丢失一个方法位，但密钥数据（`TotpSecretKey`/`EmailForTwoFactor`）不受影响，用户可通过重新验证对应 setup 端点恢复。

## Open Questions

- 无。调查案卷已提供完整的代码级证据，所有设计决策有明确依据。审查中发现的边缘案例已通过 Assumptions 或 Non-goals 明确处理。

## Known Limitations

以下问题在审查中被发现，记录在此供后续工作参考：

- **无法移除单个 2FA 方法**：用户配置 TOTP+Email 后无法单独移除 Email。如需此功能，建议新增 `POST /api/auth/2fa/remove-method` 端点。
- **`ConfiguredMethods=None` + `TwoFactorEnabled=true` 空方法状态**：正常流程不会产生此状态，但 `ChallengeAsync` 已添加防御性检查：若检测到空方法则自动将 `TwoFactorEnabled` 置 false 并签发完整登录 Cookie。
- **并发设置两种方法存在竞态条件**：极低概率下 `ConfiguredMethods` 可能丢失一个方法位。本次不处理，后续可添加 RowVersion 并发保护。
- **WebAuthn 凭证与 TOTP/Email 的清除不一致**：`VerifyEmailAsync` 旧代码清除 TOTP 密钥但不清除 WebAuthn 凭证。本次修复后两种方法均不互相清除，WebAuthn 实现时需遵循相同的方法隔离原则。
- **Email 验证码在用户选择 TOTP 时仍会被发送**：`ChallengeAsync` 无论用户最终选择哪种方法，只要配置了 Email 就会发送验证码。这是有意权衡——用户可能切换选择，提前发送减少等待。可接受少量 SMTP 配额浪费。
- **`PUT /api/auth/2fa/switch-method` 端点已废弃**：返回 410 Gone。客户端（如有）需改用独立的 setup/verify 端点流程。
- **恢复码耗尽无提前警告**：用户初始获得 8 个恢复码，使用任一恢复码登录会立即清除所有恢复码并禁用 2FA（核选项）。不存在"剩余 3 个恢复码"的提示或低余量警告。用户恢复码用完后，如同时丢失所有 2FA 设备/邮箱访问，将无法登录——需要管理员后台重置。后续可在 `TwoFactorStatusDto` 中增加 `RemainingRecoveryCodes` 字段，在设置页展示余量。
