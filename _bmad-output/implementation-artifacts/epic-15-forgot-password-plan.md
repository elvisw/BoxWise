# 忘记密码功能实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 实现用户自助忘记密码重置流程（用户名输入 → 邮件发送重置链接 → 设新密码）

**Architecture:** 新增 4 个 Server 端 Identity Razor Pages（ForgotPassword / ForgotPasswordConfirmation / ResetPassword / ResetPasswordConfirmation），修改 Login.cshtml 添加入口链接，修改 Program.cs 配置 token 有效期和速率限制。基于 ASP.NET Core Identity 脚手架模板，适配 BoxWise 的用户名登录体系。

**Tech Stack:** ASP.NET Core 10 Identity Razor Pages, MailKit, Bootstrap 5

---

## 任务 1: Program.cs — Token 有效期 + ForgotPassword 速率限制

**Files:**
- Modify: `src/BoxWise.Server/Program.cs`
- Modify: `src/BoxWise.Server/appsettings.json`

- [ ] **Step 1: 添加 TokenLifespan 配置**

在 `AddDefaultTokenProviders()` 或附近插入：

```csharp
builder.Services.Configure<DataProtectionTokenProviderOptions>(options =>
    options.TokenLifespan = TimeSpan.FromHours(1));
```

- [ ] **Step 2: 添加 ForgotPassword 速率限制策略**

在 `AddRateLimiter` 的 `options` 块中，`passkey-login` 策略之后添加：

```csharp
    // 忘记密码端点 - 按 IP（防邮件轰炸和用户名枚举）
    options.AddFixedWindowLimiter(policyName: "forgot-password", config =>
    {
        config.PermitLimit = builder.Configuration.GetValue("RateLimit:ForgotPasswordPermitLimit", 1);
        config.Window = TimeSpan.FromSeconds(
            builder.Configuration.GetValue("RateLimit:ForgotPasswordWindowSeconds", 60));
        config.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        config.QueueLimit = 0;
    });
```

- [ ] **Step 3: 在 appsettings.json 中添加 ForgotPassword 速率限制配置**

编辑 `src/BoxWise.Server/appsettings.json`，在 `RateLimit` 块末尾（`TwoFactorRecoveryWindowMinutes` 之后）添加：

```json
    "ForgotPasswordPermitLimit": 1,
    "ForgotPasswordWindowSeconds": 60
```

> 注意：单位是 `Seconds`（非 `Minutes`），与 60 秒窗口匹配。

- [ ] **Step 4: 构建验证**

```bash
dotnet build src/BoxWise.Server
```

Expected: 构建成功，无编译错误。

- [ ] **Step 5: 提交**

```bash
git add src/BoxWise.Server/Program.cs src/BoxWise.Server/appsettings.json
git commit -m "feat: 配置重置令牌1h有效期 + ForgotPassword速率限制"
```

---

## 任务 2: ForgotPassword 页面（表单 + 后端）

> **前置依赖：** 任务 1 中的 `forgot-password` 速率限制策略。跳过任务 1 直接运行会抛出 `InvalidOperationException: No policy 'forgot-password' found`。

**Files:**
- Create: `src/BoxWise.Server/Areas/Identity/Pages/Account/ForgotPassword.cshtml`
- Create: `src/BoxWise.Server/Areas/Identity/Pages/Account/ForgotPassword.cshtml.cs`

- [ ] **Step 1: 创建 ForgotPassword.cshtml.cs**

```csharp
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
#nullable disable

using System;
using System.ComponentModel.DataAnnotations;
using System.Text;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using BoxWise.Server.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.WebUtilities;

namespace BoxWise.Server.Areas.Identity.Pages.Account
{
    [AllowAnonymous]
    public class ForgotPasswordModel : PageModel
    {
        private readonly UserManager<AppUser> _userManager;
        private readonly IEmailSender _emailSender;

        public ForgotPasswordModel(UserManager<AppUser> userManager, IEmailSender emailSender)
        {
            _userManager = userManager;
            _emailSender = emailSender;
        }

        [BindProperty]
        public InputModel Input { get; set; }

        public class InputModel
        {
            [Required(ErrorMessage = "请输入用户名")]
            public string Username { get; set; }
        }

        [EnableRateLimiting("forgot-password")]
        public async Task<IActionResult> OnPostAsync()
        {
            if (ModelState.IsValid)
            {
                var user = await _userManager.FindByNameAsync(Input.Username);
                if (user == null || string.IsNullOrWhiteSpace(user.Email) || !user.EmailConfirmed)
                {
                    return RedirectToPage("./ForgotPasswordConfirmation");
                }

                var code = await _userManager.GeneratePasswordResetTokenAsync(user);
                code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(code));
                var callbackUrl = Url.Page(
                    "/Account/ResetPassword",
                    pageHandler: null,
                    values: new { area = "Identity", userId = user.Id, code },
                    protocol: Request.Scheme);

                await _emailSender.SendEmailAsync(
                    user.Email,
                    "BoxWise - 密码重置",
                    $@"<p>您好，</p>
<p>我们收到了您的 BoxWise 账户密码重置请求。</p>
<p>请点击以下链接在 1 小时内重置密码：</p>
<p><a href='{HtmlEncoder.Default.Encode(callbackUrl)}'>重置密码</a></p>
<p>如果您没有请求重置密码，请忽略此邮件。</p>");

                return RedirectToPage("./ForgotPasswordConfirmation");
            }

            return Page();
        }
    }
}
```

- [ ] **Step 2: 创建 ForgotPassword.cshtml**

```html
@page
@model ForgotPasswordModel

@{
    ViewData["Title"] = "忘记密码？";
}

<h1>@ViewData["Title"]</h1>
<h2>请输入您的用户名。</h2>
<hr />
<div class="row">
    <div class="col-md-4">
        <form method="post">
            <div asp-validation-summary="ModelOnly" class="text-danger" role="alert"></div>
            <div class="form-floating mb-3">
                <input asp-for="Input.Username" class="form-control" autocomplete="username" aria-required="true" placeholder="请输入用户名" />
                <label asp-for="Input.Username" class="form-label">用户名</label>
                <span asp-validation-for="Input.Username" class="text-danger"></span>
            </div>
            <button type="submit" class="w-100 btn btn-lg btn-primary">发送重置邮件</button>
        </form>
        <div class="mt-3">
            <a asp-page="./Login">返回登录</a>
        </div>
    </div>
</div>

@section Scripts {
    <partial name="_ValidationScriptsPartial" />
}
```

- [ ] **Step 3: 构建验证**

```bash
dotnet build src/BoxWise.Server
```

Expected: 构建成功，无编译错误。

- [ ] **Step 4: 提交**

```bash
git add src/BoxWise.Server/Areas/Identity/Pages/Account/ForgotPassword.cshtml src/BoxWise.Server/Areas/Identity/Pages/Account/ForgotPassword.cshtml.cs
git commit -m "feat: 新增 ForgotPassword Identity 页面"
```

---

## 任务 3: ForgotPasswordConfirmation 页面（纯提示）

**Files:**
- Create: `src/BoxWise.Server/Areas/Identity/Pages/Account/ForgotPasswordConfirmation.cshtml`
- Create: `src/BoxWise.Server/Areas/Identity/Pages/Account/ForgotPasswordConfirmation.cshtml.cs`

- [ ] **Step 1: 创建 ForgotPasswordConfirmation.cshtml.cs**

```csharp
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
#nullable disable

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace BoxWise.Server.Areas.Identity.Pages.Account
{
    [AllowAnonymous]
    public class ForgotPasswordConfirmationModel : PageModel
    {
    }
}
```

- [ ] **Step 2: 创建 ForgotPasswordConfirmation.cshtml**

```html
@page
@model ForgotPasswordConfirmationModel

@{
    ViewData["Title"] = "忘记密码确认";
}

<h1>@ViewData["Title"]</h1>
<p>
    请检查您的邮箱，点击邮件中的链接重置密码。如未收到邮件，请检查垃圾邮件文件夹。
</p>
```

- [ ] **Step 3: 构建验证**

```bash
dotnet build src/BoxWise.Server
```

Expected: 构建成功，无编译错误。

- [ ] **Step 4: 提交**

```bash
git add src/BoxWise.Server/Areas/Identity/Pages/Account/ForgotPasswordConfirmation.cshtml src/BoxWise.Server/Areas/Identity/Pages/Account/ForgotPasswordConfirmation.cshtml.cs
git commit -m "feat: 新增 ForgotPasswordConfirmation Identity 页面"
```

---

## 任务 4: Login.cshtml — 添加"忘记密码？"链接

**Files:**
- Modify: `src/BoxWise.Server/Areas/Identity/Pages/Account/Login.cshtml`

- [ ] **Step 1: 在通行密钥链接下方添加忘记密码链接**

编辑 `Login.cshtml`，在 `<a href="/login">使用通行密钥登录</a>` 所在 `</div>` 之后添加：

```html
<div class="mt-2">
    <a asp-page="./ForgotPassword">忘记密码？</a>
</div>
```

修改后该区域应为：

```html
            <div class="mt-3">
                <a href="/login">使用通行密钥登录</a>
            </div>
            <div class="mt-2">
                <a asp-page="./ForgotPassword">忘记密码？</a>
            </div>
```

- [ ] **Step 2: 构建验证**

```bash
dotnet build src/BoxWise.Server
```

Expected: 构建成功，无编译错误。

- [ ] **Step 3: 提交**

```bash
git add src/BoxWise.Server/Areas/Identity/Pages/Account/Login.cshtml
git commit -m "feat: Login 页面添加忘记密码链接"
```

---

## 任务 5: ResetPassword 页面（表单 + 后端）

**Files:**
- Create: `src/BoxWise.Server/Areas/Identity/Pages/Account/ResetPassword.cshtml`
- Create: `src/BoxWise.Server/Areas/Identity/Pages/Account/ResetPassword.cshtml.cs`

- [ ] **Step 1: 创建 ResetPassword.cshtml.cs**

```csharp
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
#nullable disable

using System;
using System.ComponentModel.DataAnnotations;
using System.Text;
using System.Threading.Tasks;
using BoxWise.Server.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;

namespace BoxWise.Server.Areas.Identity.Pages.Account
{
    [AllowAnonymous]
    public class ResetPasswordModel : PageModel
    {
        private readonly UserManager<AppUser> _userManager;
        private readonly IEmailSender _emailSender;

        public ResetPasswordModel(UserManager<AppUser> userManager, IEmailSender emailSender)
        {
            _userManager = userManager;
            _emailSender = emailSender;
        }

        [BindProperty]
        public InputModel Input { get; set; }

        public string MaskedEmail { get; set; }

        public class InputModel
        {
            [Required]
            public string UserId { get; set; }

            [Required(ErrorMessage = "请输入新密码")]
            [StringLength(100, ErrorMessage = "密码长度至少为 {2} 个字符。", MinimumLength = 8)]
            [DataType(DataType.Password)]
            [Display(Name = "新密码")]
            public string Password { get; set; }

            [DataType(DataType.Password)]
            [Display(Name = "确认新密码")]
            [Compare("Password", ErrorMessage = "密码和确认密码不匹配。")]
            public string ConfirmPassword { get; set; }

            [Required]
            public string Code { get; set; }
        }

        public async Task<IActionResult> OnGetAsync(string userId, string code)
        {
            if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(code))
            {
                return BadRequest("重置密码需要提供用户标识和验证码。");
            }

            var user = await _userManager.FindByIdAsync(userId);
            if (user == null)
            {
                return RedirectToPage("./ResetPasswordConfirmation");
            }

            MaskedEmail = MaskEmail(user.Email ?? "");

            Input = new InputModel
            {
                UserId = userId,
                Code = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(code))
            };
            return Page();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            var user = await _userManager.FindByIdAsync(Input.UserId);
            if (user == null)
            {
                return RedirectToPage("./ResetPasswordConfirmation");
            }

            MaskedEmail = MaskEmail(user.Email ?? "");

            if (!ModelState.IsValid)
            {
                return Page();
            }

            var result = await _userManager.ResetPasswordAsync(user, Input.Code, Input.Password);
            if (result.Succeeded)
            {
                await _emailSender.SendEmailAsync(
                    user.Email,
                    "BoxWise - 密码已重置",
                    "<p>您好，</p><p>您的 BoxWise 账户密码已被重置。如果不是您本人操作，请立即联系管理员。</p>");

                return RedirectToPage("./ResetPasswordConfirmation");
            }

            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(string.Empty, error.Description);
            }
            return Page();
        }

        private static string MaskEmail(string email)
        {
            if (string.IsNullOrEmpty(email) || !email.Contains('@'))
                return email;

            var parts = email.Split('@');
            var name = parts[0];
            var domain = parts[1];

            if (name.Length <= 2)
                return $"{name[0]}***@{domain}";

            return $"{name[0]}***{name[^1]}@{domain}";
        }
    }
}
```

- [ ] **Step 2: 创建 ResetPassword.cshtml**

```html
@page
@model ResetPasswordModel

@{
    ViewData["Title"] = "重置密码";
}

<h1>@ViewData["Title"]</h1>
<h2>重置您的密码。</h2>
<hr />
<div class="row">
    <div class="col-md-4">
        <form method="post">
            <div asp-validation-summary="ModelOnly" class="text-danger" role="alert"></div>
            <input asp-for="Input.UserId" type="hidden" />
            <input asp-for="Input.Code" type="hidden" />
            @if (!string.IsNullOrEmpty(Model.MaskedEmail))
            {
                <p>正在为 <strong>@Model.MaskedEmail</strong> 重置密码</p>
            }
            <input type="text" autocomplete="username" value="@Model.MaskedEmail" style="display:none" aria-hidden="true" />
            <div class="form-floating mb-3">
                <input asp-for="Input.Password" class="form-control" autocomplete="new-password" aria-required="true" placeholder="请输入新密码" />
                <label asp-for="Input.Password" class="form-label">新密码</label>
                <span asp-validation-for="Input.Password" class="text-danger"></span>
            </div>
            <div class="form-floating mb-3">
                <input asp-for="Input.ConfirmPassword" class="form-control" autocomplete="new-password" aria-required="true" placeholder="请确认新密码" />
                <label asp-for="Input.ConfirmPassword" class="form-label">确认新密码</label>
                <span asp-validation-for="Input.ConfirmPassword" class="text-danger"></span>
            </div>
            <button type="submit" class="w-100 btn btn-lg btn-primary">重置密码</button>
        </form>
    </div>
</div>

@section Scripts {
    <partial name="_ValidationScriptsPartial" />
}
```

- [ ] **Step 3: 构建验证**

```bash
dotnet build src/BoxWise.Server
```

Expected: 构建成功，无编译错误。

- [ ] **Step 4: 提交**

```bash
git add src/BoxWise.Server/Areas/Identity/Pages/Account/ResetPassword.cshtml src/BoxWise.Server/Areas/Identity/Pages/Account/ResetPassword.cshtml.cs
git commit -m "feat: 新增 ResetPassword Identity 页面"
```

---

## 任务 6: ResetPasswordConfirmation 页面（纯提示）

**Files:**
- Create: `src/BoxWise.Server/Areas/Identity/Pages/Account/ResetPasswordConfirmation.cshtml`
- Create: `src/BoxWise.Server/Areas/Identity/Pages/Account/ResetPasswordConfirmation.cshtml.cs`

- [ ] **Step 1: 创建 ResetPasswordConfirmation.cshtml.cs**

```csharp
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
#nullable disable

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace BoxWise.Server.Areas.Identity.Pages.Account
{
    [AllowAnonymous]
    public class ResetPasswordConfirmationModel : PageModel
    {
    }
}
```

- [ ] **Step 2: 创建 ResetPasswordConfirmation.cshtml**

```html
@page
@model ResetPasswordConfirmationModel

@{
    ViewData["Title"] = "密码重置完成";
}

<h1>@ViewData["Title"]</h1>
<p>
    密码已重置。请 <a asp-page="./Login">点击此处登录</a>。
</p>
```

- [ ] **Step 3: 构建验证**

```bash
dotnet build src/BoxWise.Server
```

Expected: 构建成功，无编译错误。

- [ ] **Step 4: 提交**

```bash
git add src/BoxWise.Server/Areas/Identity/Pages/Account/ResetPasswordConfirmation.cshtml src/BoxWise.Server/Areas/Identity/Pages/Account/ResetPasswordConfirmation.cshtml.cs
git commit -m "feat: 新增 ResetPasswordConfirmation Identity 页面"
```

---

## 任务 7: 更新脚手架修改文档

**Files:**
- Modify: `docs/identity-scaffold-modifications.md`

- [ ] **Step 1: 更新排除列表**

将第 49 行的排除项移除（因为现在已实现）：

编辑 `docs/identity-scaffold-modifications.md`，将第 49 行：

```
- `Account.ForgotPassword` / `Account.ResetPassword` — v1 未实现
```

改为：

```
- 无（ForgotPassword 和 ResetPassword 已在 Epic 15 实现）
```

- [ ] **Step 2: 新增修改记录**

在修改记录表格末尾（#32 之后）添加 4 条记录：

```markdown
| 33 | `Account/ForgotPassword.cshtml.cs` | 基于脚手架模板：`FindByEmailAsync` → `FindByNameAsync`，`[AllowAnonymous]` + `[EnableRateLimiting("forgot-password")]`，`EmailConfirmed` 检查，UTF8 → Base64UrlEncode 令牌编码，`protocol: Request.Scheme` 绝对 URL | BoxWise 用户名登录体系；防枚举和邮件轰炸 | Epic 15 | 重新适配 |
| 34 | `Account/ForgotPassword.cshtml` | Email → Username 输入框，中文化，`form-floating` 样式，"返回登录"链接 | 与 Login.cshtml 风格一致 | Epic 15 | 重新适配 |
| 35 | `Account/ForgotPasswordConfirmation.cshtml` | 新建：`[AllowAnonymous]`，中文化提示（含垃圾邮件文件夹提示） | 脚手架排除列表中无此页面，全新创建 | Epic 15 | 重新适配 |
| 35a | `Program.cs` | `DataProtectionTokenProviderOptions.TokenLifespan = 1h` + `AddFixedWindowLimiter("forgot-password")` 速率限制策略 | 默认 TokenLifespan 为 24h；无默认 ForgotPassword 限流策略 | Epic 15 | 保留 |
| 36 | `Account/ResetPassword.cshtml.cs` | 基于脚手架模板：`FindByEmailAsync` → `FindByIdAsync`，`OnGet` 参数 `userId` + `code`，Base64UrlDecode → UTF8 令牌解码，脱敏邮箱显示，重置成功后发送安全通知邮件 | BoxWise 用户名/ID 体系；URL 传递 userId 替代邮箱 | Epic 15 | 重新适配 |
| 37 | `Account/ResetPassword.cshtml` | Email → userId（hidden），脱敏邮箱提示，`autocomplete="username"` 隐藏框，中文化 | 配合 PasswordManager 保存新密码 | Epic 15 | 重新适配 |
| 38 | `Account/ResetPasswordConfirmation.cshtml` | 新建：`[AllowAnonymous]`，中文化，"点击此处登录"链接 | 脚手架模板仅英文 | Epic 15 | 重新适配 |
| 39 | `Account/Login.cshtml` | 底部添加"忘记密码？"链接 | 用户入口 | Epic 15 | 保留 |
```

- [ ] **Step 3: 提交**

```bash
git add docs/identity-scaffold-modifications.md
git commit -m "docs: 更新脚手架修改记录（Epic 15 忘记密码）"
```

---

## 任务 8: 构建与最终验证

**Files:** 无（仅验证）

- [ ] **Step 1: 全量构建**

```bash
dotnet build
```

Expected: 构建成功，无编译错误、无警告。

- [ ] **Step 2: 运行所有测试**

```bash
dotnet test BoxWise.slnx
```

Expected: 所有测试通过。

- [ ] **Step 3: 手动验证清单**

启动 Server：

```bash
cd src/BoxWise.Server && dotnet run
```

访问以下页面确认功能正常：
- `https://localhost:5000/Identity/Account/Login` — 可以看到"忘记密码？"链接
- `https://localhost:5000/Identity/Account/ForgotPassword` — 可以输入用户名提交
- `https://localhost:5000/Identity/Account/ForgotPasswordConfirmation` — 显示确认提示
- `https://localhost:5000/Identity/Account/ResetPasswordConfirmation` — 显示完成提示
