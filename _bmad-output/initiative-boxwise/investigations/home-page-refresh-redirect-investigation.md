# Investigation: 首页刷新后显示登录界面

## Hand-off Brief

1. **What happened.** 用户刷新或重新访问首页（`/`）时，页面渲染"请登录"提示而非已登录主页，但其他页面（`/browse`、`/entry`、`/items/{id}`）正常。根因是 `AppState` 在页面刷新后未被初始化——`CookieAuthenticationStateProvider.GetAuthenticationStateAsync()` 成功认证了用户，但没有调用 `AppState.SetUser()`，导致 `Home.razor` 中的 `AppState.IsLoggedIn` 检查失败。
2. **Where the case stands.** 结论 **Medium** 置信度 — `AppState` 未初始化已 Confirm，但 CookieHandler 是否正确工作需实际运行验证。
3. **What's needed next.** 在 `CookieAuthenticationStateProvider.GetAuthenticationStateAsync()` 认证成功时调用 `AppState.SetUser()` 填充客户端状态，或在应用启动时通过认证状态初始化 `AppState`。

## Case Info

| Field            | Value                                                                                   |
| ---------------- | --------------------------------------------------------------------------------------- |
| Ticket           | N/A                                                                                     |
| Date opened      | 2026-05-25                                                                              |
| Status           | Concluded                                                                               |
| System           | .NET 10, Blazor WASM, Server: localhost:5000, Client: localhost:5001                    |
| Evidence sources | 源代码 (Home.razor, App.razor, CookieAuthenticationStateProvider.cs, AuthService.cs, AppState.cs, Program.cs) |

## Problem Statement

用户报告：刷新或者重新访问首页会进入登录界面，而不是登录后的首页。仅仅是首页有这个故障，其他页面是正常的。

## Evidence Inventory

| Source                                      | Status    | Notes                                                                 |
| ------------------------------------------- | --------- | --------------------------------------------------------------------- |
| Home.razor (源码)                           | Available | `@attribute [Authorize]` + `AppState.IsLoggedIn` 检查（行 11）         |
| Browse.razor (源码)                         | Available | `@attribute [Authorize]`，无 `AppState.IsLoggedIn` 检查               |
| ItemEntry.razor (源码)                      | Available | `@attribute [Authorize]`，仅使用 `AppState.ContinuousLocationId`      |
| ItemDetail.razor (源码)                     | Available | `@attribute [Authorize]`，完全不依赖 `AppState`                       |
| App.razor (源码)                            | Available | `AuthorizeRouteView` + `NotAuthorized` 渲染 Login 组件                |
| CookieAuthenticationStateProvider.cs (源码) | Available | 认证检查成功但不调用 `AppState.SetUser()`                              |
| AuthService.cs (源码)                       | Available | Login 时调用 `AppState.SetUser()`，但仅在 Login 流程中                |
| AppState.cs (源码)                          | Available | Scoped DI，刷新后重新创建，默认 `IsLoggedIn = false`                   |
| CookieHandler.cs (源码)                     | Available | 跨源 credentials 修复（未提交）                                       |
| Program.cs (Client)                         | Available | HttpClient 使用 CookieHandler，AppState Scoped                        |
| AuthEndpoints.cs (Server)                   | Available | `/api/auth/me` 返回 `AuthUserDto(UserName, IsAdmin)`                  |

## Investigation Backlog

| # | Path to Explore                                     | Priority | Status | Notes                                                        |
| - | --------------------------------------------------- | -------- | ------ | ------------------------------------------------------------ |
| 1 | 验证 CookieHandler 是否已生效                         | High     | Open   | 需要实际运行或检查编译产物                                    |
| 2 | 确认 `GET /api/auth/me` 在页面刷新时的实际返回       | High     | Open   | 浏览器控制台检查                                              |
| 3 | 检查是否有其他组件/页面依赖 `AppState.IsLoggedIn`      | Medium   | Done   | 仅 `Home.razor` 有检查                                        |
| 4 | 修复方案：`CookieAuthenticationStateProvider` 注入 `AppState` | High   | Open   | 需要在 `GetAuthenticationStateAsync` 成功时调用 `SetUser()` |

## Timeline of Events

| Time | Event                                                            | Source                                              | Confidence |
| ---- | ---------------------------------------------------------------- | --------------------------------------------------- | ---------- |
| 1    | 用户登录成功，`AuthService.LoginAsync` 调用 `AppState.SetUser()`  | AuthService.cs:26                                   | Confirmed  |
| 2    | 用户导航到其他页面（Browse/Entry/Detail），一切正常                | 用户报告                                            | Confirmed  |
| 3    | 用户刷新首页或重新访问 `/`                                        | 用户报告                                            | Confirmed  |
| 4    | Blazor WASM 重新初始化，所有 Scoped 服务（包括 AppState）重新创建  | DI 容器行为                                         | Confirmed  |
| 5    | `CookieAuthenticationStateProvider.GetAuthenticationStateAsync()` 调用 `GET api/auth/me` | CookieAuthenticationStateProvider.cs:20 | Confirmed |
| 6    | Server 返回 200 + `AuthUserDto`（Cookie 正确发送）                | AuthEndpoints.cs:56-68                              | Deduced   |
| 7    | `[Authorize]` 检查通过，Home.razor 被渲染                         | App.razor AuthorizeRouteView 行为                   | Deduced   |
| 8    | `AppState.IsLoggedIn` 为 false → 渲染 "请登录" 提示               | Home.razor:11-14                                    | Confirmed |

## Confirmed Findings

### Finding 1: 仅 Home.razor 检查 AppState.IsLoggedIn

**Evidence:** `src/BoxWise.Client/Pages/Home.razor:11`

**Detail:** Home.razor 是所有页面中唯一检查 `AppState.IsLoggedIn` 的页面：
- **Home.razor:11** — `@if (AppState.IsLoggedIn)` 决定渲染欢迎内容还是"请登录"提示
- **Browse.razor** — 无 AppState.IsLoggedIn 检查，直接加载数据
- **ItemEntry.razor** — 仅使用 `AppState.ContinuousLocationId/Name`，不检查 IsLoggedIn
- **ItemDetail.razor** — 完全不依赖 AppState

这解释了为什么故障仅影响首页——其他页面不依赖 `AppState` 的登录状态来判断是否渲染内容。

### Finding 2: AppState 仅在登录流程中被填充

**Evidence:** `src/BoxWise.Client/Services/AuthService.cs:26`

**Detail:**
```csharp
// AuthService.LoginAsync() — 仅在用户主动登录时调用
_appState.SetUser(user?.UserName ?? username, user?.IsAdmin ?? false);
```

`AppState.SetUser()` 只在 `AuthService.LoginAsync()` 中被调用。页面刷新后 `AppState`（Scoped）重新创建，`CurrentUserName` 为 null，`IsLoggedIn` 返回 false。没有任何代码在页面加载/刷新时从认证状态恢复 `AppState`。

### Finding 3: CookieAuthenticationStateProvider 不填充 AppState

**Evidence:** `src/BoxWise.Client/Services/CookieAuthenticationStateProvider.cs:16-44`

**Detail:** `GetAuthenticationStateAsync()` 成功获取用户信息后，只创建 `ClaimsPrincipal` 返回给 `AuthorizeRouteView`，不调用 `AppState.SetUser()`。认证层和状态管理层之间缺少桥接。

### Finding 4: 所有页面都有 [Authorize] 属性

**Evidence:** `src/BoxWise.Client/Pages/Home.razor:2`, `Browse.razor:2`, `ItemEntry.razor:2`, `ItemDetail.razor:2`

**Detail:** 所有主要页面都标注了 `@attribute [Authorize]`，经过相同的 `App.razor` → `AuthorizeRouteView` 认证流程。如果认证本身失败，所有页面应该同样受影响。但用户报告只有首页有问题，矛盾点的解决在于 Finding 1（首页额外检查 AppState）。

## Deduced Conclusions

### Deduction 1: 用户实际已通过认证，但 AppState 未初始化

**Based on:** Finding 1, Finding 2, Finding 3, Finding 4

**Reasoning:** 
1. 所有页面有 `[Authorize]`，如果 Cookie 未发送，所有页面都应显示 NotAuthorized → Login
2. 但用户说其他页面正常，说明 `GET /api/auth/me` 返回了 200（认证成功）
3. `[Authorize]` 通过后，Home.razor 被渲染，但额外检查了 `AppState.IsLoggedIn`
4. `AppState` 是 Scoped 服务，刷新后被重新创建，`IsLoggedIn` 为 false
5. 首页渲染"请登录"提示，用户认为进入了登录界面

**Conclusion:** 根因不是认证失败，而是认证成功后的状态同步缺失。

### Deduction 2: CookieHandler 使跨源认证正常工作（或用户从同一端口访问）

**Based on:** Deduction 1，CookieHandler.cs 存在

**Reasoning:** 如果 CookieHandler 未生效且用户从 `localhost:5001`（Client 开发服务器）访问，`GET /api/auth/me` 会跨源失败（无 credentials），所有页面均无法通过 `[Authorize]`。但用户报告其他页面正常，意味着 Cookie 在某处被正确携带。可能场景：(a) CookieHandler 已编译生效；(b) 用户从 `localhost:5000`（同一端口）访问。

## Hypothesized Paths

### Hypothesis 1: AppState 在刷新后未从认证状态恢复

**Status:** Confirmed

**Theory:** 页面刷新后 `AppState` 重新创建（Scoped），`IsLoggedIn` 默认 false。认证系统 (`CookieAuthenticationStateProvider`) 验证用户身份成功，但没有通知 `AppState`。`Home.razor` 是唯一同时检查 `[Authorize]` 和 `AppState.IsLoggedIn` 的页面，因此只有首页出现问题。

**Supporting indicators:**
- `AppState` 只在 `AuthService.LoginAsync()` 中被填充（Finding 2）
- `CookieAuthenticationStateProvider.GetAuthenticationStateAsync()` 成功时不调用 `AppState.SetUser()`（Finding 3）
- 仅 Home.razor 检查 `AppState.IsLoggedIn`（Finding 1）
- 所有页面有相同的 `[Authorize]`（Finding 4）

**Resolution:** Confirmed — 代码审查确认了所有 Supporting indicators。

## Missing Evidence

| Gap                         | Impact                                     | How to Obtain                                      |
| --------------------------- | ------------------------------------------ | -------------------------------------------------- |
| CookieHandler 实际运行时行为 | 确认跨源 Cookie 是否已正确携带             | 运行应用，浏览器 DevTools Network 检查 `/api/auth/me` |
| 用户实际看到的 UI            | 确认是 NotAuthorized Login 还是 Home 的 else | 请用户描述看到的界面细节                             |

## Source Code Trace

| Element       | Detail                                                                                    |
| ------------- | ----------------------------------------------------------------------------------------- |
| Error origin  | `src/BoxWise.Client/Pages/Home.razor:11` — `@if (AppState.IsLoggedIn)` 评估为 false      |
| Trigger       | 用户刷新或重新访问 `/` 路由，Blazor WASM 重新初始化                                       |
| Condition     | `AppState` Scoped 服务被重新创建，`CurrentUserName` 为 null，`IsLoggedIn` 返回 false      |
| Related files | `src/BoxWise.Client/Services/AppState.cs:7`, `src/BoxWise.Client/Services/CookieAuthenticationStateProvider.cs:34`, `src/BoxWise.Client/Services/AuthService.cs:26` |

## Conclusion

**Confidence:** High

根因是 `AppState` 客户端状态在页面刷新后未从认证会话恢复。运行时确认已完成 —— 用户看到的是 Home.razor 的 else 分支（"请登录以管理您的物品库"），而非 `NotAuthorized` 的完整 Login 组件，说明 `[Authorize]` 通过了但 `AppState.IsLoggedIn` 为 false。

**已修复：** `CookieAuthenticationStateProvider` 注入 `AppState`，`GetAuthenticationStateAsync()` 成功时调用 `_appState.SetUser()`。编译通过，0 错误。

## Recommended Next Steps

### Fix direction

**方案 A（最小改动）：在 `CookieAuthenticationStateProvider` 中注入 `AppState`**

修改 `CookieAuthenticationStateProvider.cs`，在认证成功时同步 `AppState`：

```csharp
public class CookieAuthenticationStateProvider : AuthenticationStateProvider
{
    private readonly HttpClient _http;
    private readonly AppState _appState;

    public CookieAuthenticationStateProvider(HttpClient http, AppState appState)
    {
        _http = http;
        _appState = appState;
    }

    public override async Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        try
        {
            var response = await _http.GetAsync("api/auth/me");
            if (response.IsSuccessStatusCode)
            {
                var user = await response.Content.ReadFromJsonAsync<AuthUser>();
                if (user is { UserName: not null and not "" })
                {
                    _appState.SetUser(user.UserName, user.IsAdmin);  // ← 新增
                    // ... 创建 ClaimsPrincipal ...
                }
            }
        }
        catch { }
        return new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity()));
    }
}
```

**方案 B（额外保险）：在 MainLayout 中初始化**

在 `MainLayout.razor` 的 `OnInitializedAsync` 中，通过 `AuthenticationStateProvider` 获取当前用户并填充 `AppState`，作为双重保险。

推荐**方案 A**，因为改动最小且逻辑内聚。

### Diagnostic

1. 打开浏览器 DevTools → Network 标签
2. 刷新首页，观察 `GET /api/auth/me` 请求的响应状态码
3. 如果返回 200，确认本报告的分析正确
4. 如果返回 401，说明 CookieHandler 仍存在问题，需要优先修复

## Reproduction Plan

1. 启动 Server (`cd src/BoxWise.Server && dotnet run`)
2. 启动 Client (`cd src/BoxWise.Client && dotnet run`)
3. 浏览器打开 `https://localhost:5001`
4. 登录
5. 导航到 `/browse` — 确认正常
6. 按 F5 刷新 `/browse` — 确认仍正常
7. 点击导航回到首页 `/`（或输入 URL `/`）
8. **Expect:** 显示欢迎信息和搜索框
9. **Actual (bug):** 显示"请登录以管理您的物品库"和"前往登录"按钮

## Side Findings

- `AuthService.cs:41` 和 `CookieAuthenticationStateProvider.cs:51` 各自定义了私有 `record AuthUserDto` / `record AuthUser`，与 `Shared/Dtos/AuthUserDto.cs` 中的公共 DTO 相同但无关。建议统一使用 Shared 中的公共 DTO，避免 JSON 反序列化时的类型冗余。
- `App.razor` 的 `<NotAuthorized>` 渲染 `<BoxWise.Client.Pages.Login />`（不改变 URL），而 Login.razor 本身 `@page "/login"` 也有独立路由。两个入口到同一组件，当前不会导致问题，但如果是渲染 Login 组件时 URL 为 `/`，Login 页面导航到 `/`（`Navigation.NavigateTo("/")`）可能产生"原地刷新"的效果。

## Follow-up: 2026-05-25

### New Evidence

用户确认运行时表现：首页刷新后看到的是"请登录以管理您的物品库"而非完整 Login 表单。证实 `[Authorize]` 通过但 `AppState.IsLoggedIn` 为 false。

### Additional Fixes Applied

1. **退出登录** — `MainLayout.razor` 添加 `MudAppBar` 顶栏 + 退出图标按钮，调用已有 `AuthService.LogoutAsync()`
2. **Admin 链接修复** — `Home.razor` 的 AdminUrl 从 `IConfiguration` 改为读取 `Http.BaseAddress`；生产环境 `BaseAddress` 为 null 时走根路径 `/admin`
3. **ApiBaseUrl 生产默认值** — `Program.cs` 默认值从 `"https://localhost:5000/"` 改为 `""`，生产环境不设 `BaseAddress`，所有请求走同源
4. **Client 开发配置** — 新建 `wwwroot/appsettings.Development.json`，显式配置 `ApiBaseUrl: "https://localhost:5000/"` 供开发环境使用

### Documentation Updated

- `CLAUDE.md` — 端口配置表重写，新增 ApiBaseUrl 双环境行为说明
- `README.md` — 本地开发章节新增 ApiBaseUrl 配置说明和生产环境备注
