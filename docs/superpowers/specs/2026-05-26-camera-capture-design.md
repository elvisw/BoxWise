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
| JS 数据传递 | base64 字符串 | Blazor WASM 不支持 Uint8Array 直接传递 |

## 文件清单

| 文件 | 操作 | 说明 |
|------|------|------|
| `src/BoxWise.Client/wwwroot/js/camera-capture.js` | 新增 | ES 模块，创建原生 input[type=file][capture=environment] |
| `src/BoxWise.Client/Models/PhotoCapture.cs` | 新增 | 统一文件数据载体，替代 IBrowserFile |
| `src/BoxWise.Client/Components/ImageUploader.razor` | 改造 | 增加拍照按钮 + JS 互操作 + ClearPreview() |
| `src/BoxWise.Client/Pages/ItemEntry.razor` | 改造 | 适配 PhotoCapture 类型 + 布局微调 |
| `_bmad-output/planning-artifacts/prds/prd-BoxWise-2026-05-21/prd.md` | 更新 | FR-1 描述对齐实现 |

## 组件设计

### camera-capture.js

```javascript
export function capturePhoto(dotNetHelper) {
    const input = document.createElement('input');
    input.type = 'file';
    input.accept = 'image/*';
    input.capture = 'environment';

    input.onchange = async (e) => {
        const file = e.target.files[0];
        if (!file) {
            dotNetHelper.invokeMethodAsync('OnPhotoCaptured', null, null, null);
            return;
        }
        const buffer = await file.arrayBuffer();
        const bytes = new Uint8Array(buffer);
        let binary = '';
        for (let i = 0; i < bytes.length; i++) {
            binary += String.fromCharCode(bytes[i]);
        }
        const base64 = btoa(binary);
        dotNetHelper.invokeMethodAsync('OnPhotoCaptured', file.name, file.type, base64);
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

- 新增拍照按钮（MudButton + CameraAlt 图标），点击触发 `CaptureAsync()`
- `CaptureAsync()` → `IJSRuntime` → JS 模块 → 原生 input + capture
- `[JSInvokable] OnPhotoCaptured(string?, string?, string?)` 接收 base64 回调
- 原有 `OnFileSelected` 保留，统一通过 `OnPhotoCaptured` EventCallback 输出 `PhotoCapture`
- 暴露 `ClearPreview()` 方法供父组件同步清除预览
- 实现 `IAsyncDisposable`，释放 `DotNetObjectReference` 和 `IJSObjectReference`

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
拍照路径: [拍照按钮] → IJSRuntime → JS capturePhoto() → 原生 input.click()
         → 用户拍照 → JS onchange → arrayBuffer → base64
         → dotNetHelper.invokeMethodAsync("OnPhotoCaptured", name, type, base64)
         → C# OnPhotoCaptured → Convert.FromBase64String → PhotoCapture
         → OnPhotoCaptured.InvokeAsync(photoCapture) → ItemEntry 接收

文件路径: [InputFile] → OnFileSelected → OpenReadStream → byte[]
         → PhotoCapture → OnPhotoCaptured.InvokeAsync(photoCapture)
         → ItemEntry 接收

保存: PhotoCapture.OpenReadStream() → MemoryStream
     → MultipartFormDataContent → POST /api/images/upload
```

## 注意事项

- `DotNetObjectReference` 在 `OnInitialized` 中创建，`DisposeAsync` 中释放
- `[JSInvokable]` 回调中须手动调用 `StateHasChanged()`
- `PhotoCapture` 的 `byte[]` 使用引用相等性，当前仅作数据载体无风险，后续如需哈希/比较需覆盖 Equals
- `wwwroot/js/` 目录需新建
- JS 模块通过动态 import 加载，无需在 index.html 预加载
- 连续收纳模式保存后导航离开页面，无需额外重置逻辑
