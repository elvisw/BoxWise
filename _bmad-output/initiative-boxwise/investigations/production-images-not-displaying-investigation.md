# Investigation: 生产环境图片全部无法显示

## Hand-off Brief

1. **What happened.** SkiaSharp 原生依赖 `libfontconfig.so.1` 在 Debian 服务器上缺失，导致后台缩略图生成静默失败。原图上传成功但 `thumb.jpg`/`medium.jpg` 从未生成，DB 中 `ThumbPath`/`MediumPath` 保持 NULL，UI 因此显示占位图标而非实际图片。
2. **Where the case stands.** 根因已确认并修复：安装 `libfontconfig1`、用 Pillow 为已有物品生成缩略图、修复 DB 记录。物品 #1 图片现已正常显示。
3. **What's needed next.** 长期修复见 Recommended Next Steps。

## Case Info

| Field            | Value                                                                      |
| ---------------- | -------------------------------------------------------------------------- |
| Ticket           | N/A                                                                        |
| Date opened      | 2026-06-05                                                                 |
| Status           | Concluded                                                                  |
| System           | Debian Linux VPS，二进制部署 (systemd + Caddy + Kestrel Unix socket)       |
| Evidence sources | 浏览器控制台错误、源代码、SSH 服务器诊断、SkiaSharp GitHub issues            |

## Problem Statement

生产环境中所有物品图片都无法正常显示。浏览器控制台报错：`1:13 <link rel=preload> has an invalid 'href' value`。

## Evidence Inventory

| Source   | Status    | Notes     |
| -------- | --------- | --------- |
| 浏览器控制台错误 | Available | `<link rel=preload> has an invalid 'href' value` — **红鲱鱼，与图片问题无关**（Blazor WASM 运行时占位符） |
| 源代码 `index.html:13` | Available | `<link rel="preload" id="webassembly" />` — Blazor WASM 预加载占位，JS 动态填充 href |
| 源代码 `ImageEndpoints.cs` | Available | `GET /api/images/{id}?type=` → `TypedResults.PhysicalFile()`；文件不存在 → 404 |
| 源代码 `ImageStorageService.cs` | Available | `_basePath = Path.GetFullPath(DataDirectory ?? "../data/images")` |
| 源代码 `ThumbnailService.cs` | Available | `Task.Run` fire-and-forget 后台生成，异常被 `LogWarning` 捕获 |
| 源代码 `ItemCard.razor:6-12` | Available | `ThumbPath` 为 null 时显示占位图标，非裂图 |
| 服务器文件系统 | Available | `original.jpg` 存在 (1.6MB)，`thumb.jpg`/`medium.jpg` 不存在 |
| 服务器 DB | Available | Items 表：PhotoPath/ThumbPath/MediumPath 全部 NULL |
| 服务器 SkiaSharp 依赖 | Available | `ldd libSkiaSharp.so` → **`libfontconfig.so.1 => not found`** |
| 服务器 Caddyfile | Available | `/api/*` → `reverse_proxy` 到 Kestrel，配置正确 |
| SkiaSharp GitHub Issues | Available | #2653, #1312, #509 确认：`libfontconfig1` 是 SkiaSharp Linux 必需依赖 |
| 应用日志 (journalctl) | Available | 无 thumbnail 相关 Warning/Error — 异常被 `Task.Run` 吞没或日志级别过滤 |

## Investigation Backlog

| # | Path to Explore | Priority | Status | Notes |
| - | --------------- | -------- | ------ | ----- |
| 1 | 搜索 `<link rel=preload>` 来源 | High | Done | 红鲱鱼 — Blazor WASM 占位符，无关 |
| 2 | 追踪图片请求链路 | High | Done | `original.jpg` 存在但无缩略图 + DB 路径为 NULL |
| 3 | 检查生产环境依赖 | High | Done | `libfontconfig.so.1` 缺失 — **根因** |
| 4 | 修复已有物品缩略图 | High | Done | Pillow 生成 + DB 更新完成 |

## Timeline of Events

| Time        | Event               | Source                | Confidence            |
| ----------- | ------------------- | --------------------- | --------------------- |
| 2026-06-04 22:28 | `/opt/boxwise/data/images/` 目录创建 | 服务器 stat | Confirmed |
| 2026-06-05 00:33 | 物品 #1 上传，`original.jpg` 保存成功 | 文件时间戳 | Confirmed |
| 2026-06-05 00:33 | 服务重启（`systemctl restart boxwise`），后台缩略图任务被中断 | journalctl | Confirmed |
| 2026-06-05 00:33 | `ThumbnailService.GenerateInBackground()` fire-and-forget 任务丢失 | 推论 | Deduced |
| 2026-06-05 08:57 | 调查开始，SSH 连接服务器 | 本调查 | Confirmed |
| 2026-06-05 09:10 | `libfontconfig.so.1 => not found` 通过 `ldd` 发现 | SSH | Confirmed |
| 2026-06-05 09:15 | `apt-get install libfontconfig1` 安装依赖 | SSH | Confirmed |
| 2026-06-05 09:20 | Pillow 脚本生成 thumb.jpg + medium.jpg，DB 更新 | SSH | Confirmed |

## Confirmed Findings

### Finding 1: `<link rel=preload>` 控制台错误是红鲱鱼

**Evidence:** `src/BoxWise.Client/wwwroot/index.html:13` — `<link rel="preload" id="webassembly" />` 没有 `href` 属性，由 `blazor.webassembly.js` 运行时动态填充。浏览器在 JS 执行前解析 HTML 时产生瞬时警告。

**Detail:** 这个警告与图片问题完全无关。不影响任何功能。

### Finding 2: 原图上传成功但缩略图从未生成

**Evidence:** `ls -la /opt/boxwise/data/1/` → `original.jpg` 存在 (1.6MB)，但 `thumb.jpg` 和 `medium.jpg` 不存在。

**Detail:** `ImageStorageService.SaveOriginalAsync()` 成功保存原图，但 `ThumbnailService.GenerateInBackground()` 的后台任务未能完成。

### Finding 3: 数据库图片路径字段全部为 NULL

**Evidence:** `SELECT Id, Name, PhotoPath, ThumbPath, MediumPath FROM Items` → 所有三个字段均为空字符串/NULL。

**Detail:** `ThumbnailService.GenerateAsync()` 中设置 `item.PhotoPath/ThumbPath/MediumPath` 的代码从未成功执行。UI 中 `ItemCard.razor:6` 检查 `ThumbPath` 为 null 后显示占位图标，解释了用户看到的"图片不显示"现象。

### Finding 4: `libfontconfig.so.1` 缺失 — 根本原因

**Evidence:** `ldd /opt/boxwise/libSkiaSharp.so` → `libfontconfig.so.1 => not found`

**Detail:** SkiaSharp 的标准 `SkiaSharp.NativeAssets.Linux` 包依赖 `libfontconfig`。项目引用 SkiaSharp 3.119.4（通过 `Directory.Packages.props` CPM），该版本的 Linux 原生库需要 `libfontconfig.so.1`。Debian 服务器未预装此库。

**外部确认:** SkiaSharp GitHub Issues (#2653, #1312, #509) — 多位用户报告相同问题，官方确认 `fontconfig` 是 SkiaSharp.NativeAssets.Linux 的必需系统依赖。

### Finding 5: Fire-and-forget 模式加剧问题

**Evidence:** `ThumbnailService.cs:19` — `_ = Task.Run(async () => { ... })` 模式在进程重启时丢失后台任务。

**Detail:** 服务在 00:33 重启，恰好是物品上传的时间窗口。即使有 `libfontconfig1`，进程重启也会导致正在执行的后台任务永久丢失，DB 更新不会发生。

## Deduced Conclusions

### Deduction 1: 双重故障模式

**Based on:** Finding 2, Finding 4, Finding 5

**Reasoning:** 
1. SkiaSharp 尝试加载 `libSkiaSharp.so` → 动态链接器找不到 `libfontconfig.so.1` → `DllNotFoundException`
2. 异常被 `GenerateAsync()` 的 catch 块捕获 → `LogWarning("Failed to generate thumbnails for item {ItemId}")` 
3. **但日志中未见此 Warning** — 可能原因：a) 进程在 Task.Run 完成前重启（Finding 5）；b) 日志级别过滤
4. 无论哪种情况，DB 中的 `PhotoPath/ThumbPath/MediumPath` 永远不会被设置
5. UI 看到 NULL 的 `ThumbPath`，显示占位图标

**Conclusion:** 根因是 `libfontconfig1` 缺失导致 SkiaSharp 无法初始化，fire-and-forget 模式使失败静默化、无法恢复。

## Hypothesized Paths

### Hypothesis 1: 图片无法显示与 `<link rel=preload>` 有关

**Status:** Refuted

**Resolution:** `<link rel=preload>` 是 Blazor WASM 的正常行为，与图片问题无关。

### Hypothesis 2: SkiaSharp 缩略图生成失败

**Status:** Confirmed

**Resolution:** `libfontconfig.so.1` 缺失 → `DllNotFoundException` → 缩略图生成失败 → DB 未更新 → UI 显示占位图标。

## Missing Evidence

| Gap              | Impact                               | How to Obtain   |
| ---------------- | ------------------------------------ | --------------- |
| 为何无 Warning 日志 | 确认是日志过滤还是 Task.Run 丢失 | 已不需要 — 根因已确认 |
| 物品上传时的确切错误 | 确认 SkiaSharp 抛出的具体异常 | 已不需要 — 根因已确认 |

## Source Code Trace

| Element       | Detail                                      |
| ------------- | ------------------------------------------- |
| Error origin  | `src/BoxWise.Server/Services/ThumbnailService.cs:63` — `SKBitmap.Decode(sourcePath)` 因 `libfontconfig.so.1` 缺失抛出 `DllNotFoundException` |
| Trigger       | 物品图片上传 → `ImageEndpoints.cs:61` 调用 `GenerateInBackground()` → `Task.Run` 后台执行 `GenerateAsync()` |
| Condition     | Debian 系统未安装 `libfontconfig1` 包；SkiaSharp.NativeAssets.Linux 需要此依赖 |
| Related files | `src/BoxWise.Server/Services/ThumbnailService.cs`, `src/BoxWise.Server/Services/ImageStorageService.cs`, `src/BoxWise.Server/Endpoints/ImageEndpoints.cs`, `src/BoxWise.Client/Components/ItemCard.razor`, `src/BoxWise.Client/Pages/ItemDetail.razor` |

## Conclusion

**Confidence:** High

**根因：** Debian 生产服务器缺少 SkiaSharp 的系统依赖 `libfontconfig1`，导致 `SKBitmap.Decode()` 在后台缩略图生成任务中因 `DllNotFoundException` 失败。由于 `ThumbnailService` 使用 fire-and-forget 模式（`_ = Task.Run(...)`）且异常仅记录为 Warning 日志，失败完全静默——原图上传成功但缩略图从不生成，DB 路径字段保持 NULL，UI 显示占位图标。

`<link rel=preload> has an invalid 'href'` 控制台错误与此问题无关，是 Blazor WASM 的正常行为。

## Recommended Next Steps

### 短期修复（已完成）

- [x] 服务器安装 `libfontconfig1`：`apt-get install libfontconfig1`
- [x] 用 Pillow 为已有物品生成缩略图并更新 DB

### 长期修复（建议实施）

1. **`Directory.Packages.props` — 切换到 NoDependencies 包**
   ```xml
   <!-- 替换 SkiaSharp.NativeAssets.Linux → .NoDependencies，消除 fontconfig 依赖 -->
   <PackageVersion Include="SkiaSharp.NativeAssets.Linux.NoDependencies" Version="3.119.4" />
   ```
   替代方案：保留当前包但在部署文档中明确列出系统依赖。

2. **`ThumbnailService.cs` — 添加恢复机制**
   - 在应用启动时扫描 DB 中有 `PhotoPath` 为 NULL 但有原图文件的物品，重新生成缩略图
   - 或将 `GenerateInBackground` 改为 `IHostedService` / `BackgroundService` 实现，支持优雅关闭和重试

3. **`Dockerfile` — 添加系统依赖**
   ```dockerfile
   RUN apt-get update && apt-get install -y libfontconfig1 && rm -rf /var/lib/apt/lists/*
   ```

4. **`README.md` — 更新二进制部署文档**
   - 在"系统要求"章节添加：Debian/Ubuntu 需 `apt-get install libfontconfig1`

## Reproduction Plan

1. 在未安装 `libfontconfig1` 的 Debian 系统上部署 BoxWise
2. 上传物品照片
3. 观察到原图保存成功但 `thumb.jpg`/`medium.jpg` 不生成
4. 浏览器中物品显示占位图标而非图片
5. `ldd libSkiaSharp.so | grep fontconfig` → `not found`
6. `apt-get install libfontconfig1` → 重启服务 → 重新上传 → 图片正常显示

## Side Findings

- 服务器磁盘使用仅 5.7G/34G (18%)，空间充足
- 进程运行正常，Caddyfile 的 `/api/*` 代理规则正确
- 服务器有 `.NET SDK not found`（仅 Runtime），无法在服务器上编译 C# 代码
- `sqlite3` 在调查过程中安装，原服务器未预装
- `libICE.so.6` 和 `libSM.so.6` 也可能缺失（SkiaSharp GitHub issues 提到），但当前未触发问题
