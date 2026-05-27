---
title: '加固错误处理 — 5 项 deferred 清理'
type: 'bugfix'
created: '2026-05-27'
status: 'done'
baseline_commit: '8af18a9'
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** 代码审查发现 5 项已有错误处理缺陷：(1) ImageUploader 中 `Convert.FromBase64String` 无效字符无 try-catch，JSInvokable 中异常导致 `_isCapturing` 未重置、UI 永久卡死；(2) commaIdx < 0 分支无错误反馈，用户不知拍照为何失败；(3) LlmClient 中 `_options.Model` 未做 null 检查，产生无效请求 + 15s 无效等待；(4) `ResolvePathNamesBatchAsync` 中 null 与 "?" 两种失败信号不一致。

**Approach:** ImageUploader.OnPhotoCaptured 加 try-catch 包裹 base64 转换 + commaIdx 分支加错误提示；LlmClient 加 Model null 守卫；ResolvePathNamesBatchAsync 统一失败信号。

## Boundaries & Constraints

**Always:**
- try-catch 中重置 `_isCapturing = false`，防止 UI 卡死
- 保持静默降级策略
- `dotnet build` + `dotnet test` 通过

**Never:**
- 不改变现有 API 签名
- 不改变 LlmClient 超时策略

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| 无效 base64 | dataUrl = `data:image/jpeg;base64,!!!` | 不崩溃，显示"拍照失败"，_isCapturing 重置 | try-catch + 错误提示 |
| 无逗号 dataUrl | dataUrl = `invalid` | 显示"拍照失败，请重试" | 已有 early return + 新增错误消息 |
| Model 未配置 | _options.Model = null | RecognizeAsync 返回 null，不产生无效 API 请求 | 静默降级 |

</frozen-after-approval>

## Code Map

- `src/BoxWise.Client/Components/ImageUploader.razor` -- 3 项修复：try-catch Convert.FromBase64String、commaIdx 分支加错误消息
- `src/BoxWise.Server/Services/LlmClient.cs` -- Model null 守卫
- `src/BoxWise.Server/Repositories/LocationRepository.cs` -- ResolvePathNamesBatchAsync 失败信号一致性

## Tasks & Acceptance

**Execution:**
- [x] `src/BoxWise.Client/Components/ImageUploader.razor` -- commaIdx < 0 分支加 `_errorMessage = "拍照失败，请重试"`；Convert.FromBase64String 加 try-catch（FormatException），catch 中设置 `_errorMessage`、重置 `_isCapturing`、`StateHasChanged()`、return
- [x] `src/BoxWise.Server/Services/LlmClient.cs` -- 在 RecognizeAsync 中与 BaseUrl/ApiKey 同级加 `string.IsNullOrWhiteSpace(_options.Model)` 检查，记录日志并 return null
- [x] `src/BoxWise.Server/Repositories/LocationRepository.cs` -- ResolvePathNamesBatchAsync 中当 allIds.Count == 0 时返回空字典而非填 null 的字典（调用方已有 TryGetValue 防护，无需区分两种空态）

**Acceptance Criteria:**
- Given base64 含无效字符，when OnPhotoCaptured 被调用，then _isCapturing 重置为 false，显示"拍照失败，请重试"，UI 不卡死
- Given dataUrl 无逗号分隔符，when OnPhotoCaptured 被调用，then 显示"拍照失败，请重试"
- Given Model 为 null，when RecognizeAsync 被调用，then 返回 null 且不发起 HTTP 请求
- Given 无有效位置 ID 的路径，when ResolvePathNamesBatchAsync 被调用，then 返回空字典（调用方通过 TryGetValue 回退到 null）

## Verification

**Commands:**
- `dotnet build BoxWise.slnx` -- 编译通过
- `dotnet test BoxWise.slnx` -- 43 测试全部通过
