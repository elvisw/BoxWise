# Investigation: 首页搜索功能不起作用

## Hand-off Brief

1. **What happened.** Home.razor 的搜索功能在用户输入时完全不触发——`OnParametersSetAsync` 是 Blazor 用于接收父组件参数的生命周期钩子，无法响应 `@bind-Value` 引起的内部状态变更。
2. **Where the case stands.** 根因已确认，修复方向明确。
3. **What's needed next.** 将搜索触发逻辑从 `OnParametersSetAsync` 迁移到合适的机制（`@bind-Value:after` 或属性 setter）。

## Case Info

| Field            | Value                                                                      |
| ---------------- | -------------------------------------------------------------------------- |
| Ticket           | N/A                                                                        |
| Date opened      | 2026-05-27                                                                 |
| Status           | Active                                                                     |
| System           | BoxWise Client (Blazor WASM .NET 10), MudBlazor 9.x                        |
| Evidence sources | src/BoxWise.Client/Pages/Home.razor, src/BoxWise.Client/Services/ItemService.cs, src/BoxWise.Server/Endpoints/ItemEndpoints.cs, src/BoxWise.Server/Repositories/ItemRepository.cs |

## Problem Statement

用户报告：首页的搜索功能似乎不起作用。具体表现为在搜索框中输入关键词后，物品列表不进行过滤。

## Evidence Inventory

| Source   | Status    | Notes                                                                           |
| -------- | --------- | ------------------------------------------------------------------------------- |
| Home.razor | Available | 完整源码 — 搜索逻辑在 `OnParametersSetAsync` 中                         |
| ItemService.cs | Available | 完整源码 — `SearchAsync` 和 `GetFilteredAsync` 构建 URL 正确            |
| ItemEndpoints.cs | Available | 完整源码 — `GET /api/items?q=...` 端点实现正确                        |
| ItemRepository.cs | Available | 完整源码 — `GetFilteredAsync` 查询逻辑正确                              |

## Timeline of Events

| Time        | Event               | Source                | Confidence            |
| ----------- | ------------------- | --------------------- | --------------------- |
| N/A | 用户打开首页 | Home.razor:77 `OnInitializedAsync` → `LoadAsync()` | Confirmed |
| N/A | 用户在搜索框输入关键词 | Home.razor:11 `@bind-Value="_query"` 更新字段值 | Confirmed |
| N/A | 搜索未触发 | Home.razor:80 `OnParametersSetAsync` 未被调用 | Confirmed |

## Confirmed Findings

### Finding 1: 搜索触发逻辑放置在错误的生命周期方法中

**Evidence:** `src/BoxWise.Client/Pages/Home.razor:80` — `OnParametersSetAsync`

**Detail:** 搜索去抖逻辑（取消前次 CTS、300ms 延迟、调用 `ItemService.SearchAsync`）全部位于 `OnParametersSetAsync` 中。根据 Blazor 组件生命周期文档，`OnParametersSet[Async]` 仅在父组件向子组件传递参数时被调用，不会在内部状态（如 `@bind-Value` 绑定的 `_query` 字段）变更时触发。

### Finding 2: 无其他搜索触发机制

**Evidence:** `src/BoxWise.Client/Pages/Home.razor:8-15` — MudTextField 绑定

**Detail:** MudTextField 仅配置了 `@bind-Value="_query"` 和 `Immediate="true"`，没有 `@bind-Value:after` 回调、没有 `OnKeyUp`/`OnKeyDown` 事件处理、没有搜索按钮。唯一触发搜索的路径是 `OnParametersSetAsync`，该路径已确认无效。

### Finding 3: API 端点和 Repository 实现正确

**Evidence:** `src/BoxWise.Server/Endpoints/ItemEndpoints.cs:104-132` — `SearchItemsAsync`
`src/BoxWise.Server/Repositories/ItemRepository.cs:80-110` — `GetFilteredAsync`
`src/BoxWise.Client/Services/ItemService.cs:42-65` — `GetFilteredAsync`

**Detail:** 服务端搜索 API（`GET /api/items?q=...`）正确接收查询参数，Repository 的 `GetFilteredAsync` 正确使用 `Contains` 在名称、备注和标签中搜索。客户端 `ItemService` 正确构建 URL 并对查询参数进行 `Uri.EscapeDataString` 编码。如果 API 被调用，搜索应正常工作。

### Finding 4: 首页初始加载正常

**Evidence:** `src/BoxWise.Client/Pages/Home.razor:77` — `OnInitializedAsync` → `LoadAsync()`

**Detail:** 初始加载通过 `LoadAsync()` 正确调用 `GetFilteredAsync(null, null, null, ...)` 获取所有物品并显示。用户应该能看到物品列表，但输入搜索关键词后列表不会变化。

## Deduced Conclusions

### Deduction 1: 搜索框输入永远不会触发 API 调用

**Based on:** Finding 1, Finding 2

**Reasoning:** 搜索触发逻辑的唯一入口是 `OnParametersSetAsync`。该钩子在 Blazor 组件生命周期中仅在父组件设置参数时调用。作为顶级路由页面（`@page "/"`），Home.razor 没有父组件传递参数。用户输入通过 `@bind-Value` 更新 `_query` 字段，触发的是内部 `StateHasChanged()` 而非 `SetParametersAsync`，因此 `OnParametersSetAsync` 不会被调用。

**Conclusion:** 无论用户在搜索框中输入什么，搜索 API 调用永远不会发生。

## Source Code Trace

| Element       | Detail                                      |
| ------------- | ------------------------------------------- |
| Error origin  | `src/BoxWise.Client/Pages/Home.razor:80` — `OnParametersSetAsync` |
| Trigger       | 用户输入触发 `@bind-Value` → `_query` 更新 + `StateHasChanged()` |
| Condition     | `OnParametersSetAsync` 在内部状态变更时不调用 |
| Related files | `src/BoxWise.Client/Services/ItemService.cs` — `SearchAsync`/`GetFilteredAsync` |
| Related files | `src/BoxWise.Server/Endpoints/ItemEndpoints.cs` — `SearchItemsAsync` |
| Related files | `src/BoxWise.Server/Repositories/ItemRepository.cs` — `GetFilteredAsync` |

## Conclusion

**Confidence:** High

**根因：** 搜索去抖逻辑错误地放置在 `OnParametersSetAsync` 生命周期方法中。Blazor 的 `OnParametersSet[Async]` 仅在父组件传递参数时调用，不会在 `@bind-Value` 引起的内部状态变更时触发。用户输入 → `_query` 更新 → `StateHasChanged()` → 重新渲染，但 `OnParametersSetAsync` 从未被调用，导致搜索 API 调用永远不会发生。

API 端点和 Repository 实现均正确，问题仅在前端触发机制。

## Recommended Next Steps

### Fix direction

将搜索触发逻辑从 `OnParametersSetAsync` 迁移到 `@bind-Value:after` 回调或自定义属性 setter。推荐使用 `MudTextField` 的 `@bind-Value:after` 参数，这是 MudBlazor 9.x 支持的方式，改动最小。

修复文件：`src/BoxWise.Client/Pages/Home.razor`

改动要点：
1. 添加一个 `OnQueryChanged()` 方法，将原 `OnParametersSetAsync` 中的搜索去抖逻辑移入
2. 在 MudTextField 上添加 `@bind-Value:after="OnQueryChanged"`
3. 移除 `OnParametersSetAsync` 或保持空实现

### Diagnostic

无需额外诊断步骤——根因通过源码静态分析已确认，可通过以下步骤验证修复：
1. 启动应用，打开首页，确认物品列表正常加载
2. 在搜索框输入关键词，确认 300ms 去抖后列表被过滤
3. 快速连续输入，确认只有最后一次搜索生效（CancellationToken 去抖正确）
4. 清除搜索框内容，确认列表恢复全量

## Reproduction Plan

1. 启动应用：`cd src/BoxWise.Server && dotnet run`
2. 访问 `https://localhost:5000` 或 `https://localhost:5001`
3. 登录后进入首页，确认有物品显示
4. 在搜索框输入物品名称关键词
5. **预期（修复前）：** 列表不变，搜索不触发
6. **预期（修复后）：** 300ms 后列表过滤为匹配项

## Side Findings

- `OnParametersSetAsync` 中 `if (_loading) return;` 的守卫条件也有问题——`_loading` 初始值为 `true`，在 `OnInitializedAsync` 完成后变为 `false`，时机依赖隐式排序。不过当前场景下这不构成实际问题。
- `OnParametersSetAsync` 中 `_searchCts` 未在组件 `Dispose` 时释放——非当前问题，但建议在 `IDisposable.Dispose` 中处理。
