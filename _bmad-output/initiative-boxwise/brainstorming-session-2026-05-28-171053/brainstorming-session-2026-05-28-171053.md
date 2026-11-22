---
stepsCompleted: [1, 2, 3]
inputDocuments: []
session_topic: 'BoxWise 账户安全加固 — 密码策略升级 + 2FA + 综合安全改进清单'
session_goals: '设计更强密码规则（无历史限制）、选择并设计 2FA 方案、产出优先级安全改进清单'
selected_approach: 'AI-Recommended Techniques'
techniques_used: ['first-principles-thinking', 'constraint-mapping', 'failure-analysis']
ideas_generated: []
context_file: ''
---

# Brainstorming Session Results

**Facilitator:** Elvis
**Date:** 2026-05-28

## Session Overview

**Topic:** BoxWise 账户安全加固 — 密码策略升级 + 2FA + 综合安全改进清单
**Goals:** 设计更强密码规则（最小长度、复杂度约束，不含历史限制）、选择并设计 2FA 方案、产出可执行的优先级安全改进清单

**威胁场景覆盖：** 暴力破解、弱密码、凭证填充、会话劫持、未授权数据访问

**约束：**
- 不做密码历史限制（避免用户因频繁修改而遗忘）
- 部署目标：NAS + Linux VPS（Debian 系发行版），安全方案需对 Debian 友好

### Session Setup

用户关注点：BoxWise 存储物品位置和照片等敏感家庭数据，当前密码规则和验证方式不足以防御各类非法访问威胁。期望通过本次会话产出完整的密码策略升级方案，包括 2FA 验证方式选型和优先级安全改进清单。

## Technique Selection

**Approach:** AI-Recommended Techniques
**Analysis Context:** 安全加固主题、技术性强、务实风格，需在安全性与家庭用户易用性间平衡

**Recommended Techniques:**

- **First Principles Thinking:** 回到安全最根本的问题 —— 保护什么、威胁是什么、家庭应用的真实攻击面
- **Constraint Mapping:** 在 NAS/Debian VPS 部署约束下找到安全性与可用性的最佳平衡
- **Failure Analysis:** 用攻击场景检验方案，确保没有盲区

**AI Rationale:** 安全方案的特殊性在于"看不到的漏洞才是真正的风险"。选择从基础原则出发（避免惯性加规则），经约束过滤（避免过度设计），最后用失败分析验证（避免盲区），形成一条从原理到实践的完整链路。

## Technique Execution Results

### Phase 1: First Principles Thinking

**Interactive Focus:** 从数据敏感性排序 → 威胁画像 → 核心防御策略，自底向上重建安全基线

**Key Breakthroughs:**

- **数据敏感性排序：** 位置树+物品名称 > 照片 > 其他（备注/标签/账户信息）
  - 位置树+物品名称 = 家庭内部地图，入室盗窃者最想要的情报
  - 照片可能无意拍到文件、屏幕、人脸等敏感内容
  - 备注/标签敏感度最低
- **主要威胁：** 自动化 bots 暴力破解（A）+ 家庭成员凭证填充（D）
- **核心策略优先级：** 2FA > 速率限制 > 密码规则
- **关键洞察：** 密码规则再强也防不住凭证填充——密码已经在别处泄露了。2FA 是唯一有效应对手段

**User Creative Strengths:** 快速聚焦关键威胁、果断排除不匹配威胁（B/C）、清晰权衡优先级

### Phase 2: Constraint Mapping

**Interactive Focus:** 在 2FA 优先策略下，逐一映射部署环境、用户水平、成本约束，形成可执行的技术选型

**Key Decisions:**

- **2FA 方案：** TOTP + 邮箱验证码 两者并列，用户任选其一
  - TOTP：无外部依赖，离线可用
  - 邮箱验证码：管理员配置 SMTP，用户仅需提供邮箱地址，零学习成本
  - WebAuthn：排除（实现成本高、需要域名）
  - 恢复码：作为兜底方案
- **强制策略：** 所有用户（权限相同）强制开启 2FA，无例外
- **速率限制：** ASP.NET Core 内置限流中间件（NAS+VPS 通用）+ fail2ban（仅 VPS 公网部署）
- **密码规则（兜底层）：**
  - 最小 8 位，最大 128 位
  - 禁止纯数字密码
  - 拒绝 top 100 常见密码
  - 不强制复杂度（大小写/特殊字符）—— 2FA 是真正的防线
  - 不做历史限制

**User Creative Strengths:** 务实选型、果断排除不合适方案、提出纯数字禁止的边界条件

### Phase 3: Failure Analysis

**Interactive Focus:** 以攻击者视角逐个测试三通道 2FA 方案，覆盖窗口期攻击、恢复码泄露、会话劫持、数据库拖库、管理员恢复五个场景

**Key Decisions:**

- **窗口期攻击（场景 D）：** 四层防护 — 设置 2FA 前重输密码 + 新设备注册邮件通知 + 仅允许同一 session 设置 + 管理员可查看 2FA 状态。窗口期 1 小时
- **恢复码安全：** 只显示一次，不存储明文，存储用 SHA-256 哈希（不加盐，高熵原值无需盐）
- **TOTP 密钥存储：** ASP.NET Core Data Protection API 加密（AES-256-CBC + HMAC），Docker 部署需持久化 key ring 目录
- **会话劫持：** 个人移动设备使用场景下风险可接受，暂不做 IP 绑定等增强
- **管理员恢复：** VPS SSH 进入后执行 CLI 命令手动重置 2FA

**User Creative Strengths:** 精准识别高风险场景、提出 WebAuthn 作为主方案的转折点、对低风险场景合理取舍（会话劫持）

## Final Output: 安全改进清单

### P0 — 第一道防线：2FA

| # | 改进项 | 详情 |
|---|--------|------|
| 1 | **WebAuthn/Passkey 支持** | 推荐方案，指纹/面容登录，.NET 10 原生 FIDO2 API |
| 2 | **TOTP 支持** | 备选方案，Authenticator App |
| 3 | **邮箱验证码** | 管理员配置 SMTP，用户提供邮箱即可 |
| 4 | **恢复码** | 8 个 10 位码，只显示一次，SHA-256 哈希存储 |
| 5 | **强制 2FA** | 所有用户强制，首次登录 1 小时内完成设置 |

### P1 — 第二道防线：速率限制

| # | 改进项 | 详情 |
|---|--------|------|
| 6 | **ASP.NET Core 限流中间件** | 登录端点 IP+账户级别限流，NAS/VPS 通用 |
| 7 | **fail2ban 集成** | VPS 部署时 OS 层封 IP，提供配置模板 |
| 8 | **2FA 设置窗口期防护** | 设置前重输密码 + 邮件通知 + 单 session 限制 |
| 9 | **管理员 2FA 状态面板** | 查看所有用户 2FA 设置状态 |

### P2 — 第三道防线：密码规则

| # | 改进项 | 详情 |
|---|--------|------|
| 10 | **最小长度 8 位** | 当前 4→8 |
| 11 | **禁止纯数字密码** | 自定义 PasswordValidator |
| 12 | **Top 100 常见密码黑名单** | 拒绝 password/12345678 等 |
| 13 | **最大长度 128 位** | DoS 防护 |

### P3 — 数据保护 & 恢复

| # | 改进项 | 详情 |
|---|--------|------|
| 14 | **TOTP 密钥加密存储** | Data Protection API，Docker 持久化 key ring |
| 15 | **管理员 CLI 2FA 重置** | dotnet boxwise admin reset-2fa --user \<name\>

### Session Highlights

**User Creative Strengths:** 快速聚焦关键威胁、果断排除不匹配方案、提出 WebAuthn 作为转折点、对低风险场景合理取舍
**Breakthrough Moments:** 2FA > 限流 > 密码规则的优先级反转、有域名后 WebAuthn 重新入选、纯数字禁止的边界条件
**Energy Flow:** 从安全基线建立（Phase 1）到技术选型（Phase 2）到攻击验证（Phase 3），层层递进无冗余
