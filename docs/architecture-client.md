# 架构文档 — BoxWise.Client

> Blazor WASM PWA 前端

## 执行摘要

BoxWise.Client 是基于 Blazor WebAssembly 的单页应用（SPA），采用 MudBlazor 9.x 组件库，支持 PWA 离线安装。通过 Cookie 认证与 Server 端 API 通信。

## 技术栈

| 层 | 技术 | 版本 |
|----|------|------|
| 运行时 | .NET Blazor WebAssembly | 10.0 |
| UI | MudBlazor | 9.4.0 |
| 认证 | ASP.NET Core Identity + Cookie | 10.0.8 |
| PWA | Service Worker + Web Manifest | - |
| JS 互操作 | IJSRuntime + ES Modules | - |

## 架构模式

**组件层级 + 服务层**

```
MainLayout (主题/导航容器)
├── Pages/ (7 路由页面)
│   ├── Home          → ItemService, AppState
│   ├── Browse        → ItemService, LocationTree, TagFilter
│   ├── ItemEntry     → ItemEntryService, AiService, ImageUploader
│   ├── ItemDetail    → ItemService
│   ├── Login         → AuthService
│   ├── Settings      → AuthService, LocationManageDialog, TagManageDialog
│   └── NotFound
├── Components/ (9 可复用组件)
│   └── ItemCard, LocationTree, TagFilter, ImageUploader...
├── Services/ (9 服务类)
│   └── HttpClient → Server API
└── Shared DTOs (BoxWise.Shared)
```

## 数据架构

### 状态管理
- **AppState** (Singleton): 全局用户状态 + 连续收纳模式
- **CookieAuthenticationStateProvider**: Blazor ↔ ASP.NET Identity 桥接
- **组件本地状态**: 每个页面维护自己的加载/错误/数据字段
- **EventCallback**: 父子组件数据流

### API 消费
- 所有 API 调用通过 `HttpClient` + `CookieHandler`（`BrowserRequestCredentials.Include`）
- 开发环境跨源 `localhost:5001` → `localhost:5000`
- 生产环境同源部署

## 路由设计

| 路由 | 页面 | 认证 |
|------|------|------|
| `/` | Home | 需要 |
| `/browse` | Browse | 需要 |
| `/entry` | ItemEntry | 需要 |
| `/items/{id}` | ItemDetail | 需要 |
| `/login` | Login | 匿名 |
| `/settings` | Settings | 需要 |
| `/not-found` | NotFound | 匿名 |

## 组件设计

### 核心页面
1. **Home** — 搜索 + 物品网格，300ms 去抖
2. **Browse** — 位置树 + 标签芯片双筛选
3. **ItemEntry** — 拍照 → AI → 表单 → 保存流程
4. **ItemDetail** — 详情 + 图片 + 删除确认
5. **Settings** — 列表入口 + 弹窗管理

### 关键组件
- **ImageUploader** — JS 互操作调起原生相机（`capture="environment"`）
- **LocationTree** — `MudTreeView<LocationDto>` 树形选择
- **TagFilter** — `MudChipSet` 多选标签
- **LocationManageDialog / TagManageDialog** — 完整 CRUD 弹窗

## 开发工作流

```bash
cd src/BoxWise.Client && dotnet run
# → https://localhost:5001 (热重载)
```

## 测试策略

- 客户端目前无独立测试项目
- 通过 Server 端集成测试 + E2E 测试覆盖

## PWA

- Service Worker 缓存策略（开发/生产两套）
- Web Manifest: `manifest.webmanifest`
- 图标: 192px + 512px
