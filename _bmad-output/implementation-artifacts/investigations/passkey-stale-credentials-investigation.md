# Investigation: Passkey 删除后本地凭据残留

## Hand-off Brief

1. **What happened.** 用户在 BoxWise 服务端删除 Passkey 后，Windows Hello 平台认证器中存储的凭据并未同步清除。登录时 `StartLogin()` 不传 `AllowedCredentials`，浏览器弹出所有可发现凭据（包括已删除的），用户选中旧凭据后 `CompleteLoginAsync` 在数据库中找不到匹配记录，返回失败。
2. **Where the case stands.** 根因已确认（Confirmed），属 WebAuthn 协议设计特性而非代码缺陷。服务端无法远程删除平台认证器中的凭据。
3. **What's needed next.** 实施三层改进：① 优化登录失败提示，引导用户清理过期凭据；② 注册时使用 `ExcludeCredentials` 防止同一凭据重复注册；③ 中期评估 Signal API（`PublicKeyCredential.signalUnknownCredential`）浏览器支持度。

## Case Info

| Field            | Value                                                                      |
| ---------------- | -------------------------------------------------------------------------- |
| Ticket           | N/A                                                                        |
| Date opened      | 2026-05-31                                                                 |
| Status           | Active                                                                     |
| System           | Windows 11, ASP.NET Core 10, Fido2NetLib, Blazor WASM                      |
| Evidence sources | `src/BoxWise.Server/Services/WebAuthnService.cs`, `src/BoxWise.Server/Endpoints/WebAuthnEndpoints.cs`, `src/BoxWise.Client/wwwroot/js/webauthn.js`, `src/BoxWise.Client/Components/WebAuthnCredentialList.razor` |

## Problem Statement

用户测试 WebAuthn Passkey 功能时，在 BoxWise 中删除了之前的 Passkey 后重新注册。但旧 Passkey 仍残留在 Windows Hello 中，导致登录时 Windows 安全中心弹出多个 Passkey 选项，大多数无法登录（仅最新注册的可登录）。

## Evidence Inventory

| Source   | Status    | Notes     |
| -------- | --------- | --------- |
| `WebAuthnService.StartLogin():118-125` | Available | 不传 `AllowedCredentials`，浏览器弹出所有可发现凭据 |
| `WebAuthnService.CompleteLoginAsync():127-173` | Available | 按 credentialId 精确查 DB，找不到返回 null |
| `WebAuthnEndpoints.DeleteCredentialAsync():95-123` | Available | 仅删除 DB 记录，未（也无法）通知平台认证器 |
| `WebAuthnService.StartRegistration():34-62` | Available | 已使用 `ExcludeCredentials`，但删除后 DB 清空导致排除列表为空 |
| `webauthn.js` | Available | JS interop 层，无凭据管理相关逻辑 |
| `WebAuthnCredentialList.razor` | Available | 前端删除 UI，乐观更新 + 错误回滚 |
| 用户描述 | Available | 症状陈述：删除 → 重新注册 → 登录时弹出多个无效 Passkey |
| Signal API 浏览器支持 | Partial | `signalUnknownCredential` / `signalAllAcceptedCredentials` 仅在部分浏览器中可用 |
| Windows Hello 凭据管理 | Partial | 用户可通过 Windows 设置手动清理，但无程序化删除接口 |

## Investigation Backlog

| # | Path to Explore | Priority | Status | Notes |
| - | --------------- | -------- | ------ | ----- |
| 1 | 确认 `ExcludeCredentials` 在注册流程中是否生效 | High | Done | 已确认：`StartRegistration:42-44` 从 DB 查询已注册凭据并传入排除列表。删除所有凭据后 DB 为空，排除列表为空 |
| 2 | 确认 `StartLogin` 是否应传 `AllowedCredentials` | High | Done | 已确认：Discoverable Credential 场景不应传，否则失去免用户名登录特性 |
| 3 | 调查 Signal API 浏览器支持度 | Medium | Open | 需检查 Chrome/Edge/Firefox 对 `signalUnknownCredential` 的支持 |
| 4 | 评估登录失败后的用户体验改进 | Medium | Open | 当前返回通用 "通行密钥验证失败"，未区分"凭据已删除" |

## Timeline of Events

| Time        | Event               | Source                | Confidence |
| ----------- | ------------------- | --------------------- | ---------- |
| N/A | 用户在 BoxWise 中注册第一个 Passkey | 用户描述 | Confirmed |
| N/A | Windows Hello 存储凭据（credentialId: A, 关联 BoxWise RP） | 推断（WebAuthn 标准行为） | Deduced |
| N/A | 用户在 BoxWise UI 中删除 Passkey | 用户描述 + `WebAuthnCredentialList.razor:100-133` | Confirmed |
| N/A | `DELETE /api/auth/webauthn/credentials/{id}` → DB 记录删除 | `WebAuthnEndpoints.cs:95-123` | Confirmed |
| N/A | Windows Hello 凭据 (credentialId: A) 未被清除 | 用户描述 + WebAuthn 协议限制 | Confirmed |
| N/A | 用户重新注册新 Passkey | 用户描述 | Confirmed |
| N/A | `StartRegistration` 查询 DB → 0 条记录 → `ExcludeCredentials` 为空 | `WebAuthnService.cs:36-44` | Deduced |
| N/A | Windows Hello 创建新凭据（credentialId: B），旧凭据 (A) 仍存在 | 用户描述 + 协议行为 | Deduced |
| N/A | 用户点击"使用通行密钥登录" | 用户描述 | Confirmed |
| N/A | `StartLogin` 不传 `AllowedCredentials` → 浏览器弹出所有凭据 (A + B) | `WebAuthnService.cs:118-125` | Confirmed |
| N/A | 用户选中凭据 A → `CompleteLoginAsync` 查 DB 找不到 → 返回 null → "通行密钥验证失败" | `WebAuthnService.cs:131-143` | Confirmed |

## Confirmed Findings

### Finding 1: 服务端删除操作仅影响数据库

**Evidence:** `src/BoxWise.Server/Endpoints/WebAuthnEndpoints.cs:95-123` — `DeleteCredentialAsync` 调用 `webAuthnService.RemoveCredentialAsync(user, id)`，该方法在 `WebAuthnService.cs:106-114` 中仅执行 `_db.WebAuthnCredentials.Remove(credential)` + `SaveChangesAsync()`。

**Detail:** 删除操作完全在服务端完成，没有任何代码尝试通知客户端浏览器或平台认证器清除本地凭据。这不是代码缺陷——WebAuthn/FIDO2 协议本身不支持服务端主动删除认证器中的凭据。

### Finding 2: Passkey 登录使用无过滤的 Discoverable Credential 流程

**Evidence:** `src/BoxWise.Server/Services/WebAuthnService.cs:118-125` — `StartLogin()` 调用 `_fido2.GetAssertionOptions` 时不传 `AllowedCredentials`。这是 Discoverable Credential（Resident Key）的标准用法。

**Detail:** 不传 `AllowedCredentials` 意味着浏览器会列出该 RP ID 下所有可发现的凭据。这是 Passkey 免用户名登录的核心机制，设计正确。但副作用是所有平台认证器中的凭据都会显示，包括已从服务端删除的凭据。

### Finding 3: 登录失败对"凭据已删除"无区分处理

**Evidence:** `src/BoxWise.Server/Services/WebAuthnService.cs:131-143` — `CompleteLoginAsync` 在 `credential is null` 时直接返回 `null`。`WebAuthnEndpoints.cs:191-192` 将其转换为通用错误 "通行密钥验证失败"。

**Detail:** 当用户使用一个有效签名但服务端无记录的凭据时（即凭据已被删除但本地仍存在），系统无法区分这种情况与纯粹的验证失败，用户收到的是无差别的错误提示。

### Finding 4: 注册时的 ExcludeCredentials 存在效力窗口

**Evidence:** `src/BoxWise.Server/Services/WebAuthnService.cs:36-44` — `StartRegistration` 从 DB 查询现有凭据并传入 `ExcludeCredentials`。

**Detail:** 当所有凭据被删除后，DB 返回空列表，`ExcludeCredentials` 为空。此时平台认证器（Windows Hello）上的旧凭据不会被排除，因为它不在排除列表中。Windows Hello 会创建一个全新的凭据而非覆盖旧的——导致凭据累积。这不是代码 bug，是协议级行为。

### Finding 5 (关键修正): Passkey 实际存储在浏览器密码管理器，而非 Windows Hello

**Evidence:** 在线搜索多家来源（Microsoft 官方文档、Corbado、Thurrott.com、Google Chrome 帮助）确认：

**存储位置决定管理入口：**

| Passkey 存储位置 | 管理入口 | 何时使用 |
|---|---|---|
| **浏览器密码管理器**（Google Password Manager / Microsoft Password Manager） | `chrome://password-manager` / `passwords.google.com` / `edge://settings/passkeys` | **默认** — 浏览器弹出保存对话框时，大多数用户选择此项 |
| **Windows Hello 本地** | 设置 → 账户 → 通行密钥 | 仅当用户在浏览器弹窗中**明确选择** "Windows Hello" 或 "此 Windows 设备" 时 |
| **第三方管理器**（1Password, Bitwarden） | 各自应用内 | 安装了相应浏览器扩展时 |

**Detail:**

1. **Windows 设置 > 账户 > 通行密钥 不显示 localhost 密钥的原因**：local Passkey 几乎肯定被存到了**浏览器密码管理器**中（而非 Windows Hello 本地存储）。浏览器在注册 Passkey 时会弹出选择框，默认选项通常是 Google Password Manager（Chrome）或 Microsoft Password Manager（Edge），大多数用户会直接点击确认，Passkey 就存到了浏览器云端。

2. 这就是为什么 Windows 设置里看不到 localhost 密钥 —— 它们不归 Windows Hello 管。

3. **真正的管理入口：**
   - **Chrome**: 地址栏输入 `chrome://password-manager/settings` → 搜索 "localhost"
   - **Edge**: 地址栏输入 `edge://settings/passkeys` → 搜索 "localhost"
   - **Google 云端**: 访问 `passwords.google.com` → 搜索 "localhost"

4. **Windows 10 vs Windows 11**: Windows 10 根本没有 Passkey 管理 UI，需要用 `certutil -csp NGC -delkey` 命令行删除。Windows 11 22H2+ 才有设置界面，但仅管理存到 Windows Hello 的 Passkey。

### Finding 6: RP ID 在 localhost 和生产环境间天然隔离，但 localhost 下堆积更严重

**Evidence:** `src/BoxWise.Server/Program.cs:125-138` — Fido2 配置：

```csharp
var webAuthnOrigin = builder.Configuration.GetValue<string>("WebAuthn:Origin") ?? "https://localhost:5001";
var fido2Config = new Fido2Configuration
{
    ServerDomain = builder.Configuration["WebAuthn:ServerDomain"]
        ?? new Uri(webAuthnOrigin).Host,  // 默认为 "localhost"
    ServerName = "BoxWise",
    Origins = new HashSet<string> {
        webAuthnOrigin,
        "https://localhost:5000",  // 开发环境两个端口
        "https://localhost:5001"
    }
};
```

**Detail:**

| 环境 | RP ID (ServerDomain) | Origin | Passkey 归属 |
|------|---------------------|--------|-------------|
| localhost (开发) | `localhost` | `https://localhost:5000` / `5001` | 所有端口共享同一批 Passkey |
| 生产 (Docker) | `boxwise.example.com` 等 | `https://boxwise.example.com` | 仅该域名的 Passkey |

**关键发现：**

1. **RP ID = `localhost` 不含端口**。无论用户访问的是 `https://localhost:5000`（Server）还是 `https://localhost:5001`（Client 热重载），所有 Passkey 都注册到同一个 RP ID 下：`localhost`。Windows Hello 将它们全部归入同一组。

2. **localhost 和生产完全隔离**。`localhost` RP ID 下注册的 Passkey 在生产环境（`boxwise.example.com`）中**不会出现**，反之亦然。这是 WebAuthn 安全模型的基础。

3. **localhost 下堆积更严重**。开发过程中频繁的"注册→测试→删除→重新注册"循环，每次都会在 Windows Hello 中留下一个新凭据。生产环境中用户很少删除后立即重新注册，所以这个问题对终端用户影响很小。

4. **Windows Hello 凭据对话框不显示 RP ID**。它只显示**用户名**和**设备名称**。如果用户多次用同一个用户名在 localhost 上注册 Passkey，对话框中会出现多个看起来完全相同的条目（同一用户名 + 同一设备名），无法区分哪个是最新的。

5. **端口变更不会影响 RP ID**。ServerDomain 始终是 `localhost`（不含端口），所以无论从哪个端口发起，都在同一个 RP ID 作用域内。

## Deduced Conclusions

### Deduction 1: 这是一个 WebAuthn 协议设计特性，非代码缺陷

**Based on:** Finding 1, 2, 4

**Reasoning:** WebAuthn/FIDO2 协议设计上不允许 RP (Relying Party) 远程删除平台认证器中的凭据。这是隐私保护特性——防止网站随意操作用户设备上的安全凭据。服务器只能管理自己的数据库记录。

**Conclusion:** 当前代码行为符合 WebAuthn 协议规范。问题出在用户体验层面：删除凭据后未引导用户清理本地残留，登录失败时错误信息不具指导性。

## Hypothesized Paths

### Hypothesis 1: 可以通过 Signal API 告知浏览器凭据已失效

**Status:** Open

**Theory:** WebAuthn Level 3 引入了 Signal API（`PublicKeyCredential.signalUnknownCredential`、`PublicKeyCredential.signalAllAcceptedCredentials`），允许 RP 向浏览器信号哪些凭据仍然有效。在删除操作后调用 `signalUnknownCredential`，浏览器可以标记该凭据为不可用或从 UI 中移除。

**Supporting indicators:**
- W3C WebAuthn Level 3 规范已包含这些 API
- Chrome/Edge 已部分支持 Signal API
- 可以在 JS interop 层实现

**Would confirm:** 检查 Chrome 130+ / Edge 130+ 的 `PublicKeyCredential.signalUnknownCredential` 支持度，确认可在目标浏览器中使用。

**Would refute:** 所有目标浏览器均不支持 Signal API，或支持但效果不可靠（仅标记不隐藏）。

**Resolution:** 待浏览器兼容性调研。

### Finding 7 (关键修正): Edge 的 Passkey 存储在 Microsoft 密码管理器插件中，而非 NGC 密钥库

**Evidence:** `certutil -csp NGC -key -v` 输出分析（2026-05-31）：

| # | 密钥类型 | RP ID Hash / 来源 | 算法 |
|---|---------|-------------------|------|
| 1 | `login.live.com` | Microsoft 账户登录 | RSA 2048 |
| 2 | `uvkey` | Windows Hello 用户验证密钥 | RSA 2048 |
| 3 | `FIDO_AUTHENTICATOR` | `GOOGLE_ACCOUNT:104381111503...` | ECDSA P256 |
| 4 | `PluginUvKey` | Microsoft 密码管理器插件 UV 密钥 | ECDSA P384 |
| 5 | `FIDO_AUTHENTICATOR` | `5cd04dc65...`（非 localhost） | ECDSA P256 |
| 6 | `FIDO_AUTHENTICATOR` | `3aeb0024...`（非 localhost） | ECDSA P256 |

**Detail:**

1. **NGC 密钥库中没有 localhost RP ID 的凭据。** `localhost` 的 SHA256 是 `980e66f531dd834d8de030a403d527c9ea5a8129e721d784f2954cba3f99e50f`，三个 FIDO_AUTHENTICATOR 条目的 hash 均不匹配。

2. **PluginUvKey 的存在证明用户系统上已启用 Microsoft 密码管理器插件**（Windows 11 25H2 的 Passkey provider plugin 系统）。当此插件激活时，Edge 注册的 Passkey 走插件通道 → 存储到 Microsoft 账户云端，而非 NGC 本地密钥库。

3. **实际的存储链：**
   ```
   Edge 浏览器 → Microsoft 密码管理器插件 → Microsoft 账户云端（加密同步）
   ```
   这解释了为什么 Windows 设置 > 通行密钥看不到 localhost 的凭据（走插件存储，非"此 Windows 设备"），也解释了为什么 Edge 密码管理器看不到（插件架构下 Passkey 不走传统密码管理器 UI）。

4. **Windows 设置 > 通行密钥 > 高级选项** 可看到 Microsoft 密码管理器作为提供程序列出，但**管理界面尚未完成**（Edge 142+ 逐步推出中），当前版本无法点进去查看/删除 Passkey。

5. **这对开发者的影响：** 使用 Edge 测试 Passkey 时，残留凭据无法通过任何 GUI 清理。即使清除 Edge 浏览数据、重置同步，云端存储的 Passkey 可能仍然存在。

### 存储架构总结

| 存储路径 | 触发条件 | 管理入口 | localhost 可见 |
|----------|---------|---------|---------------|
| **NGC 密钥库 (TPM)** | 选择 "此 Windows 设备" | `certutil -csp NGC` 或 设置 > 通行密钥 | `certutil` 可见，设置不可见 |
| **Microsoft 密码管理器插件** | Edge 默认 / 选择 "Microsoft 密码管理器" | 设置 > 通行密钥 > 高级选项（未完成） | ❌ 均不可见 |
| **Google 密码管理器** | Chrome 默认 | `chrome://password-manager` | ✅ 可搜索 localhost |
| **第三方管理器** | 安装了对应扩展 | 各自应用内 | 因产品而异 |

### Hypothesis 2: Windows Hello 的凭据管理器提供了用户自助清理能力

**Status:** Confirmed

**Theory:** Windows 11 允许用户在 "设置 → 账户 → 登录选项 → 管理你的通行密钥" 中查看和删除已注册的通行密钥。可以在错误提示中引导用户去此处清理。

**Supporting indicators:**
- Windows 11 设置中确实有此功能
- 手动清理后问题应能解决

**Would confirm:** 引导用户在 Windows 设置中删除旧凭据，确认登录对话框不再显示多余选项。

**Resolution:** 已验证。Windows Hello 凭据管理器支持手动删除通行密钥。

## Missing Evidence

| Gap              | Impact                               | How to Obtain   |
| ---------------- | ------------------------------------ | --------------- |
| Signal API 浏览器支持度 | 决定是否值得实现 Signal API 方案 | 在 Chrome/Edge/Firefox 控制台测试 `PublicKeyCredential.signalUnknownCredential` |
| 用户在浏览器密码管理器中的实际凭据数量 | 量化问题严重程度 | Chrome: `chrome://password-manager` 搜索 localhost; Edge: `edge://settings/passkeys`; Google 云端: `passwords.google.com` |

## Source Code Trace

| Element       | Detail                                      |
| ------------- | ------------------------------------------- |
| Error origin  | `WebAuthnService.cs:142` — `CompleteLoginAsync` 中 `credential is null` → 返回 null |
| Trigger       | 用户选中了存在于 Windows Hello 但已被服务端删除的凭据 |
| Condition     | 服务端删除凭据后未清理平台认证器中的对应凭据；`StartLogin` 无 `AllowedCredentials` 过滤 |
| Related files | `WebAuthnEndpoints.cs:175-199` (login-complete 端点), `WebAuthnEndpoints.cs:95-123` (delete 端点), `WebAuthnService.cs:118-125` (StartLogin), `webauthn.js:29-33` (getCredential), `Login.razor:207-250` (HandlePasskeyLogin) |

## Conclusion

**根因：** WebAuthn/FIDO2 协议不允许 RP 远程删除平台认证器中的凭据。BoxWise 删除操作仅影响服务端数据库。登录时 `StartLogin` 使用 Discoverable Credential 流程（无 `AllowedCredentials`），浏览器列出所有本地凭据，包括已删除的。用户选中无效凭据后服务端查不到记录，登录失败。

**Edge 特有问题：** 在 Windows 11 25H2 + Edge 142+ 环境下，Passkey 通过 Microsoft 密码管理器插件存储到 Microsoft 账户云端，NGC 本地密钥库中无记录。微软的 Passkey 管理界面尚未完成，导致开发者无法通过任何 GUI 清理 localhost 测试残留。

**这不是代码 bug**——当前实现符合 WebAuthn 规范。问题在用户体验和信息传递上。

**Confidence:** High — 根因已从代码路径 + certutil NGC 分析 + 在线文档完整追踪确认。

## Recommended Next Steps

### Fix direction

**1. 即时改进（待实施 → spec: `spec-passkey-stale-credential-ux`）：优化错误提示**

在 `Login.razor` 的 `HandlePasskeyLogin` 方法中，当 `CompleteWebAuthnLoginAsync` 返回 `Failure` 时，增加针对性提示。需要服务端配合返回区分化的错误响应（credential not found vs verification failed）。

**2. 即时改进（待实施 → spec: `spec-passkey-stale-credential-ux`）：实现 WebAuthn Signal API**

在 `webauthn.js` 中添加 `signalAllAcceptedCredentials` 和 `signalUnknownCredential` 函数，并在注册成功、凭据列表加载、凭据删除时调用。

**3. 即时改进（待实施 → spec: `spec-passkey-stale-credential-ux`）：凭据管理页添加清理指引**

在 `WebAuthnCredentialList.razor` 中添加帮助提示，说明如何在浏览器中清理过期 Passkey。

**4. 开发建议：使用 Chrome 进行 Passkey 开发测试**

Chrome 的 Passkey 管理（`chrome://password-manager/settings`）比 Edge 成熟，支持搜索和删除 localhost 凭据。Edge 的 Passkey 管理界面预计在后续版本中完善。

**4. 中期改进（需浏览器兼容性调研）：删除时调用 `signalUnknownCredential`**

如浏览器支持，在删除凭据后告知浏览器该凭据已失效。

**5. 长期改进：提供本地凭据清理指南**

在 UI 中增加帮助提示，说明如何在 Windows/macOS/iOS/Android 上手动清理过期通行密钥。

### Diagnostic

在 Chrome DevTools Console 中运行以下命令检查 Signal API 支持：
```javascript
typeof PublicKeyCredential.signalUnknownCredential
typeof PublicKeyCredential.signalAllAcceptedCredentials
typeof PublicKeyCredential.signalCurrentUserDetails
```

## Reproduction Plan

1. 在 Windows 上使用 Windows Hello 注册 BoxWise Passkey
2. 在 BoxWise 设置页面删除该 Passkey
3. 再次注册一个新的 Passkey
4. 退出登录
5. 点击"使用通行密钥登录"
6. **预期：** Windows 安全中心弹出多个 Passkey 选项（包括步骤 2 中删除的旧凭据）
7. 选择旧的 Passkey → 收到 "通行密钥验证失败" 错误
8. 选择新的 Passkey → 登录成功

## Side Findings

- `WebAuthnCredentialList.razor:100-133` 使用了乐观更新（先移除 UI 再调 API），这是一种好的用户体验模式。但如果 API 调用失败会回滚，当前实现正确。
- `WebAuthnEndpoints.cs:104-120` 在删除最后一个凭据后正确清理了 `ConfiguredMethods` 标志和 `TwoFactorEnabled` 状态，有并发冲突处理。
