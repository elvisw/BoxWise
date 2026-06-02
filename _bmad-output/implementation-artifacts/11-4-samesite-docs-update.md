---
story_id: "11.4"
story_key: 11-4-samesite-docs-update
epic_num: 11
story_num: 4
baseline_commit: d7e1caa
review_baseline: |
  3-agent parallel audit (2026-06-02) — 完整性 + 技术正确性 + 可执行性.
  7 项问题已修复 (3 blocking + 4 minor):
  - AC-5 erroneously referenced non-existent Entry Flow login description → removed
  - Session Cookie (AddSession) SameSite/SecurePolicy added to scope
  - CLAUDE.md stale TwoFactorEndpoints.cs reference added to AC-6
  - Lockout.cshtml.cs description corrected (OnGetAsync → OnGet())
  - "17 个文件" inaccurate counts removed
  - decommission-checklist.md path fixed
  - Program.cs comment sync note added
---

# Story 11.4: SameSite 策略 + 更新 Architecture/UX 文档

Status: done

## Story

As a 开发者，
I want 生产环境 SameSite 策略正确配置，Architecture 和 UX Design 文档反映迁移后的新架构，
so that 安全配置完整，后续开发者有准确的参考文档，Identity 脚手架迁移正式完成。

## Acceptance Criteria

### AC-1: SameSite + SecurePolicy 环境判断

**Given** `Program.cs` 中主 Cookie 配置（`ConfigureApplicationCookie`）目前硬编码 `SameSiteMode.None` + `CookieSecurePolicy.Always`
**When** 提取 `var env = builder.Environment;` 并在 lambda 中使用：
```csharp
var env = builder.Environment;
builder.Services.ConfigureApplicationCookie(options =>
{
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = env.IsDevelopment() ? SameSiteMode.None : SameSiteMode.Lax;
    options.Cookie.SecurePolicy = env.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
    // ... 其余配置不变
});
```
**Then** 开发环境保持 `SameSite=None` + `Secure`（跨端口 5000↔5001 需要），生产环境 `Lax` + `Always`（Caddy 反向代理场景下强制 Secure）

### AC-2: TwoFactorUserIdScheme Cookie 同步

**Given** `IdentityConstants.TwoFactorUserIdScheme` Cookie 配置（`Program.cs:75-80`）目前硬编码 `SameSiteMode.None` + `SecurePolicy.Always`
**When** 同步应用 AC-1 的 `env.IsDevelopment()` 判断：
```csharp
builder.Services.Configure<CookieAuthenticationOptions>(IdentityConstants.TwoFactorUserIdScheme, options =>
{
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = env.IsDevelopment() ? SameSiteMode.None : SameSiteMode.Lax;
    options.Cookie.SecurePolicy = env.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
});
```
**Then** 两处 Cookie 配置的环境判断逻辑一致

### AC-2b: Session Cookie 同步

**Given** `AddSession` Cookie 配置（`Program.cs:147-155`）目前硬编码 `SameSiteMode.None` + `CookieSecurePolicy.Always`，注释 "Blazor WASM 跨端口需要 None"
**When** 同步应用 `env.IsDevelopment()` 判断：
```csharp
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(5);
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = env.IsDevelopment() ? SameSiteMode.None : SameSiteMode.Lax;
    options.Cookie.SecurePolicy = env.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
});
```
**Then** 三处 Cookie 配置（主 Cookie / TwoFactorUserId / Session）的环境判断逻辑一致——生产环境 Session Cookie 不再使用不必要的 `SameSite=None`

### AC-3: Lockout 参数验证

**Given** `Program.cs` 中未显式配置 Identity Lockout 参数（使用 ASP.NET Core Identity 默认值）
**When** 确认 Identity 默认值：
- `options.Lockout.MaxFailedAccessAttempts` = 5（默认）
- `options.Lockout.DefaultLockoutTimeSpan` = 5 分钟（默认）
**And** 对 `Areas/Identity/Pages/Account/Lockout.cshtml.cs` 执行代码审查：确认 `OnGet()` 方法为同步空方法（脚手架默认行为——仅展示静态锁定提示文本，不读取 LockoutEnd 属性）。默认参数与脚手架页面行为一致
**Then** Lockout 配置一致——无需要修改的代码，仅做文档记录
**And** 在 Dev Notes 中记录结论：BoxWise 使用 Identity 默认 Lockout 参数（5 次失败 / 5 分钟锁定），与脚手架 Lockout 页面兼容

### AC-4: Architecture 文档更新 — 认证流程章节

**Given** `_bmad-output/planning-artifacts/architecture.md` 的认证章节反映了旧的 Blazor WASM 自建认证流程
**When** 更新以下章节：

**§ Authentication & Security → Identity Integration:**
- 旧描述："Server hosts `/login`, `/logout`, `/register` endpoints"
- 新描述：登录/登出/2FA 验证由 Identity 脚手架 Razor Pages（`Areas/Identity/Pages/Account/`）处理，非手写 API 端点
- Cookie 通过 `CookieAuthenticationStateProvider` + `GET /api/auth/me` 桥接到 Blazor WASM

**§ Authentication & Security → 新增子章节 "Identity Scaffold Migration (Epic 10-11)":**
```markdown
#### Identity 脚手架混合模式迁移 (2026-06-02)

**决策：** 用 ASP.NET Core Identity 脚手架 Razor Pages 替换手写认证 UI 和 2FA 设置管理，退役 ~1600 行代码。

**迁移范围：**
- 登录/登出：`Areas/Identity/Pages/Account/Login.cshtml` + `Logout.cshtml`（替代 `AuthEndpoints.LoginAsync/LogoutAsync` + `Login.razor` 用户名密码表单）
- 2FA 登录验证：`LoginWith2fa.cshtml` + `LoginWithRecoveryCode.cshtml`（替代 `TwoFactorEndpoints.VerifyAsync/VerifyRecoveryCodeDuringLoginAsync`）
- 账户管理：`Account.Manage.*` 系列页面（替代 `TwoFactorModifyEndpoints.cs` + `TwoFactorManage.razor`）
- 邮箱验证：`Account.Manage.Email`（替代 `EmailVerificationEndpoints.cs`）
- 密码修改：`Account.Manage.ChangePassword`（替代 `ChangePasswordDialog.razor`）

**保留的手写代码：**
- WebAuthn/Passkey 端点（`WebAuthnEndpoints.cs`）——通行密钥登录不可替代
- `CookieAuthenticationStateProvider` —— WASM 感知服务器 Cookie 的核心桥接
- `GET /api/auth/me` —— 认证状态同步端点
- `RecoveryCodeService` —— WebAuthn 注册后恢复码生成 + Admin 后台依赖

**关键架构决策：**
- 通行密钥验证成功后 `WebAuthnEndpoints.LoginCompleteAsync` 直接 `SignInAsync`，不经过 2FA 验证——通行密钥本身作为硬件令牌已满足第二因子要求
- Identity 页面使用 Bootstrap 默认样式，不与 MudBlazor 做样式桥接——双 UI 风格并存是已接受的权衡
- .NET 10 `GetTwoFactorAuthenticationUserAsync()` Bug (#66929)：在 `LoginWith2fa.cshtml.cs` / `LoginWithRecoveryCode.cshtml.cs` PageModel 中应用 workaround，待上游修复后移除
```

**§ Project Structure → Server 目录结构：**
- 新增 `Areas/Identity/Pages/Account/` 目录（Identity 脚手架 Razor Pages）
- 移除退役端点文件引用（`TwoFactorEndpoints.cs`、`TwoFactorModifyEndpoints.cs`、`EmailVerificationEndpoints.cs`）
- 新增 `Services/IdentityEmailSender.cs`（IEmailSender 适配器）
- 新增 `Utilities/AuthConstants.cs`

**Then** Architecture 文档准确反映迁移后的认证架构

### AC-5: UX Design 文档更新 — 登录/设置章节

**Given** `_bmad-output/planning-artifacts/ux-design-specification.md` 描述的登录流程基于 Blazor WASM SPA
**When** 新增子章节 "## Identity 认证页面（Bootstrap 风格）" 在 "## Key Interaction Flows" 之后：
```markdown
## Identity 认证页面（Bootstrap 风格）

**背景：** Epic 10-11 迁移后，登录/登出/2FA 验证/账户管理使用 ASP.NET Core Identity 脚手架 Razor Pages，默认 Bootstrap 样式。Blazor WASM 仅保留通行密钥登录。

### 双 UI 风格并存

| 页面类型 | UI 框架 | 路由 | 说明 |
|---------|---------|------|------|
| 登录/注册/2FA | Bootstrap (Identity 默认) | `/Identity/Account/*` | Server 端 Razor Pages |
| 账户管理 | Bootstrap (Identity 默认) | `/Identity/Account/Manage/*` | 2FA 设置、密码修改、邮箱管理 |
| 通行密钥登录 | MudBlazor | `/login` | Blazor WASM，仅保留通行密钥按钮 |
| 首页/录入/浏览/设置 | MudBlazor | `/` | Blazor WASM SPA 主体 |

### 登录流程（新）

```
未登录用户访问 Blazor WASM → Cookie 认证中间件拦截 → 302 重定向到 /Identity/Account/Login
  → 输入用户名/密码 → POST Login.cshtml
  → 无 2FA：签发 Cookie → 302 重定向到 / (Blazor WASM 首页)
  → 有 2FA：重定向到 /Identity/Account/LoginWith2fa → 输入 TOTP 验证码
    → 签发 Cookie → 重定向到 /
  → CookieAuthenticationStateProvider 调用 GET /api/auth/me → AppState.SetUser() → UI 更新为已登录
```

### 通行密钥登录流程（保留）

```
用户访问 /login (Blazor WASM) → 点击"使用通行密钥登录"按钮
  → 浏览器 WebAuthn API → 验证成功 → AppState.SetUser() → 导航到 /
```
```

**Then** UX Design 文档准确反映双 UI 风格并存的用户体验架构
> **Note:** 当前 UX Design 文档的 Entry Flow 仅描述物品录入流程，不涉及登录页面——无需修改 Entry Flow。Dev Notes 中建议在 Key Interaction Flows 开头新增 Authentication Flow 子章节，实施时以此为准。

### AC-6: CLAUDE.md 更新

**Given** `CLAUDE.md` 的项目架构描述未反映 Identity 脚手架迁移后的变化
**When** 更新以下章节：

**项目架构图：**
- `src/BoxWise.Server/` 下新增 `Areas/Identity/Pages/Account/`（Identity 脚手架 Razor Pages）
- `Endpoints/` 下移除退役文件引用
- 新增 `Services/IdentityEmailSender.cs`

**认证流程章节：**
- 步骤 1 更新：`CookieAuthenticationStateProvider.GetAuthenticationStateAsync()` 调用 `GET /api/auth/me` 检查 Identity Cookie（非旧版手写登录 Cookie）
- 步骤 2 更新：登录 → Identity `Login.cshtml`（Server 端 Razor Page）→ Cookie 签发

**.NET Framework 已知问题章节：**
- 修复已失效的 `TwoFactorEndpoints.cs` 引用（该文件已在 Story 11.3 退役）
- 将 workaround 描述改为指向当前代码位置：`LoginWith2fa.cshtml.cs` / `LoginWithRecoveryCode.cshtml.cs` PageModel 中的内联 workaround

**新增 Identity 脚手架修改清单引用**（如尚未在 CLAUDE.md 中存在）：
```markdown
- **脚手架修改清单：** `docs/identity-scaffold-modifications.md` — 所有对 `Areas/Identity/` 下文件的修改必须记录在此。**每次涉及脚手架代码的改动，在修改代码前先查阅此文件。**
```

**Then** `CLAUDE.md` 准确描述当前项目架构

### AC-7: 最终验证

**Given** 所有修改完成
**When** `dotnet build`
**Then** 0 错误 0 警告

**Given** `dotnet test`
**When** 执行所有测试
**Then** 全部通过（无回归——本 Story 仅改配置 + 文档，不涉及代码逻辑变更）

**Given** 所有变更文件（1 个代码 + 3 个文档）已更新
**When** 审阅每个文件
**Then** 内容准确、无拼写错误、与实际代码一致

## Tasks / Subtasks

- [x] Task 1: SameSite + SecurePolicy 环境判断 (AC: #1, #2, #2b)
  - [x] 1.1 在 `Program.cs` 中 `builder.Services.ConfigureApplicationCookie` 之前提取 `var env = builder.Environment;`
  - [x] 1.2 修改主 Cookie 的 `SameSite` 和 `SecurePolicy` 使用 `env.IsDevelopment()` 条件判断
  - [x] 1.3 修改 `TwoFactorUserIdScheme` Cookie 同步应用 `env.IsDevelopment()` 判断
  - [x] 1.4 修改 `AddSession` Cookie 同步应用 `env.IsDevelopment()` 判断（Session Cookie 在 WebAuthn 端点使用，同样需要跨端口）
  - [x] 1.5 同步更新第 74 行注释（`TwoFactorUserId`）和第 154 行注释（Session）：从 "跨端口需要 None" 改为 "开发环境跨端口需要 None"
  - [x] 1.6 `dotnet build` 验证 0 错误

- [x] Task 2: Lockout 参数验证 (AC: #3)
  - [x] 2.1 审查 `Program.cs` Identity 选项确认未显式配置 Lockout（使用默认值）
  - [x] 2.2 审查 `Areas/Identity/Pages/Account/Lockout.cshtml.cs` 确认 LockoutEnd 展示逻辑
  - [x] 2.3 在 Dev Notes 中记录结论（无需代码修改）

- [x] Task 3: Architecture 文档更新 (AC: #4)
  - [x] 3.1 更新 "Identity Integration: Cookie + Blazor WASM" 章节——反映 Identity 脚手架替代手写端点
  - [x] 3.2 新增 "Identity 脚手架混合模式迁移" 子章节
  - [x] 3.3 更新 Server 目录结构——新增 `Areas/Identity/`、移除退役文件、新增 `IdentityEmailSender.cs` / `AuthConstants.cs`
  - [x] 3.4 更新 API Route Structure 表格——移除退役端点路由

- [x] Task 4: UX Design 文档更新 (AC: #5)
  - [x] 4.1 新增 "Identity 认证页面（Bootstrap 风格）" 子章节——双 UI 风格并存、新登录流程、通行密钥保留
  - [x] 4.2 在 "Key Interaction Flows" 开头新增 "### Authentication Flow（2026-06 更新）" 子章节——描述新登录流程（见 Dev Notes）
  - [x] 4.3 在 "### Design System: MudBlazor" 末尾添加双 UI 风格并存注记

- [x] Task 5: CLAUDE.md 更新 (AC: #6)
  - [x] 5.1 更新项目架构图——新增 `Areas/Identity/`、移除退役文件
  - [x] 5.2 更新认证流程步骤——反映 Identity 脚手架登录流程
  - [x] 5.3 修复 ".NET Framework 已知问题" 章节中已失效的 `TwoFactorEndpoints.cs` 引用——指向 `LoginWith2fa.cshtml.cs` / `LoginWithRecoveryCode.cshtml.cs`
  - [x] 5.4 确认 Identity 脚手架修改清单引用已存在（Epic 10 回顾添加）

- [x] Task 6: 最终验证 (AC: #7)
  - [x] 6.1 `dotnet build` 0 错误 0 警告
  - [x] 6.2 `dotnet test` 全部通过
  - [x] 6.3 最终文档审阅——确认所有更新准确反映实际代码

## Dev Notes

### 架构上下文

**当前状态：** Story 11.1/11.2/11.3 已完成。Identity 脚手架页面替代了所有手写认证 UI 和端点。退役清单已执行（~1600 行代码删除）。本 Story 是 Identity 脚手架迁移的最后一个 Story——收尾 SameSite 安全配置和文档同步。

**本 Story 目标：** 将 SameSite/SecurePolicy 从硬编码开发值切换为环境感知配置；更新 Architecture/UX/CLAUDE.md 三个文档反映迁移后架构。

**关键约束：**
- 开发环境必须保持 `SameSite=None`（跨端口 5000↔5001 需要）
- 生产环境 `SameSite=Lax` + `Secure=Always`（Caddy 反向代理场景）
- 文档更新必须与代码实际状态一致——不虚构不存在的功能
- 本 Story 不删除任何代码文件（退役已在 Story 11.3 完成）

### Program.cs 修改详解

**修改位置 1：主 Cookie 配置（行 49-72）**

修改前：
```csharp
builder.Services.ConfigureApplicationCookie(options =>
{
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.None; // Blazor WASM 跨端口 fetch 需要 None
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always; // SameSite=None 必须配合 Secure
```

修改后：
```csharp
var env = builder.Environment;
builder.Services.ConfigureApplicationCookie(options =>
{
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = env.IsDevelopment() ? SameSiteMode.None : SameSiteMode.Lax;
    options.Cookie.SecurePolicy = env.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
```

**修改位置 2：TwoFactorUserIdScheme Cookie 配置（行 75-80）**

修改前：
```csharp
builder.Services.Configure<CookieAuthenticationOptions>(IdentityConstants.TwoFactorUserIdScheme, options =>
{
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.None;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
});
```

修改后：
```csharp
builder.Services.Configure<CookieAuthenticationOptions>(IdentityConstants.TwoFactorUserIdScheme, options =>
{
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = env.IsDevelopment() ? SameSiteMode.None : SameSiteMode.Lax;
    options.Cookie.SecurePolicy = env.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
});
```

**修改位置 3：Session Cookie 配置（行 147-155）**

修改前：
```csharp
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(5);
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.Cookie.SameSite = SameSiteMode.None; // Blazor WASM 跨端口需要 None（与 auth cookie 一致）
});
```

修改后：
```csharp
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(5);
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = env.IsDevelopment() ? SameSiteMode.None : SameSiteMode.Lax;
    options.Cookie.SecurePolicy = env.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
});
```

**注释同步更新：**
- 第 74 行 `// TwoFactorUserId Cookie — 也需要跨端口 SameSite=None（Blazor WASM:5001 → API:5000）` → 改为 `// TwoFactorUserId Cookie — 开发环境跨端口需要 SameSite=None`
- 第 154 行 `options.Cookie.SameSite = SameSiteMode.None; // Blazor WASM 跨端口需要 None（与 auth cookie 一致）` → 改为环境判断，注释同步更新

**`env` 变量作用域：** `builder.Environment` 是 `IWebHostEnvironment` 类型，需要在所有 `ConfigureApplicationCookie` / `Configure<CookieAuthenticationOptions>` 调用之前提取为局部变量，确保 lambda 能捕获。

**注意：** 当前 `Program.cs` 没有在顶部提前声明 `var env = builder.Environment;`，需要新增这一行。`env` 变量需放在 `var builder = WebApplication.CreateBuilder(args);` 之后、`builder.Services.ConfigureApplicationCookie(...)` 之前。

### Lockout 参数说明

ASP.NET Core Identity 默认 Lockout 参数（`IdentityOptions.Lockout`）：
- `MaxFailedAccessAttempts` = 5（默认）
- `DefaultLockoutTimeSpan` = TimeSpan.FromMinutes(5)（默认）
- `AllowedForNewUsers` = true（默认）

BoxWise `Program.cs` 中 `AddIdentity<AppUser, IdentityRole>(options => { ... })` 的 `options` 回调仅配置了 Password 相关参数（行 35-41），未配置 Lockout。因此使用 Identity 默认值。

`Areas/Identity/Pages/Account/Lockout.cshtml.cs` 的 `OnGet()` 方法为同步空方法（脚手架默认行为——仅展示静态锁定提示文本 "This account has been locked out, please try again later."）。默认参数与脚手架页面行为一致——无代码修改。

### Architecture 文档更新指南

**文件：** `_bmad-output/planning-artifacts/architecture.md`

**更新点 1：§ Authentication & Security → Identity Integration: Cookie + Blazor WASM（行 200-207）**

将旧的 "Server hosts `/login`, `/logout`, `/register` (admin-only) endpoints" 描述替换为 Identity 脚手架说明。原文：
> **Decision:** ASP.NET Core Identity with Cookie authentication. Server hosts `/login`, `/logout`, `/register` (admin-only) endpoints. Blazor WASM uses a custom `CookieAuthenticationStateProvider` that calls `/api/auth/me` on startup to retrieve the current authenticated user.

更新为：
> **Decision:** ASP.NET Core Identity with Cookie authentication. Login, logout, 2FA verification, and account management are handled by Identity scaffold Razor Pages (`Areas/Identity/Pages/Account/`). Blazor WASM uses a custom `CookieAuthenticationStateProvider` that calls `/api/auth/me` on startup to retrieve the current authenticated user. WebAuthn/Passkey login is retained in Blazor WASM (`Login.razor`).

**更新点 2：新增子章节（紧接 § Authentication & Security → Admin UI Expansion 之后）**

添加 "#### Identity 脚手架混合模式迁移 (2026-06-02)" 子章节。内容见 AC-4。

**更新点 3：§ Project Structure → Server 目录（行 430-462）**

在 `Endpoints/` 列表中移除退役端点：
- ~~AuthEndpoints.cs~~ 保留（仅 WebAuthn + GetCurrentUserAsync）
- ~~TwoFactorEndpoints.cs~~ 已退役
- ~~TwoFactorModifyEndpoints.cs~~ 已退役
- ~~EmailVerificationEndpoints.cs~~ 已退役

新增：
```
├── Areas/
│   └── Identity/
│       └── Pages/
│           └── Account/          ← Identity 脚手架 Razor Pages
├── Services/
│   ├── IdentityEmailSender.cs    ← IEmailSender 适配器
├── Utilities/
│   ├── AuthConstants.cs          ← 认证常量
```

**更新点 4：§ API Route Structure 表格（行 256-278）**

移除退役路由：
- ~~`/api/auth/login` POST~~ → Identity `Account.Login`
- ~~`/api/auth/logout` POST~~ → Identity `Account.Logout`
- ~~`/api/auth/2fa/*`~~ → Identity 管理页面

保留：
- `/api/auth/me` GET — WASM 认证同步
- `/api/auth/webauthn/*` — 通行密钥端点

### UX Design 文档更新指南

**文件：** `_bmad-output/planning-artifacts/ux-design-specification.md`

**更新点 1：新增子章节**

在 "## Key Interaction Flows" 章节之后（"### Entry Flow" 之前），插入 "## Identity 认证页面（Bootstrap 风格）" 章节。完整内容见 AC-5。

**更新点 2：新增 Authentication Flow 子章节**

在 "## Key Interaction Flows" 开头新增 "### Authentication Flow（2026-06 更新）" 子章节，描述新登录流程（见 AC-5 中的 content block）。保持 Entry Flow 和 Find Flow 不变。

**Note:** 当前 UX Design 文档的 Entry Flow 仅描述物品录入流程（拍照→填信息→选位置→保存），全文未出现 "login"、"登录" 等词。无需修改 Entry Flow。本 Story 的 UX 文档变更为纯粹的新增内容。

**更新点 3：Design System 注记**

在 "### Design System: MudBlazor" 章节末尾添加注记：
> **注（2026-06）：** Identity 认证页面（登录/2FA/账户管理）使用 ASP.NET Core Identity 默认 Bootstrap 样式，与 Blazor WASM 的 MudBlazor Material Design 风格并存。这是 Epic 10-11 迁移的有意设计决策——双 UI 风格在 ≤5 人家用场景可接受。

### CLAUDE.md 更新指南

**文件：** `CLAUDE.md`（项目根目录）

**更新点 1：项目架构图（"## 项目架构" 章节）**

在 `src/BoxWise.Server/` 下新增：
```
│   ├── Areas/
│   │   └── Identity/
│   │       └── Pages/
│   │           └── Account/      # Identity 脚手架 Razor Pages
```

在 `Endpoints/` 下移除：
- `TwoFactorEndpoints.cs`（已退役）
- `TwoFactorModifyEndpoints.cs`（已退役）
- `EmailVerificationEndpoints.cs`（已退役）

在 `Services/` 下新增：
- `IdentityEmailSender.cs`（IEmailSender 适配器）

**更新点 2：认证流程章节（"## 认证流程"）**

步骤 2 更新：
- 旧：登录 → `POST /api/auth/login` → Cookie 签发
- 新：登录 → Identity `Login.cshtml` POST → Cookie 签发 → HTTP 302 重定向到 `/`

步骤 3 保留：`CookieAuthenticationStateProvider` → `GET /api/auth/me` → `AppState.SetUser()`

新增步骤 5：通行密钥登录
- 用户访问 `/login` (Blazor WASM) → 点击"使用通行密钥登录" → WebAuthn API → 验证成功 → 导航到 `/`

**更新点 3：确认 Identity 脚手架修改清单引用**

检查 CLAUDE.md 中是否已存在 `identity-scaffold-modifications.md` 引用。如不存在则添加（Epic 10 回顾 Action Item #2 应该已添加）。

### 从之前 Story 学到的经验

**Story 11.3 (退役) 教训：**
- Grep 验证是关键——退役前必须确认无残留引用
- 本 Story 不涉及代码删除，但文档更新前同样需要 grep 当前代码状态确认描述准确

**Story 11.2 (Login.razor) 教训：**
- 开发环境跨端口链接不可达（Client 5001 → Server 5000 Identity 页面），已知限制
- 本 Story 的 SameSite 配置确保开发环境 Cookie 跨端口正常工作

**Story 11.1 (Settings.razor) 教训：**
- Code review 发现 6 项问题——本 Story 的文档更新涉及多个文件，review 时需确认所有更新一致性

**Epic 10 回顾教训：**
- "每次 Story 独立 commit"——本 Story commit message: `chore(identity): SameSite env-switching + architecture/UX/CLAUDE.md docs update`

### 本 Story 不改动的内容（边界明确）

| 不改动 | 原因 |
|--------|------|
| `Areas/Identity/Pages/Account/*` 任何文件 | 脚手架代码，非本 Story 目标 |
| `src/BoxWise.Client/` 下任何文件 | 前端代码已在 Story 11.1/11.2 完成 |
| `src/BoxWise.Server/Endpoints/` 下任何文件 | 退役已在 Story 11.3 完成 |
| `src/BoxWise.Server/Services/` 下任何文件 | 非本 Story 目标 |
| 测试文件 | 无测试变更——配置变更不改变业务逻辑 |
| `docs/identity-scaffold-modifications.md` | Epic 10 回顾已添加，本 Story 不涉及脚手架修改 |
| `Session` Cookie 之外的配置（IdleTimeout 等） | 仅修改 SameSite/SecurePolicy，其余 AddSession 配置不变 |
| `TwoFactorRememberMeScheme` Cookie | Program.cs 中未显式配置（使用框架默认 SameSite=Unspecified→Lax），2FA 流程在 Server 端同源，跨端口场景不涉及

### 文件变更总览

| 操作 | 文件 | 说明 |
|------|------|------|
| ✏️ MODIFY | `src/BoxWise.Server/Program.cs` | SameSite + SecurePolicy 环境判断（行 49-80） |
| ✏️ MODIFY | `_bmad-output/planning-artifacts/architecture.md` | 认证流程 + 目录结构更新 |
| ✏️ MODIFY | `_bmad-output/planning-artifacts/ux-design-specification.md` | 双 UI 风格 + 新登录流程 |
| ✏️ MODIFY | `CLAUDE.md` | 架构图 + 认证流程更新 |

### 测试策略

**无测试变更。** 本 Story 仅涉及：
1. Cookie SameSite/SecurePolicy 配置——ASP.NET Core 框架行为，单元测试不覆盖配置值
2. 文档更新——Markdown 文件内容变更，无需自动化测试

**验证方式：**
- `dotnet build` — 确认 Program.cs 修改编译通过
- `dotnet test` — 确认无回归（配置变更不影响业务逻辑）
- 文档审阅 — 人工确认更新准确

### References

- [Source: SPEC.md CAP-5/6/7] — 迁移需求和边界
- [Source: migration-phases.md Phase 6] — SameSite 策略 + 文档更新
- [Source: epics-identity-scaffold-migration.md Story 2.4] — 验收标准
- [Source: architecture.md §Authentication & Security] — 待更新的架构文档
- [Source: ux-design-specification.md] — 待更新的 UX 文档
- [Source: CLAUDE.md] — 待更新的项目文档
- [Source: Program.cs:49-80] — Cookie 配置修改点
- [Source: Story 11.1 Dev Agent Record] — Settings.razor 重构教训
- [Source: Story 11.2 Dev Agent Record] — Login.razor 精简教训
- [Source: Story 11.3 Dev Agent Record] — 退役清单教训
- [Source: Epic 10 Retrospective] — 13 scaffold fixes + deferred items
- [Source: identity-scaffold-modifications.md] — 脚手架修改记录
- [Source: specs/spec-identity-scaffold-migration/decommission-checklist.md] — 退役/保留清单

### Review Findings

- [x] [Review][Patch] Session Cookie 注释已更新 — Task 1.5 要求将第 154 行注释从 "跨端口需要 None" 改为 "开发环境跨端口需要 None" ✅ [src/BoxWise.Server/Program.cs:156]
- [x] [Review][Defer] 三处 Cookie 配置的 SameSite/SecurePolicy 三元表达式重复（DRY）— 可提取为 helper 方法，但三处配置服务于不同用途（主 Cookie / TwoFactorUserId / Session），当前代码清晰度可接受
- [x] [Review][Defer] TwoFactorRememberMeScheme 未显式配置 — 使用框架默认值（SameSite=Unspecified→Lax, SecurePolicy=SameAsRequest），Story 边界表已明确排除。预存问题，非本 diff 引入
- [x] [Review][Defer] UseForwardedHeaders 未配置 — Caddy 反向代理后 Request.IsHttps 可能不准确。预存问题，非本 diff 引入。如未来需要，在 `if (!env.IsDevelopment())` 块中添加

## Dev Agent Record

### Agent Model Used

Claude Code (deepseek-v4-pro)

### Debug Log References

- `dotnet build` — 0 错误 0 警告，一次通过
- `dotnet test` — 261 通过 0 失败（29 Client + 232 Server）

### Completion Notes List

- ✅ Task 1: `Program.cs` 三处 Cookie 配置（主/TwoFactorUserId/Session）全部改为 `env.IsDevelopment()` 条件判断，注释同步更新
- ✅ Task 2: Lockout 验证通过——`OnGet()` 同步空方法，Identity 默认参数与脚手架兼容，无代码修改
- ✅ Task 3: `architecture.md` 更新 4 处：Identity Integration 描述、新增脚手架迁移子章节、路由表退役端点移除、Server 目录结构更新
- ✅ Task 4: `ux-design-specification.md` 更新 3 处：新增 Identity 认证页面章节（双 UI 风格表）、Authentication Flow 子章节、Design System 注记
- ✅ Task 5: `CLAUDE.md` 更新 3 处：项目架构图（新增 Areas/Identity/等）、认证流程步骤、.NET Framework 已知问题过期引用修复；确认 `identity-scaffold-modifications.md` 引用已存在
- ✅ Task 6: `dotnet build` 0 错误 0 警告 + `dotnet test` 261/261 全部通过 + 文档审阅通过

### Change Log

- 2026-06-02: Implementation completed — 1 file modified (Program.cs), 3 docs updated (architecture/UX/CLAUDE.md)
- 2026-06-02: Code review — 3-layer parallel audit, 1 patch applied (Session Cookie comment restoration), 3 deferred, 12 dismissed

### File List

| 操作 | 文件 | 说明 |
|------|------|------|
| ✏️ MODIFY | `src/BoxWise.Server/Program.cs` | SameSite + SecurePolicy 环境判断（3 处 Cookie 配置） |
| ✏️ MODIFY | `_bmad-output/planning-artifacts/architecture.md` | 认证流程 + 脚手架迁移章节 + 路由表 + 目录结构 |
| ✏️ MODIFY | `_bmad-output/planning-artifacts/ux-design-specification.md` | 双 UI 风格 + Authentication Flow + Design System 注记 |
| ✏️ MODIFY | `CLAUDE.md` | 架构图 + 认证流程 + 过期引用修复 |
