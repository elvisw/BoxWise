---
title: '管理后台创建用户时邮箱地址必填'
type: 'feature'
created: '2026-05-31'
status: 'done'
route: 'one-shot'
base_commit: '1e56e29'
context: ['{project-root}/_bmad-output/project-context.md']
---

# 管理后台创建用户时邮箱地址必填

## Intent

**Problem:** 管理后台创建用户时邮箱是可选的，但邮箱是双因素认证（Email 2FA）的必要依赖。用户被创建后如果没有邮箱，无法启用 Email 2FA，也无法通过忘记密码找回账户。

**Approach:** 在 DTO、前端表单、后端校验三个层面将邮箱改为必填。创建时同步设置 `user.Email` 和 `user.EmailForTwoFactor`，确保后续 2FA 流程可用。新增单元测试覆盖空邮箱、无效格式、重复邮箱三个边界场景。

## Suggested Review Order

DTO 变更——新增 Email 属性
  [`CreateAccountRequest.cs:3`](../../src/BoxWise.Shared/Dtos/CreateAccountRequest.cs#L3)

前端表单——新增邮箱输入框（type=email, required）
  [`CreateAccount.cshtml:1`](../../src/BoxWise.Server/Pages/Admin/CreateAccount.cshtml#L1)

后端校验——空值/格式/唯一性三重检查 + EmailForTwoFactor 同步
  [`CreateAccount.cshtml.cs:32`](../../src/BoxWise.Server/Pages/Admin/CreateAccount.cshtml.cs#L32)

测试覆盖——空邮箱/无效格式/重复邮箱 + 成功路径断言
  [`AdminUserManagementTests.cs:1`](../../src/BoxWise.Server.Tests/AdminUserManagementTests.cs#L1)
