---
title: Sprint Change Proposal — 技术债务清偿（第三轮）
date: 2026-06-07
status: approved
trigger: deferred-work.md 中 6 条未清偿技术债务（GitHub Issue #27-#32）
scope: Minor
reviewed_by: Adversarial Review + Edge Case Hunter (2 rounds, 4 sub-agents)
review_date: 2026-06-07
review_findings: Round 1 — 1 Critical + 3 Warnings (all fixed). Round 2 — 2 Medium + 5 Low (3 actionable fixed, rest accepted as-is)
---

# Sprint Change Proposal: 技术债务清偿（第三轮）

## 1. Issue Summary

**触发源:** `spec-tech-debt-cleanup-2` (2026-06-07) 的 code review 暴露了 6 条预存技术债务，各自对应一个 GitHub Issue。这些是已有功能中的缺陷/不完善之处，不影响 PRD MVP。

**影响范围:** 全部在已完成代码中，无新增功能。4 条需代码/配置变更，2 条仅关闭 Issue。

## 2. Impact Analysis

### Epic Impact

- **所有 13 个 Epic 已完成** — 无需修改现有 Epic
- **新增 Epic 14: 技术债务清偿（第三轮）** — 4 条 active Story + 2 条 close-only

### Artifact Conflicts

| 制品 | 影响 | 说明 |
|------|------|------|
| PRD | 无 | 代码级修复，不涉及功能需求 |
| Architecture | 无 | 无架构变更 |
| UX Design | 无 | 无 UI 变更 |
| README | 微小 | #31 grep 命令精确化 |
| CI (release.yml) | 微小 | #28 tar/ZIP 验证步骤 |

## 3. Recommended Approach

**选择: Option 1 — Direct Adjustment（直接调整）**

- **Effort:** Low（4 条 active 变更，预计 30 分钟）
- **Risk:** Low（所有变更在 Admin 后台 + CI + README，不影响用户端功能）
- **Rationale:** 无需回滚、无需修改 PRD。直接在现有代码基础上做靶向修复，新增 Epic 14 追踪。

## 4. Detailed Change Proposals

### Story 14.1: #29 ResetTwoFactor 管理员自保护

**类型:** Bug Fix
**文件:** `src/BoxWise.Server/Pages/Admin/ResetTwoFactor.cshtml.cs`

在 `OnPostAsync` 中添加 `id == currentUserId` 守卫，与 `Index.cshtml.cs` 中 `OnPostDeleteAsync` / `OnPostToggleRoleAsync` 现有保护模式一致。采用 `[TempData] StatusMessage + RedirectToPage()` 模式以消除错误处理风格分歧。

```diff
     private readonly ILogger<ResetTwoFactorModel> _logger;

+    [TempData]
+    public string? StatusMessage { get; set; }
+
     public string TargetUsername { get; set; } = "";
-    public string? ErrorMessage { get; set; }

     public async Task<IActionResult> OnPostAsync(string id)
     {
         if (string.IsNullOrWhiteSpace(id))
             return NotFound();

+        var currentUserId = _userManager.GetUserId(User);
+        if (id == currentUserId)
+        {
+            StatusMessage = "不能重置自己的双因素认证。请让其他管理员操作。";
+            return RedirectToPage("/Admin/Index");
+        }

         var targetUser = await _userManager.FindByIdAsync(id);
```

同时将 `OnPostAsync` 中成功路径的 `TempData["StatusMessage"]` 替换为 `StatusMessage`（使用 `[TempData]` 属性版）：

```diff
-        TempData["StatusMessage"] = $"已重置 '{targetUser.UserName}' 的双因素认证";
+        StatusMessage = $"已重置 '{targetUser.UserName}' 的双因素认证";
```

错误处理路径同理：
```diff
-            ErrorMessage = "2FA 重置失败，请稍后重试";
+            StatusMessage = "2FA 重置失败，请稍后重试";
-            return Page();
+            return RedirectToPage("/Admin/Index");
```

需同步更新 `ResetTwoFactor.cshtml` 视图：移除死代码消息块（所有 3 条 POST 路径均 redirect，消息块永不可达）。

```diff
  <h2>重置双因素认证</h2>

- @if (Model.ErrorMessage is not null)
- {
-     <div class="error-message">@Model.ErrorMessage</div>
- }

  <div class="warning-banner">
```

### Story 14.2: #30 HasFlag switch 精确值匹配

**类型:** Defensive Improvement
**文件:** `src/BoxWise.Server/Pages/Admin/Index.cshtml.cs`

将 `LoadUsersAsync` 中的 `HasFlag` 模式替换为精确枚举值匹配，防止未来新增标志位时被现有分支部分匹配而静默丢弃。添加注释说明整数值与 TwoFactorMethod 枚举的耦合关系。

```diff
+         // 精确值匹配 — 与 TwoFactorMethod [Flags] 枚举值一一对应。
+         // None=0, TOTP=1, Email=2, WebAuthn=4.
+         // 7=TOTP|Email|WebAuthn, 6=Email|WebAuthn, 5=TOTP|WebAuthn, 4=WebAuthn, 3=TOTP|Email.
+         // 如枚举值变更，此处需同步更新。
          var methodDisplay = u.ConfiguredMethods switch
          {
-             TwoFactorMethod m when m.HasFlag(TwoFactorMethod.TOTP) && m.HasFlag(TwoFactorMethod.Email) && m.HasFlag(TwoFactorMethod.WebAuthn) => "...",
-             TwoFactorMethod m when m.HasFlag(TwoFactorMethod.TOTP) && m.HasFlag(TwoFactorMethod.WebAuthn) => "...",
-             TwoFactorMethod m when m.HasFlag(TwoFactorMethod.Email) && m.HasFlag(TwoFactorMethod.WebAuthn) => "...",
-             TwoFactorMethod m when m.HasFlag(TwoFactorMethod.TOTP) && m.HasFlag(TwoFactorMethod.Email) => "...",
-             TwoFactorMethod m when m.HasFlag(TwoFactorMethod.WebAuthn) => "WebAuthn",
-             TwoFactorMethod m when m.HasFlag(TwoFactorMethod.TOTP) => "TOTP",
-             TwoFactorMethod m when m.HasFlag(TwoFactorMethod.Email) => "Email",
+             (TwoFactorMethod)7 => "TOTP + Email + WebAuthn",
+             (TwoFactorMethod)6 => "Email + WebAuthn",
+             (TwoFactorMethod)5 => "TOTP + WebAuthn",
+             (TwoFactorMethod)4 => "WebAuthn",
+             (TwoFactorMethod)3 => "TOTP + Email",
+             TwoFactorMethod.Email => "Email",
+             TwoFactorMethod.TOTP => "TOTP",
              TwoFactorMethod.None => null,
              _ => u.ConfiguredMethods.ToString()
          };
```

> **Note:** `TwoFactorMethod.cs` 已确认枚举值: `None=0, TOTP=1, Email=2, WebAuthn=4`。整数 7=(1|2|4), 6=(2|4), 5=(1|4), 4=(4), 3=(1|2)。`HasFlag` 在 `LoginWith2fa.cshtml.cs` / `TwoFactorService.cs` / `TwoFactorFlowE2ETests.cs` 中仍用于逻辑判断（非显示），其场景为单标志位检查，不受此问题影响。

### Story 14.3: #27 SW 缓存键不匹配 — 关闭 Issue

**类型:** Documentation (已完成)
**操作:** 关闭 Issue #27。限制已在 `service-worker.published.js:4-9` 以注释形式文档化，框架级 .NET 10 已知限制，无应用层代码修复。

### Story 14.4: #28 CI tar/ZIP 包安全验证

**类型:** CI Improvement
**文件:** `.github/workflows/release.yml`

在 Linux 和 Windows 两个归档验证步骤中增加 `.env` 和 `data/` 存在性检查。**关键修复:** `tar -C publish/boxwise .` 产生 `./` 前缀路径，regex 必须匹配 `^\./\.env$` 而非 `^\.env$`（Critical find from review）。

```diff
       - name: Validate archive
         run: |
           test -f boxwise-linux-x64.tar.gz || { echo "Archive not found"; exit 1; }
           tar -tzf boxwise-linux-x64.tar.gz | grep -q "BoxWise.Server.dll" || { echo "Missing: BoxWise.Server.dll"; exit 1; }
           tar -tzf boxwise-linux-x64.tar.gz | grep -q "wwwroot/index.html" || { echo "Missing: wwwroot/index.html"; exit 1; }
+          # 安全检查：tar 包不得包含 .env 或 data/（防御 CI 误配置）
+          tar -tzf boxwise-linux-x64.tar.gz | grep -qE '(^|/)\.env$' && { echo "ERROR: .env found in archive"; exit 1; } || true
+          tar -tzf boxwise-linux-x64.tar.gz | grep -qE '(^|/)data(/|$)' && { echo "ERROR: data/ found in archive"; exit 1; } || true
```

**Windows ZIP 同等验证：**

```diff
       - name: Validate archive
         shell: pwsh
         run: |
           $entries = [System.IO.Compression.ZipFile]::OpenRead("boxwise-win-x64.zip").Entries
           $names = $entries | % { $_.FullName }
           if (-not ($names -match "BoxWise.Server.dll")) { Write-Error "Missing: BoxWise.Server.dll"; exit 1 }
           if (-not ($names -match "wwwroot/index.html")) { Write-Error "Missing: wwwroot/index.html"; exit 1 }
+          # 安全检查：ZIP 包不得包含 .env 或 data/（防御 CI 误配置）
+          if ($names -match "(^|/)\.env$") { Write-Error "ERROR: .env found in archive"; exit 1 }
+          if ($names -match "(^|/)data(/|$)") { Write-Error "ERROR: data/ found in archive"; exit 1 }
```

> **Note:** `|| true` 仅在 grep 未匹配时生效（将退出码 1→0）。匹配时 `exit 1` 直接终止脚本，`|| true` 不会执行。行为正确。

### Story 14.5: #31 grep 验证命令通用化

**类型:** Documentation
**文件:** `README.md:327`

扩展 grep 正则字符类以同时匹配单引号和双引号 HTML 属性。使用 `-E` 扩展正则确保 `\'` 在双引号字符串中的行为明确。

```diff
- curl -s https://你的域名/ | grep -o 'src="[^"]*blazor\.webassembly\.js[^"]*"'
+ curl -s https://你的域名/ | grep -oE "src=['\"][^'\"]*blazor\.webassembly\.js[^'\"]*['\"]"
```

### Story 14.6: #32 i18n 硬编码 — 关闭 Issue

**类型:** Close Only
**操作:** 关闭 Issue #32。项目全程中文（PRD、UI、Admin、文档），无 i18n 基础设施。为单一常量引入 i18n 框架属于过度工程。

## 5. Implementation Handoff

**Scope Classification:** Minor

**Route to:** Developer agent (`bmad-dev-story`)

**Epic 14 结构:**

| Story | Issue | 状态 | 文件变更 | 预计工作量 |
|-------|-------|------|----------|-----------|
| 14.1-admin-2fa-self-protect | #29 | Active | `ResetTwoFactor.cshtml.cs` + `.cshtml` | 10 min |
| 14.2-hasflag-exact-match | #30 | Active | `Index.cshtml.cs` | 10 min |
| 14.3-sw-cache-close | #27 | Close-only | — | — |
| 14.4-ci-tar-validate | #28 | Active | `release.yml` | 10 min |
| 14.5-grep-regex-universal | #31 | Active | `README.md` | 2 min |
| 14.6-i18n-close | #32 | Close-only | — | — |

**Acceptance Criteria:**
- `dotnet build` 零错误零警告
- `dotnet test BoxWise.slnx` 全部通过（无回归）
- 6 条 GitHub Issue 全部关闭
- CI release.yml Linux `tar -tzf` 和 Windows `ZipFile` 验证均含 `.env`/`data/` 安全检查
- `ResetTwoFactor.cshtml.cs` 使用 `[TempData] StatusMessage + RedirectToPage()` 模式，与 `Index.cshtml.cs` 一致

**Success Criteria:** Epic 13 Retro 记录的 deferred-work.md 全部清偿完毕，零残留。

## 6. Review Amendments

本提案经由两轮共 4 个子代理评审。

### Round 1 (Adversarial Review + Edge Case Hunter)

| 发现 | 严重度 | 修正 |
|------|--------|------|
| `^\.env$` 不匹配 tar 的 `./.env` 前缀 | Critical | 改为 `grep -qE '(^|\/)\.env$'` |
| Windows `publish-windows` 缺少同等安全检查 | Warning | 新增 PowerShell 等效验证 |
| `ErrorMessage + return Page()` 与现有 `[TempData] StatusMessage + RedirectToPage()` 模式不一致 | Warning | 对齐为 `[TempData]` 模式 |
| 整数硬编码与 TwoFactorMethod 枚举值隐性耦合 | Warning | 添加注释说明同步要求 |

### Round 2 (Adversarial Review + Edge Case Hunter — 复查修正版)

| 发现 | 严重度 | 修正 |
|------|--------|------|
| `.cshtml` 消息块变为死代码（全部 POST 路径均 redirect） | Medium | 移除 `.cshtml` 中的 `@if (Model.ErrorMessage...)` 块 |
| `data/` regex 漏检裸 `data` 文件名 | Low | 改为 `(^|\/)data(\/|\$)` 覆盖目录+裸文件 |
| 枚举值映射表仅存于 proposal 文件，不写入代码 | Low | 注释中直接内联 `None=0, TOTP=1, Email=2, WebAuthn=4` 对照表 |

其余 Low/Info 级发现（ZipArchive 未释放、错误 UX 回归、首个错误阻断后续检查）已评估为可接受，不影响提案通过。
