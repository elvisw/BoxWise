# Epic 4 回顾 — 查找浏览与生产部署

**日期:** 2026-05-24
**Epic:** 4 — 查找浏览与生产部署
**状态:** 完成（6/6 Stories）

## 交付总结

| Story | 内容 | CR 发现 | 关键模式 |
|-------|------|---------|---------|
| 4.1 | 全文搜索 + 防抖搜索栏 | HIGH: 防抖失效 + 竞态 | EF Core LIKE + Timer 防抖 |
| 4.2 | 缩略图网格浏览 | LOW: HTTP 失败无反馈 | MudGrid 响应式 + ItemCard |
| 4.3 | 位置与标签筛选 | HIGH: @bind- 不触发 | IQueryable 组合 + 物化路径 |
| 4.4 | 物品详情与删除 | HIGH: I/O 未保护 | 级联删除 + MudOverlay |
| 4.5 | PWA 离线支持 | CRITICAL: SW 崩溃 | SWR 缓存策略 |
| 4.6 | Docker 容器化部署 | CRITICAL: Caddy 配置 | 多阶段构建 |

## 关键教训

### 1. MudBlazor 9.x API 兼容性
每个 Story 都遭遇 MUD0002 分析器错误。CLAUDE.md 中维护的 API 参考表是有效的解决方案。

### 2. 客户端生命周期陷阱
`OnParametersSet` 不响应 `@bind-` 内部状态变更，4.1 和 4.3 两次踩坑。统一改为手动 `ValueChanged` 回调。

### 3. CR 价值不可替代
6 个 Story 的 CR 总共发现 20+ 个问题，包括 2 个 CRITICAL、5 个 HIGH。无 CR 的代码不应合并。

### 4. Repository 统一抽象
`GetFilteredAsync` 替代了 `GetAllAsync` + `SearchAsync`，IQueryable 组合模式可复用。

### 5. 基础设施配置需审查
Docker/Caddy/PWA 配置虽简单但细节致命（localhost vs 服务名、端口映射、SPA 回退）。

## 项目收官指标

- 全部 4 个 Epic 完成
- 28 个单元测试，0 失败
- 18 个 Story（实际提交）
- 0 已知回归缺陷
