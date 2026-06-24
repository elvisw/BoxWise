---
title: '移除关于页面第三方依赖硬编码版本号'
type: 'refactor'
created: '2026-06-24'
status: 'done'
route: 'one-shot'
---

# 移除关于页面第三方依赖硬编码版本号

## Intent

**Problem:** About.razor 中第三方依赖版本号硬编码，升级 NuGet 包后需手动同步，7 个库中 5 个已过期（MudBlazor 9.4.0→9.5.0、SkiaSharp 3.119.2→4.148.0、ASP.NET Core 10.0.8→10.0.9、EF Core 10.0.8→10.0.9、coverlet 6.0.4→10.0.1）。

**Approach:** Blazor WASM 运行时无法读取 Directory.Packages.props，自动更新方案需维护额外映射关系且脆弱。直接移除版本号显示，保留库名和许可证（信息不折旧）。

## Suggested Review Order

1. `src/BoxWise.Client/Pages/About.razor:78` — `LibInfo` record struct 从 3 字段改为 2 字段（移除 Version）
2. `src/BoxWise.Client/Pages/About.razor:80` — `_libraries` 列表移除所有版本号参数
3. `src/BoxWise.Client/Pages/About.razor:37` — foreach 循环中移除版本号渲染行
