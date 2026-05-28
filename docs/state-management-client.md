# 状态管理

> BoxWise.Client — Blazor WASM 状态管理模式

## 核心: AppState（全局单例）

`AppState` 是 `Singleton` 生命周期注册的中心化状态容器，采用事件驱动的通知模式。

```csharp
// DI 注册 (Program.cs)
builder.Services.AddSingleton<AppState>();
```

### 属性

| 属性 | 类型 | 说明 |
|------|------|------|
| `CurrentUserName` | `string?` | 当前登录用户名，null = 未登录 |
| `IsAdmin` | `bool` | 管理员标志 |
| `IsLoggedIn` | `bool` (计算) | `CurrentUserName is not null` |
| `ContinuousLocationId` | `int?` | 连续收纳模式的位置 ID |
| `ContinuousLocationName` | `string?` | 连续收纳模式的位置名称 |

### 方法

| 方法 | 说明 |
|------|------|
| `SetUser(userName, isAdmin)` | 登录成功后设置用户状态 |
| `Clear()` | 登出时清空所有状态 |
| `SetContinuousLocation(locationId, locationName)` | 保存物品后预填下次位置 |
| `ClearContinuousLocation()` | 取消连续收纳模式 |

### 事件通知

```csharp
public event Action? StateChanged;
```

所有订阅者（组件）在 `StateChanged` 触发时调用 `StateHasChanged()` 重新渲染。

### 订阅者

| 消费者 | 订阅方式 |
|--------|---------|
| `MainLayout.razor` | `OnInitialized` 中 `AppState.StateChanged += StateHasChanged` |
| `Home.razor` | 读取 `AppState.IsAdmin` 控制"管理后台"按钮 |
| `ContinuityBanner.razor` | 读取 `ContinuousLocationId` / `ContinuousLocationName` |
| `ItemEntry.razor` | 读取 `ContinuousLocationId` 预填位置，保存后调用 `SetContinuousLocation` |

---

## 认证状态: CookieAuthenticationStateProvider

`CookieAuthenticationStateProvider : AuthenticationStateProvider`

双重 DI 注册方式:
```csharp
builder.Services.AddScoped<CookieAuthenticationStateProvider>();
builder.Services.AddScoped<AuthenticationStateProvider>(sp =>
    sp.GetRequiredService<CookieAuthenticationStateProvider>());
```

**工作流:**
1. 浏览器加载 → `GetAuthenticationStateAsync()` 调用 `GET /api/auth/me`
2. Cookie 有效 → 构建 `ClaimsPrincipal` (Name claim + "IsAdmin" claim)
3. Cookie 无效/过期 → 返回匿名 `ClaimsPrincipal`
4. `AuthorizeRouteView` 自动响应认证状态变化

---

## 组件本地状态

| 组件 | 本地状态 | 模式 |
|------|---------|------|
| `Home.razor` | `_query`, `_items`, `_loading`, `_error` | 字段 + `StateHasChanged` |
| `Browse.razor` | `_items`, `_loading`, `_error`, `_locationId`, `_tagIds` | 字段 + EventCallback |
| `ItemDetail.razor` | `_item`, `_loading`, `_notFound`, `_isDeleting` | 字段 + 参数 Id |
| `ItemEntry.razor` | `_name`, `_note`, `_selectedLocationId`, `_photo`, `_saving`, `_isRecognizing` | 字段 + IDisposable |
| `Login.razor` | `LoginModel` (内部类), `_error`, `_isLoading` | 内部类 + 字段 |
| `Settings.razor` | 无本地状态 | 纯导航入口 |

---

## 状态传递路径

```
用户登录 → AuthService → AppState.SetUser() → StateChanged → UI 刷新
     ↑
Cookie 恢复 → CookieAuthenticationStateProvider → ClaimsPrincipal → AuthorizeRouteView

物品保存 → ItemEntry → ItemEntryService.CreateItemAsync()
                  → ImageUploader → POST /api/images/upload
                  → AppState.SetContinuousLocation()
                  → Navigation → Home
```

---

## 跨组件通信

| 方向 | 机制 |
|------|------|
| 父 → 子 | `[Parameter]` 属性传递 |
| 子 → 父 | `EventCallback<T>` |
| 兄弟/全局 | `AppState` 事件 |
| 对话框 ↔ 宿主 | `[CascadingParameter] IMudDialogInstance` + `MudDialog.Close()` |
