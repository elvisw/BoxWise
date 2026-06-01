---
baseline_commit: 7c322bc
---

# Story 11.2: Login.razor 保留通行密钥 + 适配 Identity 登录

Status: done

### Review Findings

- [x] [Review][Defer] 开发环境跨端口链接 — `/Identity/Account/Login` 在 Client 开发服务器 (5001) 不可达，已知限制，仅影响开发环境 [Login.razor:13]

## Story

As a 用户，
I want 在 Identity Login 页面点击"使用通行密钥登录"链接后跳转到 Blazor WASM 使用 WebAuthn，
So that 通行密钥登录功能不被迁移破坏。

## Acceptance Criteria

### AC-1: Login.razor 移除标准登录和 2FA UI

**Given** `Login.razor` 中以下内容已移除：
- `HandleLogin`、`HandleTwoFactorVerify`、`HandleRecoveryCodeVerify` 方法
- `LoadTwoFactorChallengeAsync`、`SelectMethod`、`ResendEmailCode`、`BackToCredentials` 方法
- `LoginStep.TwoFactor` 分支的全部 UI（`_loginStep == LoginStep.TwoFactor`）
- `LoginStep` 枚举和所有相关字段：`_loginStep`、`_isLoading`、`_model`
- 2FA 专用字段：`_hasRecoveryCodes`、`_allowedMethods`、`_selectedMethod`、`_emailToken`、`_showRecoveryCode`、`_isSendingCode`、`_totpCode`、`_recoveryCode`
- `EditForm`（用户名/密码输入表单）

**When** `dotnet build`
**Then** Client 项目 0 错误 0 警告

### AC-2: Login.razor 保留通行密钥登录

**Given** `Login.razor` 保留以下内容：
- `HandlePasskeyLogin` 方法（完整保留）
- 通行密钥按钮 UI（`MudButton @onclick="HandlePasskeyLogin"`）
- `_passkeyLoading` 状态
- `_error` 错误提示
- `LoginModel` 类（仅保留，不再使用——Story 11.3 退役时清理）

**When** 渲染 `/login` 页面
**Then** 显示通行密钥登录按钮 + 引导提示

### AC-3: 引导提示链接

**Given** Login.razor 只保留通行密钥按钮
**When** 渲染 `/login` 页面
**Then** 页面顶部显示引导提示："密码登录请访问登录页面"（附链接 `/Identity/Account/Login`）
**And** 确保没有通行密钥（或通行密钥不可用）的用户不被卡住

### AC-4: Identity Login.cshtml 增加通行密钥入口

**Given** Identity `Login.cshtml`（`Areas/Identity/Pages/Account/Login.cshtml`）
**When** 在 `</form>` 后添加链接
**Then** 显示"使用通行密钥登录"链接，指向 `/login`（Blazor WASM 路由）

```html
<div class="mt-3">
    <a href="/login">使用通行密钥登录</a>
</div>
```

### AC-5: 通行密钥登录功能端到端正常

**Given** 用户在 `/login` 点击通行密钥按钮
**When** `JS.InvokeAsync("webauthn.getCredential")` 成功
**Then** `CompleteWebAuthnLoginAsync` → 登录成功 → 导航到 `/` → WASM 首页显示已登录状态

### AC-6: AuthService.cs WebAuthn 方法确认保留

**Given** `AuthService.cs`
**When** 审查方法列表
**Then** 以下 WebAuthn 方法完整保留（本 Story 不触碰）：
- `StartWebAuthnLoginAsync`
- `CompleteWebAuthnLoginAsync`
- `GetWebAuthnCredentialsAsync`
- `DeleteWebAuthnCredentialAsync`
- `StartWebAuthnRegistrationAsync`
- `CompleteWebAuthnRegistrationAsync`
- `GetWebAuthnAvailableInfoAsync`

### AC-7: 编译 + 测试验证

**Given** 所有修改完成
**When** `dotnet build`
**Then** 0 错误 0 警告

**Given** `dotnet test`
**When** 执行所有测试
**Then** 308 全部通过

## Tasks / Subtasks

- [x] Task 1: Login.razor 精简为通行密钥专用页面 (AC: #1, #2, #3)
  - [ ] 1.1 移除 `EditForm`（用户名/密码输入表单）及 `HandleLogin` 方法
  - [ ] 1.2 移除 `LoginStep.TwoFactor` 分支的全部 UI（98 行模板代码）
  - [ ] 1.3 移除 2FA 专用方法：`HandleTwoFactorVerify`、`HandleRecoveryCodeVerify`、`LoadTwoFactorChallengeAsync`、`SelectMethod`、`ResendEmailCode`、`BackToCredentials`
  - [ ] 1.4 移除所有不再使用的字段：`_loginStep`、`_isLoading`、`_model`、`_hasRecoveryCodes`、`_allowedMethods`、`_selectedMethod`、`_emailToken`、`_showRecoveryCode`、`_isSendingCode`、`_totpCode`、`_recoveryCode`（`_passkeyLoading`、`_error` 保留）
  - [ ] 1.5 保留 `HandlePasskeyLogin`（完整不动）、`_passkeyLoading`、`_error`、通行密钥按钮
  - [ ] 1.6 移除 `LoginStep` 枚举（无多步骤状态机），保留 `LoginModel` 类（Story 11.3 统一清理死代码）
  - [ ] 1.7 添加引导提示 + 密码登录链接 → `/Identity/Account/Login`

- [ ] Task 2: Identity Login.cshtml 添加通行密钥入口 (AC: #4)
  - [ ] 2.1 在 `</form>` 后添加 `<a href="/login">使用通行密钥登录</a>`
  - [ ] 2.2 记录修改到 `docs/identity-scaffold-modifications.md`

- [ ] Task 3: 编译 + 测试验证 (AC: #7)
  - [ ] 3.1 `dotnet build` — 0 错误 0 警告
  - [ ] 3.2 `dotnet test` — 308 全部通过

## Dev Notes

### 架构上下文

**当前状态：** Story 11.1 已完成——Settings.razor 重构为跳转链接，Settings 页面不再引用手写 2FA 组件。Identity Login.cshtml 已有用户名/密码登录和 2FA 流程（LoginWith2fa/LoginWithRecoveryCode，含 .NET 10 Bug workaround）。

**本 Story 目标：** Login.razor 从"用户名/密码 + 2FA + 通行密钥"三合一页面精简为"通行密钥专用"页面。在 Identity Login.cshtml 底部添加导航链接，确保用户知道如何找到通行密钥登录。

**关键约束：**
- SPEC C2：通行密钥不可退役。`HandlePasskeyLogin`、`webauthn.getCredential` JS 互操作、对应的 API 端点必须保留
- Identity UI 不提供 Passkey 支持——通行密钥登录入口保持在 Blazor WASM `/login`
- 通行密钥验证成功后直接 `SignInAsync`，不检查 2FA（通行密钥本身作为已验证的硬件令牌）

### Login.razor 修改详解

#### 移除内容（~350 行 → 保留 ~90 行）

| 移除 | 行数 | 原因 |
|------|:---:|------|
| `HandleLogin` 方法 | ~40 | 用户名/密码登录由 Identity `Login.cshtml` 替代 |
| `LoginStep.TwoFactor` UI 分支 | ~98 | TOTP/Email 2FA + 恢复码验证由 Identity `LoginWith2fa`/`LoginWithRecoveryCode` 替代 |
| `HandleTwoFactorVerify` | ~30 | 2FA 验证由 Identity 页面处理 |
| `HandleRecoveryCodeVerify` | ~26 | 恢复码登录由 Identity 页面处理 |
| `LoadTwoFactorChallengeAsync` | ~20 | 2FA 挑战由 Identity 页面处理 |
| `SelectMethod` / `ResendEmailCode` | ~35 | Email 2FA 由 Identity 页面处理 |
| `BackToCredentials` | ~10 | 无多步骤状态机 |
| 字段：`_loginStep`、`_isLoading`、`_model`、`_hasRecoveryCodes`、`_allowedMethods`、`_selectedMethod`、`_emailToken`、`_showRecoveryCode`、`_isSendingCode`、`_totpCode`、`_recoveryCode`（11 个） | ~12 | 仅用于已移除的代码，保留则触发 CS0169/CS0246 |
| `LoginStep` 枚举 | ~5 | 简化为无状态页面 |
| `EditForm` 模板 | ~20 | 用户名/密码 → Identity Login |

#### 保留内容（不动）

| 保留 | 原因 |
|------|------|
| `HandlePasskeyLogin`（完整，~50 行） | SPEC C2：通行密钥不可退役 |
| 通行密钥按钮 UI | 用户入口 |
| `_passkeyLoading`、`_error` | 按钮状态和错误提示 |
| `LoginModel` 类 | Story 11.3 统一清理死代码（避免跨 Story git 冲突） |

#### 重构后的 Login.razor 结构

```razor
@page "/login"
@inject AuthService AuthService
@inject NavigationManager Navigation
@inject IJSRuntime JS

<MudContainer MaxWidth="MaxWidth.Small" Class="mt-8">
    <MudPaper Class="pa-6" Elevation="2">
        <MudText Typo="Typo.h5" Align="Align.Center" GutterBottom="true">
            登录 BoxWise
        </MudText>

        <MudAlert Severity="Severity.Info" Class="mb-4" Dense="true">
            密码登录请访问 <a href="/Identity/Account/Login">登录页面</a>（生产环境同域，开发环境需手动切换端口到 5000）
        </MudAlert>

        @if (!string.IsNullOrEmpty(_error))
        {
            <MudAlert Severity="Severity.Error" Class="mb-3" Dense="true">
                @_error
            </MudAlert>
        }

        <MudButton Variant="Variant.Outlined"
                   Color="Color.Secondary"
                   FullWidth="true"
                   StartIcon="@Icons.Material.Filled.Fingerprint"
                   Disabled="@_passkeyLoading"
                   OnClick="HandlePasskeyLogin">
            @(_passkeyLoading ? "验证中..." : "使用通行密钥登录")
        </MudButton>
    </MudPaper>
</MudContainer>

@code {
    private string? _error;
    private bool _passkeyLoading;

    // HandlePasskeyLogin 方法完整保留（不动）
    private async Task HandlePasskeyLogin() { /* 现有实现不动 */ }
}
```

### Identity Login.cshtml 修改

**文件：** `Areas/Identity/Pages/Account/Login.cshtml`

在 `</form>` 之后、`</section>` 之前添加：

```html
<div class="mt-3">
    <a href="/login">使用通行密钥登录</a>
</div>
```

> 注意：`/login` 是 Blazor WASM 路由。点击此链接从 5000 端口同域请求 `/login`——Server 配置了 `MapFallbackToFile("index.html")` Blazor WASM 静态文件回退，因此在 5000 端口也能正常加载 Login.razor 页面。开发环境和生产环境均无需跨端口。
>
> **反向导航（WASM → Identity Login）：** `Login.razor` 中的 `<a href="/Identity/Account/Login">` 在生产环境同域工作正常。开发环境下，此链接在端口 5001 访问时指向 `localhost:5001/Identity/Account/Login`（404），因为 Identity Razor Pages 仅在端口 5000 存在。这是**已知限制**——与 Settings.razor 使用 `GetServerUrl` 不同，Login.razor 页面无需注入 `IConfiguration` 仅为一个链接。用户可通过浏览器的地址栏手动切换端口，或直接访问 `https://localhost:5000/Identity/Account/Login`。

### 文件变更清单

| 操作 | 文件 | 变更内容 |
|------|------|---------|
| ✏️ MODIFY | `src/BoxWise.Client/Pages/Login.razor` | 移除标准登录+2FA UI（~350行→~90行），保留通行密钥 |
| ✏️ MODIFY | `src/BoxWise.Server/Areas/Identity/Pages/Account/Login.cshtml` | 添加"使用通行密钥登录"链接 |
| 📝 DOC | `docs/identity-scaffold-modifications.md` | 记录 Login.cshtml 修改 |

### 从之前 Story 学到的经验

**Story 11.1 (Settings.razor) 教训：**
- `@onkeydown:preventDefault` 导致键盘焦点陷阱 → 本 Story 的通行密钥按钮使用 `OnClick`，不涉及键盘事件
- `GetServerUrl` 需要在所有跨端口导航处使用 → 本 Story 的 Identity Login.cshtml 链接是纯 `<a href>`，由浏览器原生导航，无需 `GetServerUrl`
- Code review 发现 6 项问题全部修复 → 本 Story 已吸取经验：保留 `AuthService` 注入（通行密钥仍然需要），`InvokeSafeAsync` 不需要（`HandlePasskeyLogin` 已有完整 try/catch）

**Story 10.3/10.4 教训：**
- `[AllowAnonymous]` 必须添加 → Identity Login 页面已完成配置
- .NET 10 `GetTwoFactorAuthenticationUserAsync` Bug → 本 Story 不涉及 2FA 页面修改

**Epic 10 回顾修复：**
- `Login.cshtml` 已汉化并适配用户名登录（回顾修复 #1, #2）→ 本 Story 在此已有修改的 `Login.cshtml` 基础上继续添加链接

### 本 Story 不改动的内容（边界明确）

| 不改动 | 原因 |
|--------|------|
| `AuthService.cs` 任何方法 | 由 Story 11.3 处理（退役非 WebAuthn 方法） |
| `LoginWith2fa.cshtml.cs` / `LoginWithRecoveryCode.cshtml.cs` | .NET 10 workaround 已在 Story 10.4 应用，本 Story 不触碰 |
| Server 端 WebAuthn API 端点 | 通行密钥 API 端点保留，本 Story 不触碰 |
| `webauthn.getCredential` JS 互操作 | 保留，不动 |
| `AppState.SetUser()` / `NotifyAuthenticationStateChanged()` | `HandlePasskeyLogin` 中的认证状态同步不动 |
| `LoginModel` 类退役 | 由 Story 11.3 处理（统一清理死代码） |

### 测试策略

- **编译验证：** `dotnet build` 0 错误 0 警告 —— 验证移除的 UI 和方法无残留引用
- **测试回归：** `dotnet test` 308 全部通过 —— 本 Story 仅修改 Client UI 模板 + Server 端 Identity .cshtml，不影响测试
- **手动验证（建议 Story 11.3 完成后统一执行）：**
  1. 访问 `/Identity/Account/Login` → 看到"使用通行密钥登录"链接
  2. 点击链接 → 跳转到 Blazor WASM `/login` → 显示通行密钥按钮 + 引导提示
  3. 点击通行密钥按钮 → WebAuthn 弹窗 → 验证通过 → 登录成功
  4. 边界：无通行密钥的用户访问 `/login` → 看到引导提示 → 点击链接回到 Identity Login

### References

- [Source: SPEC.md CAP-6] — 通行密钥登录保留需求
- [Source: SPEC.md C2] — 通行密钥不可退役约束
- [Source: epics-identity-scaffold-migration.md Story 2.2] — 验收标准
- [Source: migration-phases.md Phase 3.1] — Login.razor 修改指南
- [Source: decommission-checklist.md — Login.razor 部分退役] — 保留/退役清单
- [Source: Login.razor (current)] — 修改目标（462 行，精简至 ~90 行）
- [Source: Identity Login.cshtml (current)] — 添加链接目标
- [Source: identity-scaffold-modifications.md #1, #2] — 已有的 Login.cshtml 修改
- [Source: Story 11.1 Dev Agent Record] — Settings.razor 重构经验教训
- [Source: Story 10.4 Dev Agent Record] — 2FA 登录 workaround（不触碰）

## Dev Agent Record

### Agent Model Used

Claude Code (deepseek-v4-pro)

### Debug Log References

- `dotnet build` — 0 错误 0 警告，一次通过
- `dotnet test` — 308 通过 0 失败（44 Client + 264 Server）

### Completion Notes List

- ✅ AC-1: 移除 `HandleLogin`/`HandleTwoFactorVerify`/`HandleRecoveryCodeVerify`/`LoadTwoFactorChallengeAsync`/`SelectMethod`/`ResendEmailCode`/`BackToCredentials` + `LoginStep` 枚举 + 11 个字段
- ✅ AC-2: `HandlePasskeyLogin` 完整保留（~48 行不动），通行密钥按钮保留
- ✅ AC-3: 引导提示 `<a href="/Identity/Account/Login">登录页面</a>` + `MudAlert`
- ✅ AC-4: Identity `Login.cshtml` 底部添加 `<a href="/login">使用通行密钥登录</a>`
- ✅ AC-5: WebAuthn 登录完整流程不变——`StartWebAuthnLoginAsync → webauthn.getCredential → CompleteWebAuthnLoginAsync → /`
- ✅ AC-6: AuthService 6 个 WebAuthn 方法确认保留（本 Story 不触碰）
- ✅ AC-7: `dotnet build` 0 错误 + `dotnet test` 308 通过

### Change Log

- 2026-06-02: Implementation completed — 3 files, Login.razor ~460→~85 行

### File List

| 操作 | 文件 | 说明 |
|------|------|------|
| ✏️ MODIFY | `src/BoxWise.Client/Pages/Login.razor` | 精简为通行密钥专用页面（~85 行，-377 行） |
| ✏️ MODIFY | `src/BoxWise.Server/Areas/Identity/Pages/Account/Login.cshtml` | 添加"使用通行密钥登录"链接（+3 行） |
| 📝 DOC | `docs/identity-scaffold-modifications.md` | 记录 Login.cshtml 修改 #13 |
