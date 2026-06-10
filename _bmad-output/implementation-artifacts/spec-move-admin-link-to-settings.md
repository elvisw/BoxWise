---
title: '将管理后台入口移至设置页面'
type: 'refactor'
created: '2026-06-10'
status: 'done'
route: 'one-shot'
---

## Intent

**Problem:** 管理后台入口按钮位于首页底部，与首页的物品搜索/浏览功能无关，管理员用户需要滚动到底部才能找到入口。

**Approach:** 将管理后台入口从 `Home.razor` 底部移至 `Settings.razor` 设置列表，放在位置管理/标签管理之后、关于之前，作为设置项展示，仅管理员可见。

## Suggested Review Order

- 移除首页的 admin 按钮、AdminUrl 属性和不再需要的 `@inject AppState` / `@inject HttpClient Http`
  [`Home.razor:1`](../../src/BoxWise.Client/Pages/Home.razor#L1)

- 设置页新增管理后台列表项，复用现有 `GetServerUrl` 跨端口逻辑，`AppState.IsAdmin` 守卫
  [`Settings.razor:69`](../../src/BoxWise.Client/Pages/Settings.razor#L69)

## Code Map

- `src/BoxWise.Client/Pages/Home.razor` -- 移除管理后台按钮、AdminUrl 属性、AppState/HttpClient 注入
- `src/BoxWise.Client/Pages/Settings.razor` -- 新增管理后台设置项（`@inject AppState` + admin button）

## Tasks & Acceptance

**Execution:**
- [x] `src/BoxWise.Client/Pages/Home.razor` -- 移除 `@inject AppState AppState`、`@inject HttpClient Http`、admin 按钮块、`AdminUrl` 属性 -- 清理首页不再需要的管理后台相关代码
- [x] `src/BoxWise.Client/Pages/Settings.razor` -- 新增 `@inject AppState AppState` + 管理后台设置列表项（`GetServerUrl("admin")`，`AppState.IsAdmin` 守卫）-- 将入口移至设置页

**Acceptance Criteria:**
- Given 管理员用户，when 打开设置页面，then 在标签管理下方看到"管理后台"入口
- Given 非管理员用户，when 打开设置页面或首页，then 不显示管理后台入口
- Given 开发环境 (localhost:5001)，when 点击管理后台，then 跳转到 `https://localhost:5000/admin`
- Given 生产环境，when 点击管理后台，then 跳转到 `/admin`

## Verification

## Review Findings

- [x] [Review][Defer] `GetServerUrl` 非 loopback 场景 URL 解析 [Settings.razor:102] — `GetServerUrl` 直接读取 `Config["ApiBaseUrl"]`，当通过局域网 IP 访问时 URL 指向 `localhost` 不可达。预存问题，`管理账户设置` 和 `退出登录` 同样使用此方法。修复方向：改用已注入的 `Http.BaseAddress`。— deferred, pre-existing
- [x] [Review][Defer] `AppState.IsAdmin` 无变更通知 [Settings.razor:69] — 管理员状态在页面渲染后变更时按钮不响应。与代码库惯例一致（仅 MainLayout 订阅 StateChanged）。— deferred, pre-existing
- [x] [Review][Defer] 管理后台按钮无 `Target="_blank"` [Settings.razor:74] — 与旧 Home.razor 行为一致。— deferred, pre-existing

**Commands:**
- `dotnet build` -- expected: 0 errors, 0 warnings
- `dotnet test BoxWise.slnx` -- expected: all 279 tests pass
