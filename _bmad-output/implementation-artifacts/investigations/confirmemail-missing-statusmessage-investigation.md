# Investigation: ConfirmEmail 缺少 _StatusMessage 分部视图

## Hand-off Brief

1. **What happened.** `ConfirmEmail.cshtml` 引用了 `_StatusMessage` 分部视图，但该视图仅存在于 `Account/Manage/` 下，不在 `Account/` 下，导致邮箱确认链接点击后抛出 `InvalidOperationException`。
2. **Where the case stands.** 根因已确认；同时附带发现确认邮件内容为英文（Identity 默认模板）。
3. **What's needed next.** 将 `_StatusMessage.cshtml` 复制到 `Account/` 目录，并处理邮件中文化。

## Case Info

| Field            | Value                                                              |
| ---------------- | ------------------------------------------------------------------ |
| Ticket           | N/A                                                                |
| Date opened      | 2026-07-02                                                         |
| Status           | Active                                                             |
| System           | Windows, .NET 10, ASP.NET Core Identity                            |
| Evidence sources | 错误堆栈、源代码、文件系统检查                                     |

## Problem Statement

用户点击邮箱确认链接后报错：`_StatusMessage` partial view not found。同时确认邮件内容为英文。

## Evidence Inventory

| Source                                      | Status    | Notes                                  |
| ------------------------------------------- | --------- | -------------------------------------- |
| `ConfirmEmail.cshtml:8`                     | Confirmed | 引用 `<partial name="_StatusMessage">` |
| `Account/Manage/_StatusMessage.cshtml`      | Confirmed | 存在于 Manage 子目录，不在 Account 下 |
| `Account/` 目录列表                         | Confirmed | 无 `_StatusMessage.cshtml`            |
| IdentityEmailSender.cs                      | Available | 发送邮件逻辑，需审查邮件模板语言       |

## Timeline of Events

| Time   | Event                                    | Source                                | Confidence |
| ------ | ---------------------------------------- | ------------------------------------- | ---------- |
| T0     | 用户点击邮箱确认链接                      | 用户报告                              | Confirmed  |
| T0+1ms | `ConfirmEmail.cshtml` 渲染时查找 partial | 堆栈跟踪                              | Confirmed  |
| T0+2ms | 6 个搜索路径均未找到 `_StatusMessage`     | 堆栈跟踪                              | Confirmed  |
| T0+3ms | `InvalidOperationException` 抛出          | 堆栈跟踪                              | Confirmed  |

## Confirmed Findings

### Finding 1: `_StatusMessage.cshtml` 缺失于 `Account/` 目录

**Evidence:** 
- `ConfirmEmail.cshtml:8` — `<partial name="_StatusMessage" model="Model.StatusMessage" />`
- 目录列表显示 `Account/` 下不存在 `_StatusMessage.cshtml`
- 堆栈跟踪确认 6 个搜索路径均未命中

**Detail:** `_StatusMessage.cshtml` 仅存在于 `Account/Manage/_StatusMessage.cshtml`。ASP.NET Core 的部分视图搜索路径按优先级顺序查找，`Account/` 目录在搜索链中排在 `Account/Manage/` 之前，但 `ConfirmEmail.cshtml` 位于 `Account/`，其局部搜索路径不包含 `Account/Manage/`。

### Finding 2: Manage 子目录下的页面正常工作

**Evidence:** `Account/Manage/` 目录包含 `_StatusMessage.cshtml`，其同级页面（如 `ChangePassword.cshtml`）能正常引用它。

**Detail:** 这个 bug 只影响 `Account/` 下的页面（当前仅 `ConfirmEmail.cshtml` 有引用）。Manage 子目录的页面在自己的目录找到该分部视图。

## Source Code Trace

| Element       | Detail                                                                              |
| ------------- | ----------------------------------------------------------------------------------- |
| Error origin  | `ConfirmEmail.cshtml:8` — `<partial name="_StatusMessage" model="Model.StatusMessage" />` |
| Trigger       | 用户点击邮箱确认链接（GET `/Identity/Account/ConfirmEmail?userId=...&code=...`）      |
| Condition     | `_StatusMessage.cshtml` 不在 `Account/` 目录的任何搜索路径内                         |
| Related files | `Account/Manage/_StatusMessage.cshtml` — 现有可复用模板                             |

## Conclusion

**Confidence:** High

根因确认：`_StatusMessage.cshtml` 分部视图仅存在于 `Account/Manage/` 目录，`ConfirmEmail.cshtml` 在 `Account/` 目录下引用该视图时无法找到。修复方案：将 `_StatusMessage.cshtml` 复制到 `Account/` 目录。

附带问题：确认邮件为英文，需审查 `IdentityEmailSender` 中的邮件模板。

## Recommended Next Steps

### Fix direction

1. 复制 `Account/Manage/_StatusMessage.cshtml` → `Account/_StatusMessage.cshtml`
2. 审查并中文化确认邮件模板
