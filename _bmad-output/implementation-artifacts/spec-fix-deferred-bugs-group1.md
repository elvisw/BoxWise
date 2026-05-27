---
title: '修复 3 个独立 Bug（空 base64 / NRE / 双重释放）'
type: 'bugfix'
created: '2026-05-27'
status: 'done'
baseline_commit: 'ccf0c27'
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** deferred-work.md 中 3 个独立 Bug：空 base64 创建 0 字节文件导致 AI 静默失败、LlmClient.BaseUrl 为 null 时 NRE 崩溃、ItemEntry 中 MemoryStream 双重释放。

**Approach:** 3 个修复互不依赖，各改一处：ImageUploader.razor 加空 base64 校验、LlmClient.cs 加 null 守卫、ItemEntry.razor 去掉多余的 stream using。

## Boundaries & Constraints

**Always:**
- 每个 Bug 修复只改一个文件，不引入新依赖
- 保持现有静默降级策略（AI 失败不弹错误框）
- 修复后 `dotnet build` 通过

**Ask First:**
- 无

**Never:**
- 不改变现有 API 签名
- 不引入新的 NuGet 包
- 不修改 LlmClient 的 15s 超时策略

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| 拍照返回空 base64 | dataUrl = `data:image/jpeg;base64,` | 不创建 PhotoCapture，显示"拍照失败"提示 | 静默处理 |
| 拍照返回空白图片 | dataUrl 含仅空格 base64 | 同上 | 静默处理 |
| BaseUrl 未配置 | _options.BaseUrl = null | RecognizeAsync 返回 null，不抛异常 | 静默降级 |
| ApiKey 未配置 | _options.ApiKey = null | 同上 | 静默降级 |
| 上传照片 | PhotoCapture 有效 | 正常上传，不双重释放 | N/A |

</frozen-after-approval>

## Code Map

- `src/BoxWise.Client/Components/ImageUploader.razor` -- Bug #1: OnPhotoCaptured 方法空 base64 校验
- `src/BoxWise.Server/Services/LlmClient.cs` -- Bug #2: RecognizeAsync 方法 null 守卫
- `src/BoxWise.Client/Pages/ItemEntry.razor` -- Bug #6: UploadPhotoAsync 方法 stream 双重释放

## Tasks & Acceptance

**Execution:**
- [x] `src/BoxWise.Client/Components/ImageUploader.razor` -- 在 OnPhotoCaptured 的 Convert.FromBase64String 之前校验 base64 是否为空/空白，为空时设置 _errorMessage = "拍照失败，请重试" 并提前返回
- [x] `src/BoxWise.Server/Services/LlmClient.cs` -- 在 RecognizeAsync 方法体开头检查 _options.BaseUrl 和 _options.ApiKey，任一为 null/空白时记录警告日志并 return null
- [x] `src/BoxWise.Client/Pages/ItemEntry.razor` -- UploadPhotoAsync 中去掉 stream 变量的 using，由 streamContent 独占所有权

**Acceptance Criteria:**
- Given 相机返回空 base64 dataUrl，when OnPhotoCaptured 被调用，then 不创建 PhotoCapture，UI 显示"拍照失败，请重试"
- Given LlmOptions.BaseUrl 为 null，when RecognizeAsync 被调用，then 返回 null 且不抛出 NullReferenceException
- Given 照片数据有效，when UploadPhotoAsync 执行，then stream 不再双重释放，编译无警告

## Spec Change Log

- **Adversarial review (2026-05-27)** — 3 个 patch 级修正：
  1. ImageUploader.razor：空 base64 错误同时清除 `_previewUrl`（避免过期预览）
  2. LlmClient.cs：BaseUrl/ApiKey 分别检查、分别日志（区分缺失项）
  3. ItemEntry.razor：`streamContent` 也去掉 `using`，由 `MultipartFormDataContent` 独占释放链
  **KEEP:** 原有 fix 的核心逻辑方向正确，未触及；错误处理保持静默降级。

## Suggested Review Order

**空值守卫**

- LlmClient 入口处分别检查 BaseUrl/ApiKey，区分缺失项日志并静默降级
  [`LlmClient.cs:28`](../../src/BoxWise.Server/Services/LlmClient.cs#L28)

- ImageUploader 空 base64 校验，同时清除过期预览防止 UI 残留
  [`ImageUploader.razor:101`](../../src/BoxWise.Client/Components/ImageUploader.razor#L101)

**资源生命周期**

- 移除双重 using，streamContent 由 MultipartFormDataContent 递归释放
  [`ItemEntry.razor:199`](../../src/BoxWise.Client/Pages/ItemEntry.razor#L199)

## Verification

**Commands:**
- `dotnet build BoxWise.slnx` -- 编译通过无错误无警告
- `dotnet test BoxWise.slnx` -- 已有 34 测试全部通过
