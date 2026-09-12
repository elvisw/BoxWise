---
title: '修复 CI 中 SkiaSharp 包版本冲突'
type: 'bugfix'
created: '2026-09-12'
status: 'done'
route: 'oneshot'
review_loop_iteration: 0
context: []
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** CI run 34689815932 在 NuGet Restore 阶段请求不存在的稳定包 `SkiaSharp.NativeAssets.Linux.NoDependencies 4.152.1`，导致后续构建和测试全部跳过。

**Approach:** 修正 CPM 中冲突合并产生的错误版本，将 SkiaSharp 托管包与 Linux 原生包统一到已发布的稳定版 `4.152.0`。

</frozen-after-approval>

## Implementation Notes

- CI 与本地 `dotnet restore BoxWise.slnx` 均复现 `NU1103`，确认 `4.152.1` 无稳定包。
- 提交历史表明 PR #72 与 PR #75 原本分别升级两个包到 `4.152.0`，冲突合并 `c87d71c` 错误生成了不一致版本。
- 生产配置仅修改 `Directory.Packages.props`，将两个包统一为 `4.152.0`；项目引用和 CI 工作流无需变更。本文件是此次修复的实施记录。
- 修复后按 `.github/workflows/release.yml` 的 `build-and-test` 顺序验证：Restore 成功；Release Build 为 0 警告、0 错误；Test 共 293 项通过、0 失败、0 跳过。

## Review Triage Log

- `low / patch`：实施记录缺少修复后的验证证据；已补充 Restore、Build、Test 的实际结果。
- `low / defer`：多份既有文档仍标注 SkiaSharp 3.x/3.119.4；该偏差早于本次修复且不影响 CI，已记录到 `deferred-work.md`。
- `low / patch`：“仅修改 Directory.Packages.props”未区分生产配置与实施工件；已修正表述并明确本文件用途。
