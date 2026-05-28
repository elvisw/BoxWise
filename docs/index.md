# BoxWise 项目文档索引

## 项目概述

- **项目:** 箱知 · BoxWise — 家庭物品收纳管理 PWA
- **类型:** 多部件（3 部分：Client + Server + Shared）
- **主语言:** C# (.NET 10)
- **架构:** Blazor WASM PWA + ASP.NET Core Minimal API + SQLite

## 快速参考

### Client — Blazor WASM PWA
- **类型:** Web 前端
- **技术栈:** .NET 10, Blazor WebAssembly, MudBlazor 9.4
- **根目录:** `src/BoxWise.Client/`

### Server — ASP.NET Core API
- **类型:** 后端
- **技术栈:** .NET 10, Minimal API, EF Core SQLite, Identity + Cookie
- **根目录:** `src/BoxWise.Server/`

### Shared — DTO 库
- **类型:** 类库
- **技术栈:** .NET 10, 零外部依赖
- **根目录:** `src/BoxWise.Shared/`

## 生成的文档

### 核心文档

- [项目概览](./project-overview.md)
- [源码树分析](./source-tree-analysis.md)
- [开发指南](./development-guide.md)
- [部署指南](./deployment-guide.md)
- [认证与安全](./auth-security.md)
- [集成架构](./integration-architecture.md)

### 架构文档（按部件）

- [架构 - Client](./architecture-client.md) — Blazor WASM 前端
- [架构 - Server](./architecture-server.md) — ASP.NET Core 后端
- [架构 - Shared](./architecture-shared.md) — 共享 DTO

### 专项文档

- [API 合约 - Server](./api-contracts-server.md) — 全部 6 组端点
- [数据模型](./data-models-server.md) — EF Core 实体与数据库
- [UI 组件清单](./component-inventory-client.md) — 7 页面 + 9 组件
- [状态管理](./state-management-client.md) — AppState + 认证状态

## 已有文档（项目自带）

- [README](../README.md) — 项目介绍与快速开始
- [LICENSE](../LICENSE) — GPLv3
- [CLAUDE.md](../CLAUDE.md) — AI 开发上下文（架构/规范/决策）
- [Dockerfile](../Dockerfile) — 容器构建
- [GitHub Actions](../.github/workflows/release.yml) — CI/CD 发布

## BMad 工作流产出

- [规划工件](../_bmad-output/planning-artifacts/) — PRD, UX 设计, 架构, Epic
- [实现工件](../_bmad-output/implementation-artifacts/) — Story 文档, Sprint 状态, 回顾

## Superpowers 设计文档

- [拍照功能设计规格](./superpowers/specs/2026-05-26-camera-capture-design.md)
- [拍照功能实现计划](./superpowers/plans/2026-05-26-camera-capture.md)
- [设置页重构设计规格](./superpowers/specs/2026-05-27-settings-navigation-design.md)
- [设置页实现计划](./superpowers/plans/2026-05-27-settings-navigation-plan.md)

## 快速开始

### 环境要求
- .NET 10 SDK

### 本地开发

```bash
# 构建
dotnet build

# 启动 Server (API + Admin + SPA)
cd src/BoxWise.Server && dotnet run   # → https://localhost:5000

# 启动 Client (热重载, 推荐 UI 开发)
cd src/BoxWise.Client && dotnet run   # → https://localhost:5001
```

### 运行测试

```bash
dotnet test BoxWise.slnx
```

### 创建管理员

```bash
Admin__Password="your-password" dotnet run
# → https://localhost:5000/admin
```

---

**文档生成日期:** 2026-05-27 | **扫描级别:** 详尽扫描 | **工作流版本:** 1.2.0
