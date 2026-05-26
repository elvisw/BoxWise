# 物品录入拍照功能 — 设计规格

**日期:** 2026-05-26
**背景:** PRD FR-1 要求"拍照采集（可选）"，当前实现仅有普通文件选择器，缺少 `capture` 属性无法直接调起设备相机

## 目标

在物品录入页添加拍照功能，移动端点击"拍照"按钮直接调起后置相机，桌面端退化为文件选择器。表单始终可见，用户可跳过拍照直接填写信息。

## 设计决策

| 决策 | 选择 | 原因 |
|------|------|------|
| 拍照交互模型 | 原生 `<input capture="environment">` | 直接调起系统相机，体验最干脆 |
| 入口布局 | 单页表单 + 顶部拍照按钮 | 少一次点击，与当前 UI 差异最小 |
| 桌面端行为 | 按钮保留，浏览器自动退化 | 笔记本/台式机也有摄像头 |
| JS 数据传递 | base64 字符串（FileReader） | Blazor WASM 不支持 Uint8Array 直接传递 |

## 文件清单

| 文件 | 操作 | 说明 |
|------|------|------|
| `src/BoxWise.Client/wwwroot/js/camera-capture.js` | 新增 | ES 模块，创建原生 input[type=file][capture=environment] |
| `src/BoxWise.Client/Models/PhotoCapture.cs` | 新增 | 统一文件数据载体，替代 IBrowserFile |
| `src/BoxWise.Client/Components/ImageUploader.razor` | 改造 | 增加拍照按钮 + JS 互操作 + loading 状态 + ClearPreview() |
| `src/BoxWise.Client/Pages/ItemEntry.razor` | 改造 | 适配 PhotoCapture 类型 + 布局微调 |
| `_bmad-output/planning-artifacts/prds/prd-BoxWise-2026-05-21/prd.md` | 更新 | FR-1 描述对齐实现 |

## 组件设计

### camera-capture.js

使用 `FileReader.readAsDataURL()` 代替手动 ArrayBuffer 迭代，浏览器原生异步编码，不阻塞主线程：

```javascript
const MAX_FILE_SIZE = 10 * 1024 * 1024; // 10MB

export function capturePhoto(dotNetHelper) {
    const input = document.createElement('input');
    input.type = 'file';
    input.accept = 'image/*';
    input.capture = 'environment';

    input.onchange = (e) => {
        const file = e.target.files[0];
        if (!file) {
            dotNetHelper.invokeMethodAsync('OnPhotoCaptured', null, null, null);
            return;
        }
        // 文件大小验证
        if (file.size > MAX_FILE_SIZE) {
            dotNetHelper.invokeMethodAsync('OnPhotoError', '照片不能超过10MB');
            return;
        }
        const reader = new FileReader();
        reader.onload = () => {
            // reader.result 格式: "data:image/jpeg;base64,..."
            dotNetHelper.invokeMethodAsync('OnPhotoCaptured', file.name, file.type, reader.result);
        };
        reader.onerror = () => {
            dotNetHelper.invokeMethodAsync('OnPhotoError', '照片读取失败');
        };
        reader.readAsDataURL(file);
    };

    input.click();
}
```

### PhotoCapture.cs

```csharp
namespace BoxWise.Client.Models;

public record PhotoCapture(string FileName, string ContentType, byte[] Bytes)
{
    public Stream OpenReadStream() => new MemoryStream(Bytes);
}
```

### ImageUploader.razor

**新增成员和状态：**
- `_isCapturing` (bool) — 拍照进行中，禁用按钮防重复点击
- `_errorMessage` (string?) — 拍照错误提示
- `_isDisposed` (bool) — 释放防护，JS 回调时检查组件是否仍存活
- `_dotNetRef` — `DotNetObjectReference<ImageUploader>`（OnInitialized 创建，DisposeAsync 释放）

**拍照按钮（模板）：**
```razor
<MudButton Variant="Variant.Filled" Color="Color.Primary"
           OnClick="CaptureAsync" StartIcon="@Icons.Material.Filled.CameraAlt"
           Disabled="@_isCapturing">
    @(_isCapturing ? "拍照中..." : "拍照")
</MudButton>
@if (_isCapturing)
{
    <MudProgressCircular Size="Size.Small" Indeterminate="true" Class="ml-2" />
}
@if (_errorMessage is not null)
{
    <MudText Color="Color.Error" Class="mt-1">@_errorMessage</MudText>
}
```

**拍照流程（@code 关键逻辑）：**
```csharp
private bool _isDisposed;
private bool _isCapturing;
private string? _errorMessage;

private async Task CaptureAsync()
{
    _errorMessage = null;
    _isCapturing = true;
    StateHasChanged();

    try
    {
        await _jsModule.InvokeVoidAsync("capturePhoto", _dotNetRef);
    }
    catch (Exception ex)
    {
        _errorMessage = "无法启动相机";
        _isCapturing = false;
        StateHasChanged();
    }
}

[JSInvokable]
public async Task OnPhotoCaptured(string? name, string? type, string? dataUrl)
{
    _isCapturing = false;
    if (_isDisposed) return;
    if (name is null || dataUrl is null)
    {
        // 用户取消拍照，保持原状态
        StateHasChanged();
        return;
    }

    // 从 data URL 提取纯 base64: "data:image/jpeg;base64,xxxx" → "xxxx"
    var base64 = dataUrl[(dataUrl.IndexOf(',') + 1)..];
    var bytes = Convert.FromBase64String(base64);
    _previewUrl = dataUrl;  // data URL 可直接用作预览

    await OnPhotoCaptured.InvokeAsync(
        new PhotoCapture(name, type ?? "image/jpeg", bytes));
    StateHasChanged();
}

[JSInvokable]
public void OnPhotoError(string message)
{
    _isCapturing = false;
    if (_isDisposed) return;
    _errorMessage = message;
    StateHasChanged();
}

public void ClearPreview()
{
    _previewUrl = null;
    _errorMessage = null;
    StateHasChanged();
}

public async ValueTask DisposeAsync()
{
    _isDisposed = true;
    // 注意：应用关闭时 IJSRuntime 可能已释放，import 的模块无需显式卸载
    _dotNetRef?.Dispose();
}
```

**参数变更：**
- `OnFileUploaded(EventCallback<IBrowserFile>)` → `OnPhotoCaptured(EventCallback<PhotoCapture>)`

### ItemEntry.razor

- `@ref="_imageUploader"` 持有组件引用
- `_photoFile (IBrowserFile?)` → `_photo (PhotoCapture?)`
- `OnFileUploaded` 回调 → `OnPhotoCaptured`
- `RemovePhoto()` 同步调用 `_imageUploader.ClearPreview()`
- `UploadPhotoAsync` 适配：`file.OpenReadStream()` → `photo.OpenReadStream()`
- 表单始终可见，未拍照时显示提示文字

## 数据流

```
拍照路径: [拍照按钮] → 设置 _isCapturing=true, 禁用按钮
         → IJSRuntime → JS capturePhoto() → 原生 input.click()
         → 用户拍照 → JS onchange → 文件大小验证(>10MB→OnPhotoError)
         → FileReader.readAsDataURL() → 浏览器异步 base64 编码
         → reader.onload → dotNetHelper.invokeMethodAsync("OnPhotoCaptured", name, type, dataUrl)
         → 用户取消 → invokeMethodAsync("OnPhotoCaptured", null, null, null)
         → C# OnPhotoCaptured → 检查 _isDisposed → 检查 null(取消) → 提取 base64
         → Convert.FromBase64String → PhotoCapture → previewUrl=dataUrl
         → OnPhotoCaptured.InvokeAsync(photoCapture) → _isCapturing=false → StateHasChanged

文件路径: [InputFile] → OnFileSelected → OpenReadStream → byte[]
         → PhotoCapture → OnPhotoCaptured.InvokeAsync(photoCapture)
         → ItemEntry 接收

保存: PhotoCapture.OpenReadStream() → MemoryStream
     → MultipartFormDataContent → POST /api/images/upload
```

## 错误处理矩阵

| 场景 | JS 端行为 | C# 端行为 | 用户体验 |
|------|----------|----------|---------|
| 用户取消拍照 | `onchange` 不触发，或 file 为空 → 回调 (null, null, null) | 检查 null，return，不清除已有预览 | 按钮恢复，之前照片保留 |
| 文件超 10MB | 回调 `OnPhotoError("照片不能超过10MB")` | 显示红色错误文字 | 看到错误提示，可重试 |
| FileReader 失败 | 回调 `OnPhotoError("照片读取失败")` | 显示红色错误文字 | 看到错误提示，可重试 |
| JS 模块加载失败 | `InvokeVoidAsync` 抛异常 | catch 显示"无法启动相机" | 看到错误提示 |
| 组件已释放（页面跳转） | JS 回调触发 | `_isDisposed` 检查，return | 静默忽略，无异常 |
| 应用关闭时 DisposeAsync | — | 不调用 JS 互操作，只释放 ref | 无异常 |

## 注意事项

- `DotNetObjectReference` 在 `OnInitialized` 中创建，`DisposeAsync` 中释放
- `_isDisposed` flag 防止组件释放后 JS 回调操作已销毁的组件状态
- 应用关闭时 `DisposeAsync` 中不调用 JS 互操作（`IJSRuntime` 可能先释放），仅释放 `DotNetObjectReference`
- `[JSInvokable]` 回调中须手动调用 `StateHasChanged()`
- `FileReader.readAsDataURL()` 返回完整 data URL（含 `data:image/xxx;base64,` 前缀），C# 端需提取纯 base64
- `PhotoCapture` 的 `byte[]` 使用引用相等性，当前仅作数据载体无风险，后续如需哈希/比较需覆盖 Equals
- `wwwroot/js/` 目录需新建
- JS 模块通过动态 import 加载，无需在 index.html 预加载
- 连续收纳模式保存后导航离开页面，组件自动销毁，无需额外重置逻辑。**隐式依赖此导航行为**，如果将来改为不跳转的保存方式需增加显式重置
