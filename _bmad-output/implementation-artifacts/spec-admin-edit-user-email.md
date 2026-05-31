---
title: '管理后台编辑用户时支持修改邮箱'
type: 'feature'
created: '2026-05-31'
status: 'done'
route: 'one-shot'
context: ['{project-root}/_bmad-output/project-context.md']
---

# 管理后台编辑用户时支持修改邮箱

## Intent

**Problem:** 管理后台编辑用户页面仅支持修改用户名，无法修改邮箱。邮箱是 Email 2FA 的关键依赖，管理员需要有途径在用户邮箱变更（如离职交接、域名迁移）时进行修改。

**Approach:** 在编辑用户页面新增邮箱输入框，与用户名一同编辑保存。后端校验邮箱格式和唯一性，保存时同步更新 `EmailForTwoFactor`。并发场景通过 `DbUpdateException` 捕获兜底。

## Suggested Review Order

前端表单——新增邮箱输入框（type=email, required）
  [`EditAccount.cshtml:1`](../../src/BoxWise.Server/Pages/Admin/EditAccount.cshtml#L1)

后端逻辑——双字段校验 + EmailForTwoFactor 同步 + 并发保护
  [`EditAccount.cshtml.cs:1`](../../src/BoxWise.Server/Pages/Admin/EditAccount.cshtml.cs#L1)

测试覆盖——修改邮箱/空邮箱/重复邮箱 + 重命名测试更新
  [`AdminUserManagementTests.cs:1`](../../src/BoxWise.Server.Tests/AdminUserManagementTests.cs#L1)
