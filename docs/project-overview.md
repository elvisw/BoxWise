# 项目概览

> 箱知 · BoxWise — 家庭物品收纳管理 PWA

## 简介

拍照 → AI 识别 → 选位置 → 保存，搜一下就知道东西在哪。

BoxWise 是一款面向家庭用户的物品收纳管理应用。用户通过拍照录入物品，AI 自动识别物品名称和属性，选择收纳位置和标签后保存。支持按位置、标签、关键词搜索物品，PWA 支持移动端安装和离线访问。

## 技术栈速览

| 层 | 技术 |
|----|------|
| 前端 | Blazor WASM (PWA) + MudBlazor 9.x |
| 后端 | ASP.NET Core Minimal API (.NET 10) |
| 数据库 | SQLite + EF Core |
| 认证 | ASP.NET Core Identity + Cookie + WebAuthn 通行密钥 |
| 图片处理 | SkiaSharp |
| AI 集成 | OpenAI 兼容 API |
| 测试 | xUnit + EF Core InMemory |
| 部署 | Docker + Caddy |

## 仓库结构

- **多部件:** Client (Blazor WASM) + Server (ASP.NET Core) + Shared (DTOs)
- **引用:** Client → Shared, Server → Shared + Client
- **测试:** Server.Tests → Server, Client.Tests → Client

## 功能概览

### 已实现 (Epic 1-11 — 全部完成)

| Epic | 功能 |
|------|------|
| Epic 1 | 项目脚手架、用户认证、管理员管理 |
| Epic 2 | 位置管理（树形结构）、标签系统、位置/标签选择器 |
| Epic 3 | 物品录入（拍照+AI识别）、图片上传、物品详情 |
| Epic 4 | 搜索功能、缩略图网格浏览、位置/标签筛选、物品删除、PWA 离线、Docker 部署 |
| Epic 5 | 物品编辑功能（更新名称/位置/标签/备注，并发冲突处理） |
| Epic 6 | 双因素认证基础（TOTP 设置/验证/恢复码，Email 2FA，宽限期，速率限制） |
| Epic 7 | 设置页重构（4 Tab 底部导航：账户/位置管理/标签管理/关于） |
| Epic 8 | WebAuthn 通行密钥（注册/管理/登录，Fido2NetLib 集成，混合 2FA，CSRF 保护） |
| Epic 9 | Admin 增强（用户列表 2FA 状态、管理员重置 2FA、账户编辑、密码修改、SMTP 配置管理） |
| Epic 10 | 账户资料管理（修改用户名/邮箱、更换密码、重新认证验证） |
| Epic 11 | Identity 脚手架迁移（Login/Register/2FA Razor Pages 纳入仓库管理，TwoFactorEndpoints 退役） |

### 详细功能说明

#### 物品管理
- 拍照或上传图片，AI 自动识别物品名称和备注
- 选择收纳位置（树形层级）和标签分类
- 编辑已存物品的名称、位置、标签和备注
- 多级缩略图（300px 缩略图 + 1200px 中图 + 原图）
- 按位置、标签、关键词搜索/筛选，支持批量查询

#### 用户认证与安全
- ASP.NET Core Identity + Cookie 认证
- 双因素认证（2FA）：TOTP 验证器应用、Email 验证码、WebAuthn 通行密钥
- 2FA 宽限期机制：首次设置后 7 天内仅首次登录验证
- 通行密钥无密码登录（passkey login）
- 恢复码生成与验证（SHA-256 哈希存储）
- CSRF 防护（写操作端点 `CsrfValidationFilter`）
- 速率限制（登录 5次/15分钟，密码修改 3次/5分钟，通行密钥 30次/5分钟）

#### 管理员后台
- Server 端独立 Razor Pages（`/admin`），`AdminOnly` 角色保护
- 用户管理：创建账户、编辑用户名/邮箱、修改密码
- 2FA 管理：查看用户 2FA 状态、管理员重置 2FA（清除所有设置/恢复码/通行密钥）
- SMTP 配置：Host/Port/用户名/密码/发件人设置，DPAPI 加密存储，测试邮件发送

#### 设置页
- 4 Tab 底部导航：账户信息、位置管理、标签管理、关于
- 位置管理弹窗：创建/重命名/删除树形位置
- 标签管理弹窗：创建/重命名/删除标签
- 账户信息：修改用户名、邮箱、密码、2FA 配置

## 文档导航

- [架构 - Client](./architecture-client.md)
- [架构 - Server](./architecture-server.md)
- [架构 - Shared](./architecture-shared.md)
- [API 合约](./api-contracts-server.md)
- [数据模型](./data-models-server.md)
- [UI 组件清单](./component-inventory-client.md)
- [状态管理](./state-management-client.md)
- [认证与安全](./auth-security.md)
- [集成架构](./integration-architecture.md)
- [源码树分析](./source-tree-analysis.md)
- [开发指南](./development-guide.md)
- [部署指南](./deployment-guide.md)
- [主索引](./index.md)
