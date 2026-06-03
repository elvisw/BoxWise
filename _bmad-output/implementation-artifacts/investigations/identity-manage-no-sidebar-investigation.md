# Investigation: Identity Manage 页面缺少侧边栏

## Hand-off Brief

1. **What happened.** Identity Area 的 Manage 子目录有专门的 `_Layout.cshtml`（含侧边栏 `_ManageNav`），但所有 Manage 页面（Index/Email/ChangePassword/2FA 等）均未引用它——它们通过父级 `_ViewStart.cshtml` 继承的是无侧边栏的 Identity 根 `_Layout.cshtml`。
2. **Where the case stands.** 根因已确认，修复方向明确。证据完备，无缺失。
3. **What's needed next.** 在 Manage 目录添加 `_ViewStart.cshtml` 将 Layout 指向本地 `_Layout.cshtml`，一行修复，覆盖全部 8 个 Manage 页面。

## Case Info

| Field            | Value                                                                      |
| ---------------- | -------------------------------------------------------------------------- |
| Ticket           | N/A                                                                        |
| Date opened      | 2026-06-03                                                                 |
| Status           | Active                                                                     |
| System           | Windows 11, .NET 10, ASP.NET Core Identity 脚手架                         |
| Evidence sources | 源代码 (`src/BoxWise.Server/Areas/Identity/Pages/`)                        |

## Problem Statement

用户报告：访问 `https://localhost:5000/Identity/Account/Manage` 个人信息维护页面时，页面左侧没有显示侧边栏导航（个人信息/邮箱/密码/双重验证等链接），导致无法在不同管理子页面之间切换。

## Evidence Inventory

| Source   | Status    | Notes     |
| -------- | --------- | --------- |
| `Areas/Identity/Pages/_ViewStart.cshtml` | Available | 设置 Layout 为父级 `_Layout.cshtml`（无侧边栏） |
| `Areas/Identity/Pages/_Layout.cshtml` | Available | 父级布局：仅 Bootstrap 导航栏 + `@RenderBody()`，无侧边栏 |
| `Areas/Identity/Pages/Account/Manage/_Layout.cshtml` | Available | Manage 专用布局：含 `_ManageNav` 侧边栏 + `@RenderBody()`，正确设计 |
| `Areas/Identity/Pages/Account/Manage/_ManageNav.cshtml` | Available | 侧边栏导航 partial：Profile/Email/Password/2FA 链接，正确实现 |
| `Index.cshtml` | Available | 未显式设置 Layout |
| `Email.cshtml` | Available | 未显式设置 Layout |
| `ChangePassword.cshtml` | Available | 未显式设置 Layout |
| `TwoFactorAuthentication.cshtml` | Available | 未显式设置 Layout |
| Manage 目录 `_ViewStart.cshtml` | **Missing** | 不存在——这是根因 |

## Investigation Backlog

| # | Path to Explore | Priority              | Status  | Notes     |
| - | --------------- | --------------------- | ------- | --------- |
| 1 | 确认 Manage `_Layout.cshtml` 是否存在并正确 | High | Done | 存在，含侧边栏 `_ManageNav` partial |
| 2 | 确认 Manage 各页面是否正确引用 Manage `_Layout.cshtml` | High | Done | 无页面显式设置 Layout |
| 3 | 确认 Razor `_ViewStart.cshtml` 的层级继承链 | High | Done | 父级 `_ViewStart.cshtml` 指向无侧边栏的布局 |
| 4 | 确认 `_ManageNav.cshtml` 内容是否完整 | Medium | Done | 含 Profile/Email/Password/2FA 导航链接 |

## Confirmed Findings

### Finding 1: Manage 目录存在正确的带侧边栏布局

**Evidence:** `src/BoxWise.Server/Areas/Identity/Pages/Account/Manage/_Layout.cshtml:12-25`

**Detail:** 该布局使用 Bootstrap row/col 布局——`col-md-3` 渲染 `_ManageNav` 侧边栏 partial，`col-md-9` 渲染 `@RenderBody()`。通过 `ViewData["ParentLayout"]` 支持布局链式继承，未设置时回退到 `/Areas/Identity/Pages/_Layout.cshtml`。

### Finding 2: 父级布局无侧边栏

**Evidence:** `src/BoxWise.Server/Areas/Identity/Pages/_Layout.cshtml:20-24`

**Detail:** 父级布局仅包含 Bootstrap 导航栏 header 和 `<div class="container">` 包裹的 `@RenderBody()`，没有任何侧边栏结构。

### Finding 3: 所有 Manage 页面均未显式设置 Layout

**Evidence:**
- `Index.cshtml:3-6` — `@{}` 块仅设置 `ViewData["Title"]` 和 `ViewData["ActivePage"]`，无 `Layout`
- `Email.cshtml:3-6` — 同上
- `ChangePassword.cshtml:3-6` — 同上
- `TwoFactorAuthentication.cshtml:3-7` — 同上
- `Disable2fa.cshtml` / `EnableAuthenticator.cshtml` / `ResetAuthenticator.cshtml` / `GenerateRecoveryCodes.cshtml` — 同上模式

### Finding 4: `_ViewStart.cshtml` 层级缺失

**Evidence:** Glob 搜索确认 `Areas/Identity/Pages/Account/Manage/` 目录下**不存在** `_ViewStart.cshtml` 文件。

**Detail:** Razor 的 `_ViewStart.cshtml` 从页面所在目录开始向上查找。Manage 目录无此文件 → 使用父级 `Areas/Identity/Pages/_ViewStart.cshtml` → 其设置 `Layout = "/Areas/Identity/Pages/_Layout.cshtml"`（无侧边栏版本）。

## Deduced Conclusions

### Deduction 1: Razor 布局继承链指向了错误的布局文件

**Based on:** Finding 2, Finding 3, Finding 4

**Reasoning:**
1. `_ViewStart.cshtml` 在 `Areas/Identity/Pages/` 设置 `Layout = "/Areas/Identity/Pages/_Layout.cshtml"`
2. Manage 目录无本地 `_ViewStart.cshtml` 覆盖此设置
3. Manage 各页面也未在 `@{}` 块中显式设置 Layout
4. 因此所有 Manage 页面继承父级 `_Layout.cshtml`（无侧边栏）

**Conclusion:** Manage 页面实际渲染的是父级 `_Layout.cshtml`，而非同目录下的 Manage `_Layout.cshtml`。Manage `_Layout.cshtml` 虽然内容正确（含侧边栏），但从未被任何页面引用——它是一段"死代码"。

## Root Cause

**Confirmed.** Manage 目录缺少 `_ViewStart.cshtml` 文件来将 Layout 指向本地 `_Layout.cshtml`。这是 Identity 脚手架生成时的遗漏——标准 Identity 脚手架应在 `Manage/` 目录生成 `_ViewStart.cshtml`，内容为 `Layout = "_Layout";`。

## Conclusion

**Confidence:** High

根因单一且明确：Manage 目录缺少 `_ViewStart.cshtml`。所有证据一致指向这一结论，无矛盾。

`_Layout.cshtml`（含侧边栏）和 `_ManageNav.cshtml`（导航链接）本身实现正确，不需要修改。问题是这些文件从未被引用。

## Recommended Next Steps

### Fix direction

在 `src/BoxWise.Server/Areas/Identity/Pages/Account/Manage/` 目录下创建 `_ViewStart.cshtml`：

```csharp
@{
    Layout = "_Layout";
}
```

**为什么这一行修复就能生效：**
1. Razor 在当前目录找到 `_ViewStart.cshtml` → 执行它，设置 `Layout = "_Layout"`
2. `"_Layout"` 在当前目录解析为 `Manage/_Layout.cshtml`
3. `Manage/_Layout.cshtml` 渲染侧边栏（`_ManageNav`）+ 内容（`@RenderBody()`）
4. `Manage/_Layout.cshtml` 的父布局通过 `ViewData["ParentLayout"]` 或回退值链到 Identity 根 `_Layout.cshtml`

**影响范围：** 一次性覆盖全部 8 个 Manage 页面（Index / Email / ChangePassword / TwoFactorAuthentication / Disable2fa / EnableAuthenticator / ResetAuthenticator / GenerateRecoveryCodes），无需逐个修改。

### Diagnostic

修复后验证步骤：
1. 启动应用，登录后访问 `/Identity/Account/Manage`
2. 确认左侧 `col-md-3` 区域显示侧边栏导航（个人信息/邮箱/密码/双重验证）
3. 依次点击各导航链接，确认高亮状态（`active` CSS class）正确切换
4. 确认各子页面内容在右侧 `col-md-9` 区域正确渲染

### 备选方案（不推荐）

也可以在每个 Manage 页面的 `@{}` 块中单独添加 `Layout = "_Layout";`。但这需要修改 8 个文件，且每次新增 Manage 页面都需记得添加。`_ViewStart.cshtml` 是更优雅、更符合 Razor 惯例的做法。

## Side Findings

- `_ManageNav.cshtml:14-15` — PersonalData 页面链接已被注释掉（"未生成，暂不开放"），修复侧边栏后也无需取消注释，这是有意为之。
