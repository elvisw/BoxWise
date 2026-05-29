---
name: boxwise-runtime-gotchas
description: BoxWise 运行时常见问题 — DI 注册顺序、端口配置、MudBlazor 9.x API
metadata: 
  node_type: memory
  type: project
  originSessionId: 5724e44f-9a68-4c00-ab51-20fba6c3f023
---

## Client DI 注册顺序

`HttpClient` 必须最先注册（在所有依赖它的 Service 之前），否则 `WebAssemblyHostBuilder` 验证 DI 图时报 `CannotResolveService`。

`CookieAuthenticationStateProvider` 需要双注册：
```csharp
builder.Services.AddScoped<CookieAuthenticationStateProvider>();
builder.Services.AddScoped<AuthenticationStateProvider>(sp => sp.GetRequiredService<CookieAuthenticationStateProvider>());
```

## 端口配置

| 组件 | 端口 |
|------|------|
| Server HTTPS | 5000 |
| Client HTTPS | 5001 |
| Client BaseAddress | https://localhost:5000/ |
| CORS 允许源 | https://localhost:5001 |

`ERR_CONNECTION_REFUSED` → 检查 `Properties/launchSettings.json` 与实际监听端口一致。

## MudBlazor 9.x 关键 API

- MudTreeView: `SelectedValue` (非 `ActivatedValue`), 用 `TreeItemData<T>`, 内容在 `BodyContent`
- MudChipSet: `SelectionMode="SelectionMode.MultiSelection"` (非 Filter/MultiSelection), `IReadOnlyCollection<T>`
- MUD0002 分析器: 不要禁用，遵守 v9 命名约定

## 架构模式

- Repository: 返回 Entity，端点负责 DTO 映射
- 错误: `TypedResults.Problem()` 直接返回 (不嵌套)
- 所有端点加 `.ProducesProblem(401)`
- DTO: positional record in Shared.Dtos

**How to apply:** 遇到类似问题先检查这些配置
