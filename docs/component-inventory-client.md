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

**参数:** `int? SelectedLocationId` (双向绑定)、`EventCallback<LocationDto?> SelectedLocationChanged`

**MudBlazor:** `MudTreeView`, `MudTreeViewItem`, `MudProgressCircular`

### LocationManageDialog.razor
位置管理弹窗。新建/编辑共用表单（名称、上级位置、描述、位置照片），编辑时上级位置只读；删除空位置沿用行内确认并清理描述/照片。失败、部分成功、清理警告与结果未知分别提示，不自动重试未知写入。

**注入:** `LocationService`, `ISnackbar`
**MudBlazor:** `MudDialog`, `MudTextField`, `MudSelect`, `MudSelectItem`, `MudButton`, `MudIconButton`, `MudIcon`, `MudPaper`

### LocationPhotoField.razor
表单内的位置照片字段。拍照/设备选图、本机预览、已保存照片按需展开（加载/失败/重试/收起）、待移除状态；不打开第二个弹窗。
拍照/选图使用原生同步按钮触发隐藏 `InputFile`，不运行会打断用户激活的相机探测。相机可用性通过
`permissions.query({ name: 'camera' })` + `enumerateDevices()` 被动采样；平台不支持 camera 权限名或在授权前隐藏
`videoinput`（`prompt` + 空设备列表）时返回 `unknown`，不显示“无法使用相机”，也不把取消选择当作失败。
仅在 `unsupported`、`denied` 或明确的 `no-device` 时显示 polite 内联提示；设备选图始终可用。

**参数:** `LocationId`, `HasSavedPhoto`, `LocationPath`, `SavedLocationPath`, `NewPhoto`, `RemoveRequested`, `Disabled`
**注入:** `LocationService`, `IJSRuntime`

### LocationPhotoDialog.razor
浏览页与物品详情共用的位置照片弹窗。打开时才请求照片，展示完整路径、照片（contain）和描述；失败可重试，关闭不改变页面状态。

**参数:** `LocationDto Location`
**注入:** `LocationService`

### LocationSaveCoordinator.cs
`BoxWise.Client/Services` 下的纯逻辑（无 DI）：按“资料 → 照片”顺序调用注入的委托，返回明确的
`MetadataRejected/MetadataUnknown/PhotoRejected/PhotoNotFound/PhotoUnknown/Remove*` 状态。
资料失败或未知时不会调用照片操作；创建部分成功后的重试使用同一位置 ID，不重复创建。

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
通行密钥注册组件。调用 WebAuthn API 注册通行密钥；注册成功后显示 8 个一次性恢复码（等宽列表）、离线保存警告和"复制全部恢复码"按钮（优先 `navigator.clipboard`，非安全上下文回退到 `execCommand`，失败时提示手动复制）。

**注入:** `AuthService`, `ISnackbar`, `IJSRuntime`
**MudBlazor:** `MudButton`, `MudAlert`, `MudText`, `MudPaper`

### WebAuthnCredentialList.razor
已注册通行密钥凭据列表。显示设备名称、注册时间、凭据 ID，支持删除。删除最后一个凭据时，确认文案提示"删除后将关闭双重验证，已保存的恢复码同时作废"。

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
| `LocationService` | Scoped | 位置 CRUD + 描述/照片；写结果分为 Success/Rejected/NotFound/Unknown |
| `LocationSaveCoordinator` | 静态 | 先资料、后照片的纯编排；资料失败/未知不触发照片，重试沿用已创建 ID |
| `TagService` | Scoped | 标签 CRUD |
| `AiService` | Scoped | AI 识别（客户端直调火山 ARK API，30s 超时，静默降级） |

---

## 模型 (1)

### PhotoCapture.cs
```csharp
record PhotoCapture(string FileName, string ContentType, byte[] Bytes)
```
统一照片数据载体，替代 `IBrowserFile`。

---

## JavaScript (3)

### camera-capture.js
ES 模块。创建 `<input type="file" capture="environment">` 调起设备相机。`FileReader.readAsDataURL` → base64 → `DotNetObjectReference` 回调 C#。

**限制:** 10MB，重复触发防抖

### webauthn.js
ES 模块。封装 WebAuthn API（`navigator.credentials.create` / `get`），供通行密钥注册和登录组件通过 `IJSRuntime` 调用 `window.webauthn.*`。

**使用方:** `Login.razor`、`WebAuthnSetup`、`WebAuthnCredentialList`、`PasskeyManageDialog`

### utils.js
工具函数模块。提供 `window.downloadFile` 辅助函数（触发浏览器文件下载），由 `index.html` 引用加载。
