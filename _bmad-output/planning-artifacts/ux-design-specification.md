---
stepsCompleted: [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14]
lastStep: 14
status: implemented — 代码已对齐设计稿（2026-05-25）
inputDocuments:
  - _bmad-output/planning-artifacts/prds/prd-BoxWise-2026-05-21/prd.md
  - _bmad-output/planning-artifacts/architecture.md
---

# UX Design Specification 箱知 · BoxWise

**Author:** Developer
**Date:** 2026-05-21

---

## Executive Summary

### Project Vision

箱知 · BoxWise 解决"不常用的东西收纳后找不到"的日常痛点。家庭成员共享物品库——任何人收纳的东西，全家都能找到。

### Target Users

开发者及其家庭成员（≤5 人）。手机拍照为主（PWA 移动端优先），界面直觉化。

---

## Core User Experience

### Defining Experience

**"拍录入 — 搜即找"** 闭环。录入端：拍照（可选）→ 填信息 → 选位置 → 保存。查找端：搜索/浏览 → 看到缩略图/文字卡片 → 找到实物。

### Experience Principles

| Principle | Meaning |
|-----------|---------|
| **零输入 > 少输入** | AI 自动识别优先于手动填写 |
| **图 > 文** | 缩略图网格是默认视图 |
| **收纳即记录** | 录入流程融合到收纳动作中 |
| **共享即透明** | 一个人收纳 = 全家受益 |

---

## Desired Emotional Response

**安心**（Peace of Mind）— 知道需要找东西时一定能找到。
**高效**（Efficiency）— 拍照→识别→保存的流畅感。
**安静不打扰、可靠不失误、轻盈不费劲**。

---

## Design System Foundation

### Design System: MudBlazor

Blazor WASM 原生 Material Design 组件库。

**Color Palette:**
- Primary: `#546E7A`（蓝灰，冷静可靠）
- Secondary: `#80CBC4`（浅湖绿，温和）
- Surface: `#FFFFFF` / Background: `#FAFAFA`
- Success: `#66BB6A` / Error: `#EF5350`

**Typography:** Roboto（Material Design 默认）

**Key Components:**
`MudGrid` + `MudItem` / `MudTreeView` / `MudChipSet` / `MudDialog` / `MudProgressCircular` / `MudTextField`

> **注（2026-06）：** Identity 认证页面（登录/2FA/账户管理）使用 ASP.NET Core Identity 默认 Bootstrap 样式，与 Blazor WASM 的 MudBlazor Material Design 风格并存。这是 Epic 10-11 迁移的有意设计决策——双 UI 风格在 ≤5 人家用场景可接受。

---

## Design Direction Decision

### Chosen Direction: 方向 1 — 底部Tab导航 · 双列网格

**Key Elements:**
- Bottom Tab Bar: `首页` | `录入` | `浏览`（3 Tab，录入居中突出，底部导航即为唯一录入入口）
- 2-column thumbnail grid（mobile），responsive → 4-col (tablet) / 6-col (desktop)
- Standard 8dp spacing, Medium density
- Text cards + placeholder icons for items without photos

**Rationale:** 最符合移动端触屏操作习惯。底部Tab直接切换主视图，中间录入按钮快速触发录入。双列网格在手机上有足够的缩略图大小（150-180px 宽），同时保持信息密度。FAB 被移除——底部导航录入 Tab 已提供唯一入口，双入口冗余。

---

## Identity 认证页面（Bootstrap 风格）

**背景：** Epic 10-11 迁移后，登录/登出/2FA 验证/账户管理使用 ASP.NET Core Identity 脚手架 Razor Pages，默认 Bootstrap 样式。Blazor WASM 仅保留通行密钥登录。

### 双 UI 风格并存

| 页面类型 | UI 框架 | 路由 | 说明 |
|---------|---------|------|------|
| 登录/2FA/注册 | Bootstrap (Identity 默认) | `/Identity/Account/*` | Server 端 Razor Pages |
| 账户管理 | Bootstrap (Identity 默认) | `/Identity/Account/Manage/*` | 2FA 设置、密码修改、邮箱管理 |
| 通行密钥登录 | MudBlazor | `/login` | Blazor WASM，仅保留通行密钥按钮 |
| 首页/录入/浏览/设置 | MudBlazor | `/` | Blazor WASM SPA 主体 |

> **设计决策（2026-06）：** 双 UI 风格并存是 Epic 10-11 迁移的有意权衡——Identity 脚手架页面使用 Bootstrap 默认样式，不与 MudBlazor 做样式桥接。在 ≤5 人家用场景下可接受，避免为样式一致性引入额外维护成本。

---

## Key Interaction Flows

### Authentication Flow（2026-06 更新）

**背景：** Epic 10-11 迁移后，登录/登出/2FA 验证/账户管理使用 ASP.NET Core Identity 脚手架 Razor Pages，默认 Bootstrap 样式。Blazor WASM 仅保留通行密钥登录。

```
未登录用户访问 Blazor WASM → Cookie 认证中间件拦截 → 302 重定向到 /Identity/Account/Login
  → 输入用户名/密码 → POST Login.cshtml
  → 无 2FA：签发 Cookie → 302 重定向到 / (Blazor WASM 首页)
  → 有 2FA：重定向到 /Identity/Account/LoginWith2fa → 输入 TOTP 验证码
    → 签发 Cookie → 重定向到 /
  → CookieAuthenticationStateProvider 调用 GET /api/auth/me → AppState.SetUser() → UI 更新为已登录
```

**通行密钥登录（保留）：** 用户访问 `/login` (Blazor WASM) → 点击"使用通行密钥登录"按钮 → 浏览器 WebAuthn API → 验证成功 → AppState.SetUser() → 导航到 `/`

### Entry Flow

```
Home Tab "录入" or FAB → 单屏录入表单
  → 照片上传（可选，ImageUploader 始终可见）
  → 物品名称（必填）
  → 备注（可选）
  → MudTreeView 选位置（必填）
  → MudChipSet 加标签（可选）
  → 保存（名称非空+位置已选 → enabled）
  → 卡片生成 + 连续收纳继承位置
```

### Find Flow

```
首页网格（默认）→ 搜索框(MudTextField) or 浏览Tab(MudTreeView) 
  → 即时模糊匹配 or 位置/标签筛选
  → 点击物品 → 详情页
  → 查看大图/删除(MudDialog确认)
```

---

## Field Validation Rules

- **物品名称**：AI 成功→预填可编辑；AI 失败或跳过拍照→空白→必填
- **位置**：始终必填，选叶子节点
- **保存按钮**：名称非空 + 位置已选 → enabled

---

## UX Pattern Analysis

| Pattern | Source | Component |
|---------|--------|-----------|
| 缩略图网格 | Google Photos | `MudGrid` |
| 搜索优先 | 1Password | `MudTextField` + Adornment |
| 快速录入 | Things 3 | 一屏完成 |
| 层级树 | Notion 侧边栏 | `MudTreeView` |
| 拍照识别 | 淘宝拍照搜物 | 拍照→分析→预填 |

### Anti-Patterns
- 不多步骤向导、不弹窗确认每步、不深层嵌套（≤2层）、不花哨动画

---

## Visual Design Foundation

### Color System

| Role | Color | Usage |
|------|-------|-------|
| Primary | `#546E7A` | 按钮、选中态、Tab 激活 |
| Secondary | `#80CBC4` | 标签 Chip、树节点 |
| Surface | `#FFFFFF` | 卡片、对话框 |
| Background | `#FAFAFA` | 页面底色 |
| Success | `#66BB6A` | 保存确认 |
| Error | `#EF5350` | 删除按钮、校验失败 |

### Spacing & Layout
- 8dp grid, MudBlazor defaults
- Mobile: 2 cols | Tablet: 4 cols | Desktop: 6 cols
- BorderRadius: 4 (subtle), Elevation: 0-1 (flat)
