# 物品录入拍照功能 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 在物品录入页面添加拍照按钮，移动端直接调起后置相机，桌面端退化为文件选择器

**Architecture:** 新增 JS 模块 (`camera-capture.js`) 通过 `DotNetObjectReference` 回调 C# 端；新增 `PhotoCapture` 记录统一承载照片数据；改造 `ImageUploader.razor` 新增拍照按钮 + JS 互操作 + loading/错误状态；改造 `ItemEntry.razor` 适配新类型

**Design Spec:** `docs/superpowers/specs/2026-05-26-camera-capture-design.md`

**Tech Stack:** Blazor WASM, C#, JavaScript, MudBlazor 9.x, IJSRuntime/DotNetObjectReference

---

### Task 1: 创建目录结构 + JS 模块

**Files:**
- Create: `src/BoxWise.Client/wwwroot/js/camera-capture.js`

- [ ] **Step 1: Create wwwroot/js/ directory**

```powershell
New-Item -ItemType Directory -Force -Path "src/BoxWise.Client/wwwroot/js"
```

- [ ] **Step 2: Write camera-capture.js**

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

- [ ] **Step 3: Verify file exists**

```powershell
Get-Content "src/BoxWise.Client/wwwroot/js/camera-capture.js" | Select-Object -First 3
```

- [ ] **Step 4: Commit**

```bash
git add src/BoxWise.Client/wwwroot/js/camera-capture.js
git commit -m "feat: add camera-capture.js — 原生 capture 拍照 + FileReader base64 编码"
```

---

### Task 2: Create PhotoCapture 数据模型

**Files:**
- Create: `src/BoxWise.Client/Models/PhotoCapture.cs`

- [ ] **Step 1: Create Models/ directory**

```powershell
New-Item -ItemType Directory -Force -Path "src/BoxWise.Client/Models"
```

- [ ] **Step 2: Write PhotoCapture.cs**

```csharp
namespace BoxWise.Client.Models;

public record PhotoCapture(string FileName, string ContentType, byte[] Bytes)
{
    public Stream OpenReadStream() => new MemoryStream(Bytes);
}
```

- [ ] **Step 3: Verify it compiles**

```bash
cd src/BoxWise.Client && dotnet build
```
Expected: Build succeeded.

- [ ] **Step 4: Commit**

```bash
git add src/BoxWise.Client/Models/PhotoCapture.cs
git commit -m "feat: add PhotoCapture record — 统一照片数据载体替代 IBrowserFile"
```

---

### Task 3: Rewrite ImageUploader component

**Files:**
- Modify: `src/BoxWise.Client/Components/ImageUploader.razor` (full rewrite)

- [ ] **Step 1: Replace ImageUploader.razor template section**

Current file starts with `@using Microsoft.AspNetCore.Components.Forms` (line 1) through `</MudPaper>` (line 9). Replace with:

```razor
@using Microsoft.AspNetCore.Components.Forms
@implements IAsyncDisposable
@inject IJSRuntime JS

<MudStack Row AlignItems="AlignItems.Center" Class="mb-3">
    <MudButton Variant="Variant.Filled" Color="Color.Primary"
               OnClick="CaptureAsync" StartIcon="@Icons.Material.Filled.CameraAlt"
               Disabled="@_isCapturing">
        @(_isCapturing ? "拍照中..." : "拍照")
    </MudButton>
    @if (_isCapturing)
    {
        <MudProgressCircular Size="Size.Small" Indeterminate="true" Class="ml-2" />
    }
</MudStack>

@if (_errorMessage is not null)
{
    <MudText Color="Color.Error" Class="mb-2">@_errorMessage</MudText>
}

<InputFile OnChange="OnFileSelected" accept="image/*" />

@if (_previewUrl is not null)
{
    <MudPaper Elevation="2" Class="my-3 pa-2" Style="position:relative">
        <img src="@_previewUrl" style="max-width:100%;max-height:300px" />
    </MudPaper>
}
```

- [ ] **Step 2: Replace ImageUploader.razor @code block**

Current @code block is lines 11-28. Replace with:

```csharp
@code {
    [Parameter] public EventCallback<PhotoCapture> OnPhotoCaptured { get; set; }

    private IJSObjectReference? _jsModule;
    private DotNetObjectReference<ImageUploader>? _dotNetRef;
    private bool _isDisposed;
    private bool _isCapturing;
    private string? _previewUrl;
    private string? _errorMessage;

    protected override void OnInitialized()
    {
        _dotNetRef = DotNetObjectReference.Create(this);
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            try
            {
                _jsModule = await JS.InvokeAsync<IJSObjectReference>(
                    "import", "./js/camera-capture.js");
            }
            catch
            {
                _errorMessage = "无法加载相机模块";
            }
        }
    }

    private async Task CaptureAsync()
    {
        if (_jsModule is null) return;
        _errorMessage = null;
        _isCapturing = true;
        StateHasChanged();

        try
        {
            await _jsModule.InvokeVoidAsync("capturePhoto", _dotNetRef);
        }
        catch
        {
            _errorMessage = "无法启动相机";
            _isCapturing = false;
            StateHasChanged();
        }
    }

    [JSInvokable]
    public async Task OnPhotoCaptured(string? name, string? type, string? dataUrl)
    {
        if (_isDisposed) return;
        if (name is null || dataUrl is null)
        {
            _isCapturing = false;
            StateHasChanged();
            return;
        }

        var commaIdx = dataUrl.IndexOf(',');
        if (commaIdx < 0)
        {
            _isCapturing = false;
            return;
        }

        var base64 = dataUrl[(commaIdx + 1)..];
        var bytes = Convert.FromBase64String(base64);

        const int maxSize = 10 * 1024 * 1024;
        if (bytes.Length > maxSize)
        {
            _isCapturing = false;
            _errorMessage = "照片不能超过10MB";
            StateHasChanged();
            return;
        }

        _previewUrl = dataUrl;
        _isCapturing = false;

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

    private async Task OnFileSelected(InputFileChangeEventArgs e)
    {
        _errorMessage = null;
        var file = e.GetMultipleFiles(1).FirstOrDefault();
        if (file is null) return;

        await using var stream = file.OpenReadStream(10 * 1024 * 1024);
        var bytes = new byte[stream.Length];
        await stream.ReadExactlyAsync(bytes);
        var base64 = Convert.ToBase64String(bytes);
        _previewUrl = $"data:{file.ContentType};base64,{base64}";

        await OnPhotoCaptured.InvokeAsync(
            new PhotoCapture(file.Name, file.ContentType, bytes));
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
        _dotNetRef?.Dispose();
        if (_jsModule is not null)
        {
            try { await _jsModule.DisposeAsync(); } catch { }
        }
    }
}
```

- [ ] **Step 3: Add PhotoCapture using in _Imports.razor**

Read `src/BoxWise.Client/_Imports.razor` first. If it doesn't contain `@using BoxWise.Client.Models`, append:

```razor
@using BoxWise.Client.Models
```

- [ ] **Step 4: Verify build**

```bash
cd src/BoxWise.Client && dotnet build
```
Expected: Build succeeded with no errors.

- [ ] **Step 5: Commit**

```bash
git add src/BoxWise.Client/Components/ImageUploader.razor src/BoxWise.Client/_Imports.razor
git commit -m "feat: rewrite ImageUploader — 拍照按钮 + JS互操作 + ClearPreview + IAsyncDisposable"
```

---

### Task 4: Update ItemEntry page

**Files:**
- Modify: `src/BoxWise.Client/Pages/ItemEntry.razor` (adapt to PhotoCapture)

- [ ] **Step 1: Add @using and @ref to ImageUploader**

ItemEntry.razor line 4 currently:
```razor
@using Microsoft.AspNetCore.Components.Forms
```
Add after it:
```razor
@using BoxWise.Client.Models
```

Line 20 currently:
```razor
<ImageUploader OnFileUploaded="OnFileUploaded" />
```
Replace with:
```razor
<ImageUploader @ref="_imageUploader" OnPhotoCaptured="OnPhotoCaptured" />
```

- [ ] **Step 2: Replace _photoFile field and callback**

Line 49:
```csharp
private IBrowserFile? _photoFile;
```
Replace with:
```csharp
private PhotoCapture? _photo;
private ImageUploader? _imageUploader;
```

Lines 62-66:
```csharp
private Task OnFileUploaded(IBrowserFile file)
{
    _photoFile = file;
    return Task.CompletedTask;
}
```
Replace with:
```csharp
private Task OnPhotoCaptured(PhotoCapture photo)
{
    _photo = photo;
    return Task.CompletedTask;
}
```

- [ ] **Step 3: Update OnSaveAsync to use PhotoCapture**

Lines 96-98:
```csharp
if (_photoFile is not null)
{
    await UploadPhotoAsync(itemId.Value, _photoFile);
}
```
Replace with:
```csharp
if (_photo is not null)
{
    await UploadPhotoAsync(itemId.Value, _photo);
}
```

- [ ] **Step 4: Update UploadPhotoAsync signature and body**

Lines 119-136. Replace the method signature:
```csharp
private async Task UploadPhotoAsync(int itemId, IBrowserFile file)
```
With:
```csharp
private async Task UploadPhotoAsync(int itemId, PhotoCapture photo)
```

Replace line 124:
```csharp
using var stream = file.OpenReadStream(10 * 1024 * 1024);
```
With:
```csharp
using var stream = photo.OpenReadStream();
```

Replace line 126:
```csharp
streamContent.Headers.ContentType = new(file.ContentType);
```
With:
```csharp
streamContent.Headers.ContentType = new(photo.ContentType);
```

Replace line 127:
```csharp
content.Add(streamContent, "\"file\"", file.Name);
```
With:
```csharp
content.Add(streamContent, "\"file\"", photo.FileName);
```

- [ ] **Step 5: Add RemovePhoto method**

After `OnPhotoCaptured` method, add:
```csharp
private void RemovePhoto()
{
    _photo = null;
    _imageUploader?.ClearPreview();
}
```

- [ ] **Step 6: Update photo section label**

Line 19 currently:
```razor
<MudText Typo="Typo.subtitle2" Class="mb-2">照片（可选）</MudText>
```
Replace with:
```razor
<MudText Typo="Typo.subtitle2" Class="mb-2">
    照片（可选）
    @if (_photo is not null)
    {
        <MudIconButton Icon="@Icons.Material.Filled.Close"
                       Size="Size.Small" OnClick="RemovePhoto"
                       Class="ml-2" />
    }
</MudText>
```

- [ ] **Step 7: Remove unused using**

Line 4 `@using Microsoft.AspNetCore.Components.Forms` is no longer needed (it was for `IBrowserFile`). Remove it.

- [ ] **Step 8: Verify build**

```bash
cd src/BoxWise.Client && dotnet build
```
Expected: Build succeeded with no errors.

- [ ] **Step 9: Commit**

```bash
git add src/BoxWise.Client/Pages/ItemEntry.razor
git commit -m "feat: adapt ItemEntry to PhotoCapture — 拍照回调 + RemovePhoto + UploadPhotoAsync 适配"
```

---

### Task 5: Update PRD

**Files:**
- Modify: `_bmad-output/planning-artifacts/prds/prd-BoxWise-2026-05-21/prd.md` (line 91-97)

- [ ] **Step 1: Update FR-1 description**

Current (line 94):
```markdown
- 录入页提供"拍照"和"跳过拍照"两个入口。
- 选择拍照时：调用设备摄像头 → 预览 → 可重拍或确认 → 照片保存至服务端文件系统。
- 跳过拍照时：直接进入信息填写步骤（物品名称 + 位置 + 标签），不触发 AI 识别。
```

Replace with:
```markdown
- 录入页顶部提供"拍照"按钮，表单始终可见。点击按钮调用设备后置摄像头，桌面端退化为文件选择器。
- 拍照后显示预览，用户可继续编辑表单信息；未拍照可直接填写，照片为可选字段。
- 无照片的物品在网格视图中显示文字卡片 + 占位图标。
```

- [ ] **Step 2: Commit**

```bash
git add _bmad-output/planning-artifacts/prds/prd-BoxWise-2026-05-21/prd.md
git commit -m "docs: update PRD FR-1 — 拍照入口对齐单页表单+按钮实现"
```

---

### Task 6: Full solution build + manual verification

**Files:**
- None (verification only)

- [ ] **Step 1: Build full solution**

```bash
dotnet build
```
Expected: Build succeeded with no errors or warnings.

- [ ] **Step 2: Start Client dev server and verify page loads**

```bash
Start-Process -NoNewWindow dotnet -ArgumentList "run", "--project", "src/BoxWise.Client"
```
Wait for "Now listening on: https://localhost:5001", then open `https://localhost:5001/entry`.

- [ ] **Step 3: Manual verification checklist**

| 检查项 | 预期行为 |
|--------|---------|
| 录入页加载 | 顶部显示"拍照"按钮（蓝色，CameraAlt 图标） |
| 点击"拍照" | 按钮变为"拍照中..."并禁用，出现圆形进度条 |
| 移动端拍照 | 打开系统后置相机（capture=environment） |
| 桌面端点击 | 弹出文件选择器（退化为普通文件选择） |
| 选择照片后 | 显示预览图，按钮恢复 |
| 取消拍照 | 按钮恢复，无异常 |
| InputFile 选文件 | 同样显示预览，功能不受影响 |
| 填写表单 + 保存 | 照片随表单一起上传，跳转首页 |
| 跳过拍照直接保存 | 无照片物品正常保存 |
| 构建无错误 | `dotnet build` 0 warnings 0 errors |
