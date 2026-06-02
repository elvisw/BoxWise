---
baseline_commit: 392229d
---

# Story 10.2: IEmailSender 适配器注册

Status: done

## Story

As a 用户，
I want Identity 管理页面的邮件发送功能正常工作，
so that 我可以收到邮箱确认邮件和 TOTP 设置验证邮件。

## Acceptance Criteria

### AC-1: IdentityEmailSender 实现 IEmailSender

**Given** `src/BoxWise.Server/Services/` 目录
**When** 创建 `IdentityEmailSender.cs`，实现 `Microsoft.AspNetCore.Identity.UI.Services.IEmailSender`
**And** 构造函数注入 `ISmtpConfigurationService`（接口，与现有 `EmailTwoFactorService` 约定一致）
**And** 构造函数注入 `ILogger<IdentityEmailSender>`
**Then** `SendEmailAsync(string email, string subject, string htmlMessage)` 方法签名正确，返回 `Task`

### AC-2: SMTP 邮件发送逻辑

**Given** `IdentityEmailSender.SendEmailAsync` 被调用
**When** 内部通过 `ISmtpConfigurationService.GetSnapshot()` 获取 `SmtpConfigDto`
**And** `config.Host` 为空或空白 → `_logger.LogWarning("SMTP 未配置，无法发送邮件到 {Email}", email)` → `return`（静默降级，不抛异常）
**And** `config.Host` 非空 → 使用 MimeKit 构建 `MimeMessage`（`From` + `To` + `Subject` + `TextPart("html")` Body），用 `MailKit.Net.Smtp.SmtpClient` 发送（30s 超时、TLS 1.2/1.3、端口自适应 `SslOnConnect`/`StartTlsWhenAvailable`、按需认证），发送后 `TryDisconnectAsync` 断开——**完整代码模式见 Dev Notes**
**Then** 邮件通过配置的 SMTP 服务器成功发送

### AC-3: 异常安全 + 日志

**Given** `SendEmailAsync` 执行过程中发生异常
**When** 异常类型为：
  - `MailKit.Security.AuthenticationException` → `_logger.LogError(ex, "SMTP 认证失败，无法发送邮件到 {Email}", email)`
  - `SmtpCommandException` → `_logger.LogError(ex, "SMTP 命令错误，无法发送邮件到 {Email}", email)`
  - `IOException or InvalidOperationException or SocketException` → `_logger.LogError(ex, "SMTP 连接失败，无法发送邮件到 {Email}", email)`
  - 其他 `Exception` → `_logger.LogError(ex, "发送邮件到 {Email} 时发生未知错误", email)`
**Then** 所有异常被 catch，不向上抛出（不抛 500）
**And** 每个 catch 块执行 `return`，等同于静默降级

### AC-4: DI 注册

**Given** `src/BoxWise.Server/Program.cs`
**When** 在 `builder.Services.AddScoped<EmailTwoFactorService>()`（L120）之后添加：
```csharp
builder.Services.AddTransient<IEmailSender, IdentityEmailSender>();
```
**Then** `dotnet build` 0 错误

### AC-5: 运行时验证

**Given** SMTP 已配置（通过 Admin 后台 SMTP 设置页面或 `smtp-config.json`）
**When** 用户访问 `/Identity/Account/Manage/Email`，输入新邮箱并点击"Send verification email"
**Then** 邮件成功发送到新邮箱，不抛 `Unable to resolve service for type 'IEmailSender'` 异常
**And** 状态消息显示"Verification email sent. Please check your email."

**Given** SMTP 未配置
**When** 用户访问 `/Identity/Account/Manage/Email` 并尝试发送验证邮件
**Then** 不报 500 错误，日志中输出 Warning 级别消息

### AC-6: 不委托给 EmailTwoFactorService

**Given** `IdentityEmailSender` 实现
**When** 审查代码
**Then** 不引用 `EmailTwoFactorService` 或调用 `SendVerificationEmailAsync` — API 签名不兼容（`SendVerificationEmailAsync` 需要 `code` + `userName` + `purpose` 参数，而 `IEmailSender` 接收 HTML 正文）
**And** 完全独立实现，仅共享 `ISmtpConfigurationService` 注入

## Tasks / Subtasks

- [x] Task 1: 创建 IdentityEmailSender (AC: #1, #2, #3, #6)
  - [x] 1.1 创建 `src/BoxWise.Server/Services/IdentityEmailSender.cs`
  - [x] 1.2 实现 `IEmailSender` 接口（`using Microsoft.AspNetCore.Identity.UI.Services`）
  - [x] 1.3 构造函数注入 `ISmtpConfigurationService` + `ILogger<IdentityEmailSender>`
  - [x] 1.4 实现 `SendEmailAsync`：SMTP 检查 → MimeKit 构建 → SmtpClient 发送 → 异常捕获
  - [x] 1.5 实现 `TryDisconnectAsync` 辅助方法（与 `EmailTwoFactorService` 一致）

- [x] Task 2: DI 注册 (AC: #4)
  - [x] 2.1 在 `Program.cs` 中添加 `builder.Services.AddTransient<IEmailSender, IdentityEmailSender>()`
  - [x] 2.2 `dotnet build` 验证 0 错误

- [x] Task 3: 测试验证 (AC: #5)
  - [x] 3.1 `dotnet test` — 全部通过（新增类不破坏现有测试）
  - [x] 3.2 （可选）手动验证：配置 SMTP → 访问 `/Identity/Account/Manage/Email` → 发送验证邮件

## Dev Notes

### 架构对齐

- **复刻现有模式：** `IdentityEmailSender` 在架构上等价于 `EmailTwoFactorService` 的邮件发送子集 — 相同的 `ISmtpConfigurationService` 注入、相同的 MimeKit + MailKit 构建方式、相同的异常处理和静默降级策略。**必须严格遵循 `EmailTwoFactorService.SendVerificationEmailAsync` 的 SMTP 发送代码路径**（L162-L219），仅替换邮件内容构建部分（`Subject` 和 `Body` 由调用方传入，而非内部生成）。

- **IEmailSender 接口定义：** `Microsoft.AspNetCore.Identity.UI.Services.IEmailSender` 只有一个方法：
  ```csharp
  Task SendEmailAsync(string email, string subject, string htmlMessage);
  ```
  - `email` — 收件人邮箱地址
  - `subject` — 邮件主题（由 Identity 页面传入，如 "Confirm your email"）
  - `htmlMessage` — HTML 格式的邮件正文（由 Identity 页面生成，如邮箱确认链接）

- **关键差异 vs EmailTwoFactorService：**
  | 维度 | EmailTwoFactorService | IdentityEmailSender |
  |------|----------------------|---------------------|
  | 接口 | 自定义方法 | `IEmailSender.SendEmailAsync` |
  | 邮件主题 | 内部生成（中文固定模板） | 调用方传入 |
  | 邮件正文 | 内部生成（纯文本验证码） | 调用方传入（HTML） |
  | 正文类型 | `TextPart("plain")` | `TextPart("html")` |
  | DI 生命周期 | Scoped | **Transient**（Identity 标准实践） |
  | 其他依赖 | `IDataProtectionProvider` | 无 |

- **为什么用 Transient 而非 Scoped：** Identity UI 框架期望 `IEmailSender` 为 Transient。这也是微软官方文档和脚手架模板的默认注册方式。`IdentityEmailSender` 无状态（不持有 DbContext 或其他 Scoped 资源），Transient 无副作用。

- **`TryDisconnectAsync` 必须独立实现：** 不要从 `EmailTwoFactorService` 提取共享方法或创建基类。这是两个职责不同的服务，未来 `EmailTwoFactorService` 可能随退役计划删除（Epic 11）。保持 `IdentityEmailSender` 完全自包含。

- **不修改任何 Identity 脚手架文件：** 本 Story 仅创建新 Service + 修改 `Program.cs` 一行。Identity 页面通过 DI 自动解析 `IEmailSender`，无需任何页面级别的代码修改。

### 本 Story 不改动的内容（边界明确）

| 不改动 | 原因 |
|--------|------|
| `EmailTwoFactorService` | 独立服务，仍在 2FA 登录流程中使用 |
| `TwoFactorService` | 与本 Story 无关 |
| 任何 Identity 脚手架 .cshtml 文件 | 通过 DI 自动解析 IEmailSender |
| `SmtpConfigurationService` | 只读消费者 |
| `ISmtpConfigurationService` 接口 | 不添加新方法 |
| 任何 Blazor WASM 文件 | 纯 Server 端变更 |
| 任何测试文件 | 纯新增类，无现有测试影响 |

### 文件变更清单

| 操作 | 文件 | 说明 |
|------|------|------|
| 🆕 NEW | `src/BoxWise.Server/Services/IdentityEmailSender.cs` | IEmailSender 实现，~100 行 |
| ✏️ MODIFY | `src/BoxWise.Server/Program.cs` | 添加 1 行 DI 注册 |

### 代码模式参考：EmailTwoFactorService SMTP 发送

以下是从 `EmailTwoFactorService.SendVerificationEmailAsync`（L162-L219）提取的 SMTP 发送模式，`IdentityEmailSender` 必须遵循：

```csharp
// 1. 获取配置
var config = _smtpConfig.GetSnapshot();

// 2. SMTP 未配置 → 静默降级
if (string.IsNullOrWhiteSpace(config.Host))
{
    _logger.LogWarning("SMTP 未配置，无法发送邮件到 {Email}", email);
    return;
}

// 3. 默认值回退
var fromAddress = string.IsNullOrWhiteSpace(config.FromAddress)
    ? "noreply@boxwise.app"
    : config.FromAddress;
var fromName = string.IsNullOrWhiteSpace(config.FromName)
    ? "BoxWise"
    : config.FromName;

// 4. MimeKit 构建邮件
var message = new MimeMessage();
message.From.Add(new MailboxAddress(fromName, fromAddress));
message.To.Add(new MailboxAddress(email, email));
message.Subject = subject;
message.Body = new TextPart("html") { Text = htmlMessage };

// 5. MailKit SmtpClient 发送
using var client = new SmtpClient();
client.Timeout = 30000;
client.SslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13;
await client.ConnectAsync(config.Host, config.Port,
    config.Port == 465 ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTlsWhenAvailable);
if (!string.IsNullOrWhiteSpace(config.Username))
    await client.AuthenticateAsync(config.Username, config.Password ?? "");
await client.SendAsync(message);
await TryDisconnectAsync(client);
```

### IdentityEmailSender 完整接口契约

```csharp
using Microsoft.AspNetCore.Identity.UI.Services;

namespace BoxWise.Server.Services;

public class IdentityEmailSender : IEmailSender
{
    private readonly ISmtpConfigurationService _smtpConfig;
    private readonly ILogger<IdentityEmailSender> _logger;

    public IdentityEmailSender(
        ISmtpConfigurationService smtpConfig,
        ILogger<IdentityEmailSender> logger)
    {
        _smtpConfig = smtpConfig;
        _logger = logger;
    }

    public async Task SendEmailAsync(string email, string subject, string htmlMessage)
    {
        // TODO: 实现（参考上方模式）
    }

    private static async Task TryDisconnectAsync(SmtpClient client)
    {
        // TODO: 实现（与 EmailTwoFactorService.TryDisconnectAsync 完全一致）
    }
}
```

### using 语句清单

```csharp
using System.IO;
using System.Net.Sockets;
using System.Security.Authentication;
using BoxWise.Shared.Dtos;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.AspNetCore.Identity.UI.Services;
using MimeKit;
```

### Program.cs 修改位置

在 `builder.Services.AddScoped<EmailTwoFactorService>()` 之后添加（当前 L120；如行号偏移，搜索 `EmailTwoFactorService` 定位插入点）：

```csharp
builder.Services.AddTransient<IEmailSender, IdentityEmailSender>();
```

### 测试策略

- **本 Story 不创建单元测试文件** — `IdentityEmailSender` 的 SMTP 发送逻辑与 `EmailTwoFactorService.SendVerificationEmailAsync` 完全一致（该逻辑已在 `EmailTwoFactorServiceTests` 中通过 mock `ISmtpConfigurationService` 覆盖）。
- **编译验证：** `dotnet build` 0 错误 0 警告 — 验证 `IEmailSender` 类型可从 `Microsoft.AspNetCore.Identity.UI` NuGet 包正确解析。
- **运行时验证：** SMTP 已配置时访问 `/Identity/Account/Manage/Email` 并发送验证邮件。
- **测试回归：** `dotnet test` 全部通过 — 新增类不破坏任何现有测试。

### 已知风险

1. **`IEmailSender` 命名空间冲突：** `Microsoft.AspNetCore.Identity.UI.Services.IEmailSender` 可能与自定义接口混淆。确保 `using` 正确：`using Microsoft.AspNetCore.Identity.UI.Services;`

2. **`TextPart("html")` vs `TextPart("plain")`：** `EmailTwoFactorService` 使用纯文本正文，但 Identity 页面生成的邮件内容是 HTML（邮箱确认链接、TOTP 设置说明等）。必须使用 `TextPart("html")`，否则用户会看到原始 HTML 标签。

3. **Transient 生命周期不持有状态：** `IdentityEmailSender` 是无状态服务，不缓存任何数据。每次调用 `SendEmailAsync` 都从 `ISmtpConfigurationService.GetSnapshot()` 获取最新配置。

### References

- [Source: SPEC.md CAP-2] — IEmailSender 适配器注册需求
- [Source: epics-identity-scaffold-migration.md Story 1.2] — 验收标准
- [Source: EmailTwoFactorService.cs:140-219] — SMTP 发送模式参考
- [Source: ISmtpConfigurationService.cs] — 注入接口
- [Source: SmtpConfigDto.cs] — 配置数据结构
- [Source: Program.cs:118-120] — DI 注册位置
- [Source: Email.cshtml.cs:23-24] — Identity 页面 IEmailSender 消费方式
- [Source: CLAUDE.md §项目架构] — Server 项目结构

## Dev Agent Record

### Agent Model Used

Claude Code (deepseek-v4-pro)

### Implementation Notes

**实际执行差异（vs Story 规格）：**

1. **`IEmailSender` 泛型冲突：** `Program.cs` 中直接使用 `IEmailSender` 导致 CS0305/CS0311 错误——C# 编译器将 `IEmailSender` 解析为 `Microsoft.AspNetCore.Identity.IEmailSender<TUser>`（泛型版本），而非 `Microsoft.AspNetCore.Identity.UI.Services.IEmailSender`（非泛型版本）。`Program.cs` 的隐式 using 中包含 `Microsoft.AspNetCore.Identity` 命名空间。**修复：** 使用完全限定类型名 `Microsoft.AspNetCore.Identity.UI.Services.IEmailSender`。

2. **`AuthenticationException` 歧义：** `using System.Security.Authentication;`（提供 `SslProtocols`）和 `using MailKit.Security;`（提供 `SecureSocketOptions`）都定义了 `AuthenticationException`。与 `EmailTwoFactorService` 一致，使用 `MailKit.Security.AuthenticationException` 完整命名空间前缀。

3. **不必要的 using：** `System.IO`（已通过隐式全局 using 引入）和 `BoxWise.Shared.Dtos`（`SmtpConfigDto` 仅通过 `var` 使用）被移除，避免 WarningsAsErrors 下的 CS8019/CS8933 编译错误。

### Debug Log References

- CS0305/CS0311: `IEmailSender` 泛型/非泛型歧义 → 使用完全限定名
- CS0104: `AuthenticationException` 歧义 → 使用 `MailKit.Security.AuthenticationException`
- CS8019/CS8933: 不必要的 using → 移除 `System.IO` 和 `BoxWise.Shared.Dtos`

### Completion Notes List

- ✅ `IdentityEmailSender.cs` 创建，实现 `IEmailSender` 接口（72 行）
- ✅ `Program.cs` 添加 1 行 DI 注册
- ✅ `dotnet build` 0 错误 0 警告
- ✅ `dotnet test` 308 通过 0 失败（264 Server + 44 Client）
- ✅ AC-1~AC-6 全部满足
- ✅ 异常处理与 `EmailTwoFactorService` 保持一致（4 种异常类型 + 静默降级）

### Change Log

- 2026-06-01: Story created + validated (Create Story → Validate)
- 2026-06-01: Implementation completed (Dev Story) — 2 files, +75 lines

### File List

| 操作 | 文件 | 说明 |
|------|------|------|
| 🆕 NEW | `src/BoxWise.Server/Services/IdentityEmailSender.cs` | IEmailSender 实现，72 行 |
| ✏️ MODIFY | `src/BoxWise.Server/Program.cs` | 添加 1 行：`AddTransient<IEmailSender, IdentityEmailSender>()` |

### Review Findings

- [x] [Review][Defer] 参数 null 校验缺失 — `SendEmailAsync` 的 `email`/`subject`/`htmlMessage` 未做 null 检查。C# nullable 已启用且 Identity UI 始终传入非 null 值，实际风险极低。添加 null 检查将破坏 IEmailSender 接口契约（参数为非 null 引用类型）。
- [x] [Review][Defer] 无 CancellationToken 支持 — `IEmailSender` 接口本身不暴露 `CancellationToken`，30s 超时的 SMTP 操作无法中途取消。非本 Story 能解决的接口限制。
- [x] [Review][Defer] SMTP Port 未校验 — `config.Port` 为 0/负数/>65535 时会产生晦涩的 SMTP 异常。端口值由 Admin SMTP Settings 页面校验，非此 Service 职责范围。
