# Sprint Change Proposal — 账户安全加固

**项目:** 箱知 · BoxWise
**日期:** 2026-05-28
**变更范围:** Minor — 新增 Epic 8
**来源:** 安全审计（头脑风暴会话）
**提出者:** Elvis
**修订:** v5 — 第四次审查（9 条发现）全部纳入

---

## 1. Issue Summary

**问题陈述：** BoxWise 存储家庭敏感数据（物品位置树 = 家庭内部地图，照片可能含隐私信息）。当前密码策略为 4 位最小长度、无任何复杂度要求、无 2FA、无速率限制、无 CSRF 防护，在自动化 bots 扫描和凭证填充攻击面前防御严重不足。

**触发场景：** 安全审计发现 `Program.cs` 中 `RequiredLength = 4`，所有 `Require*` 选项均为 false，且代码库中无任何 2FA、限流或 CSRF 机制。

**当前状态证据：**

| 证据 | 位置 |
|------|------|
| `RequiredLength = 4` | `src/BoxWise.Server/Program.cs:32` |
| 所有复杂度选项为 false | `src/BoxWise.Server/Program.cs:28-31` |
| 无 2FA 机制 | 代码库全局搜索无结果 |
| 无速率限制 | 未使用 ASP.NET Core Rate Limiting 中间件 |
| 无 CSRF 防护 | Cookie SameSite=None 但无 Anti-Forgery 令牌 |

**问题类型：** 新需求浮现 — 安全审计发现

---

## 2. Impact Analysis

### Epic Impact

| Epic | 影响 | 说明 |
|------|------|------|
| Epic 1-7 (已完成) | 无影响 | 安全加固为纯增量，不修改已有 API 契约 |
| **新增 Epic 8** | 新增 | 账户安全加固 — 9 个 Story |

### Artifact Conflicts

| 工件 | 冲突 | 所需变更 |
|------|------|---------|
| PRD | 缺少 2FA、密码规则、速率限制、CSRF 需求；§11 假设需更新 | 新增 FR-22 (a-h) + 扩展 NFR-1/NFR-6 + 更新假设索引 |
| Architecture | 认证体系无 2FA 设计、无限流中间件、无 Data Protection 部署、无 JS Interop 设计 | 新增 5 个 Service + API 路由 + 数据模型扩展 + JS Interop + Data Protection 部署 |
| UX Design | 登录页无双阶段设计、无 2FA 设置向导、无 WebAuthn 可用性检测 | 新增 2FA 设置向导 + 登录流程变两阶段 + 方法可用性动态检测 |

### Technical Impact

| 层 | 变更 |
|------|------|
| Shared | 新增 2FA 相关 DTO |
| Server/Models | AppUser 新增 7 字段 + WebAuthnCredential + RecoveryCode 实体 |
| Server/Services | 新增 TwoFactorService、WebAuthnService、EmailTwoFactorService、RecoveryCodeService |
| Server/Endpoints | AuthEndpoints 新增 12+ 2FA 路由 + 限流中间件 + CSRF 防护 |
| Server/Admin | 新增用户 2FA 状态 API + 重置端点 + 审计日志 |
| Client/wwwroot | 新增 `js/webauthn.js`（WebAuthn JS Interop） |
| Client/Pages | 登录页增强、新增 2FA 设置向导、设置页 2FA 管理区 |
| Client/Components | 新增 TwoFactorSetup、TotpSetup、WebAuthnSetup、EmailTwoFactorSetup、RecoveryCodesDisplay |
| Client/Services | AuthService 扩展 2FA 方法 |
| Tests | Repository + Service + Endpoint + PageModel 测试（WebAuthn 含手动测试说明） |
| Deployment | docker-compose Data Protection 持久化 + fail2ban 模板 + SMTP 配置 |

---

## 3. Recommended Approach

**选择：Option 1 — 直接调整（新增 Epic 8）**

| 维度 | 评估 |
|------|------|
| 工作量 | 中等 — 9 个 Story |
| 风险 | 低 — ASP.NET Core Identity 原生支持扩展、fido2-net-lib 成熟稳定 |
| 时间线 | 新增 1 个 Epic |
| 架构一致性 | 完全遵循已有 Repository + Minimal API + MudBlazor 模式 |

**策略优先级（源自头脑风暴 Phase 1 第一性原理分析）：**
2FA > 速率限制 > 密码规则

**关键设计决策：**

| 决策 | 内容 |
|------|------|
| WebAuthn 优先级 | P1（依赖 8-2a-2 二阶段登录管道，串行执行） |
| 恢复码归属 | 8-2b（它是 TOTP/邮箱的兜底，与 WebAuthn 无关） |
| WebAuthn 库 | **锁定 `fido2-net-lib` NuGet**（.NET WebAuthn/FIDO2 标准实现，GitHub: passwordless-lib/fido2-net-lib） |
| CSRF 防护 | 2FA 设置/修改端点要求自定义 header `X-Requested-With: XMLHttpRequest` |
| 会话安全 | 2FA 验证后调用 `SignInManager.SignInAsync` 重新颁发 Cookie，添加 `amr` claim |
| Cookie claim 方案 | 二阶段登录：密码通过后使用 `IdentityConstants.TwoFactorUserIdScheme` 暂存（含随机 `SessionToken` claim），2FA 完成后颁发完整认证 Cookie 含 `"2fa": "verified"` claim |
| Session 标识方案 | stage-1 Cookie 注入 `Guid.NewGuid()` 作为 `SessionToken` claim，2FA 设置时校验该 token 匹配 |
| 2FA 方法切换 | 用户设置页选择新方法 → 验证新方法 → 新方法生效后旧方法自动清除。不提供独立的"禁用 2FA"端点（防止攻击者用已泄露密码绕过 2FA） |
| 现有用户迁移 | 首次登录检测密码合规性（不符则强制修改）+ 2FA 设置 24h 宽限期（非 1h） |
| WebAuthn 可用性 | VPS（有域名）：WebAuthn 可用；NAS（无域名/IP访问）：前端检测 origin，不支持时自动隐藏 WebAuthn 选项 |
| SMTP 未配置 | 邮箱验证码选项动态隐藏 |
| 本地调试 | WebAuthn 使用 `localhost` 豁免 |
| 密码修改 | 修改成功后调用 `UserManager.UpdateSecurityStampAsync` 作废所有旧 session |
| 恢复码后宽限期 | 使用恢复码登录后重置 `TwoFactorGracePeriodUntil` 为当前时间 + 24h |
| 审计日志 | 管理员重置 2FA 时写 `ILogger.LogWarning` |

### DI 生命周期

| Service | 生命周期 | 理由 |
|---------|---------|------|
| `TwoFactorService` | Scoped | 依赖 `UserManager<T>`（Scoped） |
| `RecoveryCodeService` | Scoped | 依赖 `DbContext`（Scoped） |
| `WebAuthnService` | Scoped | 依赖 `DbContext` + `Fido2` 库 |
| `EmailTwoFactorService` | Scoped | 依赖 `ISmtpClient`（MailKit 库） |

---

## 4. Detailed Change Proposals

### Epic 8: 账户安全加固 — Story 结构

| Story | 内容 | 优先级 | 依赖 |
|-------|------|--------|------|
| **8-1** | 密码规则升级 + 旧密码强制更新 | P2 | 无 |
| **8-2a-1** | 2FA 数据模型 + Service 层 + NuGet 包 | P0 | 无 |
| **8-2a-2** | 2FA API 路由 + 两阶段登录 + TOTP UI | P0 | 8-2a-1 |
| **8-2b** | 邮箱验证码 + 恢复码 | P0 | 8-2a-2, 8-5b |
| **8-3** | WebAuthn 集成 | P1 | 8-2a-2 |
| **8-4** | 速率限制 + CSRF + 会话安全 | P1 | 8-2a-2 |
| **8-5a** | 管理员 2FA 面板 + 审计日志 | P1 | 8-2b, 8-3 |
| **8-5b** | 部署配置（SMTP/RateLimit/Data Protection） | P1 | 无 |
| **8-5c** | CLI 2FA 重置工具 | P3 | 8-2a-2, 8-2b, 8-3, 8-5b |

**实施顺序：**
```
Phase 1（并行启动）:
  8-1  密码规则升级
  8-2a-1  数据模型 + Service 层
  8-5b 部署配置

Phase 2（8-2a-1 完成后并行启动）:
  8-2a-1 → 8-2a-2  API + UI + 登录流程
  8-2a-2 + 8-5b → 8-2b  邮箱 + 恢复码
  8-2a-2 → 8-3  WebAuthn
  8-2a-2 → 8-4  速率限制 + CSRF + 会话

Phase 3（收尾）:
  8-2b + 8-3 → 8-5a  管理员面板
  8-2a-2 + 8-2b + 8-3 + 8-5b → 8-5c  CLI 工具
```

---

### Story 8-1: 密码规则升级 + 旧密码强制更新

**PRD:**

```
新增 FR-22a: 密码规则升级

系统必须强制执行以下密码规则：
- 密码最小长度 8 位字符
- 密码不能为纯数字（至少包含一个非数字字符）
- 密码不能为常见密码黑名单中的值（top 100）
- 密码最大长度 128 位（防 DoS）
- 不强制大小写和特殊字符要求
- 不做密码历史限制
- 创建和修改密码时均需校验
- 首次登录时检测现有密码是否符合新规则，不符合则引导用户修改
- 密码修改成功后调用 UserManager.UpdateSecurityStampAsync 作废所有旧 session
```

**代码变更：**

- `Program.cs`: `RequiredLength = 8`，注册 `NoNumericOnlyValidator` + `CommonPasswordValidator`
- 新增 `Services/PasswordValidators/NoNumericOnlyValidator.cs`
- 新增 `Services/PasswordValidators/CommonPasswordValidator.cs`
- `AuthEndpoints.ChangePasswordAsync`: 密码修改成功后调用 `UpdateSecurityStampAsync`
- `AuthEndpoints.LoginAsync`: 登录成功后检测密码是否符合新规则，返回 `PasswordRequiresChange` 标记
- `GET /api/auth/me`: 响应中增加 `PasswordRequiresChange` 字段

---

### Story 8-2a-1: 2FA 数据模型 + Service 层 + NuGet 包

**说明：** 本 Story 仅涉及后端基础设施——数据模型、Service 和 NuGet 包。不包含 API 路由和 UI。8-2a-2 在此基础上添加 API 和前端。

**Architecture 变更：**

```
AppUser 新增字段:
  - TwoFactorMethod (enum: None/TOTP/Email/WebAuthn, default None)
    - Email 和 WebAuthn 枚举值为前向预留，对应逻辑分别在 8-2b 和 8-3 中实现
    - TwoFactorService 中对未实现方法添加分支保护（throw NotSupportedException）
  - TotpSecretKey (string? encrypted via Data Protection API)
  - EmailForTwoFactor (string?)
  - TwoFactorEnabled (bool, default false)
  - TwoFactorSetupCompletedAt (DateTime?)
  - TwoFactorGracePeriodUntil (DateTime?) — 24h 宽限期截止

登录流程（两阶段——本 Story 实现 Service 层逻辑）:
  阶段一: PasswordSignInAsync → 成功 → SignInAsync(TwoFactorUserIdScheme)
         → Cookie 含随机 SessionToken claim（Guid.NewGuid()）
         → 返回 { requiresTwoFactor: true, allowedMethods: [...] }
  阶段二: 验证 2FA 因子 → SignInAsync(ApplicationScheme)
         → Cookie 含 claim "2fa": "verified"

新增 Service:
  - TwoFactorService (Scoped):
    - GenerateTotpSecretAsync(userId) → (secretKey, qrCodeUri)
    - VerifyTotpSetupAsync(userId, code, sessionToken) → bool
    - VerifyTotpChallengeAsync(userId, code) → bool
    - GetTwoFactorStatusAsync(userId) → TwoFactorStatusDto
    - SwitchMethodAsync(userId, newMethod, sessionToken) — 新方法验证通过后替换旧方法

新增 NuGet 包:
  - Otp.NET（TOTP 标准实现 RFC 6238，MIT 许可）
  - QRCoder（生成二维码 PNG，MIT 许可）
```

**测试：**
- `TwoFactorService` 单元测试：TOTP 密钥生成、TOTP 码验证（有效/无效/过期）
- 使用 InMemory EF Core + TestIdentityFactory

---

### Story 8-2a-2: 2FA API 路由 + 两阶段登录 + TOTP UI

**依赖：** 8-2a-1

**PRD:**

```
新增 FR-22b (部分): 双因素认证基础设施 + TOTP

所有用户必须设置并使用双因素认证。系统提供 TOTP 作为基础 2FA 方式。

Consequences (testable):
- 登录流程变更为两阶段:
  阶段一: POST /api/auth/login → 验证密码，返回 TwoFactorUserIdScheme Cookie
  响应体含 PasswordRequiresChange 字段（继承 8-1）：
    - true → 客户端引导用户修改密码 → 修改完成后继续阶段二
    - false → 直接进入阶段二
  阶段二: POST /api/auth/2fa/verify → 验证 2FA 因子，颁发完整认证 Cookie
- 完整 Cookie 包含 amr claim 证明已通过 2FA
- 首次登录后必须在 24 小时内完成 2FA 设置（覆盖现有用户迁移场景）
- 2FA 设置前需通过 re-authenticate 端点验证密码，获取临时 SessionToken:
  - `POST /api/auth/2fa/re-authenticate` — 接收密码，返回一次性 SessionToken（响应体，非 Cookie）
  - 后续 setup-totp/setup-email 端点通过 `X-Session-Token` 请求头传递该 Token
  - SessionToken 使用 Data Protection API 生成自包含加密令牌（含 userId + 过期时间 + 用途标识），服务端零存储，5 分钟有效
- TOTP 验证: 同账户 30 秒内最多 3 次失败尝试（速率限制在 8-4）
- 2FA 方法切换：用户在设置页选择新方法 → 验证通过后新方法生效，旧方法自动清除
  switch-method 不允许选择 None（变相禁用），传入 None 返回 400
  （不提供独立"禁用 2FA"端点——防止攻击者用已泄露密码绕过 2FA）
```

**API 路由：**

```
POST /api/auth/2fa/re-authenticate  → 接收密码，返回一次性 SessionToken（响应体），5 分钟有效
POST /api/auth/2fa/setup-totp     → 生成 TOTP 密钥+二维码 URI（需 X-Session-Token 请求头）
POST /api/auth/2fa/verify-totp    → 首次验证 TOTP 码，启用 2FA（需 SessionToken）
POST /api/auth/2fa/challenge      → 登录阶段二：发起 2FA 挑战，返回可用方法
POST /api/auth/2fa/verify         → 登录阶段二：验证 2FA 响应，颁发完整 Cookie + amr claim
GET  /api/auth/2fa/status         → 返回用户 2FA 状态 + 可用方法列表（动态过滤不支持的方法）
PUT  /api/auth/2fa/switch-method  → 切换到其他 2FA 方法（新方法验证通过后旧方法自动清除）
```

**UI 变更：**

```
新增组件:
  - Components/TwoFactorSetup.razor — 2FA 方式选择向导（动态显示可用方法）
  - Components/TotpSetup.razor — QR 码展示 + TOTP 验证码确认

修改:
  - Pages/Login.razor — 增加阶段二 2FA 挑战步骤:
        状态机:
         [输入凭证] → [密码验证]
                         ↓  PasswordRequiresChange?
                     [是] → [强制修改密码] → [返回重登]
                     [否] → [检查 TwoFactorEnabled]
                                 ↓  未启用且在宽限期内?
                             [是] → [登录完成 + 提示设置 2FA]
                             [否] → [2FA 挑战] → [登录完成]
                                         ↓ (重试,不超过限流)
                                         [超限] → [返回重输密码]
    浏览器"返回"按钮: 清除阶段一状态，需重新输入密码
  - Pages/Settings.razor — 增加 2FA 管理区域（查看当前方法、切换方法）
  - AuthService — 扩展两阶段登录方法:
    LoginAsync → (success, requiresTwoFactor, challengeToken)
    VerifyTwoFactorAsync(challengeToken, method, code) → (success)
```

---

### Story 8-2b: 邮箱验证码 + 恢复码

**依赖：** 8-2a-2, 8-5b

**说明：** 8-2b 于 Phase 2 启动，依赖 8-2a-2（API+UI）和 8-5b（SMTP 配置节定义）。8-5b 在 Phase 1 并行启动，包含 SMTP 配置节结构和 Data Protection 持久化。

**PRD:**

```
新增 FR-22b (补充): 邮箱验证码

系统提供邮箱验证码作为备选 2FA 方式。
- 管理员配置 SMTP 后该选项自动可用，未配置时对用户隐藏
  - 6 位数字验证码，5 分钟有效
  - 验证码使用 Data Protection API 生成自包含加密令牌（含验证码 + 过期时间 + 目标邮箱），
    通过邮件发送，验证时解密比对，服务端零存储
- 邮箱验证码验证: 同账户 5 分钟内最多 3 次失败

新增 FR-22d: 恢复码

用户设置 2FA 后生成一次性恢复码，用于 2FA 设备丢失时的账户恢复。
- 生成 8 个 10 位恢复码（base32 编码，每码 50 bits 熵）
- 恢复码只显示一次，不存储明文
- 服务端存储 SHA-256 哈希（不加盐，原值高熵）
- 使用任一恢复码登录即触发 2FA 全量重置（清除方法/密钥/凭证/所有恢复码）
- 恢复码验证: 同账户 15 分钟内最多 5 次失败
- 使用恢复码登录后:
  - 清除所有 2FA 设置（方法/密钥/凭证/恢复码）
  - 重置 TwoFactorGracePeriodUntil 为当前时间 + 24h
  - 强制重新走 2FA 设置向导
- RecoveryCode 实体仅需 CodeHash 字段（无需 IsUsed 标记——首次使用任一码即全部删除）
- 支持重新生成恢复码（旧码全部失效）
```

**代码变更：**

- 新增 `EmailTwoFactorService` (Scoped): 使用 MailKit `ISmtpClient` 发送验证码（MIT 许可，.NET SMTP 标准库）
- 新增 NuGet 包: MailKit（SMTP 客户端，MIT 许可）
- 新增 `RecoveryCodeService` (Scoped): 恢复码生成/验证/失效
- 新增 `RecoveryCode` 实体: CodeHash（SHA-256，无 IsUsed 字段——首次使用任一码即删除全部）
- AppUser 新增 `RecoveryCodes` 导航属性（EmailForTwoFactor 已在 8-2a-1 AppUser 字段列表定义）
- 新增 API 路由:
  - `POST /api/auth/2fa/setup-email` — 发送验证码到邮箱（需 SessionToken）
  - `POST /api/auth/2fa/verify-email` — 验证邮箱验证码并启用 2FA
  - `POST /api/auth/2fa/recovery/verify` — 使用恢复码登录，清除 2FA + 重置宽限期
  - `POST /api/auth/2fa/recovery/regenerate` — 重新生成恢复码
- Client: 新增 `EmailTwoFactorSetup.razor`, `RecoveryCodesDisplay.razor`
- `TwoFactorService.SwitchMethodAsync`: 扩展分支处理 Email 方法
- `TwoFactorSetup.razor`: switch-method UI 中增加邮箱选项

---

### Story 8-3: WebAuthn 集成

**依赖：** 8-2a-2

**PRD:**

```
新增 FR-22c: WebAuthn/Passkey 支持

WebAuthn 作为推荐的 2FA 方式（需要 HTTPS + 域名）。
- VPS 部署（有域名）：WebAuthn 作为首选 2FA 方式
- NAS 部署（无域名/纯 IP）：前端检测 origin 不符合 WebAuthn 安全上下文要求时自动隐藏
- 本地开发：使用 localhost 豁免

Consequences (testable):
- 支持平台认证器（Touch ID/Windows Hello/Android Biometric）和漫游认证器（YubiKey）
- 用户可注册多个 WebAuthn 凭证（多设备），每用户最多 10 个凭证
  - 注册时校验：`POST /api/auth/webauthn/register-begin` 检查现有凭证数，超限返回 400
- WebAuthn 注册: 浏览器 navigator.credentials.create() → 公钥存储服务端
- WebAuthn 验证: 浏览器 navigator.credentials.get() → 签名验证
- 2FA 设置页面检测当前 origin 是否支持 WebAuthn:
  支持: origin 为 https://域名 或 https://localhost
  不支持: origin 为 http:// 或 纯 IP 或 .local 域名
  不支持时隐藏 WebAuthn 选项，不阻塞用户选择其他方式
- 不支持 WebAuthn 的设备自动降级到 TOTP/邮箱
```

**技术栈锁定：**

```
NuGet: fido2-net-lib（passwordless-lib/fido2-net-lib，.NET WebAuthn/FIDO2 标准实现）
  - 负责: CBOR 编解码、Base64Url 编解码、签名验证（ECDSA with SHA-256）
  - 不自行实现底层协议，避免安全漏洞
```

**JS Interop 架构：**

```
新增 wwwroot/js/webauthn.js:
  - createCredential(challengeJson) → PublicKeyCredential (JSON)
  - getCredential(challengeJson) → PublicKeyCredential (JSON)
  Blazor 组件通过 IJSRuntime.InvokeAsync<T>(name, args) 调用
```

**代码变更：**

- 新增 `WebAuthnCredential` 实体: CredentialId (base64url), PublicKey (PEM), SignCount, DeviceName, CreatedAt
- AppUser 新增 `WebAuthnCredentials` 导航属性
- 新增 `WebAuthnService` (Scoped): 使用 fido2-net-lib 进行挑战生成/凭证验证
- 新增 API 路由:
  - `POST /api/auth/webauthn/register-begin` → 生成注册挑战
  - `POST /api/auth/webauthn/register-complete` → 验证 attestation，存储凭证
  - `POST /api/auth/webauthn/verify-begin` → 生成验证挑战
  - `POST /api/auth/webauthn/verify-complete` → 验证 assertion，签名校验
  - `GET  /api/auth/webauthn/credentials` → 列出已注册凭证
  - `DELETE /api/auth/webauthn/credentials/{id}` → 删除凭证
- Client: 新增 `wwwroot/js/webauthn.js`, `WebAuthnSetup.razor`, `WebAuthnCredentialList.razor`
- TwoFactorSetup.razor: 调用 `checkWebAuthnAvailability()` 检测 origin，决定是否显示 WebAuthn
- `TwoFactorService.SwitchMethodAsync`: 扩展分支处理 WebAuthn 方法
- `TwoFactorSetup.razor`: switch-method UI 中增加 WebAuthn 选项

**测试策略：**
- 服务层单元测试: `WebAuthnService.RegisterAsync` / `VerifyAssertionAsync`（mock `IFido2` 接口）
- WebAuthn 完整注册+验证流程为**手动测试**（依赖浏览器 WebAuthn API，无法在单元测试中模拟）
  - 文档提供手动测试步骤清单
- 自动降级测试: 验证不支持 WebAuthn 的环境下 2FA 设置页不显示该选项

---

### Story 8-4: 速率限制 + CSRF + 会话安全

**依赖：** 8-2a-2

**PRD:**

```
新增 FR-22e: 速率限制与暴力破解防护

Consequences (testable):

登录端点:
  - 同 IP: 15 分钟内最多 5 次失败 (HTTP 429)
  - 同账户: 15 分钟内最多 5 次失败 (HTTP 429)

2FA 验证端点:
  - TOTP 验证: 同账户 30 秒内最多 3 次失败
  - 邮箱验证码: 同账户 5 分钟内最多 3 次失败
  - 恢复码验证: 同账户 15 分钟内最多 5 次失败

CSRF 防护:
  - 所有 2FA 设置/修改端点要求请求头 X-Requested-With: XMLHttpRequest
  - 受影响端点: setup-totp, verify-totp, setup-email, verify-email,
    webauthn/register-*, recovery/regenerate, switch-method
  - 拒绝无此 Header 的请求 (HTTP 400)

会话安全:
  - 2FA 验证通过后调用 SignInManager.SignInAsync 重新颁发 Cookie
    （含新 session ID + amr claim "2fa":"verified"）
  - 密码修改成功后调用 UserManager.UpdateSecurityStampAsync 使所有旧 session 失效
  - 2FA 设置窗口验证: 重输密码 + SessionToken 校验 + 邮件通知（SMTP 可用时）

VPS 部署额外防护:
  - 提供 fail2ban 配置模板: 监控日志中的 429/401，OS 层 IP 封禁
```

**代码变更：**

- `Program.cs`: 注册 `AddRateLimiter()` + 按 IP 和账户分区策略 + `UseRateLimiter()`（在认证中间件之前）
- 新增 `Services/CsrfValidationFilter.cs`: 验证 `X-Requested-With` header 的 IEndpointFilter
- 2FA 端点 route builder 上添加 CSRF filter
- `TwoFactorService`: SessionToken 窗口期防护逻辑
- `AuthEndpoints.LoginAsync`: 登录成功后检查 `TwoFactorEnabled`，未启用则标记需设置
- `AuthEndpoints.ChangePasswordAsync`: 添加 `UpdateSecurityStampAsync` 调用
- `docs/deployment/fail2ban-jail.conf`: VPS 部署配置模板

---

### Story 8-5a: 管理员 2FA 面板 + 审计日志

**依赖：** 8-2b, 8-3

**PRD:**

```
新增 FR-22f: 管理员 2FA 管理

Consequences (testable):
- Admin 用户列表新增"2FA 状态"列（已启用/方法/未设置）
- 管理员可为丢失 2FA 设备的用户重置 2FA（确认对话框 + 二次确认）
- 2FA 重置操作记录审计日志:
  ILogger.LogWarning("Admin {AdminUsername} reset 2FA for user {TargetUsername} at {Timestamp}")
```

**代码变更：**

- Admin API:
  - `GET /api/admin/users/{id}/two-factor-status` → 返回 2FA 详情
  - `POST /api/admin/users/{id}/reset-two-factor` → 清除 TOTP/WebAuthn/RecoveryCodes + 审计日志
- Admin UI:
  - `Pages/Admin/Index.cshtml` → 用户列表新增"2FA 状态"列
  - `Pages/Admin/ResetTwoFactor.cshtml` → 确认对话框 + 二次确认 + 结果展示
- Admin 权限: `[Authorize(Roles = "Admin")]`

---

### Story 8-5b: 部署配置

**PRD:** 无新增 FR（运维配置）。

**代码变更：**

```
docker-compose.yml:
  volumes:
    - ./data:/app/data
    - ./data/keys:/root/.aspnet/DataProtection-Keys  ← 新增

二进制部署（非 Docker）:
  Program.cs 注册 Data Protection key 持久化:
    builder.Services.AddDataProtection()
        .PersistKeysToFileSystem(new DirectoryInfo(
            Path.Combine(config["DataDirectory"] ?? "data", "keys")));
  确保 TOTP 密钥加密在服务器重启后仍可解密

config/appsettings.json 新增配置节:
  "Smtp": {
    "Host": "",
    "Port": 587,
    "Username": "",
    "Password": "",
    "From": "boxwise@example.com",
    "EnableSsl": true
  },
  "RateLimit": {
    "LoginPermitLimit": 5,
    "LoginWindowMinutes": 15,
    "TwoFactorTotpPermitLimit": 3,
    "TwoFactorTotpWindowSeconds": 30,
    "TwoFactorEmailPermitLimit": 3,
    "TwoFactorEmailWindowMinutes": 5,
    "TwoFactorRecoveryPermitLimit": 5,
    "TwoFactorRecoveryWindowMinutes": 15
  },
  "TwoFactor": {
    "SetupGracePeriodHours": 24,
    "RecoveryCodeCount": 8,
    "RecoveryCodeLength": 10
  }

Caddyfile: 无需变更（反向代理规则不变）

新增: docs/deployment/fail2ban-jail.conf
  - 监控路径: /var/log/boxwise/*.log
  - 匹配规则: "Failed login attempt" / "Rate limit exceeded"
  - 封禁策略: 10 分钟内 5 次失败 → 封 30 分钟
```

---

### Story 8-5c: CLI 2FA 重置工具

**依赖：** 8-2a-2, 8-2b, 8-3, 8-5b

**说明：** CLI 工具需要访问 AppUser（8-2a-1）、RecoveryCode（8-2b）、WebAuthnCredential（8-3）的数据模型来清除 2FA 设置，并依赖 8-5b 的 Data Protection key ring 持久化。

**PRD:**

```
新增 FR-22g: 管理员 2FA CLI 恢复

管理员丢失 2FA 设备时，可通过服务器本地 CLI 强制重置。

Consequences (testable):
- dotnet boxwise admin reset-2fa --user <username>
- CLI 工具通过 dotnet run -- admin reset-2fa 子命令嵌入 Server 项目，
  复用同一 DI 容器和 Data Protection key ring
- 仅限服务器本地执行（非网络 API）
- 重置操作写审计日志: ILogger.LogWarning("CLI 2FA reset for user {Username} at {Timestamp}")
```

**代码变更：**

- `Program.cs`: 添加命令行参数解析（`args` 匹配 `["admin", "reset-2fa", "--user", name]`）
- 若匹配：在 `app.Build()` 前创建 ServiceScope，执行重置逻辑，输出 JSON 结果，退出进程
- Data Protection key ring 通过宿主 DI 自动加载（与 Web 应用共享同一 key ring 路径）
- 审计日志输出到 console + ILogger

---

## 5. Implementation Handoff

**变更范围等级：Minor** — 可由 Developer agent 直接实现

**实施顺序：**

```
Phase 1（并行启动，无相互依赖）:
  8-1  密码规则升级
  8-2a-1  数据模型 + Service 层
  8-5b 部署配置（SMTP/RateLimit/Data Protection）

Phase 2（8-2a-1 完成后并行启动）:
  8-2a-1 → 8-2a-2  API + UI + 登录流程
  8-2a-2 + 8-5b → 8-2b  邮箱 + 恢复码
  8-2a-2 → 8-3  WebAuthn
  8-2a-2 → 8-4  速率限制 + CSRF + 会话

Phase 3（收尾）:
  8-2b + 8-3 → 8-5a  管理员面板
  8-2a-2 + 8-2b + 8-3 + 8-5b → 8-5c  CLI 工具
```

**成功标准：**
- 所有用户登录必须通过 2FA 验证（P0）
- WebAuthn 在有域名环境作为推荐方式；无域名环境自动隐藏（P1）
- TOTP/邮箱验证码作为备选方案可用（邮箱依赖 SMTP 配置）（P0）
- 密码规则：8 位 + 非纯数字 + 拒绝常见密码；旧用户首次登录引导更新（P2）
- 所有登录和 2FA 端点受多层级速率限制保护（P1）
- 2FA 端点有 CSRF 防护（P1）
- 会话在 2FA 验证后重新颁发，密码修改后旧 session 作废（P1）
- 管理员可通过后台和 CLI 管理 2FA，操作有审计日志（P1/P3）
- 现有用户有 24 小时 2FA 设置宽限期
- 恢复码登录后重置宽限期
- 不提供独立的"禁用 2FA"端点（防止绕过）
- 所有现有测试仍通过

---

## 6. 审查追溯

### V1 审查（21 条）

| 编号 | 发现 | 处理 |
|------|------|------|
| B1 | 8-2/8-3 串行依赖 | 8-3 降为 P1 |
| B2 | 恢复码归属不当 | 移至 8-2b |
| B3 | 缺少 CSRF 防护 | 8-4 新增 CSRF filter |
| B4 | TOTP/邮箱/恢复码限流缺失 | 8-4 新增独立端点限流 |
| B5 | 会话固定攻击 | 8-4 新颁发 Cookie + amr claim |
| I1 | 8-5 粒度过大 | 拆分为 8-5a/8-5b/8-5c |
| I2 | PRD 假设未更新 | 8-2a-2 24h 宽限期覆盖迁移 |
| I3 | NAS 无域名 WebAuthn | 8-3 动态检测可用性 |
| I4 | 现有用户迁移 | 24h 宽限期 + 密码合规检测 |
| I5 | SMTP 未配置 | 8-2b 动态隐藏邮箱选项 |
| I6 | DI 生命周期 | §3 明确声明（全部 Scoped） |
| I7 | Cookie claim 方案 | TwoFactorUserIdScheme + SessionToken + amr |
| I8 | JS Interop | 8-3 wwwroot/js/webauthn.js |
| S1 | 8-2 粒度过大 | 拆分为 8-2a/8-2b (v2) → 8-2a-1/8-2a-2/8-2b (v3) |
| S2 | 8-1 顺序冲突 | 8-1 与 8-2a-1 并行 |
| S3 | 密码修改未作废 session | 8-1 UpdateSecurityStampAsync |
| S4 | CLI key ring | 8-5c dotnet run -- 子命令复用 DI |
| S5 | 审计日志 | 8-5a + 8-5c 添加日志 |
| S6 | WebAuthn 测试 | 8-3 三层测试策略 |
| S7 | 旧密码合规 | 8-1 PasswordRequiresChange |
| S8 | 两阶段 UX | 8-2a-2 状态机 + 返回按钮 + 重试逻辑 |

### V2 审查（8 条）

| 编号 | 发现 | 处理 |
|------|------|------|
| N-I1 | `PUT /api/auth/2fa/disable` 与强制 2FA 矛盾 | 移除禁用端点，改为 `switch-method`（新方法验证后旧方法自动清除） |
| N-I2 | 8-2a 体量过大 + 关键路径风险 | 拆分为 8-2a-1（数据+Service+NuGet）和 8-2a-2（API+UI+登录） |
| N-I3 | 8-2b 缺少 8-5b SMTP 依赖 | 依赖表补充 `8-2b → 8-5b` |
| N-I4 | 8-5c 数据模型依赖不完整 | 依赖更新为 `8-2a-2, 8-2b, 8-3, 8-5b` |
| N-S1 | EmailForTwoFactor 字段归属 | 8-2a-1 AppUser 字段列表补充 `EmailForTwoFactor` |
| N-S2 | 枚举值前向引用 | 8-2a-1 添加分支保护说明 |
| N-S3 | WebAuthn 库未锁定 | 锁定 `fido2-net-lib` |
| N-S4 | Session 标识实现方案 | 8-2a-1 Cookie 注入 `SessionToken` claim (Guid) |
| N-S5 | 恢复码后宽限期 | 8-2b 恢复码登录后重置 `TwoFactorGracePeriodUntil` |

### V3 审查（7 条）

| 编号 | 发现 | 处理 |
|------|------|------|
| B1 | EmailTwoFactorService DI 声明与 SMTP 协议不匹配 | 改为 MailKit `ISmtpClient`，新增 MailKit NuGet 包 |
| B2 | §3 依赖图缺 Phase 2 标签 | 补上 Phase 2 标签，与 §5 一致 |
| B3 | 8-5a 缺 8-3 依赖 | 依赖表补充 `8-5a → 8-3` |
| C1 | 恢复码 IsUsed 标记与全量清除逻辑矛盾 | 移除 IsUsed，简化为"首次使用任一码即全部删除" |
| C2 | SessionToken 在已认证用户设置场景无法获取 | 新增 `re-authenticate` 端点，返回一次性 SessionToken（响应体） |
| C3 | switch-method 是否允许 None 未声明 | 显式声明 None → 400 |
| C4 | 二进制部署 Data Protection key 未持久化 | 8-5b 补充 `PersistKeysToFileSystem` 配置 |
| — | fido2-net-lib .NET 10 兼容性验证 | 建议 8-3 实施前先 `dotnet add package` 验证编译 |

### V4 审查（9 条）

| 编号 | 发现 | 处理 |
|------|------|------|
| D1 | 8-5a Story 头部依赖只写 8-2b，漏 8-3 | Story 头部补充 `8-3`，与依赖表一致 |
| D2 | PasswordRequiresChange 与两阶段登录无集成 | 8-2a-2 阶段一响应体增加该字段 + 状态机增加密码修改分支 |
| D3 | SessionToken 实现方案未定义 | 使用 Data Protection API 自包含加密令牌（零存储） |
| D4 | 邮箱验证码存储位置未指定 | 使用 Data Protection API 自包含加密令牌（零存储） |
| D5 | 依赖图/Phase 3 缺少 8-3→8-5a 边 | 依赖图和 Phase 3 均补充该依赖 |
| D6 | 8-2b/8-3 需扩展 switch-method 但未提及 | 两个 Story 代码变更中补充扩展说明 |
| D7 | WebAuthn 凭证数量无上限 | 每用户最多 10 个，注册时校验 |
| D8 | 登录 UI 状态机缺少宽限期分支 | 状态机扩展为五分支（密码合规+宽限期+2FA挑战+成功+超限） |
| D9 | 8-2a-2 PRD "重输密码"两行冗余 | 合并为单行描述 |
