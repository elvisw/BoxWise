---
date: 2026-05-27
status: approved
scope: moderate
---

# Sprint Change Proposal — 用户管理功能扩展

## 1. Issue Summary

**问题陈述：** MVP 阶段的用户管理仅实现了账户创建和列表查看，缺少基本的账户维护功能——编辑用户信息、修改密码、删除用户、角色分配。随着家庭实际使用，管理员需要这些功能来维护账户。

**发现时机：** MVP（Epic 1–4）全部完成后，前瞻性架构审视。

**触发讨论：** 是否需要引入第三方用户管理类库来补充缺失功能。

## 2. Impact Analysis

### 评估结论：继续基于 ASP.NET Core Identity 自建

**关键发现：** 所有缺失功能的 API 已由 ASP.NET Core Identity 内置提供，当前缺失的只是调用这些 API 的 Admin UI 页面：

| 需求 | Identity API | 现状 |
|------|-------------|------|
| 管理员修改用户信息 | `UserManager.UpdateAsync()` | 未暴露 UI |
| 管理员修改任意用户密码 | `UserManager.RemovePasswordAsync()` + `AddPasswordAsync()` | 未暴露 UI |
| 管理员删除用户 | `UserManager.DeleteAsync()` | 未暴露 UI |
| 管理员角色分配 | `AddToRoleAsync()` / `RemoveFromRoleAsync()` | 未暴露 UI |
| 用户修改自己的信息 | `UserManager.UpdateAsync()` | 未暴露 UI |
| 用户修改自己的密码 | `UserManager.ChangePasswordAsync()` | 未暴露 UI |

### 第三方方案评估

| 方案 | 不适合原因 |
|------|-----------|
| **Microsoft Identity UI** (`AddDefaultUI`) | 详细分析见下方 |
| **Auth0 / Clerk / Okta** | 引入外部网络依赖和费用，对 ≤5 人家用场景过重 |
| **ABP Framework** | 要求全面采用 ABP 架构，相当于重写项目 |
| **独立 Admin 管理库** | .NET 生态中不存在主流的、仅提供 "Identity Admin UI" 的轻量库 |

### Microsoft Identity UI 详细评估

`AddDefaultUI()` 提供登录/注册/改密/2FA 全套 Razor Pages，表面看省代码。实际上：

**注册页禁用 — 可行但只是最小问题：**
- Scaffold Register 页面后在其 `OnGet`/`OnPost` 返回 404
- 或中间件拦截 `/Identity/Account/Register` 路由

**但真正的痛点不在这里：**

1. **不提供 Admin 管理页面** — Identity UI 是自助服务设计，你需要的 Admin 列表/创建/编辑/删除/角色管理它一个都没有。引入后这些页面仍然要自己写。
2. **UI 风格分裂** — Identity UI 基于 Bootstrap + Razor Pages，你的 App 是 MudBlazor + Blazor WASM。两套设计语言并存，维护体验差。
3. **默认假设不匹配** — 以 Email 为主标识（`SignInManager` 配置需额外调整），页面全英文，Scaffold 后定制代码量不小。
4. **边际收益低** — 真正省掉的只有"用户改密码"一个页面，但它带来的副作用需要额外处理。

**结论：** Identity UI 适合多租户 SaaS 或自助注册场景，不适合 BoxWise 的家庭内部使用定位。

### PRD 影响

需新增功能需求：
- **FR-21**: 管理员编辑用户信息（用户名等）
- **FR-22**: 管理员修改用户密码
- **FR-23**: 管理员删除用户账户
- **FR-24**: 管理员分配/移除 Admin 角色
- **FR-25**: 用户修改自己的信息
- **FR-26**: 用户修改自己的密码

### Architecture 影响

- 现有决策 "Admin UI: Independent Razor Pages Area" 保持不变
- 现有决策 "Identity Integration: Cookie + Blazor WASM" 保持不变
- Admin Razor Pages 从 2 页扩展到 4-5 页
- 客户端新增 Settings 页面（用户自助修改信息/密码）

### UX 影响

- Settings Tab 新增"账户信息"/"修改密码"区块
- Admin 后台账户列表新增"编辑"/"删除"/"角色"操作按钮

## 3. Recommended Approach

**选择方案：直接扩展（Direct Adjustment）— 在现有 Identity + Razor Pages 基础上新增 Admin UI**

### 理由

1. **工作量低**: 每项功能 = 1 个 Razor Page + 调用现有 `UserManager` API，无需引入新依赖
2. **风险低**: 无外部服务依赖，代码完全可控，Identity API 经过微软验证
3. **架构一致性**: 延续现有 `Pages/Admin/` Razor Pages + `AdminOnly` 策略模式
4. **规模匹配**: 1C1G VPS、≤5 用户、家庭场景——Identity 本身就是正确选择
5. **无合适替代方案**: .NET 生态中没有适合此场景的第三方管理层

### 建议实施范围

**Admin 端（Razor Pages，AdminOnly 保护）：**
- 用户列表页增加操作列（编辑/删除/角色切换）
- 编辑用户信息页（EditAccount.cshtml）
- 修改用户密码页（ChangeUserPassword.cshtml）

**用户端（Blazor WASM Settings 页）：**
- 修改自己的用户名（Settings.razor "账户信息"区块）
- 修改自己的密码（Settings.razor "修改密码"区块）

### Effort Estimate

| 类型 | 预估 |
|------|------|
| 新增 Admin Razor Pages | 3 页（Edit, ChangePassword, 增强 Index） |
| 新增/修改 Blazor 页面 | 1 页（Settings 新增 2 个区块） |
| 新增 API 端点 | 2-3 个（用户自助修改信息/密码） |
| 测试 | 5-8 个新测试 |
| **总工作量** | **小（1-2 个 Story）** |

### Risk Level

**低** — 所有依赖项已就绪，仅需扩展 UI 层。

## 4. Detailed Change Proposals

### 4.1 Architecture 文档更新

**章节:** Authentication & Security > Admin UI

**变更:** 补充 Admin UI 功能范围说明

```
Admin Razor Pages 目录结构（更新后）:
  Pages/Admin/
  ├── Index.cshtml              ← 账户列表（含操作列：编辑/删除/角色切换）
  ├── CreateAccount.cshtml      ← 创建账户
  ├── EditAccount.cshtml        ← [新增] 编辑用户信息
  ├── ChangeUserPassword.cshtml ← [新增] 修改用户密码
  ├── _Layout.cshtml
  ├── _ViewImports.cshtml
  └── _ViewStart.cshtml
```

### 4.2 PRD 更新

**章节:** §4.7 账户认证

**新增 FR:**

> **FR-21: 管理员编辑用户信息**
> 管理员可在后台编辑已有用户的用户名。
>
> **FR-22: 管理员修改用户密码**
> 管理员可在后台为任意用户重置密码。
>
> **FR-23: 管理员删除用户**
> 管理员可删除不再需要的用户账户。
>
> **FR-24: 管理员角色分配**
> 管理员可在 Admin/成员 之间切换用户角色。
>
> **FR-25: 用户修改自己的信息**
> 已登录用户可在设置页修改自己的用户名。
>
> **FR-26: 用户修改自己的密码**
> 已登录用户可在设置页修改自己的密码（需输入当前密码验证）。

### 4.3 Epics 更新

新建 **Epic 5: 用户管理增强**（或合并到已有设置优化工作中）：

```
Epic 5: 用户管理增强
├── Story 5.1: Admin 后台用户管理扩展（编辑/删除/角色/密码重置）
└── Story 5.2: 用户自助账户管理（修改信息/密码）
```

## 5. Implementation Handoff

**变更范围分类：Moderate**
- 需 Product Owner（用户本人）确认新 FR 优先级
- 由 Developer agent 实现

**成功标准：**
1. Admin 可编辑/删除用户、修改密码、切换角色
2. 普通用户可修改自己的用户名和密码
3. 所有现有 43 测试继续通过
4. 新增功能有对应测试覆盖

---

## 6. 品牌与版权完善 (2026-05-27 追加)

### 6.1 应用图标

从 SVG 矢量图标 (`logo.svg`) 生成全尺寸 PNG：

| 文件 | 尺寸 | 用途 |
|------|------|------|
| `favicon-32.png` | 32×32 | 浏览器标签页 |
| `favicon.ico` | 32+16 | 传统浏览器兼容 |
| `apple-touch-icon.png` | 180×180 | iOS 主屏幕 |
| `icon-192.png` | 192×192 | PWA 清单 |
| `icon-512.png` | 512×512 | PWA 清单 + OG 图片 |

### 6.2 页脚与品牌标识

- **App Bar**: 用 `<img src="logo.svg">` 替换 emoji 📦
- **Footer**: 页面底部显示 `© 2026 BoxWise · GitHub`，链接到 `https://github.com/elvisw/BoxWise`
- **`index.html`**: 新增 SVG favicon、PNG fallback、Open Graph 元标签 (`og:title`/`og:description`/`og:image`/`twitter:card`)、`lang="zh-CN"`、`theme-color`
- **`manifest.webmanifest`**: 新增 `description` 字段

### 6.3 关于页面 (`/about`)

Settings 页面新增"关于"入口，独立页面展示：

- **应用信息**: Logo + 名称 + 副标题 + 版本号 (`Assembly.GetExecutingAssembly().GetName().Version`)
- **运行环境**: .NET 运行时版本 (`RuntimeInformation.FrameworkDescription`)、OS、架构、数据库类型
- **第三方依赖与许可**:

  | 类库 | 版本 | 许可证 |
  |------|------|--------|
  | MudBlazor | 9.4.0 | MIT |
  | SkiaSharp | 3.119.2 | MIT |
  | SixLabors.ImageSharp | 3.1.7 | Apache-2.0 |
  | ASP.NET Core | 10.0.8 | MIT |
  | Entity Framework Core | 10.0.8 | MIT |
  | xunit | 2.9.3 | MIT |
  | coverlet | 6.0.4 | MIT |

- **图标归属**: Noto Emoji (Google Fonts) — SIL Open Font License 1.1 — `github.com/googlefonts/noto-emoji`
- **开源许可**: BoxWise — GPL-3.0 — `github.com/elvisw/BoxWise`

### 6.4 版本号

`Directory.Build.props` 新增 `<Version>1.0.0</Version>`，所有项目统一版本。

### 6.5 Epics 更新

在 Epic 5 中新增第三个 Story：

```
Epic 5: 用户管理增强 + 品牌收尾
├── Story 5.1: Admin 后台用户管理扩展（编辑/删除/角色/密码重置）
├── Story 5.2: 用户自助账户管理（修改信息/密码）
└── Story 5.3: 品牌与版权完善（已完成 ✅）
```

### 6.6 构建与测试状态

- 构建: 0 错误 0 警告
- 测试: 43/43 通过（品牌变更未破坏任何现有测试）
