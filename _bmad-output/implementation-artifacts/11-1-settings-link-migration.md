---
baseline_commit: d2f38b3
---

# Story 11.1: Settings.razor 替换为跳转链接

Status: done

### Review Findings

- [x] [Review][Defer] DateTimeKind 比较 — SQLite/JSON 往返 Kind=Unspecified vs UtcNow，预存问题 [Settings.razor:108]
- [x] [Review][Defer] LoadCredentialsAsync 仅捕获 InvalidOperationException — 预存问题 [WebAuthnCredentialList.razor:98]

## Story

As a 用户，
I want 从 Blazor WASM Settings 页面点击按钮跳转到 Identity 账户管理页面，
So that 我可以在 Server 端 Identity Bootstrap 页面中管理 2FA、修改密码和邮箱，而不依赖即将退役的手写组件。

## Acceptance Criteria

### AC-1: 移除已替代的组件引用

**Given** `Settings.razor` 中 `TwoFactorManage`、`TwoFactorSetup`、`AccountInfoDialog`、`ChangePasswordDialog` 组件引用已移除
**When** `dotnet build`
**Then** Client 项目 0 错误，无未解析的组件引用

### AC-2: "管理账户设置"链接按钮

**Given** 开发环境 `ApiBaseUrl = "https://localhost:5000/"`（`wwwroot/appsettings.Development.json`）
**When** 点击"管理账户设置"按钮
**Then** 新标签页打开 `https://localhost:5000/Identity/Account/Manage`（使用 `Target="_blank"`）

**Given** 生产环境 `ApiBaseUrl` 为空
**When** 点击"管理账户设置"按钮
**Then** 同域相对路径 `/Identity/Account/Manage`，不跨端口

**Given** 已登录用户的 Cookie 有效
**When** 访问 `/Identity/Account/Manage`
**Then** 直接显示管理页面，不要求重新登录（Identity Cookie 认证已由 Epic 10 配置）

### AC-3: "通行密钥管理"对话框

**Given** 用户需要管理已注册的通行密钥
**When** 点击"通行密钥管理"条目
**Then** 弹出对话框，内含 `WebAuthnCredentialList` 组件（已注册密钥列表+删除）+ `WebAuthnSetup` 组件（注册新密钥）
**And** 对话框关闭后自动刷新列表

**Given** `WebAuthnCredentialList` 和 `WebAuthnSetup` 是已存在的独立组件
**When** 在 Settings.razor 中使用它们
**Then** 不创建新组件——复用现有组件，通过 `MudDialog` 包裹

### AC-4: 退出登录改造

**Given** Settings.razor 的"退出登录"按钮
**When** 点击时
**Then** 通过 `GetServerUrl("Identity/Account/Logout")` 导航（`forceLoad: true`），不调用 `AuthService.LogoutAsync`
**And** `Logout.cshtml.cs` 已在 Story 10.3 添加 `OnGet` handler，GET 请求即可登出
**And** 不传 `returnUrl` 时默认重定向到 `/Identity/Account/Login`（Identity 默认行为），如需登出后回到首页，传递 `?returnUrl=/`

### AC-5: 2FA 状态加载代码移除

**Given** Settings.razor 不再需要显示 2FA 状态文字或自动弹窗
**When** 审查 `@code` 块
**Then** 以下代码已移除：
- `_twoFactorStatusText`、`_twoFactorEnabled` 字段
- `LoadTwoFactorStatusAsync` 方法
- `OpenTwoFactorSetupDialog` 方法（`TwoFactorSetup`/`TwoFactorManage` 引用已移除）
- `OpenAccountInfoDialog` 方法（`AccountInfoDialog` 引用已移除）
- `OpenChangePasswordDialog` 方法（`ChangePasswordDialog` 引用已移除）
- `OnInitializedAsync` / `OnAfterRenderAsync` 中的 2FA 状态加载
- 2FA 宽限期过期自动弹窗逻辑
- `@inject AuthService AuthService`（Settings.razor 中不再直接调用任何 `AuthService` 方法——通行密钥管理通过 `WebAuthnCredentialList`/`WebAuthnSetup` 组件间接调用）

### AC-6: Client Program.cs 环境配置文件加载

**Given** `ApiBaseUrl` 定义在 `wwwroot/appsettings.Development.json` 中
**When** Blazor WASM 在开发环境下启动
**Then** `builder.Configuration["ApiBaseUrl"]` 可读取到 `"https://localhost:5000/"`
**And** 如 Blazor WASM 默认未加载环境特定配置文件，在 `Program.cs` 中添加：
```csharp
if (builder.HostEnvironment.IsDevelopment())
    builder.Configuration.AddJsonFile("appsettings.Development.json", optional: true);
```

### AC-7: 已存在的功能不受影响

**Given** Settings 页面渲染
**When** 检查以下条目
**Then** 这些功能完整保留，无任何改动：
- 位置管理（`LocationManageDialog`）
- 标签管理（`TagManageDialog`）
- 关于（`GoAbout`）
- 退出登录（改造为通过 `GetServerUrl` 导航到 Identity Logout）

### AC-8: 编译 + 测试验证

**Given** 所有修改完成
**When** `dotnet build`
**Then** 0 错误 0 警告

**Given** `dotnet test`
**When** 执行所有测试
**Then** 全部通过——本 Story 仅修改 Client 端 UI，不影响 Server 端测试

## Tasks / Subtasks

- [x] Task 1: Settings.razor UI 重构 (AC: #1, #2, #3, #4, #5, #7)
  - [x] 1.1 移除 `AccountInfoDialog`、`ChangePasswordDialog` 条目及其 key handler
  - [x] 1.2 移除"双因素认证与通行密钥"条目，拆分为两个条目：
    - "管理账户设置" → `MudButton Href` 指向 Identity Manage 页面（`Target="_blank"`）
    - "通行密钥管理" → 内联 dialog，内含 `WebAuthnCredentialList` + `WebAuthnSetup`
  - [x] 1.3 实现 `GetServerUrl` 辅助方法（通过 `IConfiguration["ApiBaseUrl"]` 判断环境，`TrimEnd('/')` + `TrimStart('/')` 处理尾部/首部斜杠健壮性）
  - [x] 1.4 修改退出登录按钮：`Navigation.NavigateTo(GetServerUrl("Identity/Account/Logout"), forceLoad: true)`，可选传递 `?returnUrl=/` 登出后回到首页
  - [x] 1.5 移除 `@code` 中的 2FA 相关代码（`_twoFactorStatusText`、`_twoFactorEnabled`、`LoadTwoFactorStatusAsync`、`OpenTwoFactorSetupDialog`、`OpenAccountInfoDialog`、`OpenChangePasswordDialog`、`OnInitializedAsync`、`OnAfterRenderAsync`），移除不再使用的 `@inject AuthService AuthService` 和 `@using BoxWise.Shared.Dtos`（后者仅用于 `TwoFactorStatusDto`，移除 2FA 代码后成为死引用）
  - [x] 1.6 移除 2FA 相关的 key handler（`HandleTwoFactorKey`、`HandleAccountKey`、`HandlePasswordKey`），新增 `HandlePasskeyKey`
  - [x] 1.7 移除"双因素认证与通行密钥"状态行的 `MudDivider`（调整分割线位置）

- [x] Task 2: Client Program.cs 配置加载 (AC: #6)
  - [x] 2.1 验证 `WebAssemblyHostBuilder.CreateDefault` 是否已在开发环境加载 `appsettings.Development.json`
  - [x] 2.2 如未加载，在 `builder.HostEnvironment.IsDevelopment()` 时添加 `builder.Configuration.AddJsonFile("appsettings.Development.json", optional: true)` — **已验证：CreateDefault 自动加载，无需修改 Program.cs**

- [x] Task 3: 编译 + 测试验证 (AC: #8)
  - [x] 3.1 `dotnet build` — 0 错误 0 警告
  - [x] 3.2 `dotnet test` — 全部通过

## Dev Notes

### 架构上下文

**当前状态：** Epic 10 已完成——17 个 Identity Razor Pages 生成完成，IEmailSender 适配器注册完成，Cookie 认证桥接工作正常（`LoginPath` + `OnRedirectToLogin`+ `[AllowAnonymous]`），2FA 登录（含 .NET 10 Bug workaround）已手动验证通过。用户从 Identity Login.cshtml 登录后，Cookie 签发，重定向到 Blazor WASM 首页，`CookieAuthenticationStateProvider` 通过 `GET /api/auth/me` 感知认证状态。

**本 Story 目标：** Settings.razor 中所有被 Identity Manage 页面覆盖的功能（2FA 设置/管理、密码修改、邮箱修改、账户信息）替换为跳转链接。通行密钥管理独立保留。退出登录改为导航到 Identity Logout 页面。

**关键约束：**
- Identity 页面使用 Bootstrap 样式，Blazor WASM 使用 MudBlazor，双 UI 风格并存是已接受的权衡（SPEC NFR-1/C1）
- Identity Manage 页面在同一 Cookie 域下，用户已登录时直接进入，无需重新认证
- `CookieAuthenticationStateProvider` 不可退役——它仅依赖 `GET /api/auth/me`，与登录流程解耦（SPEC C5）

### Settings.razor 修改详解

#### 移除的条目（3个）

| 条目 | 去除理由 | 替代者 |
|------|---------|--------|
| 账户信息（`AccountInfoDialog`） | 用户名/邮箱查看+修改 | Identity `Account.Manage.Index` + `Account.Manage.Email` |
| 修改密码（`ChangePasswordDialog`） | 密码修改 | Identity `Account.Manage.ChangePassword` |
| 双因素认证与通行密钥（`TwoFactorSetup`/`TwoFactorManage`） | 2FA 设置/管理 | Identity `Account.Manage` — EnableAuthenticator/ResetAuthenticator/Disable2fa/TwoFactorAuthentication/GenerateRecoveryCodes |

#### 新增的条目（2个）

**1. 管理账户设置**

```razor
@inject IConfiguration Config

<MudButton Href="@GetServerUrl("Identity/Account/Manage")"
           Target="_blank"
           StartIcon="@Icons.Material.Filled.Security"
           Color="Color.Primary"
           Variant="Variant.Text"
           Class="bw-settings-item pa-4 mb-1"
           FullWidth="true">
    <div class="d-flex align-center flex-grow-1">
        <MudText Typo="Typo.body1" Style="font-weight:500;">管理账户设置</MudText>
        <MudText Typo="Typo.caption" Color="Color.Default">管理密码、邮箱与两步验证</MudText>
    </div>
</MudButton>

@code {
    private string GetServerUrl(string path)
    {
        var apiBase = Config["ApiBaseUrl"];
        if (!string.IsNullOrEmpty(apiBase))
        {
            var trimmed = apiBase.TrimEnd('/');
            return $"{trimmed}/{path.TrimStart('/')}";
        }
        return $"/{path.TrimStart('/')}";
    }
}
```

> **设计决策：** 使用 `IConfiguration["ApiBaseUrl"]` 直接读取 Server 端口配置。与 Home.razor 的 `Http.BaseAddress` 不同——`Http.BaseAddress` 可能被 `Program.cs` 的端口改写逻辑（`IsLoopback` + 端口比对）修改为 Client 开发服务器端口（5001），而 `IConfiguration["ApiBaseUrl"]` 始终指向 Server 端口（5000）。这对 Identity 页面导航是**正确行为**——Identity Razor Pages 仅在 Server 端存在，Client 开发服务器（5001）上没有这些路由。
>
> 这个按钮同时覆盖首次 2FA 设置和后续 2FA 管理——Identity Manage 页面的导航（`_ManageNav`）已包含 `TwoFactorAuthentication`、`EnableAuthenticator`、`ResetAuthenticator` 等所有子页面。

**2. 通行密钥管理**

```razor
<div class="pa-4 mb-1 bw-settings-item" role="button" tabindex="0"
     @onclick="OpenPasskeyManageDialog" @onkeydown="HandlePasskeyKey" @onkeydown:preventDefault>
    <div class="d-flex align-center">
        <MudIcon Icon="@Icons.Material.Filled.Fingerprint" Size="Size.Medium" Class="mr-3" Color="Color.Tertiary" />
        <div class="flex-grow-1">
            <MudText Typo="Typo.body1" Style="font-weight:500;">通行密钥管理</MudText>
            <MudText Typo="Typo.caption" Color="Color.Default">管理已注册的指纹/面容/硬件密钥</MudText>
        </div>
        <MudIcon Icon="@Icons.Material.Filled.ChevronRight" Color="Color.Default" />
    </div>
</div>
```

通行密钥管理对话框内嵌 `WebAuthnCredentialList` + `WebAuthnSetup`：

```csharp
private async Task OpenPasskeyManageDialog()
{
    var options = new DialogOptions { CloseButton = true, MaxWidth = MaxWidth.Small, FullWidth = true };
    await DialogService.ShowAsync<PasskeyManageDialog>("通行密钥管理", options);
}
```

**PasskeyManageDialog.razor（新建）：**

```razor
@namespace BoxWise.Client.Components

<MudText Typo="Typo.subtitle1" Class="mb-4">已注册的通行密钥</MudText>
<WebAuthnCredentialList @ref="_credentialList" />

<MudDivider Class="my-4" />

<MudText Typo="Typo.subtitle1" Class="mb-3">注册新通行密钥</MudText>
<WebAuthnSetup OnSetupComplete="HandleSetupComplete" />

@code {
    private WebAuthnCredentialList _credentialList = default!;

    private async Task HandleSetupComplete()
    {
        await _credentialList.LoadCredentialsAsync();
        StateHasChanged();
    }
}
```

> **注意：** `WebAuthnSetup` 的 `OnSetupComplete` EventCallback **已存在**（`WebAuthnSetup.razor` 第 60-61 行声明 `[Parameter] public EventCallback OnSetupComplete { get; set; }`，第 157 行调用 `await OnSetupComplete.InvokeAsync()`）。**无需修改 `WebAuthnSetup.razor`**——直接使用现有参数即可。

#### 退出登录改造

```csharp
// 修改前
private async Task LogoutAsync()
{
    try { await AuthService.LogoutAsync(); }
    catch { }
    Navigation.NavigateTo("/", forceLoad: true);
}

// 修改后
private void Logout()
{
    Navigation.NavigateTo(GetServerUrl("Identity/Account/Logout"), forceLoad: true);
}
```

> **端口说明：** Identity Logout 是 Server 端 Razor Page，仅在端口 5000 可用。必须使用 `GetServerUrl()` 而非相对路径 `/Identity/Account/Logout`——否则在 Client 开发服务器（5001）上会导致 404。
>
> **UX 变化：** Logout 后默认重定向到 `/Identity/Account/Login`（Identity `Logout.cshtml.cs` 默认行为）。如需登出后回到首页，传递 `?returnUrl=/`。
>
> `forceLoad: true` 确保完整页面加载——Identity Logout 是 Server 端 Razor Page，不是 Blazor WASM 路由。

#### 保留的条目（4个，无改动）

- 位置管理（`LocationManageDialog`）
- 标签管理（`TagManageDialog`）
- 关于（`GoAbout` → `/about`）
- 退出登录（改造为通过 `GetServerUrl` 导航到 Identity Logout）

### Client Program.cs 配置加载

**背景：** `appsettings.Development.json`（`ApiBaseUrl: "https://localhost:5000/"`）是 Blazor WASM 开发环境的关键配置。Blazor WASM `WebAssemblyHostBuilder.CreateDefault(args)` 在 .NET 8+ 应自动加载 `appsettings.{environment}.json`，但存在环境检测不一致的情况——`HostEnvironment.Environment` 可能不总是 "Development"。

**验证方法：** 在 `Program.cs` 第 13 行后添加调试日志 `Console.WriteLine($"ApiBaseUrl: {builder.Configuration["ApiBaseUrl"]}")`，启动 Client 开发服务器，检查控制台输出。

**如未加载，添加以下代码（在 `CreateDefault` 之后，`builder.Services` 之前）：**

```csharp
if (builder.HostEnvironment.IsDevelopment())
{
    builder.Configuration.AddJsonFile("appsettings.Development.json", optional: true);
}
```

> **注意：** 此代码可能已在之前的开发中隐式工作（如 `CreateDefault` 已正确加载），Story 的任务是先验证再决定是否添加。

### 文件变更清单

| 操作 | 文件 | 变更内容 |
|------|------|---------|
| ✏️ MODIFY | `src/BoxWise.Client/Pages/Settings.razor` | 移除 3 个条目（账户信息/修改密码/双因素认证）+ 新增 2 个条目（管理账户设置/通行密钥管理）+ 退出登录导航改造 |
| ✨ NEW | `src/BoxWise.Client/Components/PasskeyManageDialog.razor` | `WebAuthnCredentialList` + `WebAuthnSetup` 组合容器组件（~20 行） |
| ✏️ COND | `src/BoxWise.Client/Program.cs` | 仅在验证发现环境配置文件未加载时添加 `AddJsonFile` |

> **无需修改：** `WebAuthnSetup.razor` — `OnSetupComplete` EventCallback 已存在（第 60-61 行），直接使用，无需任何改动。

### 从之前 Story 学到的经验

**Story 10.1 教训：**
- 脚手架生成的 `IdentityHostingStartup.cs` 导致重复 Identity 注册 → 本 Story 不涉及脚手架生成
- `AddDefaultIdentity` vs `AddIdentity` 冲突 → 本 Story 不修改 Identity 服务注册

**Story 10.2 教训：**
- Transient vs Scoped 生命周期 → 本 Story 不涉及 DI 注册

**Story 10.3 教训：**
- `[AllowAnonymous]` 必须添加才能防止 FallbackPolicy 无限重定向循环 → Identity 页面已配置，本 Story 不涉及
- `Logout.cshtml.cs` 的 `OnGet` handler 已添加（Story 10.3 + 回顾修复 #5）→ Settings.razor 的 Logout 导航直接可用

**Story 10.4 教训：**
- .NET 10 `GetTwoFactorAuthenticationUserAsync` Bug 已 workaround → 本 Story 不涉及 2FA 登录流程
- 308 测试全部通过，零回归 → 本 Story 仅修改 Client UI 组件，不影响测试

**Epic 10 回顾修复（12 处）：**
- Settings.razor 退出登录改造使用 `GetServerUrl` + `forceLoad: true` 导航到 Identity Logout——对比原来 `AuthService.LogoutAsync()` + 导航到 `/` 的改进
- 回顾修复 #5（`Logout.cshtml.cs` 添加 OnGet handler）直接为本 Story 提供支持

### 本 Story 不改动的内容（边界明确）

| 不改动 | 原因 |
|--------|------|
| `Login.razor` | 由 Story 11.2 处理 |
| `TwoFactorManage.razor` 退役 | 由 Story 11.3 处理（退役） |
| `TwoFactorSetup.razor` 退役 | 由 Story 11.3 处理（退役） |
| `AccountInfoDialog.razor` 退役 | 由 Story 11.3 处理（退役） |
| `ChangePasswordDialog.razor` 退役 | 由 Story 11.3 处理（退役） |
| `AuthService.cs` 方法退役 | 由 Story 11.3 处理（仅方法删除） |
| `TwoFactorEndpoints.cs` 端点退役 | 由 Story 11.3 处理 |
| SameSite 策略 | 由 Story 11.4 处理 |
| Architecture/UX 文档更新 | 由 Story 11.4 处理 |
| `_ManageNav` 死链接 | Epic 11 deferred work（_ManageNav 的 ExternalLogins/PersonalData 链接），不影响用户功能 |
| Identity 页面中文本地化 | Epic 11 deferred work |
| 任何 Server 端代码 | 本 Story 纯 Client 端变更 |
| `WebAuthnSetup.razor` | `OnSetupComplete` EventCallback 已存在，直接使用无需修改 |

### 代码风格对齐

- **MudBlazor 9.x API：** `SelectedValue`（非 `ActivatedValue`）、`SelectionMode`（非 `Filter`/`MultiSelection`）、`BodyContent`（非 `Text`）
- **命名：** 私有方法 `PascalCase`、字段 `_camelCase`、`InvokeSafeAsync` 复用现有模式
- **DTO：** 不新增 DTO——使用现有的 `WebAuthnCredentialDto`
- **DI：** `@inject IConfiguration Config` 是 Blazor WASM 标准模式

### 测试策略

- **编译验证：** `dotnet build` 0 错误 0 警告 —— 验证组件引用已正确移除、新组件正确 import
- **测试回归：** `dotnet test` 全部通过 —— 本 Story 仅修改 Client UI，不影响 Server 测试
- **手动验证（建议 Story 11-3 完成后统一执行）：**
  1. 启动 Server + Client，访问 Settings 页面
  2. 点击"管理账户设置"→ 新标签页打开 Identity Manage 页面
  3. 点击"通行密钥管理"→ 弹出对话框，可查看/删除/注册密钥
  4. 点击"退出登录"→ 导航到 Identity Logout → Cookie 清除 → 重定向到 Login 页面（或首页）
  5. 位置管理、标签管理、关于 功能正常
  6. 边界：未登录访问 `/settings` → 重定向到 `/Identity/Account/Login`
  7. 边界：Cookie 过期后点击"管理账户设置"→ Identity 页面要求重新登录（正确行为）
  8. 边界：在 Identity Manage 页面修改密码后回到 Blazor WASM → "管理账户设置"按钮仍正常打开（新 Cookie 有效）

### References

- [Source: SPEC.md CAP-5] — 用户从 Blazor WASM Settings 跳转到 Identity 管理页面
- [Source: SPEC.md C1] — Bootstrap 样式隔离，双 UI 风格并存
- [Source: SPEC.md C5] — `CookieAuthenticationStateProvider` 不可退役
- [Source: migration-phases.md Phase 3.2] — Settings.razor 修改指南
- [Source: migration-phases.md Phase 3.3] — Client Program.cs 配置加载
- [Source: decommission-checklist.md] — 退役代码清单（本 Story 不移除，仅停止引用）
- [Source: epics-identity-scaffold-migration.md Story 2.1] — 验收标准
- [Source: Project Context §Blazor WASM DI 注册顺序] — Client Program.cs 修改约束
- [Source: Project Context §端口与入口] — 开发环境 5001（Client）/ 5000（Server）
- [Source: identity-scaffold-modifications.md #5] — Logout.cshtml.cs OnGet handler（已添加）
- [Source: Settings.razor (current)] — 修改目标文件
- [Source: WebAuthnCredentialList.razor] — 复用组件（通行密钥列表+删除）
- [Source: WebAuthnSetup.razor] — 复用组件（通行密钥注册，`OnSetupComplete` EventCallback 已存在）
- [Source: wwwroot/appsettings.Development.json] — ApiBaseUrl 定义
- [Source: Story 10.3 Dev Agent Record] — Cookie 认证桥接完成
- [Source: Story 10.4 Dev Agent Record] — 2FA 登录 workaround 完成

## Dev Agent Record

### Agent Model Used

Claude Code (deepseek-v4-pro)

### Debug Log References

- `dotnet build` — 0 错误 0 警告，一次通过
- `dotnet test` — 308 通过 0 失败（44 Client + 264 Server）

### Completion Notes List

- ✅ AC-1: `TwoFactorManage`、`TwoFactorSetup`、`AccountInfoDialog`、`ChangePasswordDialog` 组件引用已移除
- ✅ AC-2: "管理账户设置" MudButton Href 指向 Identity Manage（`GetServerUrl` 判断环境）
- ✅ AC-3: "通行密钥管理" dialog → `PasskeyManageDialog`（WebAuthnCredentialList + WebAuthnSetup）
- ✅ AC-4: 退出登录 → `GetServerUrl("Identity/Account/Logout")` + `forceLoad: true`
- ✅ AC-5: 2FA 状态代码全部移除（字段/方法/key handler/DI/@using）
- ✅ AC-6: Program.cs 验证——`CreateDefault` 已自动加载 `appsettings.Development.json`，无需修改
- ✅ AC-7: 位置管理/标签管理/关于/退出登录 功能完整保留
- ✅ AC-8: `dotnet build` 0 错误 + `dotnet test` 308 通过

### Change Log

- 2026-06-01: Implementation completed (Dev Story) — 2 files, +170 / -200 lines

### File List

| 操作 | 文件 | 说明 |
|------|------|------|
| ✏️ MODIFY | `src/BoxWise.Client/Pages/Settings.razor` | 重构 Settings 页面：移除 3 条目 + 新增 2 条目 + 退出登录改造（-56 行净减少） |
| ✨ NEW | `src/BoxWise.Client/Components/PasskeyManageDialog.razor` | WebAuthnCredentialList + WebAuthnSetup 组合容器（22 行） |
| ⏭️ SKIP | `src/BoxWise.Client/Program.cs` | 无需修改 — `CreateDefault` 已自动加载环境配置文件 |
