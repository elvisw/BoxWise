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
| 认证 | ASP.NET Core Identity + Cookie |
| 图片处理 | SkiaSharp |
| AI 集成 | OpenAI 兼容 API |
| 测试 | xUnit + EF Core InMemory |
| 部署 | Docker + Caddy |

## 仓库结构

- **多部件:** Client (Blazor WASM) + Server (ASP.NET Core) + Shared (DTOs)
- **引用:** Client → Shared, Server → Shared + Client
- **测试:** Server.Tests → Server

## 功能概览

### 已实现 (MVP — Epic 1-4)

| Epic | 功能 |
|------|------|
| Epic 1 | 项目脚手架、用户认证、管理员管理 |
| Epic 2 | 位置管理（树形结构）、标签系统、位置/标签选择器 |
| Epic 3 | 物品录入（拍照+AI识别）、图片上传、物品详情 |
| Epic 4 | 搜索功能、缩略图网格浏览、位置/标签筛选、物品删除、PWA 离线、Docker 部署 |

### Admin 后台

- Server 端 Razor Pages（`/admin`）
- 管理员创建家庭成员账户
- `AdminOnly` 角色保护

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
