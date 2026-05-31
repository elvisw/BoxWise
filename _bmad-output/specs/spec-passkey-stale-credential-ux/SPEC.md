---
title: 'Passkey 过期凭据 UX 改进 + Signal API'
type: 'feature'
created: '2026-05-31'
status: 'done'
baseline_commit: 'f18a439'
context: ['_bmad-output/implementation-artifacts/investigations/passkey-stale-credentials-investigation.md']
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** 用户在 BoxWise 服务端删除 Passkey 后，平台认证器/浏览器密码管理器中的凭据未被清除。登录时选中已删除的凭据仅收到"通行密钥验证失败"通用错误，用户不知道发生了什么。

**Approach:** 四管齐下：① 服务端区分"凭据不存在"vs"验证失败"，返回不同错误响应；② 客户端据此显示针对性提示；③ 引入 WebAuthn Signal API 主动告知浏览器有效/失效凭据；④ 凭据管理页添加清理指引。

## Boundaries & Constraints

**Always:**
- Signal API 调用前必须做 feature detection（`typeof PublicKeyCredential.signalXxx === 'function'`）
- Signal API 失败时静默降级，不阻塞主流程
- 所有面向用户的文本使用中文
- 保持现有代码风格（MudBlazor 9.x 组件 API）

**Ask First:**
- 无

**Never:**
- 不修改数据库 schema
- 不改变 Passkey 注册/登录的核心流程逻辑
- 凭据列表加载失败时不调用 `signalAllAccepted`（空数组会误清除用户凭据）

## I/O & Edge-Case Matrix

| # | Scenario | Input / State | Expected Output / Behavior | Error Handling |
|---|----------|--------------|---------------------------|----------------|
| 1 | Passkey 登录成功 | 有效凭据，DB 中存在 | 正常登录，Status 200 | N/A |
| 2 | 凭据已被服务端删除 | 凭据签名有效但 `CompleteLoginAsync` 中 `credential is null` | 服务端返回 `Problem("此通行密钥未绑定到您的账户", 404)`，客户端显示"此通行密钥未绑定到您的账户（可能已被删除）。如有多个通行密钥，请尝试其他密钥。" | 与通用验证失败区分（404 vs 400） |
| 3 | 签名验证失败 | `Fido2VerificationException` | 服务端返回 `Problem("通行密钥验证失败", 400)` | 通用错误 |
| 4 | 并发冲突 | `DbUpdateConcurrencyException` | 服务端返回 `Problem("通行密钥验证失败", 400)` | 通用错误 |
| 5 | Signal API 不可用 | 旧浏览器 | 静默跳过，不影响功能 | 静默降级 |
| 6 | 凭据列表为空 | `_credentials.Count == 0` | 不调用 `signalAllAccepted` | N/A |
| 7 | 凭据列表加载失败 | 网络错误/401 | 不调用 `signalAllAccepted` | 静默跳过 |
| 8 | 删除凭据成功 | 服务端返回 200 | 调用 `signalUnknownCredential` 后更新 UI | 信号失败不阻塞删除 |
| 9 | 注册成功后 | 服务端返回恢复码 | 调用 `signalAllAccepted`（含新注册凭据 ID） | 信号失败不阻塞 |

</frozen-after-approval>

## Code Map

- `src/BoxWise.Server/Services/WebAuthnService.cs` -- 修改 `CompleteLoginAsync` 返回 `(AppUser?, bool credentialNotFound)` 以区分失败原因
- `src/BoxWise.Server/Endpoints/WebAuthnEndpoints.cs` -- `LoginCompleteAsync` 根据 `credentialNotFound` 返回不同 status code；`GetCredentialsAsync` 映射 CredentialId
- `src/BoxWise.Shared/Dtos/WebAuthnCredentialDto.cs` -- 新增 `string CredentialId` 字段（positional record）
- `src/BoxWise.Client/wwwroot/js/webauthn.js` -- 新增 `signalAllAccepted` 和 `signalUnknown` 两个 Signal API 封装函数
- `src/BoxWise.Client/Services/AuthService.cs` -- `CompleteWebAuthnLoginAsync` 区分 404（凭据已删除）vs 其他失败
- `src/BoxWise.Client/Pages/Login.razor` -- `HandlePasskeyLogin` 根据失败原因显示不同错误提示
- `src/BoxWise.Client/Components/WebAuthnSetup.razor` -- 注册成功后调 `signalAllAccepted`（userId 从注册完成响应或凭据列表获取）
- `src/BoxWise.Client/Components/WebAuthnCredentialList.razor` -- 加载列表后调 `signalAllAccepted`；删除后调 `signalUnknown`；底部加清理帮助文字

## Tasks & Acceptance

**Execution:**
- [x] `src/BoxWise.Server/Services/WebAuthnService.cs` -- 修改 `CompleteLoginAsync` 返回类型为 `(AppUser? User, bool CredentialNotFound)`，在 `credential is null` 路径设置 `CredentialNotFound = true` -- 使调用方可以区分"凭据不存在"和"验证失败"
- [x] `src/BoxWise.Server/Endpoints/WebAuthnEndpoints.cs` -- `LoginCompleteAsync` 接收新返回值，`credentialNotFound` 时返回 404 + 特定错误消息，其余失败返回 400；`GetCredentialsAsync` 映射新建的 DTO 字段 -- 为前端提供可区分的错误响应 + Signal API 参数
- [x] `src/BoxWise.Shared/Dtos/WebAuthnCredentialDto.cs` -- 新增 `string CredentialId` 字段（positional record 末尾追加） -- Signal API 需要凭据 ID 参数；注意此变更需同步更新 `WebAuthnEndpoints.cs:90` 的 DTO 构造调用
- [x] `src/BoxWise.Client/wwwroot/js/webauthn.js` -- 新增 `signalAllAccepted(rpId, userId, credentialIds)` 和 `signalUnknown(rpId, credentialId)` 函数 -- 封装 WebAuthn Signal API，含 feature detection + try/catch 静默降级
- [x] `src/BoxWise.Client/Services/AuthService.cs` -- `CompleteWebAuthnLoginAsync` 在收到 404 时返回新 `LoginResult` 值 `CredentialNotFound` -- 将服务端区分传递给 UI 层
- [x] `src/BoxWise.Client/Pages/Login.razor` -- `HandlePasskeyLogin` 处理 `CredentialNotFound` 结果，显示针对性错误提示 -- 改善凭据已删除场景的 UX
- [x] `src/BoxWise.Client/Components/WebAuthnSetup.razor` -- 注册成功后 `OnSetupComplete` 触发前调用 `webauthn.signalAllAccepted` -- 告知浏览器新凭据已生效
- [x] `src/BoxWise.Client/Components/WebAuthnCredentialList.razor` -- 加载凭据列表后（仅成功且有数据时）调 `signalAllAccepted`；确认删除后调 `signalUnknown`；底部加帮助区域 -- 完整 Signal API 集成 + 清理指引

**Acceptance Criteria:**
- Given 用户使用已删除的 Passkey 登录，when 服务端 `credential is null`，then 服务端返回 404 + "此通行密钥未绑定到您的账户"
- Given 客户端收到 404 响应，when 显示错误提示，then 显示"此通行密钥未绑定到您的账户（可能已被删除）。如有多个通行密钥，请尝试其他密钥。"
- Given 浏览器支持 Signal API，when 用户注册 Passkey 成功，then `webauthn.signalAllAccepted()` 被调用
- Given 浏览器支持 Signal API 且凭据列表加载成功且有数据，when 列表渲染完成，then `webauthn.signalAllAccepted()` 被调用
- Given 浏览器支持 Signal API，when 用户确认删除 Passkey 且服务端返回成功，then `webauthn.signalUnknown()` 被调用
- Given 浏览器不支持 Signal API，when 任何 Signal API 被调用，then 静默跳过且不影响主流程
- Given 凭据列表加载失败或为空，when Signal 调用被触发，then `signalAllAccepted` 不被调用
- Given 用户查看凭据管理页，when 列表底部存在帮助区域，then 显示凭据清理指引文字（通用文案，不区分浏览器类型——通过 JS interop 获取 `navigator.userAgent` 动态提示可后续迭代）

## Design Notes

**Signal API 封装（webauthn.js 新增函数）：**

```javascript
// 告知浏览器哪些凭据仍然有效
signalAllAccepted: async function (rpId, userId, credentialIds) {
    if (typeof PublicKeyCredential === 'undefined') return;
    if (!PublicKeyCredential.signalAllAcceptedCredentials) return;
    if (!credentialIds || credentialIds.length === 0) return;  // 空数组可能造成误清除
    try {
        var ids = credentialIds.map(function (c) { return this.base64urlToArrayBuffer(c); }, this);
        await PublicKeyCredential.signalAllAcceptedCredentials({
            rpId: rpId,
            userId: this.base64urlToArrayBuffer(userId),
            allAcceptedCredentialIds: ids
        });
    } catch { /* 静默降级 */ }
},

// 告知浏览器特定凭据已失效
signalUnknown: async function (rpId, credentialId) {
    if (typeof PublicKeyCredential === 'undefined') return;
    if (!PublicKeyCredential.signalUnknownCredential) return;
    try {
        await PublicKeyCredential.signalUnknownCredential({
            rpId: rpId,
            unknownCredentialId: this.base64urlToArrayBuffer(credentialId)
        });
    } catch { /* 静默降级 */ }
}
```

**参数来源（JS→C# 桥接）：**

| 参数 | 来源 | 传递方式 |
|------|------|---------|
| `rpId` | `/api/auth/webauthn/available` 返回的 `origin` 中提取 hostname | C# 端 `new Uri(origin).Host` 传给 JS |
| `userId` | 服务端需在注册完成和凭据列表响应中附带（base64url 编码的 user handle） | 通过 AuthService 返回的 DTO 传递 |
| `credentialIds` | `WebAuthnCredentialDto` 列表中的 `CredentialId`（注意：当前 DTO 不包含此字段，需扩展） | 通过 AuthService 返回 |

**WebAuthnCredentialDto 扩展：** 当前 DTO 为 `(int Id, string DeviceName, DateTime CreatedAt)`，需新增 `string CredentialId` 字段以支持 Signal API 调用。

## Verification

**Commands:**
- `dotnet build` -- expected: 全解决方案编译成功，0 errors 0 warnings

**Manual checks:**
- 启动 Client 开发服务器，查看凭据管理页确认帮助文字显示
- 在浏览器控制台确认 Signal API 调用（Chrome DevTools → Console）

## Suggested Review Order

**服务端错误区分（入口）**

- `CompleteLoginAsync` 返回类型改为元组，credential is null 时标记 CredentialNotFound
  [`WebAuthnService.cs:127`](../../../../src/BoxWise.Server/Services/WebAuthnService.cs#L127)

- `LoginCompleteAsync` 根据 credentialNotFound 返回 404 vs 400，首次实现凭据状态可区分
  [`WebAuthnEndpoints.cs:200`](../../../../src/BoxWise.Server/Endpoints/WebAuthnEndpoints.cs#L200)

- `IsAvailableAsync` 改为异步，新增 UserHandle 字段供前端 Signal API 使用
  [`WebAuthnEndpoints.cs:57`](../../../../src/BoxWise.Server/Endpoints/WebAuthnEndpoints.cs#L57)

- DTO 扩展：CredentialDto 加 CredentialId，AvailableResponse 加 UserHandle（可选参数向后兼容）
  [`WebAuthnCredentialDto.cs:3`](../../../../src/BoxWise.Shared/Dtos/WebAuthnCredentialDto.cs#L3)
  [`WebAuthnAvailableResponse.cs:3`](../../../../src/BoxWise.Shared/Dtos/WebAuthnAvailableResponse.cs#L3)

**JS Signal API 封装**

- `signalAllAccepted` + `signalUnknown` 函数，含 feature detection + try/catch 静默降级
  [`webauthn.js:34`](../../../../src/BoxWise.Client/wwwroot/js/webauthn.js#L34)

**客户端错误传递链**

- `CompleteWebAuthnLoginAsync` 区分 404 → CredentialNotFound，防御 null user 返回
  [`AuthService.cs:510`](../../../../src/BoxWise.Client/Services/AuthService.cs#L510)

- `LoginResult` enum 新增 CredentialNotFound
  [`AuthService.cs:594`](../../../../src/BoxWise.Client/Services/AuthService.cs#L594)

- `GetWebAuthnAvailableInfoAsync` 返回完整响应（含 Origin 和 UserHandle）
  [`AuthService.cs:403`](../../../../src/BoxWise.Client/Services/AuthService.cs#L403)

**UI 层改动**

- Login 页：处理 CredentialNotFound，显示针对性中文提示
  [`Login.razor:237`](../../../../src/BoxWise.Client/Pages/Login.razor#L237)

- 注册组件：注册成功后调用 signalAllAccepted，userId 从 GetWebAuthnAvailableInfoAsync 获取
  [`WebAuthnSetup.razor:140`](../../../../src/BoxWise.Client/Components/WebAuthnSetup.razor#L140)

- 凭据列表：加载后调 signalAllAccepted，删除后调 signalUnknown，底部显示清理指引
  [`WebAuthnCredentialList.razor:95`](../../../../src/BoxWise.Client/Components/WebAuthnCredentialList.razor#L95)
