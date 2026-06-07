---
title: '技术债务清偿（第二轮） — deferred-work.md 剩余条目'
type: 'chore'
created: '2026-06-07'
status: 'done'
baseline_commit: '5a23bbf3d25a024596c1ccceb370c229f9231287'
context: ['_bmad-output/implementation-artifacts/deferred-work.md']
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** deferred-work.md 中有 7 条未清偿的技术债务条目。其中 3 条为 admin-2fa 代码问题（DTO 重复/魔术字符串/WebAuthn 显示），3 条为 README 部署文档问题（tar 防护/验证命令），1 条 SW 离线缓存在子代理审查中确认为 Blazor WASM + MapStaticAssets 已知限制，改为文档说明。

**Approach:** 按文件分组修复：admin UI 代码 → README 文档 → SW 缓存文档说明。

## Boundaries & Constraints

**Always:**
- `dotnet build` 零错误零警告
- `dotnet test` 全部通过
- 不引入新依赖
- README 中 `tar -xzf`、`scp`/`rsync` 命令修复后保持可读性和正确性

**Ask First:**
- 任何修改影响生产部署流程的判断

**Never:**
- 不修改 Identity 脚手架认证逻辑
- 不引入新的 NuGet 包
- 不删除 README 中的核心部署步骤

## TwoFactorMethod 显示字符串映射表

实现 D3 时，switch 表达式应覆盖以下所有枚举值组合：

| ConfiguredMethods 值 | 枚举组合 | 显示字符串 |
|---------------------|---------|-----------|
| 0 | None | `null`（TwoFactorEnabled=false 时正常；true 时 fallback 为 "未知"） |
| 1 | TOTP | `"TOTP"` |
| 2 | Email | `"Email"` |
| 4 | WebAuthn | `"WebAuthn"` |
| 3 | TOTP \| Email | `"TOTP + Email"` |
| 5 | TOTP \| WebAuthn | `"TOTP + WebAuthn"` |
| 6 | Email \| WebAuthn | `"Email + WebAuthn"` |
| 7 | TOTP \| Email \| WebAuthn | `"TOTP + Email + WebAuthn"` |

</frozen-after-approval>

## Code Map

- `src/BoxWise.Shared/Dtos/UserListItemDto.cs` -- DTO，`TwoFactorMethod` 字段未被视图使用
- `docs/architecture-shared.md` -- 架构文档中记录了 DTO 签名，移除字段后需同步更新
- `src/BoxWise.Server/Pages/Admin/Index.cshtml.cs` -- Admin PageModel，构建 UserListItemDto，含 switch 表达式和 DTO 映射
- `src/BoxWise.Server/Pages/Admin/Index.cshtml` -- Admin 用户列表视图，2FA 状态显示 + 重置按钮条件
- `src/BoxWise.Client/wwwroot/service-worker.published.js` -- 生产 SW（仅用于理解离线缓存逻辑，不修改代码）
- `README.md:182,317` -- `tar -xzf` 命令处
- `README.md:327` -- `grep blazor.webassembly.js` 验证命令
- `README.md:344` -- `scp -r publish/*` 命令

## Tasks & Acceptance

**Execution:**

### 组 A：Admin 2FA 显示代码（3 条，按依赖顺序）

- [x] `src/BoxWise.Shared/Dtos/UserListItemDto.cs` + `src/BoxWise.Server/Pages/Admin/Index.cshtml.cs` + `docs/architecture-shared.md` -- 移除 DTO 的 `TwoFactorMethod` 字段的同时，更新 `Index.cshtml.cs` 中 `new UserListItemDto(...)` 构造函数调用从 6 个参数减为 5 个，并同步更新 `docs/architecture-shared.md` 中的 DTO 签名。**这些修改必须原子完成**，单独执行任一个都会导致编译失败或文档过时 -- D1
- [x] `src/BoxWise.Server/Pages/Admin/Index.cshtml.cs` -- 在类级添加 `private const string UnknownMethod = "未知";`，替换 `LoadUsersAsync` 中的硬编码字符串 -- D2
- [x] `src/BoxWise.Server/Pages/Admin/Index.cshtml.cs` -- 修复 switch 表达式，覆盖 TwoFactorMethod 映射表中全部 8 种组合。分支必须按最具体优先排序：值 7（三者全开）→ 值 5/6/3（双组合）→ 值 4/2/1（单方法）→ 值 0（None） -- D3

### 组 B：SW 离线缓存文档说明（1 条）

- [x] `src/BoxWise.Client/wwwroot/service-worker.published.js` -- 在文件头部现有模板注释之后、`self.importScripts` 之前，添加注释："Caveat: Blazor WASM MapStaticAssets() may append content-hash fingerprints to asset URLs. When a fingerprinted URL differs from the pre-cached URL in assetsManifest, the SW cache-miss falls through to the network. This is a known limitation in .NET 10 Blazor WASM + MapStaticAssets; offline access to blazor.webassembly.js may fail in this scenario. Online operations are unaffected." -- SW缓存键

### 组 C：README 文档修复（3 条）

- [x] `README.md` -- ~~两处 `tar -xzf` 命令添加 `--skip-old-files` 标志~~ [已撤回：Review BLOCKER — `--skip-old-files` 在更新场景下阻止 DLL 覆盖导致部署静默失败。README 已注明 `.env`/`data/` 不在 tar 包中，无需额外保护] -- README备份
- [x] `README.md` -- 将 `scp -r publish/*`（第 344 行附近）替换为 `rsync -avz publish/ elvisw@你的服务器:/opt/boxwise/`（注意 `publish/` 尾随斜杠确保复制目录内容而非目录本身）。附服务器端安装说明（`apt install rsync`）。不提供 scp 备选方案（`scp -r publish/` 会复制目录自身导致 `/opt/boxwise/publish/`，破坏 systemd 服务路径） -- scp边缘
- [x] `README.md` -- 将 `grep blazor.webassembly.js`（第 327 行）精确化为 `grep -o 'src="[^"]*blazor\.webassembly\.js[^"]*"'`（针对 Blazor WASM 生成的标准双引号属性 HTML） -- grep精确

**Acceptance Criteria:**
- Given 用户仅启用 WebAuthn（`ConfiguredMethods = 4`），when Admin 查看用户列表，then 2FA 状态列显示 "已启用 (WebAuthn)"
- Given 用户启用 WebAuthn + TOTP（`ConfiguredMethods = 5`），when Admin 查看用户列表，then 2FA 状态列显示 "已启用 (TOTP + WebAuthn)"
- Given 用户启用全部三种方法（`ConfiguredMethods = 7`），when Admin 查看用户列表，then 2FA 状态列显示 "已启用 (TOTP + Email + WebAuthn)"
- Given `TwoFactorEnabled=true` 且 `ConfiguredMethods = TwoFactorMethod.None`（值 0），when Admin 查看用户列表，then 显示 "已启用 (未知)" 且有"重置 2FA"按钮
- Given 生产部署，when 执行 README 中的 `tar -xzf` 更新命令，then 已有 `.env` 和 `data/` 不会被覆盖（tar 包中不含这些文件，无需 `--skip-old-files`）
- Given 生产部署，when 执行 README 中的 `grep -o` 验证命令，then grep 仅匹配 `<script src="...blazor.webassembly.js...">` 标签

## Verification

**Commands:**
- `dotnet build` -- expected: 零错误零警告
- `dotnet test BoxWise.slnx` -- expected: 全部测试通过（无回归）

**Manual checks:**
- `grep -c 'tar -xzf' README.md` -- expected: 2（两处 tar 命令均存在，不使用 --skip-old-files）
- `grep -c 'rsync\|scp -r publish/' README.md` -- expected: >= 1（scp 边缘情况已修复）
- `grep -c "grep -o 'src=" README.md` -- expected: 1（grep 验证命令已精确化）
- `grep -c 'MapStaticAssets\|fingerprint\|known limitation' src/BoxWise.Client/wwwroot/service-worker.published.js` -- expected: >= 1（SW 注释已添加）

## Spec Change Log

- **2026-06-07 — BLOCKER: `--skip-old-files` 在更新场景下阻止 DLL 覆盖。** C1 任务撤回。`--skip-old-files` 在系统已停止旧进程后解压新 tar 包时，会跳过所有已存在的 DLL/静态文件，导致新旧代码混合运行。README 已明确声明 `.env`/`data/` 不在 tar 包中故永不被覆盖，无需此标志。KEEP: 其余 6 项任务正确，SW 注释、rsync、grep、DTO 移除、switch 表达式均验证通过。

## Suggested Review Order

**入口点 — DTO 与 switch 核心逻辑**

- DTO 移除冗余 `TwoFactorMethod` 字段，switch 覆盖全部 8 种 TwoFactorMethod 组合
  [`UserListItemDto.cs:3`](../../src/BoxWise.Shared/Dtos/UserListItemDto.cs#L3)
- 类级常量 + 最具体优先 switch 分支 + 原子 DTO 构造函数更新
  [`Index.cshtml.cs:17`](../../src/BoxWise.Server/Pages/Admin/Index.cshtml.cs#L17)

**视图层 — 重置按钮兜底**

- 2FA 启用时始终显示重置按钮
  [`Index.cshtml:75`](../../src/BoxWise.Server/Pages/Admin/Index.cshtml#L75)

**SW 离线缓存 — 已知限制注释**

- MapStaticAssets 指纹 URL 与 SW 预缓存不匹配的文档说明
  [`service-worker.published.js:4`](../../src/BoxWise.Client/wwwroot/service-worker.published.js#L4)

**README 部署文档**

- scp glob 替换为 rsync（含安装说明）
  [`README.md:344`](../../README.md#L344)
- grep 验证命令精确化
  [`README.md:327`](../../README.md#L327)

**架构文档同步**

- DTO 签名更新
  [`architecture-shared.md:26`](../../docs/architecture-shared.md#L26)
