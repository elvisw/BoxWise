---
title: '修复生产环境 Blazor WASM 加载卡住（fingerprint 占位符未替换）'
type: 'bugfix'
created: '2026-06-04'
status: 'done'
route: 'one-shot'
context: []
---

## Intent

**Problem:** 部署到 Debian 生产环境后，`https://im.elvisw.com/` 页面显示静止 Loading 圆圈，Blazor WASM 应用无法启动。根因为 .NET 10 已知 Bug（dotnet/aspnetcore#64543）：当通过 Server 项目 `dotnet publish` 时，`index.html` 中 `#[.{fingerprint}]` 占位符不被替换，导致浏览器请求 `_framework/blazor.webassembly`（`#` 截断后缀）无法匹配 `MapStaticAssets()` manifest 中的 `_framework/blazor.webassembly.js` 路由，SPA fallback 返回 HTML 而非 JS。

**Approach:** 移除 `OverrideHtmlAssetPlaceholders` MSBuild 属性并去掉 `index.html` 中的 `#[.{fingerprint}]` 占位符，改为直接引用 `_framework/blazor.webassembly.js`，由 `MapStaticAssets()` 通过 ETag 处理缓存并将请求映射到指纹化实际文件。

## Suggested Review Order

1. [`src/BoxWise.Client/BoxWise.Client.csproj:6`](../../../../src/BoxWise.Client/BoxWise.Client.csproj#L6) — 删除 `OverrideHtmlAssetPlaceholders`，禁用有缺陷的 fingerprint 替换
2. [`src/BoxWise.Client/wwwroot/index.html:41`](../../../../src/BoxWise.Client/wwwroot/index.html#L41) — script src 去掉 `#[.{fingerprint}]`，指向 `MapStaticAssets()` 可匹配的路径

### Review Findings

- [x] [Review][Defer] SW 缓存键不匹配，离线时 `blazor.webassembly.js` 无法命中 Service Worker 缓存 [src/BoxWise.Client/wwwroot/index.html:41] — deferred, pre-existing
- [x] [Review][Defer] README 备份说明依赖 CI 约定，`tar` 命令无防护 [README.md:313] — deferred, pre-existing
- [x] [Review][Defer] `scp publish/*` 边缘情况（空目录、大文件） [README.md:351] — deferred, pre-existing
- [x] [Review][Defer] grep 验证模式可用更精确的匹配 [README.md:338] — deferred, pre-existing
- [x] [Review][Patch] `publish-test/` 目录包含旧占位符残留，应清理避免误用 — 已删除
