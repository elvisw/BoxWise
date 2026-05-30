---
title: '恢复码批量复制与下载'
type: 'enhancement'
created: '2026-05-30'
status: 'done'
route: 'one-shot'
---

# 恢复码批量复制与下载

## Intent

**Problem:** 2FA 设置完成后，恢复码展示区域仅支持逐个复制，用户需要手动点击每个恢复码的复制按钮，操作繁琐且容易遗漏。

**Approach:** 在恢复码列表下方新增"复制全部"和"下载"两个按钮，分别通过 `navigator.clipboard.writeText` 和 `data:` URI 下载实现批量操作。纯前端改动，不涉及 API、数据模型或业务逻辑变更。

## Suggested Review Order

1. [`src/BoxWise.Client/wwwroot/js/utils.js`](../../../src/BoxWise.Client/wwwroot/js/utils.js) — 新增 `downloadFile` 全局函数（2 行核心逻辑）
2. [`src/BoxWise.Client/wwwroot/index.html`](../../../src/BoxWise.Client/wwwroot/index.html) — 引用新 JS 文件
3. [`src/BoxWise.Client/Components/TwoFactorSetup.razor`](../../../src/BoxWise.Client/Components/TwoFactorSetup.razor) — 新增按钮 UI + `CopyAllCodes`/`DownloadCodes` 方法
