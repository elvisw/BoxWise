---
title: '补完 WebAuthn 通行密钥功能'
type: 'feature'
created: '2026-05-31'
status: 'done'
baseline_commit: '6d46cda'
context: []
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** Epic 8-3 WebAuthn 后端已实现 API 端点、Fido2NetLib 服务和 JS 互操作层，但有两个严重 Bug 和前端缺失：
- **后端 Bug 1：** `RegisterCompleteAsync` 保存凭证后未设置 `user.TwoFactorEnabled = true` 和 `user.ConfiguredMethods |= WebAuthn`——用户注册通行密钥后 2FA 实际上没开启
- **后端 Bug 2：** 注册成功不返回恢复码（TOTP/Email 都返回，仅 WebAuthn 缺失），首个 2FA 方法为 WebAuthn 时丢失设备即永久锁定
- **前端缺失：** 无凭证注册 UI、无凭据列表/删除功能、AuthService 缺少客户端方法
- **登录缺失：** 通行密钥应支持无密码首次登录（Passkey/Discoverable Credential），而非仅作为 2FA 第二因素

**Approach:** 先修复后端两个 Bug，再参考 TotpSetup 模式新建 WebAuthnSetup 注册组件、补完 WebAuthnCredentialList 凭据管理组件、在 TwoFactorSetup 中集成 WebAuthn 选项、补充 AuthService 方法。最后实现通行密钥无密码登录：登录页新增"使用通行密钥登录"按钮，调用无需预知用户身份的 assertion 端点，浏览器弹出通行密钥选择器直接完成登录。

## Boundaries & Constraints

**Always:**
- 使用 MudBlazor 9.x API（`SelectedValue` 非 `ActivatedValue`，`SelectionMode` 枚举非 bool）
- 参考 TotpSetup.razor 的组件结构和错误处理模式
- 通过 IJSRuntime 调用已有 `window.webauthn.createCredential(json)` JS 方法
- 所有 API 调用走 AuthService 的 HttpClient（已配置 Cookie 认证）
- 设备名输入 Trim + 非空校验 + 最大 100 字符（后端默认"未知设备"兜底）
- WebAuthnSetup **不需要 SessionToken** 参数（WebAuthn 用 Cookie + Session 认证，非 X-Session-Token）
- 凭据列表使用 MudList（非 MudTable——项目没有 MudTable 先例，MudList 更紧凑）
- 注册按钮全程防抖锁（`_isRegistering` 门控），防止重复点击覆盖 Session

**Ask First:**
- （无需——所有设计决策在审查中已确认）

**Never:**
- 修改数据库 schema 或添加新迁移
- 引入新的 NuGet 包
- 修改 webauthn.js 已有函数

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| WebAuthn 可用 | origin=HTTPS localhost + `isAvailable()`=true | 显示"通行密钥"选项卡片，状态"可设置" | N/A |
| WebAuthn 不可用 | HTTP 或非本地 origin 或浏览器不支持 | 卡片灰显 + "当前环境不支持通行密钥" | 灰显 |
| 注册成功 | 输入设备名 → 点击注册 → 浏览器验证通过 | 2FA 启用 + 凭据保存 + 显示恢复码 | N/A |
| 用户取消浏览器弹窗 | 点击注册 → 弹窗 → 用户取消 | 显示"您已取消操作"，按钮恢复 | `NotAllowedError` catch |
| 浏览器弹窗超时 | 60s 内未完成验证 | 显示"验证超时，请重试" | `NotAllowedError` + 时间推断 |
| Session 过期 | Begin→超时(5min)→Complete | 显示"会话已过期，请重新开始" + 重试按钮 | 后端 400 → 保留设备名重新 Begin |
| 凭证数量达上限(10) | 注册请求 → 后端 400 | 显示"已达凭证数量上限（10个）"，按钮禁用 | 列表 >0 时前端提前禁用按钮 |
| 重复注册同一设备 | 同一 credentialId 再次注册 → 409 | 显示"该设备已注册，无需重复操作"，刷新列表 | 409 Conflict → Snackbar |
| 凭据列表加载中 | 页面首次加载 | 显示 MudProgressLinear 加载条 | try/catch 401 |
| 凭据列表为空 | GET /credentials 返回 [] | 空状态："暂无通行密钥，注册后可在此管理" | N/A |
| 凭据列表有数据 | 1~10 个凭据 | MudList 展示设备名 + 创建日期 + 删除按钮 | N/A |
| 删除凭据确认 | 点击删除 → MudDialog 确认 | "确定要移除此通行密钥吗？" + 取消/删除 | 确认按钮加载中禁用 |
| 删除成功 | API 200 | 乐观移除 → Snackbar 成功 | N/A |
| 删除失败（网络） | API 超时/离线 | 恢复列表原状 → Snackbar 错误 + 重试 | try/catch → 恢复 |
| 删除失败（404） | 凭据已被删 | 刷新列表 → Snackbar"凭证不存在" | 404 → 重新 GET 列表 |
| 注册成功后列表刷新 | OnSetupComplete → Complete 步骤 | TwoFactorSetup 的凭据列表自动重新加载 | 回调触发 |
| 401 未登录 | Cookie 过期 | Snackbar"登录已过期，请重新登录" | try/catch 所有 API 调用 |
| Passkey 登录成功 | 登录页 → 点击"使用通行密钥登录" → 浏览器弹窗 → 选择凭证 → 指纹验证 | 直接登录进首页 | N/A |
| Passkey 登录失败（无凭证） | 浏览器弹出通行密钥选择器 → 用户无可选凭证 → 取消 | 显示"未找到通行密钥，请使用密码登录" | catch NotAllowedError |
| Passkey 登录失败（凭证未注册） | 浏览器返回 assertion → 服务器查不到 credentialId | 显示"通行密钥未注册" | 后端 400 |

</frozen-after-approval>

## Code Map

- `src/BoxWise.Server/Endpoints/WebAuthnEndpoints.cs` — 修复：RegisterCompleteAsync 启用 2FA + 返回恢复码
- `src/BoxWise.Client/Services/AuthService.cs` — 添加 4 个凭证管理方法（签名见 Design Notes）
- `src/BoxWise.Client/Components/WebAuthnSetup.razor` — 新建：通行密钥注册 UI（无 SessionToken）
- `src/BoxWise.Client/Components/WebAuthnCredentialList.razor` — 补完：MudList 凭据列表/删除 UI
- `src/BoxWise.Client/Components/TwoFactorSetup.razor` — 添加 WebAuthn 选项卡片 + SetupWebAuthn 步骤
- `src/BoxWise.Shared/Dtos/WebAuthnCredentialDto.cs` — 已有 DTO（不变）
- `src/BoxWise.Shared/Dtos/RecoveryCodesResponse.cs` — 已有：恢复码响应 DTO
- `src/BoxWise.Client/wwwroot/js/webauthn.js` — 已有 JS 互操作（不变）
- `src/BoxWise.Server/Services/WebAuthnService.cs` — 新增：Passkey 登录方法（无需知悉用户身份的 assertion 开始 + 完成）
- `src/BoxWise.Server/Endpoints/WebAuthnEndpoints.cs` — 新增：`/login-begin` + `/login-complete` 端点（匿名访问）
- `src/BoxWise.Client/Services/AuthService.cs` — 新增：Passkey 登录客户端方法
- `src/BoxWise.Client/Pages/Login.razor` — 添加"使用通行密钥登录"按钮 + 验证流程
- `docs/webauthn-setup-guide.md` — 新建：测试 + 生产环境部署文档

## Tasks & Acceptance

**Execution（已完成的注册/管理功能）：**
- [x] `src/BoxWise.Server/Endpoints/WebAuthnEndpoints.cs` — 修复 RegisterCompleteAsync：① 启用 2FA + ConfiguredMethods ② 生成恢复码
- [x] `src/BoxWise.Client/Services/AuthService.cs` — 添加 4 个凭证管理方法
- [x] `src/BoxWise.Client/Components/WebAuthnSetup.razor` — 新建注册组件
- [x] `src/BoxWise.Client/Components/WebAuthnCredentialList.razor` — 补完凭据列表
- [x] `src/BoxWise.Client/Components/TwoFactorSetup.razor` — 集成 WebAuthn
- [x] `src/BoxWise.Client/Components/TwoFactorManage.razor` — 添加通行密钥管理入口
- [x] `docs/webauthn-setup-guide.md` — 编写部署文档

**Execution（新增：Passkey 首次登录）：**

**Execution:**
- [x] `src/BoxWise.Server/Endpoints/WebAuthnEndpoints.cs` — 修复 RegisterCompleteAsync：① 设置 user.TwoFactorEnabled=true + ConfiguredMethods\|=WebAuthn + SetupCompletedAt ② 调用 RecoveryCodeService 生成恢复码并返回 RecoveryCodesResponse — 与 TOTP/Email 流程一致
- [x] `src/BoxWise.Client/Services/AuthService.cs` — 添加 4 个方法（带完整错误处理）：GetWebAuthnCredentialsAsync(Task<List<WebAuthnCredentialDto>>)、DeleteWebAuthnCredentialAsync(Task<bool>)、StartWebAuthnRegistrationAsync(Task<string?>)、CompleteWebAuthnRegistrationAsync(Task<bool>)，注册方法用 HttpRequestMessage 以正确发送 X-Device-Name header
- [x] `src/BoxWise.Client/Components/WebAuthnSetup.razor` — 新建：设备名输入（Trim+非空校验+max100）+ `_isRegistering` 防抖锁 + JS webauthn.createCredential + begin/complete 流程 + Session 过期重试 — **无 SessionToken 参数**
- [x] `src/BoxWise.Client/Components/WebAuthnCredentialList.razor` — 实现：MudList 展示（设备图标+设备名+日期+删除按钮）+ 加载中（MudProgressLinear）+ 空状态 + 删除 MudDialog 确认（乐观移除 + 失败恢复）+ try/catch 401
- [x] `src/BoxWise.Client/Components/TwoFactorSetup.razor` — ① 添加 _webauthnAvailable 检查（JS+API）② TOTP/Email 间插入 WebAuthn 通行密钥选项卡片（Fingerprint 图标）③ 可用时显示"设置"按钮，不可用时灰显 ④ SetupWebAuthn 步骤渲染 WebAuthnSetup 组件 ⑤ 注册成功后触发生成恢复码显示
- [x] `docs/webauthn-setup-guide.md` — 编写文档：WebAuthn 原理简介 + 测试环境配置（localhost HTTPS + 双端口 origin 一致性注意事项）+ 生产环境配置（HTTPS 域名 + 反向代理 + WebAuthn:Origin/ServerDomain 配置）+ 常见问题排查

**Execution（新增：Passkey 首次登录）：**
- [ ] `src/BoxWise.Server/Services/WebAuthnService.cs` — 新增 `StartLoginAsync()`（返回空 AllowCredentials 的 AssertionOptions，允许用户选择任意已注册凭证）和 `CompleteLoginAsync(UserManager, SignInManager, assertion, options)`（根据 credentialId 查找用户 → SignInManager 签发 Cookie） — 无密码登录
- [ ] `src/BoxWise.Server/Endpoints/WebAuthnEndpoints.cs` — 新增 `/login-begin`（`.AllowAnonymous()`）和 `/login-complete` 端点 — 匿名可访问
- [ ] `src/BoxWise.Client/Services/AuthService.cs` — 新增 `StartWebAuthnLoginAsync()` 和 `CompleteWebAuthnLoginAsync(assertionJson)` 方法
- [ ] `src/BoxWise.Client/Pages/Login.razor` — ① 添加"使用通行密钥登录"按钮（Fingerprint 图标，在密码表单下方）② `HandlePasskeyLogin` 方法：begin → webauthn.getCredential → complete → 跳转首页
- [ ] `src/BoxWise.Server/Endpoints/TwoFactorEndpoints.cs` — ChallengeAsync 添加 `WebAuthn` 方法检查（if HasFlag WebAuthn → add "WebAuthn"）

**Acceptance Criteria（已有）：**
- Given 用户在 2FA 设置页，when WebAuthn 可用，then 显示"通行密钥"选项卡片（TOTP 和 Email 之间）且可点击设置
- Given 用户点击设置通行密钥，when 输入设备名并完成浏览器验证，then 2FA 启用成功 + 显示恢复码 + 凭据出现在列表中
- Given 用户已完成 WebAuthn 注册，when 查看凭据列表，then 显示设备名和创建日期，可删除
- Given 用户点击删除凭据，when 确认后 API 失败，then 凭据恢复到列表中并显示错误提示
- Given WebAuthn 为用户的第一个 2FA 方法，when 注册完成，then 显示恢复码（与 TOTP/Email 行为一致）
- Given `dotnet build BoxWise.slnx`，then 0 警告 0 错误
- Given `dotnet test BoxWise.slnx`，then 全部现有测试通过不退化
- Given 用户阅读 docs/webauthn-setup-guide.md，when 按文档操作，then 可在测试/生产环境成功使用 WebAuthn

**Acceptance Criteria（新增 Passkey 登录）：**
- Given 用户在登录页，when 点击"使用通行密钥登录"并完成浏览器验证，then 无需输入用户名密码直接登录成功
- Given 用户未注册任何通行密钥，when 点击"使用通行密钥登录"，then 显示"未找到通行密钥，请使用密码登录"
- Given `dotnet build BoxWise.slnx`，then 0 警告 0 错误
- Given `dotnet test BoxWise.slnx`，then 全部现有测试不退化

## Design Notes

**AuthService 新增方法签名（精确指定）：**

```csharp
// POST /api/auth/webauthn/register-begin（POST，非 GET——端点修改 Session 状态）
public async Task<string?> StartWebAuthnRegistrationAsync()
{
    var response = await _http.PostAsync("api/auth/webauthn/register-begin", null);
    if (!response.IsSuccessStatusCode) return null;
    return await response.Content.ReadAsStringAsync(); // CredentialCreateOptions JSON
}

// POST /api/auth/webauthn/register-complete（用 HttpRequestMessage 发 X-Device-Name 头）
// 注意：需要 using System.Text;（Encoding.UTF8）和 using System.Net.Http;（HttpRequestMessage）
// 返回 List<string>? 而非 bool——需要读取响应体中的 RecoveryCodesResponse
public async Task<List<string>?> CompleteWebAuthnRegistrationAsync(string attestationJson, string deviceName)
{
    var request = new HttpRequestMessage(HttpMethod.Post, "api/auth/webauthn/register-complete");
    request.Content = new StringContent(attestationJson, Encoding.UTF8, "application/json");
    request.Headers.Add("X-Device-Name", deviceName.Trim());
    var response = await _http.SendAsync(request);
    if (!response.IsSuccessStatusCode) return null;

    var result = await response.Content.ReadFromJsonAsync<RecoveryCodesResponse>();
    _lastRecoveryCodes = result?.Codes;
    return result?.Codes;
}

// GET /api/auth/webauthn/credentials
public async Task<List<WebAuthnCredentialDto>> GetWebAuthnCredentialsAsync()
{
    var response = await _http.GetAsync("api/auth/webauthn/credentials");
    response.EnsureSuccessStatusCode();
    return await response.Content.ReadFromJsonAsync<List<WebAuthnCredentialDto>>() ?? new();
}

// DELETE /api/auth/webauthn/credentials/{id}
public async Task<bool> DeleteWebAuthnCredentialAsync(int id)
{
    var response = await _http.DeleteAsync($"api/auth/webauthn/credentials/{id}");
    if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return false;
    response.EnsureSuccessStatusCode();
    return true;
}
```

**WebAuthnSetup 为何不需要 SessionToken：**
- TOTP/Email：用户输入密码 → `ReAuthenticateAsync` 返回 Server 签发的 SessionToken → 后续 API 用 X-Session-Token 头验证
- WebAuthn：用户点击注册 → 浏览器弹出原生指纹/面容/PIN 对话框 → 浏览器本身就是"存在证明"
- WebAuthn 端点用 `UserManager.GetUserAsync(httpContext.User)` 从 Cookie 获取用户，用 `httpContext.Session` 存 CredentialCreateOptions
- **结论：** WebAuthnSetup 只需 OnSetupComplete 和 OnBack 回调，不需要 SessionToken

**后端 RegisterCompleteAsync 修复（伪代码）：**

```csharp
// 现有：保存凭证 → 返回 Ok()
// 修复后：保存凭证 → 更新 ConfiguredMethods → 按需启用 2FA → 生成恢复码 → 返回 RecoveryCodesResponse
var success = await webAuthnService.CompleteRegistration(user, attestation, options, deviceName);
if (!success) return TypedResults.Problem("WebAuthn 注册失败", statusCode: 400);

// 始终更新 ConfiguredMethods（无论是否首个 2FA 方法）——参考 TwoFactorEndpoints.VerifyEmailAsync 模式
user.ConfiguredMethods |= TwoFactorMethod.WebAuthn;
if (!user.TwoFactorEnabled)
{
    user.TwoFactorEnabled = true;
    user.TwoFactorSetupCompletedAt = DateTime.UtcNow;
}
await userManager.UpdateAsync(user);

// 生成恢复码（首个方法时创建，后续方法时重新生成）
// 方法签名需添加 RecoveryCodeService recoveryCodeService 参数
// 返回类型需从 Results<Ok, ProblemHttpResult> 改为 Results<Ok<RecoveryCodesResponse>, ProblemHttpResult>
var codes = await recoveryCodeService.RegenerateRecoveryCodesAsync(user);
httpContext.Session.Remove("WebAuthnRegisterOptions");
return TypedResults.Ok(new RecoveryCodesResponse(codes));
```

## Verification

**Commands:**
- `dotnet build BoxWise.slnx` — expected: 0 警告, 0 错误
- `dotnet test BoxWise.slnx` — expected: 全部通过（现有 233 测试不退化）

**Manual checks (if no CLI):**
- 启动 Server + Client，访问 2FA 设置页，确认通行密钥选项卡片在 TOTP 和 Email 之间显示
- 在支持 WebAuthn 的浏览器中完成注册流程，确认 2FA 启用 + 恢复码显示
- 验证凭据列表正确显示已注册密钥，删除功能正常
- 验证删除失败时凭据恢复
- 登录页点击"使用通行密钥登录" → 完成浏览器验证 → 直接登录
- 登录验证"未找到通行密钥"的降级提示

## Destgn Notes — Passkey 无密码登录

```csharp
// WebAuthnService.StartLoginAsync — 空 AllowCredentials，浏览器弹出所有通行密钥
public async Task<AssertionOptions> StartLoginAsync()
{
    return _fido2.GetAssertionOptions(new GetAssertionOptionsParams
    {
        UserVerification = UserVerificationRequirement.Preferred
    });
}

// WebAuthnService.CompleteLoginAsync — 遍历凭证匹配 credentialId → 找到用户
public async Task<AppUser?> CompleteLoginAsync(
    AuthenticatorAssertionRawResponse assertion, AssertionOptions options)
{
    var allCredentials = await _db.WebAuthnCredentials.Include(c => c.User).ToListAsync();
    foreach (var credential in allCredentials)
    {
        try
        {
            var result = await _fido2.MakeAssertionAsync(new MakeAssertionParams
            {
                AssertionResponse = assertion, OriginalOptions = options,
                StoredPublicKey = Convert.FromBase64String(credential.PublicKey),
                StoredSignatureCounter = (uint)credential.SignCount,
                IsUserHandleOwnerOfCredentialIdCallback = (args, ct) =>
                    Task.FromResult(credential.CredentialId
                        == Convert.ToBase64String(args.CredentialId))
            });
            credential.SignCount = (int)result.SignCount;
            await _db.SaveChangesAsync();
            return credential.User;
        }
        catch { }
    }
    return null;
}
```

**端点：** `POST /api/auth/webauthn/login-begin` + `login-complete` — `.AllowAnonymous()`
**Login.razor：** 密码表单下方，MudDivider 分隔，"使用通行密钥登录"按钮 + Fingerprint 图标

## Suggested Review Order

**后端：Passkey 无密码登录核心**

- 入口：匿名登录端点，Session 存取 AssertionOptions，SignInManager 签发 Cookie
  [`WebAuthnEndpoints.cs:192`](../../src/BoxWise.Server/Endpoints/WebAuthnEndpoints.cs#L192)

- 按 credentialId 精确查询 + Base64url 转换 + 乐观并发控制
  [`WebAuthnService.cs:182`](../../src/BoxWise.Server/Services/WebAuthnService.cs#L182)

- SignCount 并发令牌 EF 配置
  [`WebAuthnCredentialConfiguration.cs:27`](../../src/BoxWise.Server/Data/Configurations/WebAuthnCredentialConfiguration.cs#L27)

- 修复 RegisterCompleteAsync：2FA 启用 + 恢复码 + UpdateAsync 返回值检查
  [`WebAuthnEndpoints.cs:134`](../../src/BoxWise.Server/Endpoints/WebAuthnEndpoints.cs#L134)

- ChallengeAsync 添加 WebAuthn 方法
  [`TwoFactorEndpoints.cs:248`](../../src/BoxWise.Server/Endpoints/TwoFactorEndpoints.cs#L248)

- FIDO2 Origins 双端口默认 + ResidentKey=Required
  [`Program.cs:124`](../../src/BoxWise.Server/Program.cs#L124)

**客户端：AuthService 方法**

- 凭证管理 4 方法 + Passkey 登录 2 方法
  [`AuthService.cs:442`](../../src/BoxWise.Client/Services/AuthService.cs#L442)

**客户端：UI 组件**

- 登录页"使用通行密钥登录"按钮 + JS 互操作流程
  [`Login.razor:46`](../../src/BoxWise.Client/Pages/Login.razor#L46)

- WebAuthnSetup 注册组件：设备名输入 + 防抖锁 + JS createCredential
  [`WebAuthnSetup.razor:1`](../../src/BoxWise.Client/Components/WebAuthnSetup.razor#L1)

- WebAuthnCredentialList 凭据列表：MudPaper + 内联确认 + 乐观回滚
  [`WebAuthnCredentialList.razor:1`](../../src/BoxWise.Client/Components/WebAuthnCredentialList.razor#L1)

- TwoFactorSetup WebAuthn 卡片集成
  [`TwoFactorSetup.razor:52`](../../src/BoxWise.Client/Components/TwoFactorSetup.razor#L52)

- TwoFactorManage 通行密钥管理入口
  [`TwoFactorManage.razor:82`](../../src/BoxWise.Client/Components/TwoFactorManage.razor#L82)

**配置与文档**

- webauthn.js 加载
  [`index.html:44`](../../src/BoxWise.Client/wwwroot/index.html#L44)

- 使用指南（测试 + 生产环境）
  [`webauthn-setup-guide.md:1`](../../docs/webauthn-setup-guide.md#L1)

- README 入口
  [`README.md:103`](../../README.md#L103)
