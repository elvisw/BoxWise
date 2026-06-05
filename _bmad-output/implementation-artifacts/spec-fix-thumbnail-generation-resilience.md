---
title: 'Fix Thumbnail Generation Resilience'
type: 'bugfix'
created: '2026-06-05'
status: 'in-review'
baseline_commit: '724aa73a7efab1cc2a62cca995621412d33598f1'
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** 生产环境 `libfontconfig1` 缺失导致 SkiaSharp 缩略图生成静默失败，`Task.Run` fire-and-forget 无恢复机制，所有物品图片无法显示。

**Approach:** 切换 `SkiaSharp.NativeAssets.Linux.NoDependencies` 消除系统依赖；用 `BackgroundService` + `Channel<T>`(100, DropWrite) 替换 fire-and-forget；启动 + 每 10 分钟周期扫描自动恢复缺失缩略图；更新部署文档。

## Boundaries & Constraints

**Always:**
- 继续使用 SkiaSharp 3.119.4，仅切换 NativeAssets 变体
- 保持 300px thumb + 1200px medium JPEG 85%
- 保持 API 契约不变（`GET/POST /api/images`）
- 保持存储布局不变（`{DataDirectory}/{itemId}/`）
- 恢复扫描不得阻塞正常上传（独立调度机制，不共享上传队列）
- Channel 满时 DropWrite，上传仍返回 202
- 周期扫描不重叠

**Never:**
- 不换图片处理引擎
- 不添加手动重生成按钮
- 不清理 per-item 锁字典（家用规模可接受）
- 不改变 SkiaSharp 主版本

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| 正常上传 | POST /api/images/upload, 10MB JPEG | 原图保存 → 入 Channel → 消费端生成 thumb+medium → DB 更新路径 → 返回 202 | N/A |
| 队列满 | Channel 中已有 100 个待处理项 | `TryWrite` 返回 false → 不入队 → Warning 日志 → 仍返回 202 | CAP-2 周期扫描兜底恢复 |
| 启动恢复-缺图 | Items 表有 ThumbPath=NULL 且 original.jpg 存在 | 扫描 → 获取 per-item 锁 → GenerateThumb → 更新 DB | 单张失败：Error 日志 + 跳过 + 继续 |
| 启动恢复-无原图 | Items 表有 ThumbPath=NULL 但 original.jpg 不存在 | 静默跳过（物品从未上传过图片，非故障） | 不记录日志或记录 Debug，不触发 Error |
| 损坏原图 | original.jpg 存在但 SKBitmap.Decode 抛异常 | Error 日志 → DB 保持 NULL → 保留文件 → 继续下一张 | 等待人工排查 |
| 上传+恢复同 item | 上传正在生成 item #1 缩略图，恢复扫描也遇到 item #1 | per-item SemaphoreSlim 互斥：后到达者等待（<100ms 持锁时间） | 不会死锁，不会并发写入 |
| 恢复扫描与队列中物品重复 | 物品 X 已入队但未处理，恢复扫描也查询到 X | per-item 锁确保只执行一次 GenerateThumb | 锁争用可忽略（持锁 <100ms）。由 Task 9 的并发安全测试验证 |
| 优雅关闭 (SIGTERM) | 正在处理缩略图 | 等待当前 item 完成（最长 30s）→ 退出；超时后接受可能残留半截文件，CAP-2 下次启动扫描/周期扫描恢复 | `stoppingToken` 传播到 `ReadAllAsync(ct)` 和 `WaitAsync(ct)`；30s 宽限期由 Host 关闭超时配置保证 |
| 周期扫描重叠 | 上一轮扫描 12 分钟未完成，第 10 分钟触发新周期 | 轻量标记检测 → 跳过本轮 | 无操作，等待下一周期 |

</frozen-after-approval>

## Code Map

- `Directory.Packages.props` — 搜索 `SkiaSharp.NativeAssets.Linux` 替换为 `.NoDependencies`
- `src/BoxWise.Server/Services/ThumbnailService.cs` — `GenerateThumb` 保持现有 `internal static` 签名不变；`_locks` 从 `private static readonly` 改为 `internal static readonly` 供 BackgroundService 共享；删除 `GenerateInBackground`/`GenerateAsync` 方法；移除不再需要的 `ImageStorageService` 和 `ILogger` 注入
- `src/BoxWise.Server/Services/ThumbnailBackgroundService.cs` — **NEW**：`BackgroundService`；构造函数注入 `IServiceScopeFactory`、`ILogger<ThumbnailBackgroundService>`、`ThumbnailService`（通过后者访问 `_locks` 字典）；单消费者 Channel 循环 + 启动扫描 + PeriodicTimer(10min) 周期扫描；需 `using System.Threading.Channels`（BCL 内置，无需 NuGet）
- `src/BoxWise.Server/Services/ThumbnailRequest.cs` — **NEW**：`internal readonly record struct ThumbnailRequest(int ItemId)`
- `src/BoxWise.Server/Endpoints/ImageEndpoints.cs` — 搜索 `GenerateInBackground`；替换端点签名：移除 `ThumbnailService thumbnail` 和 `IServiceScopeFactory scopeFactory` 参数 → 新增 `ThumbnailBackgroundService thumbnailBg` 参数；调用改为 `thumbnailBg.TryEnqueue(itemId)`
- `src/BoxWise.Server/Program.cs` — 搜索 `ThumbnailService` → 保留 Singleton 注册；新增 `AddHostedService<ThumbnailBackgroundService>()`
- `Dockerfile` — 审计（见任务 6 条件分支）
- `README.md` — 审计（见任务 7 条件分支）
- `docs/deployment-guide.md` — 新增"系统依赖"章节
- `src/BoxWise.Server.Tests/Services/ThumbnailBackgroundServiceTests.cs` — **NEW**：BackgroundService 单元测试
- `src/BoxWise.Server.Tests/Endpoints/ImageEndpointsTests.cs` — **NEW**：集成测试——使用 `WebApplicationFactory` + 内存中生成测试图片 + `FakeLogCollector` 验证满队列行为
- `src/BoxWise.Server.Tests/Services/ThumbnailServiceTests.cs` — 删除 `GenerateAsync_ValidItem_UpdatesDbPaths` 和 `GenerateAsync_ItemNotFound_NoOp`；新增 `GenerateThumb` 错误处理测试

## Tasks & Acceptance

依赖顺序：任务 0 → 任务 1（若 0 通过）；任务 1、2 可并行 → 任务 3 依赖 2 → 任务 4、5 依赖 3 → 任务 10 依赖 2 → 任务 6、7、8、9、11 依赖 4、5

**Execution:**
- [x] 0. NuGet.org 验证 `SkiaSharp.NativeAssets.Linux.NoDependencies` 3.119.4 存在 —— 若不存在则执行回退方案（见 Design Notes），跳过任务 1
- [x] 1. `Directory.Packages.props` — 替换 `SkiaSharp.NativeAssets.Linux` → `SkiaSharp.NativeAssets.Linux.NoDependencies` 3.119.4 — 消除 fontconfig 系统依赖（仅当任务 0 确认存在时执行）
- [x] 2. `src/BoxWise.Server/Services/ThumbnailService.cs` — `GenerateThumb` 保持现有 `internal static` 签名不变；`_locks` 从 `private static readonly` 改为 `internal static readonly`；删除 `GenerateInBackground` 和 `GenerateAsync` 方法及其调用的所有成员；若 `ImageStorageService _storage` 和 `ILogger _logger` 不再被使用则移除注入 — 解耦核心逻辑，暴露锁字典
- [x] 3. `src/BoxWise.Server/Services/ThumbnailBackgroundService.cs` — 新建 BackgroundService。构造函数注入 `IServiceScopeFactory`、`ILogger<ThumbnailBackgroundService>`、`ThumbnailService`（通过后者访问 `_locks` 字典）。实现：单消费者 `Channel<ThumbnailRequest>`(100, DropWrite) 循环 + 启动全量扫描 + PeriodicTimer(10min) 周期扫描 + 来源 SPEC 规定的全部日志。`stoppingToken` 直接传给 `ReadAllAsync(ct)` 和 `WaitAsync(ct)`——30s 宽限期由 Host 关闭超时保证。需 `using System.Threading.Channels`（BCL 内置，无需 NuGet 包）— 替换 fire-and-forget，提供恢复能力
- [x] 4. `src/BoxWise.Server/Endpoints/ImageEndpoints.cs` — 端点签名变更：移除 `ThumbnailService thumbnail` 和 `IServiceScopeFactory scopeFactory` 参数，新增 `ThumbnailBackgroundService thumbnailBg`；`GenerateInBackground(itemId, scopeFactory)` → `thumbnailBg.TryEnqueue(itemId)` — 上传路径接入 BackgroundService
- [x] 5. `src/BoxWise.Server/Program.cs` — `builder.Services.AddSingleton<ThumbnailService>()` 保留；新增 `builder.Services.AddHostedService<ThumbnailBackgroundService>()` — DI 注册
- [x] 6. `Dockerfile` — **若无回退（任务 0 通过）：** 审计确认无 `libfontconfig1` 安装步骤，如有则移除。**若回退生效（任务 0 失败）：** 添加 `RUN apt-get update && apt-get install -y libfontconfig1` — CAP-4 交付
- [x] 7. `README.md` — **若无回退：** 审计确认无 `libfontconfig1` 安装要求。**若回退生效：** 二进制部署系统要求添加 `apt-get install libfontconfig1` — CAP-4 文档对齐
- [x] 8. `docs/deployment-guide.md` — 新增"系统依赖"章节：Docker 零额外依赖，二进制部署仅需 .NET Runtime 10.0 —— CAP-4 文档交付
- [x] 9. `src/BoxWise.Server.Tests/Services/ThumbnailBackgroundServiceTests.cs` — BackgroundService 单元测试：启动恢复（含无原图物品静默跳过）、周期扫描、队列满拒绝、CancellationToken 优雅关闭、上传与恢复同 item 并发安全（验证 per-item 锁阻止并发写入）— 覆盖 I/O Matrix 7/9 场景
- [x] 10. `src/BoxWise.Server.Tests/Services/ThumbnailServiceTests.cs` — 删除 `GenerateAsync_ValidItem_UpdatesDbPaths` 和 `GenerateAsync_ItemNotFound_NoOp`（方法已移除）；新增测试：损坏原图 → `GenerateThumb` 抛异常；正常图片 → 生成 thumb+medium。通过临时目录中的合成 JPEG 文件测试 `internal static GenerateThumb`
- [x] 11. `src/BoxWise.Server.Tests/Endpoints/ImageEndpointsTests.cs` — **NEW 集成测试**。使用 `WebApplicationFactory<Program>`；内存中生成 101 张测试图片（避免磁盘 I/O）；通过 `CookieAuthenticationStateProvider` 的测试模式或 `WithAuthentication` 扩展处理认证；使用 `FakeLogCollector` 或 `ITestOutputHelper` 捕获日志；确认第 101 次上传返回 202 + Warning — 验证端到端满队列行为

**Acceptance Criteria:**
- Given 全新 Debian 部署（无 libfontconfig1），when 上传图片，then thumb.jpg + medium.jpg 正常生成且 DB 路径正确（手动验证：docker compose up + 浏览器）
- Given 某物品缩略图被手动删除且 DB 路径清空，when 等待 10 分钟（无需重启），then 缩略图自动恢复
- Given Channel 队列已有 100 项，when 上传新图片，then 返回 202 + Warning 日志，CAP-2 周期扫描兜底
- Given 正在生成缩略图，when 发送 SIGTERM，then 30s 内当前 item 完成（超时则接受可能残留的半截文件，CAP-2 周期扫描或下次启动恢复）（手动验证：docker compose + `docker kill -s TERM`）
- Given `dotnet test BoxWise.slnx`，when 运行全部测试，then 所有测试通过

## Design Notes

### ThumbnailRequest 类型

```csharp
// 新文件：src/BoxWise.Server/Services/ThumbnailRequest.cs
internal readonly record struct ThumbnailRequest(int ItemId);
```

### Channel 与 TryEnqueue 语义

`Channel.CreateBounded<ThumbnailRequest>(100, BoundedChannelFullMode.DropWrite)`。`TryEnqueue(int itemId)` 封装 `_channel.Writer.TryWrite(new ThumbnailRequest(itemId))`——同步方法，返回 `bool`（true=入队成功，false=队列满已丢弃）。不使用 CancellationToken（TryWrite 永不阻塞）。

### Channel 消费端异常处理

消费者循环 `await foreach (var req in _channel.Reader.ReadAllAsync(ct))` 主体必须包裹 `try-catch`——与恢复扫描路径对称处理损坏文件。`SKBitmap.Decode` 失败 → Error 日志 → DB 保持 NULL → 继续下一项。DB `SaveChangesAsync` 失败（SQLite 忙/瞬态错误）→ Error 日志 → 继续下一项，下次周期扫描重试。不会导致消费者循环退出。

### Per-item 锁共享

`ThumbnailService._locks`（`internal static readonly ConcurrentDictionary<int, SemaphoreSlim>`）由 `ThumbnailBackgroundService` 在构造函数中通过注入的 `ThumbnailService` 单例访问——无需引入新的抽象层。恢复扫描和 Channel 消费在获取锁时遵循相同模式：`await locks.GetOrAdd(itemId, ...).WaitAsync()` → `GenerateThumb()` → `finally { Release() }`。

### 恢复扫描实现

启动扫描优先执行 → 然后创建 `PeriodicTimer(10min)` 并进入等待循环。**计时器在启动扫描完成后才创建**，避免启动扫描耗时 >10min 导致首个 tick 立即触发。轻量标记（`Interlocked.CompareExchange`）防止周期重叠。扫描查询复用 EF Core `AppDbContext`（通过 `IServiceScopeFactory` 创建 scope）。无原图的物品（`original.jpg` 不存在）静默跳过，不记录 Error。

### 回退方案

若 `SkiaSharp.NativeAssets.Linux.NoDependencies` 3.119.4 在 NuGet.org 不存在（任务 0 验证失败）：跳过任务 1，保留当前 `SkiaSharp.NativeAssets.Linux`，在任务 6（Dockerfile）和任务 7（README.md）中**添加** `apt-get install libfontconfig1` 依赖说明（而非移除）。

### Review Findings

- [ ] [Review][Patch] P1: TryEnqueue always returns true + no Warning log on queue full [`src/BoxWise.Server/Services/ThumbnailBackgroundService.cs:35-42`] — Propagate TryWrite return value; return false when dropped; log Warning on drop; only log Debug on actual success
- [ ] [Review][Patch] P2: Channel never completed on shutdown — items lost silently [`src/BoxWise.Server/Services/ThumbnailBackgroundService.cs`] — Override StopAsync, call _channel.Writer.TryComplete(), drain remaining items during shutdown grace period
- [ ] [Review][Patch] P3: Flaky test — channel fill races with consumer [`src/BoxWise.Server.Tests/Endpoints/ImageEndpointsTests.cs:277-291`] — Fill channel BEFORE starting the host or use synchronous fill
- [ ] [Review][Patch] P4: Task.Delay(500) fragile timing assumption [`src/BoxWise.Server.Tests/Services/ThumbnailBackgroundServiceTests.cs:593`] — Use ManualResetEvent or polling with timeout instead
- [ ] [Review][Patch] P5: Dual scope antipattern in ProcessItemAsync [`src/BoxWise.Server/Services/ThumbnailBackgroundService.cs:162,190`] — Inject ImageStorageService directly (singleton), eliminate unnecessary first scope
- [ ] [Review][Patch] P6: Recovery query misses ThumbPath="" empty string [`src/BoxWise.Server/Services/ThumbnailBackgroundService.cs:116`] — Add `|| i.ThumbPath == ""` to WHERE clause
- [ ] [Review][Patch] P7: ThumbnailService constructor parameter unused [`src/BoxWise.Server/Services/ThumbnailBackgroundService.cs:20-29`] — Remove unused parameter; static Locks initializes on first access
- [ ] [Review][Patch] P8: ExecuteAsync_CancelsGracefully resource leak [`src/BoxWise.Server.Tests/Services/ThumbnailBackgroundServiceTests.cs:564-569`] — Call StopAsync/Dispose after test
- [ ] [Review][Patch] P9: Initial scan missing _scanInProgress guard [`src/BoxWise.Server/Services/ThumbnailBackgroundService.cs:49`] — Set _scanInProgress=1 before initial scan to prevent overlap with first periodic tick
- [x] [Review][Defer] D1: ConcurrentDictionary SemaphoreSlim leak — deferred, pre-existing (spec explicitly marks as "不清理")
- [x] [Review][Defer] D2: Thread.Sleep(200) in test Dispose cleanup — deferred, low-impact test helper

## Verification

**Commands:**
- `dotnet build BoxWise.slnx` -- expected: 零错误零警告
- `dotnet test BoxWise.slnx` -- expected: 全部测试通过（含新增）

**Manual checks:**
- `ldd publish/libSkiaSharp.so | grep fontconfig` -- expected: not found（NoDependencies 后无需此依赖）
- `docker compose up` + 浏览器上传图片 -- expected: 缩略图正常显示
- `docker compose up` + `docker kill -s TERM boxwise` -- expected: 优雅关闭，30s 内当前缩略图完成
