# Acceptance Auditor — Story 4.1 搜索功能

请审查 diff 与规格说明（Story 4.1）的符合程度。检查 Acceptance Criteria 违反、规约偏差、遗漏实现、规格约束与代码矛盾。

## Story 4.1 AC

1. **AC-1: 搜索 API** — `GET /api/items?q={keyword}`，EF Core LIKE 模糊匹配物品名称、备注和标签，返回 `ItemSummaryDto[]` + `X-Total-Count` 响应头
2. **AC-2: 搜索结果展示** — 列表展示缩略图 + 名称 + 位置路径 + 标签，按名称匹配优先排列
3. **AC-3: 空结果处理** — 搜索无匹配时显示 EmptyState 空状态提示
4. **AC-4: 导航到详情** — 点击某个搜索结果跳转至物品详情页 `/items/{id}`
5. **AC-5: 搜索栏组件** — `SearchBar.razor` 组件，MudTextField + Adornment 搜索图标，防抖 300ms
6. **AC-6: 认证保护** — 搜索端点和页面均需登录

## Diff

(详见完整 diff)
