---
id: SPEC-fix-thumbnail-generation-resilience
companions: []
sources:
  - _bmad-output/implementation-artifacts/investigations/production-images-not-displaying-investigation.md
---

> **Canonical contract.** This SPEC and the files in `companions:` are the complete, preservation-validated contract for what to build, test, and validate. Source documents listed in frontmatter are for traceability only — consult them only if you need narrative rationale or prose color this contract intentionally omits.

# Fix Thumbnail Generation Resilience

## Why

生产环境 Debian VPS 上所有物品图片无法显示。调查确认根因为 `libfontconfig.so.1` 缺失导致 SkiaSharp 原生库加载失败，后台缩略图生成静默失败。同时 `ThumbnailService` 使用 `_ = Task.Run(...)` fire-and-forget 模式，无 CancellationToken、无优雅关闭、无失败恢复。这是一个**运维风险**（漏装依赖 → 静默功能缺失）+ **架构韧性缺陷**（无恢复机制）的组合问题。

## Capabilities

实施顺序：CAP-1 和 CAP-3 可并行 → CAP-2 依赖 CAP-3 的 BackgroundService 骨架 → CAP-4 最后。

- id: CAP-1
  intent: 消除 SkiaSharp 对 `libfontconfig1` 的系统级依赖，使应用在最小化 Debian/Ubuntu 环境中无需手动安装额外系统包即可正常运行
  success: 在仅包含 `libc6`、`libpthread` 等基础库的 Debian 容器中，上传图片后 `thumb.jpg` 和 `medium.jpg` 正常生成，DB 路径字段正确更新（手动验证——项目当前无 Docker CI，通过 `docker compose up` + 浏览器确认）

- id: CAP-2
  intent: 应用启动时 + 运行期间每 10 分钟自动扫描所有有原图但缺缩略图的物品并重新生成（恢复扫描使用独立的 `SemaphoreSlim(1, 1)` 串行调度，与上传路径共享 per-item 锁）；若上一轮扫描未完成则跳过本轮；确保崩溃、磁盘满恢复后、队列满被拒等场景下的缩略图缺失都能自愈
  success: 手动删除某物品的 `thumb.jpg` 和 `medium.jpg` 并清空 DB 路径字段后，10 分钟内缩略图自动恢复（无需重启）；恢复期间 `GET /api/items` 响应正常（手动验证：浏览器浏览页面不卡顿，图片正常加载）

- id: CAP-3
  intent: 后台缩略图生成任务改为 `BackgroundService` 托管，使用有界 `Channel<T>`(100, `BoundedChannelFullMode.DropWrite`) 接收上传触发的生成请求，单消费者循环出队处理；支持通过 `CancellationToken` 优雅关闭（等待当前正在生成的缩略图完成后退出，最长等待 30s；超时后接受当前 item 可能残留半截文件，由 CAP-2 周期扫描或下次启动恢复）；满队列时上传端点仍返回 202 但不入队（记录 Warning）
  success: 上传端点满队列时返回 202 且日志含 `"Thumbnail queue full"`；SIGTERM 后正在处理的缩略图在 30s 内完成则无半截文件残留，超过 30s 则 CAP-2 在下次周期扫描中恢复

- id: CAP-4
  intent: 部署文档明确列出 Linux 部署的所有必需系统依赖，Dockerfile 同步添加，确保按文档部署后零手动修复
  success: 在全新 Debian 13 最小化安装上按 README 步骤部署，所有功能（含图片缩略图生成）开箱即用

### 恢复扫描工作方式

CAP-2 的恢复扫描在 `BackgroundService.ExecuteAsync` 中实现：
1. 启动时立即执行一次全量扫描
2. 此后每 10 分钟执行一次全量扫描（查询：`SELECT Id FROM Items WHERE ThumbPath IS NULL OR ThumbPath = ''`——大多数周期结果集为空，开销 ~ 一次索引查询）
3. 若上一轮扫描尚未完成，本轮跳过（轻量标记保护，不引入重量级定时器同步）
4. 对每个结果：**先获取 per-item SemaphoreSlim** → 检查 `original.jpg` 是否存在 → 存在则调用 `GenerateThumb()`（**不经过 Channel**，绕过上传队列，避免与正常上传争抢队列槽位）
5. 使用独立的 `SemaphoreSlim(1, 1)` 控制恢复任务间串行调度——恢复不急，不抢占 CPU
6. 每次扫描完成后记录 Information 日志

### Per-item 并发保护

上传路径（CAP-3 的 Channel 单消费者循环）和恢复路径（CAP-2 的周期扫描）**共享 per-item `ConcurrentDictionary<int, SemaphoreSlim>`**：
- 上传消费端出队后：`await _locks.GetOrAdd(itemId, ...).WaitAsync()` → `GenerateThumb()` → `finally { Release() }`
- 恢复扫描处理 item 时：同上流程（先获取锁再检查文件，与上传路径对称）
- 同 item 的两个路径互斥，防止并发写入导致文件损坏
- **已知限制：** 字典不清理——SemaphoreSlim 实例随历史上传过的 item 数线性增长（每个 ~200B，万级 item ≈ 2MB）。在 BoxWise 家用规模下可接受，不作为本次修复范围

### 损坏原图恢复策略

`GenerateThumb()` 中 `SKBitmap.Decode(original.jpg)` 抛出异常时：
1. 记录 Error 日志 `"Failed to recover thumbnails for item {ItemId}: {exception}"`（唯一一条，包含完整异常）
2. 保留 `original.jpg` 文件不动（不删除、不移动）——等待人工排查
3. DB 路径字段保持 NULL
4. 继续处理下一个物品
5. 残留的半截/0 字节输出文件由 `GenerateThumb` 的 `File.Create` 自动覆盖——下次重试时自然修复

### 可观测性

所有日志使用统一的模板：

- 恢复扫描完成时：Information `"Thumbnail recovery scan: {recovered} recovered, {failed} failed, {scanned} scanned"`
- 单张恢复失败时：Error `"Failed to recover thumbnails for item {ItemId}"` + 异常详情
- 上传路径满队列时：Warning `"Thumbnail queue full ({capacity}), item {ItemId} will be recovered on next recovery scan"`
- 优雅关闭超时时：Warning `"Thumbnail generation did not complete within shutdown grace period ({pendingCount} items pending, will be recovered)"`

### 可测试性

- `BackgroundService.ExecuteAsync` 接受 `CancellationToken` — 单元测试中通过 `CancellationTokenSource.Cancel()` 触发退出
- Channel 在 `BackgroundService` 内部创建为 `Channel.CreateBounded<ThumbnailRequest>(100)`（硬编码，与 constraint 一致）；满队列行为通过**集成测试**验证——快速连续上传 101 张图片，确认第 101 次返回 202 + Warning 日志
- `GenerateThumb` 为 `internal static` — 测试可直接调用，不需真实文件（通过临时目录 + 测试图片）

## Constraints

- 不更换图片处理引擎 — 继续使用 SkiaSharp（`SkiaSharp` NuGet 包），仅切换 NativeAssets 变体
- 不改变缩略图尺寸规格 — 保持 300px (thumb) + 1200px (medium) JPEG 85%
- 不改变 API 契约 — `GET /api/images/{itemId}?type=` 和 `POST /api/images/upload` 的行为不变
- 不改变存储布局 — 继续使用 `{DataDirectory}/{itemId}/original.jpg|thumb.jpg|medium.jpg`
- 不引入新的外部服务依赖 — 恢复队列为进程内 `Channel<T>` 有界队列，最大容量 100，满时 `DropWrite`；消费端为单消费者循环
- 恢复扫描使用独立的调度机制（不经过 Channel 直接生成、独立 `SemaphoreSlim(1, 1)` 串行），但与上传路径共享 per-item 锁防止同 item 并发写入
- 周期扫描不重叠 — 新周期触发时若上一轮未完成则跳过
- 优雅关闭最长等待 30 秒 — 超出后强制退出，CAP-2 的启动扫描或周期扫描兜底恢复

## Non-goals

- 不迁移已有的 `original.jpg` 文件或重新编码
- 不添加手动"重新生成缩略图"的 Admin UI 按钮（CAP-2 的自动恢复已覆盖）
- 不改变 SkiaSharp 主版本（保持 3.119.x）
- 不处理图片上传本身的重试或断点续传
- 不提供缩略图生成的实时进度查询 API
- 不清理 per-item `ConcurrentDictionary` 中的历史 SemaphoreSlim 实例（家用规模下内存影响可忽略）

## Success signal

在全新 Debian 13 最小化安装上按 README 部署 BoxWise：
1. 上传一张物品照片 → 浏览页面缩略图正常显示
2. 故意删除缩略图文件并清空 DB 路径字段 → 10 分钟内缩略图自动恢复（无需重启），日志含 `"Thumbnail recovery scan: 1 recovered, 0 failed"`
3. 队列满场景：快速连续上传超过 100 张图片 → 第 101 次上传仍返回 202，日志含 `"Thumbnail queue full"`
4. SIGTERM 优雅关闭时，正在处理的缩略图在 30s 内完成且无半截文件

以上均通过浏览器 + `docker compose logs` 手动验证（项目当前无端到端 CI）。

## Assumptions

- 假设 `SkiaSharp.NativeAssets.Linux.NoDependencies` 3.119.4 在 NuGet.org 存在且与 `SkiaSharp` 3.119.4 版本对齐（CAP-1 实施前需验证，验证失败则回退到保留 `SkiaSharp.NativeAssets.Linux` + 文档中添加 `libfontconfig1` 依赖说明）
- 假设 NoDependencies 包的核心编解码能力（JPEG/PNG/WebP 解码、缩放、JPEG 编码）与当前 `SkiaSharp.NativeAssets.Linux` 等效；NoDependencies 移除的 `libfreetype`/`libharfbuzz` 仅影响文字渲染，当前缩略图生成不涉及文字
- 假设启动时恢复扫描的 Items 数量在千级以内，全表扫描可在 100ms 内完成；超过万级时扫描本身仍安全但恢复耗时线性增长，届时需考虑分页
- `.NET 10` 默认 Docker 基础镜像已切换至 Ubuntu 24.04 Noble，该镜像与 Debian Slim 均不预装 `libfontconfig1`——CAP-1 对 Docker 和二进制部署均有实际收益
- CAP-1 切换 NoDependencies 后，Dockerfile 无需任何变更（Ubuntu Noble 基础镜像已包含 NoDependencies 所需的所有库：`libc6`、`libpthread`、`libdl`、`libm`）

### 交付物清单（CAP-4）

基于 CAP-1 切换 NoDependencies 的最终决策：

| 文件 | 变更 |
|------|------|
| `Directory.Packages.props` | 替换 `SkiaSharp.NativeAssets.Linux` → `SkiaSharp.NativeAssets.Linux.NoDependencies`（版本 3.119.4） |
| `README.md` | 审计确认文档中未要求安装 `libfontconfig1`（NoDependencies 已消除此依赖）；如有则移除 |
| `Dockerfile` | **无需变更**（NoDependencies + Ubuntu Noble 基础镜像已包含所有必需库） |
| `docs/deployment-guide.md` | 新增"系统依赖"章节说明：Docker 零额外依赖，二进制部署仅需 .NET Runtime 10.0 |
