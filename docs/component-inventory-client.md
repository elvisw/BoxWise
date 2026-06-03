# UI 组件清单

> BoxWise.Client — Blazor WASM 组件与页面

## 概览

- **UI 框架:** MudBlazor 9.5.0
- **组件模式:** 所有逻辑内嵌在 `.razor` 的 `@code {}` 块中
- **JS 互操作:** `camera-capture.js` (ES Module)
- **主题色:** Primary `#546E7A`, Secondary `#80CBC4`

---

## 页面 (7)

### 1. Home.razor — `/`
首页搜索 + 物品网格。即时搜索带 300ms 去抖，空状态占位图标，错误重试。管理员显示"管理后台"链接。

**MudBlazor:** `MudTextField`, `MudProgressCircular`, `MudIcon`, `MudText`, `MudButton`, `MudGrid`, `MudItem`

### 2. Browse.razor — `/browse`
位置/标签筛选浏览。两个筛选组件 (`LocationTree`, `TagFilter`) 通过 EventCallback 触发重新加载。

**MudBlazor:** `MudGrid`, `MudItem`, `MudText`, `MudDivider`, `MudProgressCircular`, `MudIcon`, `MudButton`

### 3. ItemDetail.razor — `/items/{id}`
物品详情与删除。展示完整信息（名称、位置路径、标签、备注、图片）。删除需确认。

**MudBlazor:** `MudProgressCircular`, `MudText`, `MudPaper`, `MudIcon`, `MudChip`, `MudButton`

### 4. ItemEntry.razor — `/entry`
物品录入主界面。拍照 → AI 识别 → 选位置 → 加标签 → 保存。支持"连续收纳"模式。

**MudBlazor:** `MudText`, `MudPaper`, `MudProgressCircular`, `MudTextField`, `MudDivider`, `MudButton`

### 5. Login.razor — `/login`
Cookie 认证登录表单。去掉了 Password 复杂度要求以降低使用门槛。支持通行密钥（Passkey）无密码登录。

**MudBlazor:** `MudContainer`, `MudPaper`, `MudText`, `MudTextField`, `MudAlert`, `MudButton`

### 6. Settings.razor — `/settings`
集中设置页。包含位置管理入口、标签管理入口、通行密钥凭证管理入口、退出登录按钮。列表式入口 + 弹窗模式。

**MudBlazor:** `MudText`, `MudIcon`, `MudDivider`, `MudPaper`

### 7. NotFound.razor — `/not-found`
404 占位页面。

---

## 可复用组件 (12)

### ContinuityBanner.razor
"连续收纳中"绿色提示横幅。读取 `AppState.ContinuousLocationId`，显示当前收纳位置并提供"取消连续收纳"按钮。

**参数:** 无（通过 `AppState` 注入读取）

### SearchBar.razor
搜索文本框（300ms 去抖）。**注意:** 页面中未实际使用此组件，Home.razor 使用内联实现。

**参数:** `EventCallback<string> SearchTextChanged`

### ConfirmDeleteDialog.razor
物品删除确认对话框。显示物品名称，取消/删除按钮。

**参数:** `string ItemName`（通过 `[CascadingParameter] IMudDialogInstance`）

### TagFilter.razor
多选标签筛选芯片组。`SelectedTagIds` 双向绑定。

**参数:** `IReadOnlyCollection<int> SelectedTagIds`, `EventCallback<IReadOnlyCollection<int>> SelectedTagIdsChanged`
**MudBlazor:** `MudChipSet` (MultiSelection), `MudChip`

### ItemCard.razor
物品网格卡片。显示缩略图 + 名称 + 位置路径。点击导航到详情。空/灰色占位处理。

**参数:** `ItemSummaryDto Item` (EditorRequired)
**MudBlazor:** `MudPaper`, `MudIcon`, `MudStack`, `MudText`

### LocationTree.razor
位置树状选择器。从 `LocationDto` 列表构建 `TreeItemData<LocationDto>` 树。包含 `RefreshAsync()` 公共方法。

**参数:** `int? SelectedLocationId` (双向绑定)
**MudBlazor:** `MudTreeView`, `MudTreeViewItem`, `MudProgressCircular`

### LocationManageDialog.razor
位置 CRUD 管理弹窗。三种模式：创建（指定名称 + 父节点）、重命名（内联文本框）、删除（含确认）。

**注入:** `LocationService`
**MudBlazor:** `MudDialog`, `MudTextField`, `MudSelect`, `MudSelectItem`, `MudButton`, `MudIconButton`, `MudIcon`

### TagManageDialog.razor
标签 CRUD 管理弹窗。创建、重命名、删除（含确认），显示每标签 `ItemCount`。

**注入:** `TagService`
**MudBlazor:** `MudDialog`, `MudTextField`, `MudButton`, `MudIconButton`

### ImageUploader.razor
图片捕获组件。拍照按钮（移动端调起相机） + 文件选择器 + 预览。通过 JS 互操作调用 `camera-capture.js`。

**参数:** `EventCallback<PhotoCapture> PhotoCaptured`
**注入:** `IJSRuntime`
**MudBlazor:** `MudButton`, `MudProgressCircular`, `MudText`, `MudPaper`

### WebAuthnSetup.razor
通行密钥注册组件。调用 WebAuthn API 注册通行密钥，注册成功后显示恢复码。

**注入:** `AuthService`
**MudBlazor:** `MudButton`, `MudAlert`, `MudProgressCircular`

### WebAuthnCredentialList.razor
已注册通行密钥凭据列表。显示设备名称、注册时间、凭据 ID，支持删除。

**注入:** `AuthService`
**MudBlazor:** `MudTable`, `MudButton`, `MudText`

### PasskeyManageDialog.razor
通行密钥管理弹窗（设置页调用）。列出并管理已注册的通行密钥，支持删除和查看恢复码。

**参数:** 无（通过 `AuthService` 注入读取）
**MudBlazor:** `MudDialog`, `MudButton`, `MudText`

---

## 布局 (1)

### MainLayout.razor
- **MudThemeProvider** + **MudDialogProvider** + **MudSnackbarProvider** + **MudPopoverProvider**
- 顶栏: 标题 + 返回按钮 (history.back)
- 底栏: 4 个 Tab (`/` `/entry` `/browse` `/settings`)，活动状态 `bw-nav-active`
- 登录页/404 隐藏导航栏

---

## 服务层 (9)

| 服务 | 生命周期 | 说明 |
|------|---------|------|
| `AppState` | Singleton | 全局状态（当前用户、管理员状态、连续收纳） |
| `AuthService` | Scoped | 登录/登出 |
| `CookieAuthenticationStateProvider` | Scoped | Blazor WASM Cookie 认证桥接 |
| `CookieHandler` | Scoped | `HttpClientHandler`，设置 `BrowserRequestCredentials.Include` |
| `ItemService` | Scoped | 物品 CRUD + 搜索 |
| `ItemEntryService` | Scoped | 物品创建 |
| `LocationService` | Scoped | 位置 CRUD |
| `TagService` | Scoped | 标签 CRUD |
| `AiService` | Scoped | AI 识别（20s 超时，静默降级） |

---

## 模型 (1)

### PhotoCapture.cs
```csharp
record PhotoCapture(string FileName, string ContentType, byte[] Bytes)
```
统一照片数据载体，替代 `IBrowserFile`。

---

## JavaScript (1)

### camera-capture.js
ES 模块。创建 `<input type="file" capture="environment">` 调起设备相机。`FileReader.readAsDataURL` → base64 → `DotNetObjectReference` 回调 C#。

**限制:** 10MB，重复触发防抖
