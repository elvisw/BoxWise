---
title: '位置照片与描述'
type: feature
ticket: '1'
created: '2026-11-25'
status: built
baseline_revision: 'd00e769f1b5a7b78caab4342046507fc4def084a'
route: full
route_source: auto
risk: medium
review: quick
review_source: pinned
lenses_ran: [quick]
review_loop_iteration: 0
context:
  - '{project-root}/_bmad-output/backlog/story-location-photo-and-description.md'
  - '{project-root}/_bmad-output/initiative-boxwise/ux-location-photo-and-description/EXPERIENCE.md'
  - '{project-root}/_bmad-output/initiative-boxwise/ux-location-photo-and-description/DESIGN.md'
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** 纯文字不易区分相似的抽屉或箱子。

**Approach:** 新建/编辑共用表单；浏览和物品详情按需查看位置照片。

## Boundaries & Constraints

**Always:** Follow Story #1 and its UX: optional description (trim before counting 300 graphemes; preserve internal line breaks) and one JPG/PNG/WebP ≤10 MiB (10 MB in the UI); validate on both sides. Fetch photos only on demand. Isolate location and item files.

**Never:** Change item photos, filters, tree keyboard behavior, or Back navigation. No URL probing, in-place overwrite, or migration/backfill of old business data. Run only with isolated temporary data.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected | Error |
|---|---|---|---|
| Save | Metadata fails or is unconfirmed | Do not submit photo mutation | Keep persisted photo |
| Create | Metadata saved, photo failed | Keep location; retry by saved ID | Keep preview; focus feedback |
| Edit | Photo replacement fails before commit | Keep original bytes and saved metadata | Retry only photo |
| Write | Commit unknown | Re-read server state before retry; block blind resubmit | Do not assert rollback |
| Delete | Child/item present at deletion | Refuse without changing record, assignments or photo | Preserve existing nonempty feedback |
| Cleanup | Location already deleted, file removal fails | Show deletion and cleanup warning separately; photo GET returns 404 | Do not retry the committed delete |

</frozen-after-approval>

## Code Map

Paths relative to `src/`.

- `BoxWise.Server/Program.cs` runs `MigrateAsync`. `Services/ImageStorageService.cs` uses numeric item directories; do not reuse its overwrite/delete paths.
- `BoxWise.Server/Repositories/LocationRepository.cs`: reuse `ResolvePathNamesAsync`; stored `Path` is an ID chain. `Data/Configurations/ItemConfiguration.cs` uses `SetNull`, not a nonempty delete guard.
- `BoxWise.Client/Pages/ItemDetail.razor` already resolves API image origin via `Http.BaseAddress`. `wwwroot/service-worker.published.js` caches `/api/images/`; keep location photos outside it.
- `BoxWise.Server.Tests/Endpoints/ImageEndpointsTests.cs` illustrates temporary storage configuration, not an auth fixture: it relaxes fallback authorization. InMemory cannot prove SQLite concurrency.

## Tasks & Acceptance

**Execution:**
- [x] `BoxWise.Server/Models/Location.cs`, `Data/Configurations/LocationConfiguration.cs`, `Migrations/` — nullable fields and `AddLocationDescriptionAndPhoto` migration; no backfill. `BoxWise.Shared/Dtos/LocationDto.cs` and `Text/GraphemeRules.cs` (new) — shared validation and photo presence.
- [x] `BoxWise.Server/Repositories/LocationRepository.cs`, `Endpoints/LocationEndpoints.cs` — by-ID metadata with name path; omitted description unchanged, explicit empty clears. Check child/item absence atomically with delete; expose a distinct cleanup-warning result.
- [x] `BoxWise.Server/Endpoints/LocationPhotoEndpoints.cs` and `Services/LocationPhotoStore.cs` (new), `Program.cs` — authenticated, protected writes; `/api/locations/{id}/photo` reads only the committed pointer; verified MIME and `no-store`. Keep multipart overhead separate from strict file limit.
- [x] `BoxWise.Client/Services/LocationService.cs` and `Components/LocationManageDialog.razor` — save changed metadata first; map rejection, partial success, cleanup warning and unknown outcomes. Only one pending photo action.
- [x] `BoxWise.Client/Components/LocationPhotoField.razor` and `LocationPhotoDialog.razor` (new), `Pages/Browse.razor`, `Pages/ItemDetail.razor` — follow UX; fetch by-ID metadata from detail without changing `ItemDto`; ignore late responses on switch/close. Render description as wrapping text with line breaks.
- [x] `BoxWise.Server.Tests/Endpoints/LocationPhotoEndpointsTests.cs` and `BoxWise.Client.Tests/Services/LocationServiceTests.cs` (new) — automate matrix, auth with actual fallback, same-ID isolation, SQLite delete race, post-staging/precommit fault and lost response.
- [x] `docs/api-contracts-server.md`, `docs/deployment-guide.md` — API outcomes, location-photo backup path, reverse proxy upload overhead.

**Acceptance Criteria:**
- Given fresh temporary SQLite, when provisioned and restarted after saving, then description and photo bytes persist.
- Given 300 graphemes including emoji and combining marks, when saved, then both sides accept 300 and reject 301 without truncation.
- Given an image of exactly 10 MiB, when uploaded through documented proxy settings, then it succeeds; 10 MiB + 1 byte is rejected without changing the saved photo.
- Given browse, item detail or management, when opened, then no photo file is requested until activation.
- Given same-ID location/item fixtures, when either is deleted, then the other's photo stays accessible.

## Implementation Notes

- Shared: `GraphemeRules` 使用 `StringInfo` 字素簇计数（emoji ZWJ、组合字符、CRLF 均计 1）；`LocationPhotoRules` 统一 10 MiB 限制、MIME 白名单与文件头魔数检测。
- Server: `Locations.Description` / `PhotoFileName` 由 `AddLocationDescriptionAndPhoto` 迁移创建，无回填。`GET /api/locations/{id}` 返回描述、`hasPhoto` 与名称路径 `namePath`；`PUT` 以 `description` 是否出现区分“保持不变”和“清除”。
- 照片存储: `<DataDirectory>/location-photos/`，服务器生成唯一文件名；暂存 → 发布 → 提交指针 → 清理旧文件。启动时清理暂存区。单实例互斥锁串行化照片写入、替换、移除与位置删除。
- 删除: 子位置/物品检查与删除在同一事务内，提交前再以 `AsNoTracking` 复核，避免检查后被并发挂载；删除已提交但文件清理失败返回 `200 + cleanupWarning:true`，客户端不重试删除。
- Client: 编辑保存先更新名称/描述，再执行照片替换/移除；未知结果禁用再次保存并要求关闭后重新核对。部分成功保留本地预览并切换为编辑已创建位置，重试只上传照片。表单内预览与弹窗均使用按需字节加载，晚到响应由 generation token 丢弃。
- 可访问性: 浏览页选择变化用隐藏 polite 区域去抖播报；照片入口关闭后显式恢复焦点；表单 disclosure 使用 `aria-expanded`/`aria-controls`，错误文本与重试/关闭控件分离。
- Tests: `dotnet build BoxWise.slnx` 0 warnings；`dotnet test BoxWise.slnx` 全绿（Server 303 / Client 59）。新增用例覆盖 300/301 字素边界、10 MiB 与 10 MiB+1、MIME/魔数、替换/移除、同 ID 隔离、删除竞争、暂存后发布失败、清理警告、跨重启持久化与丢失响应。

### 主代理复验修复（2026-11-25）

- 删除原子性（修正上一条“提交前复核”描述）：关系型数据库改为单条条件 DELETE（`NOT EXISTS` 子查询检查子位置/物品），由 SQLite 语句原子性保证；移除了事务后重复查询与生产 `SimulateConcurrentWriteAsync` 虚拟方法。InMemory 仍走内存检查路径。
- 上传失败语义：`SaveChangesAsync` 失败后用新 scope/连接、且不依赖已取消请求 token 读取已提交指针；区分“确认已引用 / 确认未引用 / 无法确认”，只有确认新文件未被引用才清理，无法确认时新旧文件都保留。响应仍为错误，不冒充回滚。删除照片的 `SaveChanges` 失败也做同样的三态核对。新增确定性测试使用真实临时 SQLite + `SaveChangesInterceptor`：提交前失败旧图字节不变且只保留一个文件；提交后失败可重新核对并读到新图；移除照片的提交前/后失败分别保留文件或按确认未引用清理。
- 写结果分类：客户端 5xx 改为 Unknown（不自动重提），仅确定的 400/413/415 等业务拒绝为 Rejected，404 保持 NotFound。照片 PUT/DELETE 复用 `CsrfValidationFilter`，客户端显式带 `X-Requested-With: XMLHttpRequest`。
- 请求边界：照片端点自行设置 10 MiB + multipart 开销的请求体上限，手动解析 multipart（只允许一个 `file` 字段），用 SkiaSharp 实际解码验证 JPG/PNG/WebP，而非只信扩展名或声明 MIME；`Program.cs` 的全局 `FormOptions` 恢复为物品上传原有的 10 MiB。
- 保存编排：新增 `LocationSaveCoordinator` 纯逻辑，资料明确失败/未知时不调用照片操作；编辑只提交实际改动（仅改名时省略 description），重试沿用已创建位置的 ID。照片/移除 404 提示位置已不存在并刷新，不提供无效重试；资料已确认保存后的界面/刷新异常不再报“未保存”。
- 描述清除契约：`UpdateLocationRequest.Description` 改为非可空 `JsonElement`（属性缺失 = `Undefined`，显式 `null` = `Null`），修复可空 `JsonElement?` 将 `null` 反序列化为缺失、导致“清空描述”不生效的问题；新增 `Description_ExplicitJsonNull_Clears` 并保留“省略不变 / 空串清除”测试。
- CSRF 重复头：`CookieHandler` 仅在请求未带 `X-Requested-With` 时补默认值，避免与照片写操作显式头叠加成 `XMLHttpRequest, XMLHttpRequest` 被过滤器拒绝。
- 删除数据矩阵证据（隔离 SQLite 两连接）：`Delete_ItemMovedInByAnotherConnection_Rejected_ItemLocationAndPhotoRemain`（另一连接先提交移入，删除被拒，物品 LocationId 未被清空，位置描述/照片保留）与 `Delete_EmptyLocationThenMoveIn_RejectedByForeignKey_NotSilentlyOrphaned`（先删空位置，后续移入被外键拒绝，物品仍留在原位置）。
- 上传/防护测试：`Upload_BeforeCommitFailure_KeepsOldBytesAndRemovesOnlyUnreferencedFile`、`Upload_AfterCommitFailure_RecheckSeesCommittedNewBytes`、`Remove_BeforeCommitFailure_KeepsPointerAndFile`、`Remove_AfterCommitFailure_RecheckClearsFile`、`PhotoWrites_WithoutCsrfHeader_Rejected`、`Upload_NonMultipart_Rejected`、`Upload_MultipleFileParts_Rejected`、`Upload_FieldNotNamedFile_Rejected`、`Upload_TruncatedImage_RejectedEvenWithJpegMagic`；原有 10 MiB/10 MiB+1、PNG/WebP、替换/移除、清理警告、同 ID 隔离继续通过。
- 客户端证据：`LocationSaveCoordinatorTests` 覆盖资料拒绝/未知/异常不触发照片、创建成功+照片失败后重试只用原 ID、仅改名省略 description、显式清空 description、无资料改动直接上传、照片/移除 404 与清理警告；`LocationServiceTests` 覆盖 5xx→Unknown、413→Rejected、CSRF 400→Rejected、PUT/DELETE 带 `X-Requested-With`。
- 本轮改动文件：`src/BoxWise.Server/Endpoints/LocationPhotoEndpoints.cs`、`src/BoxWise.Server/Repositories/LocationRepository.cs`、`src/BoxWise.Server/Program.cs`、`src/BoxWise.Client/Services/LocationService.cs`、`src/BoxWise.Client/Services/LocationSaveCoordinator.cs`（新）、`src/BoxWise.Client/Services/CookieHandler.cs`、`src/BoxWise.Client/Components/LocationManageDialog.razor`、`src/BoxWise.Client/Components/LocationPhotoField.razor`、`src/BoxWise.Shared/Dtos/UpdateLocationRequest.cs`、`src/BoxWise.Server.Tests/Endpoints/LocationPhotoEndpointsTests.cs`、`src/BoxWise.Client.Tests/Services/LocationServiceTests.cs`、`src/BoxWise.Client.Tests/Services/LocationSaveCoordinatorTests.cs`（新）、`docs/api-contracts-server.md`、`docs/deployment-guide.md`、`docs/component-inventory-client.md`。
- 剩余：真实浏览器/代理/读屏/手机相机、320px/200%/横屏/44×44 的实测仍待主代理下一阶段；本轮只用隔离临时 SQLite 与临时图片目录，未启动应用、未访问现有 `data/` 或业务数据库。
- 本轮最终验证：`dotnet build BoxWise.slnx` 0 warnings；`dotnet test BoxWise.slnx --no-build` → Server 314 通过、Client 79 通过、0 失败。

### 复审补充修复（2026-11-25，第二轮）

- 焦点恢复：浏览页/物品详情等待 `dialog.Result` 完成后再恢复；入口按钮不可用时回退到页面内预设的 `tabindex="-1"` 容器，使用 `preventScroll`，不导航。
- 管理位置：照片重试仅在资料未改动且待处理照片意图匹配时显示；照片意图变化清除旧重试提示；保存成功后同步服务端规范化字段并更新本地已确认行；刷新失败时禁用过期编辑；创建/编辑拒绝文案区分；删除异常按 Unknown 重载；保存/删除期间禁用 MudDialog 自带关闭入口，仅保留受守卫的“完成”。
- 保存结果：上传成功返回服务端 DTO，移除成功返回 `HasPhoto=false` 的更新 DTO；200 清理警告正文缺失/畸形/形状不符时按“可能有警告”保守处理。
- 服务端：SkiaSharp 有界解码（优先缩放整图，否则解码两个角落的有界子区域）验证像素可读；新增良性截断 JPEG 回归。删除并发证据改为在条件 DELETE 执行前暂停、另一条隔离 SQLite 连接提交物品移入。
- 客户端：照片选择改为原生同步按钮触发隐藏 `InputFile`，移除相机权限探测；对话路径改用无箭头可访问文本并对视觉路径 `aria-hidden`。
- 文档/清理：修正条件 DELETE 描述与服务表位置；移除未使用的 `RenameLocationRequest` 及活动文档引用；明确 `LocationPhotoStore` 仅清理 staging，不扫描已发布孤儿文件。
- 本轮 focused tests：`LocationPhotoEndpointsTests` 35 通过；`LocationServiceTests` + `LocationSaveCoordinatorTests` 51 通过。
- 浏览器复跑修复：`MudSelectItem` 显式 `T="int?"` 并全部传 nullable（含根位置）；照片重试在开始加载后把焦点移到稳定区域（对话框内容/内联预览）而非被移除的按钮，不改变初始展开/查看的焦点；空 `FileList` 在读取 `e.File` 前直接返回，取消选择不报错、不进入加载；相机提示改为首次渲染时被动查询权限与 `enumerateDevices()`，仅在 `unsupported`/`denied`/明确 `no-device` 时显示原有的 polite 内联文案，`prompt` + 空列表或权限名不支持时返回 unknown 不推断拒绝，设备选图始终可用；原生同步 `InputFile` 触发保持。

### 像素验证回归修复（2026-11-25，第三轮）

- 原因：512px 缩放/子区域接口并非所有 codec 支持，合法 PNG（768×576、1200×900、4000×3000、6000×4500）和 JPEG（6000×4500）被误拒。
- 修复：像素验证改为分层策略——JPEG 逐行 scanline 全图 → WebP 512px `SKBitmap` 缩放解码 → 受限完整解码（PNG）。不再依赖 codec 的缩放/子区域接口，避免把不支持该接口的合法 PNG/JPEG 误判为损坏。
- 内存/并发：JPEG scanline 使用 ≤64 行、单行 ≤256 KiB 的缓冲；完整解码上限 32 MP / 128 MiB，并用进程内静态 `SemaphoreSlim(1,1)` 串行，避免并发放大内存峰值；超过 64 MP 的 scanline/缩放路径直接拒绝。
- 新增实际端点验收：PNG 768×576、PNG 1200×900、PNG 4000×3000、PNG 6000×4500、JPEG 6000×4500 均上传成功并可读回原字节；截断 PNG/WebP（头部可解析、像素不完整）被 400 拒绝且旧照片不变；既有截断 JPEG 用例继续通过。`LocationPhotoEndpointsTests` 42 通过。
- 安全边界：PNG 等需要完整解码的图片超过 32 MP 时返回 400 `图片尺寸过大，无法安全处理，请换一张照片。`；这是解码炸弹防护，未修改 frozen plan 或文件 10 MiB/格式规则。若产品不接受该边界，需要另行确认。

### 独立验收补记（2026-11-25）

- .NET SDK 10.0.404：隔离数据条件下，依赖审计、Release 构建（0 警告）与 408 项测试（Server 323、Client 85，0 失败、0 跳过）通过。此轮包含像素校验和弹窗 ARIA 修复，早于最后一行相机状态修正。
- Chrome/SQLite/Caddy：按需加载、层级选择、故障后重试、未知结果下不重复创建、同 ID 隔离、重启持久化及 10 MiB 代理边界通过。窄屏、横屏、触控尺寸、键盘测试通过；200% 文本缩放为模拟。
- 弹窗路径：Chrome DOM/CDP AX 专项 21/21，实际 `role=dialog` 关联无箭头的完整路径；重试、重开及各关闭方式未丢焦点。未执行真实读屏、手机相机或 Nginx 验收。
- 相机：Chrome 注入模拟捕捉到未知权限加空枚举误报；修复后主代理用 Node 独立验证 7 个状态通过、无主动取用相机。此结果不是手机实测。
- 结项复跑（含最后的相机判断）：.NET 10.0.404 Release 0 警告、0 错误；408 项通过（Server 323、Client 85），0 失败、0 跳过。继续保留真实手机、读屏和 Nginx 未覆盖说明。

## Plan Change Log

- 2026-11-25 — 用户确认位置照片保留解码像素上限：PNG 3200 万；JPEG/WebP 6400 万。文件上限仍为 10 MiB；无法安全解码时提示换图，保留原图。物品照片规则不变。

## Review Triage Log

Quick review 1: 1 high, 11 medium, 3 low, 1 false, 1 maybe-false.

| Finding | Verdict / route | Evidence |
|---|---|---|
| Photo dialog focus | medium / patch | Both callers restore focus after `ShowAsync`, not after `Result`; neither supplies the UX fallback. |
| Camera activation | maybe-false / defer | The helper awaits enumeration and possibly a permission prompt before opening the input; dismissal varies by browser and needs a physical mobile check. The redundant permission probe is not required by the approved design. |
| Retry loses metadata draft | high / patch | `forceNoMetadataChange` bypasses edited inputs, then success resets them and reports all data saved. |
| Retry changes photo action | medium / patch | The retry label comes from the old notice but uses the current selection/remove flags. |
| Partially saved row remains stale | medium / patch | Edit exit refreshes only for newly created/unknown cases; `LoadAsync` retains old editable rows on failure. |
| Metadata rejection copy | low / patch | One message covers both new and existing locations, even when name/description were only being updated. |
| Management dialog busy dismissal | medium / patch | Its close handlers/options are not gated along with Save; a request can finish after dismissal and hide its outcome. |
| Image validity | medium / patch | Codec creation and dimension lookup don't read pixels. Existing truncation fixture does not reach pixel validation. |
| Stale successful photo DTO | low / patch | The coordinator returns the pre-write DTO and `ApplySaveResultAsync` reads `HasPhoto` from it. |
| Form path | low / patch | Parent labels introduce mixed separators; saved photo alt should use the committed name path rather than draft name. |
| Staging startup and orphan lifecycle | false / reject | The approved plan requires cleanup only for known-unreferenced files and a warning for committed deletion, not eager startup, a replacement warning, or a crash orphan sweep. The singleton's first-use staging cleanup is documented separately from initialization. |
| Delete description in API docs | medium / patch | The text promises a second check before commit; production uses one conditional SQLite delete. |
| Broken client service table | medium / patch | The write-result paragraph splits the table before `TagService`. |
| Retired rename DTO | medium / patch | No source consumer remains; the architecture and tree inventory still present the DTO as active. |
| SQLite race evidence | medium / patch | Two serialized orderings pass, but the move-in occurs before delete starts. Add deterministic ordering between snapshot and delete so removing the condition fails the test. |
| Screen-reader path | medium / patch | The visual arrow-delimited path drives both `aria-describedby` and alt; UX asks for spoken path without separator glyphs. |
| Delete response uncertainty | medium / patch | The warning parser can throw after a commit and the catch currently asserts deletion failed. |

## Design Notes

- Under `DataDirectory`, use a dedicated location root and server-generated relative names. Stage/validate on the same volume; publish file before pointer commit. Clean only files known unreferenced. Serialize photo mutations and delete on the current single-instance deployment.
- Native capture may not distinguish denial from cancel; only report observable errors and preserve device selection.

## Verification

**Commands:**
- `dotnet build BoxWise.slnx` — 0 warnings.
- `dotnet test BoxWise.slnx` — all pass.

**Manual checks (post-development, real browser/device):**
- Assert both SQLite and image roots are temporary before startup. Execute Story Verify 1–8 (automated cases above plus browser network, proxy limits, focus/zoom/44×44, keyboard/reader, and phone camera); report unavailable environments separately.
