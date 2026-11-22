---
type: migration
title: "BoxWise：BMad v6 → 官方 v7 Preview Alpha"
status: approved
approved: 2026-11-22
approval: A
created: 2026-11-22
---

# BoxWise：BMad v6 → 官方 v7 Preview Alpha

**状态：approved（例外 A，2026-11-22）。** 本文件是计划与执行记录；项目内迁移已执行并本地提交。

**审批范围：** 在现有 BoxWise Git 仓库建立一个 v7 initiative，迁移本地 OpenCode 的 BMad 安装与项目工件。不改全局技能，不拆仓，不推送、不合并，不碰应用、业务数据库、认证或 SMTP，不执行历史工件中的命令，不删除来源不明或未追踪的原件。

**批准后授权：**

- 在 `main` 之外创建本地分支 `chore/bmad-v7-preview`，按官方步骤切分本地提交；只暂存本计划和 `_bmad-output/` 的迁移工件。
- 移动本计划逐文件映射中的 143 个源文件，创建 `initiative-boxwise/`、`backlog/`、`archive/`、票据树及必要 router。
- 将 58 个旧技能/命令路径移入独立本地备份区，安装项目内 `core-tools`、`method`，运行目标版本 setup，并写入本地 `active_initiative`。
- 改写 store 内活动文档的路径；留置文件的例外见“已知断链与留置链接”。

**本计划不授权：** 全局技能变更、`--clean` 删除、`AGENTS.md`/`docs/index.md` 的 BMad 入口同步、历史脚本执行、应用/数据库/认证/SMTP 操作、推送或合并、删除未知/未追踪文件、改写首份备份、覆盖用户当前未提交的 README/docs 改动。发现来源不明、目标冲突、CLI checkout 与固定 commit 不一致、备份校验失败时停止并报告。

## 版本与来源

- BoxWise 当前安装：`_bmad/_config/manifest.yaml:2,8,15` 为 `6.12.0`（`core`、`bmm`）。
- 目标：官方 [v7 Preview Alpha 源码](https://github.com/bmad-code-org/BMAD-METHOD/tree/e435a22574fe55722841086e5566420178b98df3)，模块版本仍显示 `6.13.0-next`。Method 迁移文件固定为 `skills/method/bmod-method/migration-1.toml`；只读核验其 Git blob 为 `9c742e759a4a4651d7076560fa30872574216417`，与规划所用模板一致。迁移入口是 `bmad` 的 migrate 流程：先用 `knowledge.py` 列出 migration，再按 `migration-1.toml` 执行；不把 Alpha 版本号伪装成正式 7.0。
- 只读核验：`migration-2.toml` 的 detect 针对“7 preview → 7”的旧扁平布局；`migration-1.toml` 的目标已是其目标形状。迁移完成后重新跑 detect；若出现信号，停止并报告，不自行执行 Migration 2。
- 原 Git 基线为 `f91819c1e9c1f4124144eed0b244da6fe402b393`。`_bmad/`、`.agents/`、`.opencode/` 均被 gitignore（已核验），Git 不能恢复；回退必须依赖备份。

## 备份与回退

- 首份备份 `.v6-v7-migration-backup-20261122-2133/` 已存在且不得再编辑：`_bmad/` 36 个文件、`_bmad-output/` 143 个文件，逐路径 SHA-256 与源一致（已只读复核）。备份内 `.gitignore` 为 `*`，不进 Git。
- 批准后、移动旧技能前，另建 `.v6-v7-local-backup-<YYYYMMDD-HHMM>/`（项目根、独立于首份备份，写 `.gitignore *`），保存 `.agents/skills/` 的 29 个 BMad 目录、`.opencode/commands/` 的 29 个 `bmad-*.md`、可能存在的项目技能锁/同名别名，保留相对路径；生成逐文件 SHA-256 清单并核对数量。未知、非 BMad 或来源不明的文件只复制报告，不移动、不删除。
- 回退步骤（仅在用户要求时执行）：停止使用本项目的 OpenCode 会话；对照迁移清单检查变更，发现任何后续或无法归属的修改先停。保留迁移分支和提交，切回原分支恢复已跟踪工件；迁移独有的未提交工件移入独立隔离区，不清扫 `_bmad-output/`。将迁移后的 `_bmad/` 整目录及本次新增的项目技能/锁移到隔离区，用首份备份恢复 `_bmad/`，按本地备份清单恢复被搬出的技能和命令；重启后验证 v6 `/bmad` 入口。不触碰原有 README/docs/AGENTS/CLAUDE 改动和 `.codegraph/`，不删除或重置分支。

## 范围确认与默认选择

| 问题 | 答案 / 待批准的默认 |
| --- | --- |
| 规划、实施来源还在别处吗？ | 同意以 `_bmad-output/planning-artifacts/`（16 个文件）和 `implementation-artifacts/`（103 个）为主来源；同时清点同一输出根的 `specs/`（22）、`brainstorming/`（1）和 `project-context.md`（1）。`docs/` 是知识引用而非票据存储。 |
| 单一仓库，还是独立票据仓库 / 多仓 workspace？ | 用户已接受原仓库；不另建仓库、worktree workspace 或外部 tracker。 |
| Initiative 如何划分？ | 一个 `initiative-boxwise`。只有一份主 PRD；Identity、密码恢复等是该产品的变更，不能拆出另一套主编号。 |
| 同名、按日期或序号版本如何处理？ | 全部保留。Epic 9 两份回顾合并为一份 v7 回顾，较早原件同时入 `archive-v6/`；文件台账标注去向。 |
| 在 v6 先完成进行中工作吗？折叠未启动 Story？ | 不适用：最新 `sprint-status.yaml` 的 60 条 Story 均为 `done`；没有未启动或待审查条目。15 份早期 build record 仍写 `Status: review`；按“状态例外（待批准）”处理，不把它们说成逐条验收。 |
| 独立实施记录如何处理？ | 18 份 `spec-*.md` 均明确 `done`，拟作为历史 standalone backlog 条目，各配同 stem plan；不计入 sprint 的 60 条，不建新 Epic。保留旧验收、类型及可信基线；`enhancement` 映射到 `feature` 并在 plan/ticket 记 `v6_type: enhancement` 保留出处。 |
| 如何执行外部路径更新？ | 官方 Method 迁移只报告 store 之外的旧路径。`CLAUDE.md → AGENTS.md` 与委派规则由用户独立完成；`AGENTS.md`、`docs/index.md` 的 BMad 入口同步不在本计划内，需另外授权、另外提交。外部路径不锁行号，执行前重扫；历史计划与设计快照不改。 |

## 状态例外（待批准）

最新 `sprint-status.yaml` 将这 15 条标为 `done`，但对应 build record 仍为 `Status: review`；`78b2fc0` 的提交说明明确记录“sprint-status.yaml: 15 条 story review→done”。这不是 15 条 Story 的逐条评审结论，也不重新验收历史业务。

- **例外 A（已批准）**：v7 plan 的状态采用较新的 tracking `done`；原 build record 原样保留 `Status: review`，plan 中注明 `status_source: sprint-status.yaml + 78b2fc0`、`legacy_status: review`，并声明这是对账例外，不是逐条验收。
- **例外 B**：若用户不接受 A，这 15 条 plan 用 `in-review`，其余 45 条仍按 tracking。
- 受影响 ID：`1-1-project-scaffolding`、`1-2-identity-auth`、`1-3-admin-account-management`、`2-1-location-entity-crud`、`2-2-location-tree-api`、`2-3-tag-system`、`2-4-location-tag-selector`、`3-1-item-image-upload`、`3-2-item-entry-api`、`3-4-item-entry-ui`、`4-1-search-functionality`、`4-3-location-tag-filter`、`4-4-item-detail-delete`、`4-5-pwa-offline`、`4-6-docker-deployment`。
- 官方 checklist ⑥ 的“review stays in-review”原文保留；例外 A 是对该条的显式、已批准的偏离，不在验收记录里静默删除。

## 需求编号与来源

- Initiative `Requirements`：`R1`–`R20` 逐条来自主 PRD `prd-BoxWise-2026-05-21.md` 的 FR-1–FR-20 原文，按 `epics.md` 的 FR Coverage Map 分配；不把后续提案混进原 PRD。
- 在 initiative `Requirements` 追加后续新增：`R21` 来自 `sprint-change-proposal-2026-05-28.md` 的 FR-21（物品编辑）；`R22a`–`R22g` 来自 `sprint-change-proposal-2026-05-28-security.md` 正文实际定义的 FR-22a–FR-22g（密码规则、2FA、WebAuthn、恢复码、速率限制、管理员 2FA、CLI）。提案概述虽写“a-h”，正文没有 FR-22h，不补造；`8-5b` 部署配置按 epic-local 来源处理。每行在 `source:`/Notes 标原文路径。
- Epic 10/11 的局部 FR-1–FR-8 是 Identity 补充文档的局部编号，不是主 PRD FR；只在 epic-local Requirements 使用，不生成 initiative R，也不冒充主 PRD。
- Epic 9（2FA 设置）优先挂 `R22b`；若原文没有逐条依据则用 epic-local，不硬造 `covers`。
- Epic 12/13/14/15 没有主 PRD FR：用 epic-local Requirements（来自各自提案/设计/回顾），`source:` 指向原文；没有可追溯验收依据的 entry 不写 `covers`，不按排列顺序造依赖。
- `epics.md` 的 FR-1–FR-20 coverage map 仍逐条至少一个 entry；新增 R21/R22 只补有原文依据的 covers，不伪造。Epic 8 的 9 条按安全提案第 114–144 行建立依赖：`2a2` 需 `2a1`；`2b` 需 `2a2` 和 `5b`；`3`、`4` 需 `2a2`；`5a` 需 `2b` 和 `3`；`5c` 需 `2a2`、`2b`、`3`、`5b`。12/13 的明确“实施顺序”写 `after`（12.2 需 12.1、12.3 需 12.2、13.2 需 13.1、13.3 需 13.2），不把普通排列当依赖。

## 工件和状态转换

目标入口：`_bmad-output/initiative-boxwise/`（文档 + `tickets/` + `archive-v6/`）、`_bmad-output/backlog/`、`_bmad-output/archive/`、`_bmad-output/migration-v6-v7/`；`project-context.md` 与两个未归属经典 SPEC 留在原处。

1. 将 `planning-artifacts/` 迁至 `_bmad-output/initiative-boxwise/`。PRD 及决策日志、Architecture、UX 主文档及 HTML 伴随件、研究、变更提案按官方布局成套迁移。已与 initiative 建立关联的经典 SPEC 和安全脑暴一起迁移；没有直接归属依据的独立经典 SPEC 留置。新建 `backlog/`、`initiative-boxwise/tickets/` 与 `archive/`（`archive/` 空时 `.gitkeep`），原始 `epics.md` 和 `sprint-status.yaml` 原字节归档至 initiative 的 `archive-v6/`；不重写归档。两个原扁平文件改为同名目录：`2026-07-02-forgot-password-design/2026-07-02-forgot-password-design.md`、`epics-identity-scaffold-migration/epics-identity-scaffold-migration.md`；两个脑暴按 session 各建同名目录；`investigations/` 增加 `investigations.md` router 列出 18 个文件。迁移后移除已空的 `planning-artifacts/`、`implementation-artifacts/` 及其空分组目录（`prds/`、`research/`、`brainstorming/` 等）；只给本次新建且为空的目录写 `.gitkeep`。
2. 以 `epics.md` 主编号、补充规划及 `sprint-status.yaml` 对账，建立 15 个 Epic、60 个 story entries/plan。`epics.md` 直接覆盖 Epic 1–6、9 的 27 条；其余 33 条由实际来源补齐。Epic 8 有 6 个含字母的 ID：`2a1`、`2a2`、`2b`、`5a`、`5b`、`5c`，TOML 中一律加引号并保留完整 `v6_key`；其中 `2a1`/`2a2` 是原 2a 的拆分。Epic 8 依赖按安全提案第 114–144 行；12/13 的明确实施顺序按上一节写 `after`。Identity 局部 1.x/2.x 映射主编号 10.x/11.x，局部 FR 不冒充主 PRD FR。
3. `Requirements` 与 `covers` 按“需求编号与来源”执行；Epic 的 Done when 只用规划目标与验收源；批准本计划即批准该取值规则，不表示重新验收历史业务。
4. 40 条有同名编号的 build record，加 Epic 15.1 的 1 份任务式计划，共 41 条有记录；其余 19 条按已追踪 `done` 写最小 plan，明示缺独立原记录、保留可证验收条件。Epic 15.1 的 8 个任务保留在单个 plan 内（不新做 8 条 Story），补 `ticket: "1"`、`status: done`、`type: feature`（派生）、无基线。Epic 14.3/14.6 明确只关闭 Issue。构建类型与基线见下一节；无基线不填当前 HEAD。
5. 实地回顾优先于对旧状态的转述：Epic 15 回顾的 `accepted` 只作为既有结论，保留两条 open action items（owner `Elvis`、epic 15）到 initiative Notes，保留手测指南但不视为已执行；各 Epic 的回顾在 Notes 写 `Retrospective:` 指向合并文件，保留原日期，不编写新 verdict。不虚构 Epic 14 回顾。历史诊断、补丁或数据库迁移示例只归档，不执行。
6. 按逐文件映射改写 store 中活动文档的链接、锚点和 `inputDocuments`/`sources`/`companions`/References；一切活动文件中的相对链接从新位置重算。留置/归档原件原则上不改，但留置文件若引用被移动文件，允许做一行路径例外并记录。

## 构建类型与基线

- 现有 41 份 sprint build record（40 条编号 + Epic 15 计划）未发现 `type:` 字段（已扫描）；其余 19 条无记录。不能声称上游类型。按下述派生规则写入每个 plan 的 frontmatter，并标 `type_source: derived`：
  - `feature`：Epic 2–5、7–10、13、15 的功能 Story，`1-2`/`1-3`、`11-1`/`11-2`、`12-1`，以及 `8-5c`；`8-5a` 等安全功能同。
  - `refactor`：`11-3-decommission-legacy-code`、`12-2-decommission-server-ai`、`13-2-ai-service-refactor`。
  - `chore`：`1-1-project-scaffolding`、`4-6-docker-deployment`、`6-1`–`6-5`、`8-5b`、`11-4`、`12-3`、`13-3`、`14-3`–`14-6`。
  - `bugfix`：`14-1`、`14-2`。
  - 其余按 Story 文本判定；没有依据时用 `feature` 并在 plan 标注。
- 18 条 backlog 的 `type` 来自源 frontmatter；`enhancement` 映射为 `feature`，在 plan/ticket 写 `v6_type: enhancement` 保留出处。
- 19 条无独立记录的 minimal plan 同样按上述规则取 type；不得因缺记录而省略 type 或伪造上游字段。
- 基线：21 条 sprint + 10 条 backlog 有确证基线（已逐行核验），写入 plan 的 `baseline_revision`；其余 39 条 sprint 与 8 条 backlog 省略该字段，在 plan 与报告列明缺口，不用当前 HEAD 替代。
- Story entry 默认 `hitl = false`、`risk = "low"`，除非原文另有明确要求；不把历史 Story 重新标成已构建或已验收。

## 本地 OpenCode 安装与清理

1. 对项目 `.agents/skills/` 的 29 个旧 BMad 目录、`.opencode/commands/` 的 29 个 `bmad-*.md` 包装入口以及若存在的技能锁/同名安装别名，复制到“备份与回退”的 `.v6-v7-local-backup-<datetime>/`（保留相对路径，逐文件 SHA-256 清单），再把这些旧 BMad 路径**移出扫描范围而非删除**。初查以上 58 个路径没有符号链接或 junction，未发现项目技能锁；如发现自定义文件、符号链接指向全局、目标冲突或未知来源，停止该路径操作并报告。不能使用可能遍历全局根的 `--remove-retired`，不能跑全局 `npx skills update -g`；其他任何技能不动。
2. 用官方 Skills CLI 为 OpenCode 在本项目安装 `core-tools`、`method` 两个模块，来源固定为 commit `e435a22574fe55722841086e5566420178b98df3` 的 `skills/core-tools` 与 `skills/method`。执行前在隔离目录核对：记录 `npx skills --version`（npm registry 只读元数据当前 `skills@1.9.0`，不代表实际执行版本）、实际 checkout SHA 必须等于上述 commit、各模块 `bmod.toml`、迁移规则与脚本的来源和 blob：`skills/method/bmod-method/migration-1.toml`、`skills/method/bmod-method/migration-2.toml`、`skills/core-tools/bmad/scripts/{setup.py,knowledge.py,migrate_v6.py}`、`skills/method/bmad-agent-po/scripts/tickets.py`；setup 后位于 `_bmad/method/scripts/tickets.py`。任一不一致即停止。可选全局 `founder`、`toolsmith` 不纳入 BoxWise。
3. 先检查 `uv` 可用（当前只读核验 `uv 0.13.8`），再运行目标版本 `bmad setup` 的只读状态、配置问题及项目初始化；项目名和输出根保持不变；`_bmad/custom/config.user.toml` 的 `[core]` 设置 `active_initiative = "initiative-boxwise"`。原仓库票据库使用默认配置，不创建外部 tracker 文件。`_bmad/custom/config.toml` 当前没有路径改写项（只读核验）；执行前再判定。
4. 单独列明可选 v6 清理项，不因批准工件迁移而自动删除：旧 `_config/{manifest.yaml,files-manifest.csv,skill-manifest.csv,bmad-help.csv}`、`config.user.toml`、`{core,bmm}/{config.yaml,module-help.csv,v6-shims}`；过时键为 `core.document_output_language` 和 `modules.bmm.{planning_artifacts,implementation_artifacts,project_knowledge}`。`--clean` 自有备份并会删除这些项目内遗留；未经明确同意，予以保留，只要它们不影响 v7 的发现与运行。`--clean` 不在本计划批准范围内。

## Store 外旧路径（Method 只报告）

- `AGENTS.md` 的 BMad 路径/入口（不锁行号；执行前重扫）。
- `docs/index.md` 的规划 / 实施入口（不锁行号；执行前重扫；未经额外授权不在 Method 内改）。
- `docs/superpowers/plans/2026-05-26-camera-capture.md` 和 `docs/superpowers/specs/2026-05-26-camera-capture-design.md` 中的历史 PRD 路径（执行前重扫，不锁行号）。

## 已知断链与留置链接

迁移前对备份内 Markdown 扫描显式 `_bmad-output/` 文件路径，确认两处已有坏目标：`implementation-artifacts/epic-9-retro-2026-05-31.md:93–94` 把 `specs/spec-2fa-modify-settings/{SPEC.md,modify-flow.md}` 误放在 `planning-artifacts/` 下。合并回顾时把两处改到 `initiative-boxwise/spec-2fa-modify-settings/spec-2fa-modify-settings.md` 与 `.../modify-flow.md`，归档的 05-30 原件保持原样。

`specs/spec-passkey-stale-credential-ux/SPEC.md` 原地留置，批准后只改其中 `context:` 一行，使它仍指向迁到 `initiative-boxwise/investigations/` 的 `passkey-stale-credentials-investigation.md`。这是一项明确的留置文件例外；其他留置项执行前重扫，若发现未列出的迁移所致断链，先停并报告，不擅自扩大修改范围。

## 执行前只读预检（批准后、写入前）

- 已只读核验：首份备份 36 + 143 个文件逐路径 SHA-256 与源一致；`.gitignore *` 且无提交；143/143 映射无重复目标；`migration-1.toml` blob `9c742e7…` 与固定 commit 一致；`uv 0.13.8`；本地 29 个技能目录与 29 个命令、无符号链接/项目技能锁。
- 执行前复核：HEAD 仍为 `f91819c…`（或记录实际 HEAD 与差异）；`_bmad/_config/manifest.yaml` 仍为 6.12.0；源文件仍为 143；无 `active_initiative`、无 `initiative-*/`、无 pre-release `.memlog`；Migration 2 detect 为空。
- 执行前记录：`npx skills --version`、实际 checkout SHA（必须 `e435a…`）、`git status`；确认用户未提交的 README/docs/AGENTS/CLAUDE 改动不被暂存；确认 `_bmad/method/scripts/tickets.py` 在 setup 后可解析。以上未跑项不填结果；任一不符即停。

## 官方验收记录（获批后执行）

- [ ] ① 每个源文件均有目标或说明；② 每个成套文档仍完整，主文件或路由可读；③ 版本全部存留；本次新建的空目录有 `.gitkeep`，已空的 v6 目录移除。
- [ ] ④ `uv run <project-root>/_bmad/method/scripts/tickets.py --project-root <project-root> status <initiative folder>` 退出 0，且 15 Epic / 60 Story 对账，backlog 分计；若 setup 后解析器路径不同，记录实际路径并以模块解析为准。⑤ `find` 逐项验证联接和唯一性，无 Epic Story 叶子文件。
- [ ] ⑥ 状态、构建类型、基线、缺失记录、回顾位置与结论正确；保留上游“done stays done, review stays in-review”语义；15 条 `review` 的 A/B 例外须在批准时由用户明确决定并逐条记录，不冒充逐条验收。⑦ 从主 PRD 的 FR 至 Epic/Story 的 `covers` 和依赖逐条验证；R21/R22 与 epic-local 分开核对。
- [ ] ⑧ 无遗留预览 spec 形状；经典 SPEC 保留历史引用，不冒称预览 spec 折叠。在移动文件的 live requirement/引用中核对 CAP 映射，若上游规则与完整保留原档冲突，在不修改归档原文的前提下列明并报告。
- [ ] ⑨ 所有活动路径都能解析；分栏列出现存断链与新增断链，后者必须修复。留置文件（含 `spec-passkey-stale-credential-ux/SPEC.md`）的引用重扫；Epic 9 live 回顾两处按上述规则处理。⑩ 本地 active initiative、输出根正确；⑪ 移动过的模块配置路径正确（无则注明）。
- [ ] ⑫ Git 保持原仓库、目标 Alpha 项目技能优先、备份不入库、全局未动；⑬ 备份完整性复核；⑭ 所有决定与逐项验证有记录。重启 OpenCode 后实测 `/bmad` 或技能入口和 Sofia 解析；如环境不能重启此会话做验证，另用新会话核对或明确未覆盖。
- [ ] ⑮ 本计划在获批前保持 `status: draft`；未执行的步骤不填“通过”，未核验的 CLI 版本不写具体值。

<!-- generated-inventory -->

## 票据迁移台账

路径相对 `_bmad-output/`；状态栏 `@` 后是旧 `sprint-status.yaml` 行号。表中每条历史 Story 拟建一个 `[[entry]]` 并连接一个 plan；除“状态例外（待批准）”列出的 15 条外，其余按 tracking `done`。例外 A（建议）待用户批准后写 `done`；例外 B 则写 `in-review`。本表“文件状态”保留 build record 的原 `review`，不代表 v7 plan 已逐条验收。

| Epic ID | 票据容器 | Story 数 | 主要来源 |
| --- | --- | ---: | --- |
| 1 | 项目搭建与账户认证 · `initiative-boxwise/tickets/epic-project-setup-and-authentication/` | 3 | `epics.md` |
| 2 | 位置体系与标签管理 · `initiative-boxwise/tickets/epic-locations-and-tags/` | 4 | `epics.md` |
| 3 | 物品录入与智能识别 · `initiative-boxwise/tickets/epic-item-entry-and-recognition/` | 5 | `epics.md` |
| 4 | 查找浏览与生产部署 · `initiative-boxwise/tickets/epic-browse-and-deployment/` | 6 | `epics.md` |
| 5 | 用户管理增强与品牌收尾 · `initiative-boxwise/tickets/epic-user-management-and-branding/` | 3 | `epics.md` |
| 6 | 单元测试补完 · `initiative-boxwise/tickets/epic-unit-test-coverage/` | 5 | `epics.md` |
| 7 | 物品编辑 · `initiative-boxwise/tickets/epic-item-editing/` | 3 | `sprint-change-proposal-2026-05-28.md` |
| 8 | 账户安全加固 · `initiative-boxwise/tickets/epic-account-security/` | 9 | `sprint-change-proposal-2026-05-28-security.md` |
| 9 | 2FA 设置管理 · `initiative-boxwise/tickets/epic-two-factor-settings/` | 1 | `epics.md` |
| 10 | Server 端 Identity 脚手架 + 认证流程 · `initiative-boxwise/tickets/epic-identity-scaffold-and-authentication/` | 4 | `epics-identity-scaffold-migration.md` |
| 11 | 前端适配 + 退役 + 文档更新 · `initiative-boxwise/tickets/epic-identity-adaptation-and-retirement/` | 4 | `epics-identity-scaffold-migration.md` |
| 12 | AI 识别架构修复 · `initiative-boxwise/tickets/epic-ai-recognition-architecture/` | 3 | `sprint-change-proposal-2026-06-05.md` |
| 13 | LLM 配置安全迁移 · `initiative-boxwise/tickets/epic-llm-configuration-security/` | 3 | `sprint-change-proposal-2026-06-06.md` |
| 14 | 技术债务清偿（第三轮） · `initiative-boxwise/tickets/epic-technical-debt-round-three/` | 6 | `sprint-change-proposal-2026-06-07-tech-debt-3.md` |
| 15 | 忘记密码功能 · `initiative-boxwise/tickets/epic-forgot-password/` | 1 | `epic-15-forgot-password-plan.md` |

| v6 key | v7 入口 ID | 标题 | 需求/验收源 | 实施记录 | v6 sprint | 文件状态 | v7 plan | 基线 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| `1-1-project-scaffolding` | `1` | 项目脚手架搭建 | `planning-artifacts/epics.md:159` | `implementation-artifacts/1-1-project-scaffolding.md` | done @ 46 | review | `initiative-boxwise/tickets/epic-project-setup-and-authentication/tickets/story-project-scaffolding-plan.md` | `无` |
| `1-2-identity-auth` | `2` | Identity 集成与登录认证 | `planning-artifacts/epics.md:180` | `implementation-artifacts/1-2-identity-auth.md` | done @ 47 | review | `initiative-boxwise/tickets/epic-project-setup-and-authentication/tickets/story-identity-auth-plan.md` | `无` |
| `1-3-admin-account-management` | `3` | 后台管理界面 — 账户管理 | `planning-artifacts/epics.md:212` | `implementation-artifacts/1-3-admin-account-management.md` | done @ 48 | review | `initiative-boxwise/tickets/epic-project-setup-and-authentication/tickets/story-admin-account-management-plan.md` | `无` |
| `2-1-location-entity-crud` | `1` | Location 实体 + 物化路径 CRUD | `planning-artifacts/epics.md:241` | `implementation-artifacts/2-1-location-entity-crud.md` | done @ 51 | review | `initiative-boxwise/tickets/epic-locations-and-tags/tickets/story-location-entity-crud-plan.md` | `无` |
| `2-2-location-tree-api` | `2` | 位置树浏览 API | `planning-artifacts/epics.md:268` | `implementation-artifacts/2-2-location-tree-api.md` | done @ 52 | review | `initiative-boxwise/tickets/epic-locations-and-tags/tickets/story-location-tree-api-plan.md` | `无` |
| `2-3-tag-system` | `3` | 标签系统 | `planning-artifacts/epics.md:284` | `implementation-artifacts/2-3-tag-system.md` | done @ 53 | review | `initiative-boxwise/tickets/epic-locations-and-tags/tickets/story-tag-system-plan.md` | `无` |
| `2-4-location-tag-selector` | `4` | 前端 — 位置树选择器 + 标签选择器 | `planning-artifacts/epics.md:302` | `implementation-artifacts/2-4-location-tag-selector.md` | done @ 54 | review | `initiative-boxwise/tickets/epic-locations-and-tags/tickets/story-location-tag-selector-plan.md` | `无` |
| `3-1-item-image-upload` | `1` | Item 实体 + 图片上传管线 | `planning-artifacts/epics.md:329` | `implementation-artifacts/3-1-item-image-upload.md` | done @ 57 | review | `initiative-boxwise/tickets/epic-item-entry-and-recognition/tickets/story-item-image-upload-plan.md` | `无` |
| `3-2-item-entry-api` | `2` | 物品录入 API + 位置分配 + 入库保存 | `planning-artifacts/epics.md:353` | `implementation-artifacts/3-2-item-entry-api.md` | done @ 58 | review | `initiative-boxwise/tickets/epic-item-entry-and-recognition/tickets/story-item-entry-api-plan.md` | `无` |
| `3-3-ai-recognition` | `3` | AI 识别集成 + 降级策略 | `planning-artifacts/epics.md:380` | `implementation-artifacts/3-3-ai-recognition.md` | done @ 59 | done | `initiative-boxwise/tickets/epic-item-entry-and-recognition/tickets/story-ai-recognition-plan.md` | `无` |
| `3-4-item-entry-ui` | `4` | 前端 — 录入页面 | `planning-artifacts/epics.md:404` | `implementation-artifacts/3-4-item-entry-ui.md` | done @ 60 | review | `initiative-boxwise/tickets/epic-item-entry-and-recognition/tickets/story-item-entry-ui-plan.md` | `无` |
| `3-5-item-detail` | `5` | 物品详情展示 + 录入者标识 | `planning-artifacts/epics.md:436` | `implementation-artifacts/3-5-item-detail.md` | done @ 61 | done | `initiative-boxwise/tickets/epic-item-entry-and-recognition/tickets/story-item-detail-plan.md` | `无` |
| `4-1-search-functionality` | `1` | 搜索功能 | `planning-artifacts/epics.md:465` | `implementation-artifacts/4-1-search-functionality.md` | done @ 64 | review | `initiative-boxwise/tickets/epic-browse-and-deployment/tickets/story-search-functionality-plan.md` | `无` |
| `4-2-thumbnail-grid-browse` | `2` | 缩略图网格浏览 | `planning-artifacts/epics.md:494` | `implementation-artifacts/4-2-thumbnail-grid-browse.md` | done @ 65 | done | `initiative-boxwise/tickets/epic-browse-and-deployment/tickets/story-thumbnail-grid-browse-plan.md` | `无` |
| `4-3-location-tag-filter` | `3` | 位置与标签筛选 | `planning-artifacts/epics.md:524` | `implementation-artifacts/4-3-location-tag-filter.md` | done @ 66 | review | `initiative-boxwise/tickets/epic-browse-and-deployment/tickets/story-location-tag-filter-plan.md` | `无` |
| `4-4-item-detail-delete` | `4` | 物品详情与删除 | `planning-artifacts/epics.md:553` | `implementation-artifacts/4-4-item-detail-delete.md` | done @ 67 | review | `initiative-boxwise/tickets/epic-browse-and-deployment/tickets/story-item-detail-delete-plan.md` | `无` |
| `4-5-pwa-offline` | `5` | PWA 离线支持 | `planning-artifacts/epics.md:585` | `implementation-artifacts/4-5-pwa-offline.md` | done @ 68 | review | `initiative-boxwise/tickets/epic-browse-and-deployment/tickets/story-pwa-offline-plan.md` | `无` |
| `4-6-docker-deployment` | `6` | Docker 容器化部署 | `planning-artifacts/epics.md:620` | `implementation-artifacts/4-6-docker-deployment.md` | done @ 69 | review | `initiative-boxwise/tickets/epic-browse-and-deployment/tickets/story-docker-deployment-plan.md` | `无` |
| `5-1-admin-user-management` | `1` | Admin 用户管理增强 | `planning-artifacts/epics.md:655` | 无编号实施记录 | done @ 72 | 无独立记录 | `initiative-boxwise/tickets/epic-user-management-and-branding/tickets/story-admin-user-management-plan.md` | `无` |
| `5-2-user-self-service` | `2` | 用户自助服务 | `planning-artifacts/epics.md:678` | 无编号实施记录 | done @ 73 | 无独立记录 | `initiative-boxwise/tickets/epic-user-management-and-branding/tickets/story-user-self-service-plan.md` | `无` |
| `5-3-branding-copyright` | `3` | 品牌与版权完善 | `planning-artifacts/epics.md:701` | 无编号实施记录 | done @ 74 | 无独立记录 | `initiative-boxwise/tickets/epic-user-management-and-branding/tickets/story-branding-copyright-plan.md` | `无` |
| `6-1-test-cleanup-quality` | `1` | 测试清理与质量改进 | `planning-artifacts/epics.md:728` | `implementation-artifacts/6-1-test-cleanup-quality.md` | done @ 77 | done | `initiative-boxwise/tickets/epic-unit-test-coverage/tickets/story-test-cleanup-quality-plan.md` | `f3936686a896ef5a356fcc502df14cf78dc2f11f` |
| `6-2-repo-coverage-gap-fill` | `2` | Repository 层覆盖补完 | `planning-artifacts/epics.md:752` | `implementation-artifacts/6-2-repo-coverage-gap-fill.md` | done @ 78 | done | `initiative-boxwise/tickets/epic-unit-test-coverage/tickets/story-repo-coverage-gap-fill-plan.md` | `0449f0e5b091cabf146cf2eb7e3413929306c3f8` |
| `6-3-service-layer-tests` | `3` | Service 层测试建立 | `planning-artifacts/epics.md:783` | `implementation-artifacts/6-3-service-layer-tests.md` | done @ 79 | done | `initiative-boxwise/tickets/epic-unit-test-coverage/tickets/story-service-layer-tests-plan.md` | `977f549f39684cda4b60a6272856d1e06dc2176f` |
| `6-4-endpoint-layer-tests` | `4` | Endpoint 层测试建立 | `planning-artifacts/epics.md:804` | `implementation-artifacts/6-4-endpoint-layer-tests.md` | done @ 80 | done | `initiative-boxwise/tickets/epic-unit-test-coverage/tickets/story-endpoint-layer-tests-plan.md` | `1517d8ebd73bc33b7eb90764aecf5e8572901fad` |
| `6-5-admin-pagemodel-tests` | `5` | Admin PageModel 测试补完 | `planning-artifacts/epics.md:832` | `implementation-artifacts/6-5-admin-pagemodel-tests.md` | done @ 81 | done | `initiative-boxwise/tickets/epic-unit-test-coverage/tickets/story-admin-pagemodel-tests-plan.md` | `97c86ba46175067072a3430b6a51546c047065db` |
| `7-1-item-edit-backend` | `1` | Repository UpdateAsync + PUT 端点 + UpdateItemRequest DTO | `planning-artifacts/sprint-change-proposal-2026-05-28.md:81` | `implementation-artifacts/7-1-item-edit-backend.md` | done @ 84 | done | `initiative-boxwise/tickets/epic-item-editing/tickets/story-item-edit-backend-plan.md` | `2b011bdb1ab8701a91bc7f88f8e9e2e6f0fef5b8` |
| `7-2-item-edit-ui` | `2` | ItemDetail.razor 内联编辑模式 | `planning-artifacts/sprint-change-proposal-2026-05-28.md:82` | `implementation-artifacts/7-2-item-edit-ui.md` | done @ 85 | done | `initiative-boxwise/tickets/epic-item-editing/tickets/story-item-edit-ui-plan.md` | `无` |
| `7-3-item-edit-tests` | `3` | Repository + Endpoint 测试 | `planning-artifacts/sprint-change-proposal-2026-05-28.md:83` | 无编号实施记录 | done @ 86 | 无独立记录 | `initiative-boxwise/tickets/epic-item-editing/tickets/story-item-edit-tests-plan.md` | `无` |
| `8-1-password-rules` | `1` | 密码规则升级 + 旧密码强制更新 | `planning-artifacts/sprint-change-proposal-2026-05-28-security.md:148` | 无编号实施记录 | done @ 89 | 无独立记录 | `initiative-boxwise/tickets/epic-account-security/tickets/story-password-rules-plan.md` | `无` |
| `8-2a-1-two-factor-model-service` | `2a1` | 2FA 数据模型 + Service 层 + NuGet 包 | `planning-artifacts/sprint-change-proposal-2026-05-28-security.md:178` | 无编号实施记录 | done @ 90 | 无独立记录 | `initiative-boxwise/tickets/epic-account-security/tickets/story-two-factor-model-service-plan.md` | `无` |
| `8-2a-2-two-factor-api-ui-login` | `2a2` | 2FA API 路由 + 两阶段登录 + TOTP UI | `planning-artifacts/sprint-change-proposal-2026-05-28-security.md:221` | 无编号实施记录 | done @ 91 | 无独立记录 | `initiative-boxwise/tickets/epic-account-security/tickets/story-two-factor-api-ui-login-plan.md` | `无` |
| `8-2b-email-verification-recovery-codes` | `2b` | 邮箱验证码 + 恢复码 | `planning-artifacts/sprint-change-proposal-2026-05-28-security.md:291` | 无编号实施记录 | done @ 92 | 无独立记录 | `initiative-boxwise/tickets/epic-account-security/tickets/story-email-verification-recovery-codes-plan.md` | `无` |
| `8-3-webauthn-integration` | `3` | WebAuthn 集成 | `planning-artifacts/sprint-change-proposal-2026-05-28-security.md:343` | 无编号实施记录 | done @ 93 | 无独立记录 | `initiative-boxwise/tickets/epic-account-security/tickets/story-webauthn-integration-plan.md` | `无` |
| `8-4-rate-limiting-csrf-session` | `4` | 速率限制 + CSRF + 会话安全 | `planning-artifacts/sprint-change-proposal-2026-05-28-security.md:412` | 无编号实施记录 | done @ 94 | 无独立记录 | `initiative-boxwise/tickets/epic-account-security/tickets/story-rate-limiting-csrf-session-plan.md` | `无` |
| `8-5a-admin-two-factor-panel` | `5a` | 管理员 2FA 面板 + 审计日志 | `planning-artifacts/sprint-change-proposal-2026-05-28-security.md:460` | 无编号实施记录 | done @ 95 | 无独立记录 | `initiative-boxwise/tickets/epic-account-security/tickets/story-admin-two-factor-panel-plan.md` | `无` |
| `8-5b-deployment-config` | `5b` | 部署配置 | `planning-artifacts/sprint-change-proposal-2026-05-28-security.md:488` | 无编号实施记录 | done @ 96 | 无独立记录 | `initiative-boxwise/tickets/epic-account-security/tickets/story-deployment-config-plan.md` | `无` |
| `8-5c-cli-two-factor-reset` | `5c` | CLI 2FA 重置工具 | `planning-artifacts/sprint-change-proposal-2026-05-28-security.md:542` | 无编号实施记录 | done @ 97 | 无独立记录 | `initiative-boxwise/tickets/epic-account-security/tickets/story-cli-two-factor-reset-plan.md` | `无` |
| `9-1-2fa-modify-settings` | `1` | 2FA 设置管理 —— 服务端与客户端完整实现 | `planning-artifacts/epics.md:858` | `implementation-artifacts/9-1-2fa-modify-settings.md` | done @ 100 | done | `initiative-boxwise/tickets/epic-two-factor-settings/tickets/story-2fa-modify-settings-plan.md` | `ce4b6b5954f4cfa2f6a558aa440ac82881627270` |
| `10-1-scaffold-identity-pages` | `1` | 脚手架 Identity 页面 + 构建验证 | `planning-artifacts/epics-identity-scaffold-migration.md:89` | `implementation-artifacts/10-1-scaffold-identity-pages.md` | done @ 103 | done | `initiative-boxwise/tickets/epic-identity-scaffold-and-authentication/tickets/story-scaffold-identity-pages-plan.md` | `6693f35f784e28fe20c746d1ed97f911c5e8f3e2` |
| `10-2-iemailsender-adapter` | `2` | IEmailSender 适配器注册 | `planning-artifacts/epics-identity-scaffold-migration.md:131` | `implementation-artifacts/10-2-iemailsender-adapter.md` | done @ 104 | done | `initiative-boxwise/tickets/epic-identity-scaffold-and-authentication/tickets/story-iemailsender-adapter-plan.md` | `392229d1c10c1336043cf1921f243736e7637007` |
| `10-3-cookie-auth-bridge` | `3` | Cookie 认证桥接 + LoginPath 配置 | `planning-artifacts/epics-identity-scaffold-migration.md:156` | `implementation-artifacts/10-3-cookie-auth-bridge.md` | done @ 105 | done | `initiative-boxwise/tickets/epic-identity-scaffold-and-authentication/tickets/story-cookie-auth-bridge-plan.md` | `e03e68e26c3b105db549c5e49fd42bdefd9d8e08` |
| `10-4-2fa-login-workaround` | `4` | 2FA 登录 + .NET 10 Bug 验证/workaround | `planning-artifacts/epics-identity-scaffold-migration.md:205` | `implementation-artifacts/10-4-2fa-login-workaround.md` | done @ 106 | done | `initiative-boxwise/tickets/epic-identity-scaffold-and-authentication/tickets/story-2fa-login-workaround-plan.md` | `0635d7b08700f31908966411d157b2772ff7a699` |
| `11-1-settings-link-migration` | `1` | Settings.razor 替换为跳转链接 | `planning-artifacts/epics-identity-scaffold-migration.md:250` | `implementation-artifacts/11-1-settings-link-migration.md` | done @ 109 | done | `initiative-boxwise/tickets/epic-identity-adaptation-and-retirement/tickets/story-settings-link-migration-plan.md` | `d2f38b3bb99c63dad709e1dbc3dade1f73b77203` |
| `11-2-passkey-login-retention` | `2` | Login.razor 保留通行密钥 + 适配 Identity 登录 | `planning-artifacts/epics-identity-scaffold-migration.md:292` | `implementation-artifacts/11-2-passkey-login-retention.md` | done @ 110 | done | `initiative-boxwise/tickets/epic-identity-adaptation-and-retirement/tickets/story-passkey-login-retention-plan.md` | `7c322bcc33b4d6ab4d9a63bc468906829cb87efc` |
| `11-3-decommission-legacy-code` | `3` | 退役旧代码 + 测试更新 | `planning-artifacts/epics-identity-scaffold-migration.md:331` | `implementation-artifacts/11-3-decommission-legacy-code.md` | done @ 111 | done | `initiative-boxwise/tickets/epic-identity-adaptation-and-retirement/tickets/story-decommission-legacy-code-plan.md` | `79a61ce4656178b9869d5ea5518773e04d331c30` |
| `11-4-samesite-docs-update` | `4` | SameSite 策略 + 更新 Architecture/UX 文档 | `planning-artifacts/epics-identity-scaffold-migration.md:400` | `implementation-artifacts/11-4-samesite-docs-update.md` | done @ 112 | done | `initiative-boxwise/tickets/epic-identity-adaptation-and-retirement/tickets/story-samesite-docs-update-plan.md` | `d7e1caa63da35e816ecde684f5872ca9e409d47f` |
| `12-1-frontend-direct-api` | `1` | 前端 AiService 直调火山 API | `planning-artifacts/sprint-change-proposal-2026-06-05.md:148` | `implementation-artifacts/12-1-frontend-direct-api.md` | done @ 115 | done | `initiative-boxwise/tickets/epic-ai-recognition-architecture/tickets/story-frontend-direct-api-plan.md` | `474611cd51b8c464db428d652a9f68273fe4aa10` |
| `12-2-decommission-server-ai` | `2` | 退役服务端 AI 识别代码 | `planning-artifacts/sprint-change-proposal-2026-06-05.md:149` | `implementation-artifacts/12-2-decommission-server-ai.md` | done @ 116 | done | `initiative-boxwise/tickets/epic-ai-recognition-architecture/tickets/story-decommission-server-ai-plan.md` | `0982e14f61a33351fda20371e23a1353a5e1faac` |
| `12-3-update-tests-docs` | `3` | 更新测试 + 文档 | `planning-artifacts/sprint-change-proposal-2026-06-05.md:150` | `implementation-artifacts/12-3-update-tests-docs.md` | done @ 117 | done | `initiative-boxwise/tickets/epic-ai-recognition-architecture/tickets/story-update-tests-docs-plan.md` | `58342e8648f854cc0636dfcfbc38bfc131dfb170` |
| `13-1-llm-config-backend` | `1` | 服务端 LlmConfig 实体与 API | `planning-artifacts/sprint-change-proposal-2026-06-06.md:123` | `implementation-artifacts/13-1-llm-config-backend.md` | done @ 120 | done | `initiative-boxwise/tickets/epic-llm-configuration-security/tickets/story-llm-config-backend-plan.md` | `79d0bae44995241e2fadf5cfff38d749fd0a6cc2` |
| `13-2-ai-service-refactor` | `2` | 客户端 AiService 重构 | `planning-artifacts/sprint-change-proposal-2026-06-06.md:124` | `implementation-artifacts/13-2-ai-service-refactor.md` | done @ 121 | done | `initiative-boxwise/tickets/epic-llm-configuration-security/tickets/story-ai-service-refactor-plan.md` | `79d0bae44995241e2fadf5cfff38d749fd0a6cc2` |
| `13-3-tests-admin-docs` | `3` | 测试 + Admin UI + 文档 | `planning-artifacts/sprint-change-proposal-2026-06-06.md:125` | `implementation-artifacts/13-3-tests-admin-docs.md` | done @ 122 | done | `initiative-boxwise/tickets/epic-llm-configuration-security/tickets/story-tests-admin-docs-plan.md` | `c534a2008424b634c54a5a050d73bee03d00628b` |
| `14-1-admin-2fa-self-protect` | `1` | #29 ResetTwoFactor 管理员自保护 | `planning-artifacts/sprint-change-proposal-2026-06-07-tech-debt-3.md:47` | 无编号实施记录 | done @ 125 | 无独立记录 | `initiative-boxwise/tickets/epic-technical-debt-round-three/tickets/story-admin-2fa-self-protect-plan.md` | `无` |
| `14-2-hasflag-exact-match` | `2` | #30 HasFlag switch 精确值匹配 | `planning-artifacts/sprint-change-proposal-2026-06-07-tech-debt-3.md:106` | 无编号实施记录 | done @ 126 | 无独立记录 | `initiative-boxwise/tickets/epic-technical-debt-round-three/tickets/story-hasflag-exact-match-plan.md` | `无` |
| `14-3-sw-cache-close` | `3` | #27 SW 缓存键不匹配 — 关闭 Issue | `planning-artifacts/sprint-change-proposal-2026-06-07-tech-debt-3.md:141` | 无编号实施记录 | done @ 127 | 无独立记录 | `initiative-boxwise/tickets/epic-technical-debt-round-three/tickets/story-sw-cache-close-plan.md` | `无` |
| `14-4-ci-tar-validate` | `4` | #28 CI tar/ZIP 包安全验证 | `planning-artifacts/sprint-change-proposal-2026-06-07-tech-debt-3.md:146` | 无编号实施记录 | done @ 128 | 无独立记录 | `initiative-boxwise/tickets/epic-technical-debt-round-three/tickets/story-ci-tar-validate-plan.md` | `无` |
| `14-5-grep-regex-universal` | `5` | #31 grep 验证命令通用化 | `planning-artifacts/sprint-change-proposal-2026-06-07-tech-debt-3.md:181` | 无编号实施记录 | done @ 129 | 无独立记录 | `initiative-boxwise/tickets/epic-technical-debt-round-three/tickets/story-grep-regex-universal-plan.md` | `无` |
| `14-6-i18n-close` | `6` | #32 i18n 硬编码 — 关闭 Issue | `planning-artifacts/sprint-change-proposal-2026-06-07-tech-debt-3.md:193` | 无编号实施记录 | done @ 130 | 无独立记录 | `initiative-boxwise/tickets/epic-technical-debt-round-three/tickets/story-i18n-close-plan.md` | `无` |
| `15-1-forgot-password-reset` | `1` | 忘记密码功能实施计划 | `implementation-artifacts/epic-15-forgot-password-plan.md:1` | `implementation-artifacts/epic-15-forgot-password-plan.md` | done @ 132 | — | `initiative-boxwise/tickets/epic-forgot-password/tickets/story-forgot-password-reset-plan.md` | `无` |

## 独立交付台账

下列 18 条与 sprint 的 60 条分开计数；所有源实施记录均明确 `done`。

| 来源 | v7 backlog 票据 | 状态 plan | 类型 | 基线 |
| --- | --- | --- | --- | --- |
| `implementation-artifacts/spec-about-remove-hardcoded-versions.md` | `backlog/story-about-remove-hardcoded-versions.md` | `backlog/story-about-remove-hardcoded-versions-plan.md` | `refactor` | `无` |
| `implementation-artifacts/spec-admin-create-user-email.md` | `backlog/story-admin-create-user-email.md` | `backlog/story-admin-create-user-email-plan.md` | `feature` | `1e56e29e49c0409906d0f04783bde7e4feca749a` |
| `implementation-artifacts/spec-admin-edit-user-email.md` | `backlog/story-admin-edit-user-email.md` | `backlog/story-admin-edit-user-email-plan.md` | `feature` | `无` |
| `implementation-artifacts/spec-complete-webauthn-frontend.md` | `backlog/story-complete-webauthn-frontend.md` | `backlog/story-complete-webauthn-frontend-plan.md` | `feature` | `6d46cdaadae6566d316a0fe46e4553a510c4b84d` |
| `implementation-artifacts/spec-consolidate-email-settings.md` | `backlog/story-consolidate-email-settings.md` | `backlog/story-consolidate-email-settings-plan.md` | `refactor` | `6edb675f3f6b906e9843a705bfac2df9e6573499` |
| `implementation-artifacts/spec-docker-standalone-deploy.md` | `backlog/story-docker-standalone-deploy.md` | `backlog/story-docker-standalone-deploy-plan.md` | `feature` | `无` |
| `implementation-artifacts/spec-fix-ci-skia-package-version.md` | `backlog/bug-fix-ci-skia-package-version.md` | `backlog/bug-fix-ci-skia-package-version-plan.md` | `bugfix` | `无` |
| `implementation-artifacts/spec-fix-deferred-bugs-group1.md` | `backlog/bug-fix-deferred-bugs-group1.md` | `backlog/bug-fix-deferred-bugs-group1-plan.md` | `bugfix` | `ccf0c271be0f88063d49e649aa2087455128a6f8` |
| `implementation-artifacts/spec-fix-thumbnail-generation-resilience.md` | `backlog/bug-fix-thumbnail-generation-resilience.md` | `backlog/bug-fix-thumbnail-generation-resilience-plan.md` | `bugfix` | `724aa73a7efab1cc2a62cca995621412d33598f1` |
| `implementation-artifacts/spec-fix-wasm-fingerprint-placeholder.md` | `backlog/bug-fix-wasm-fingerprint-placeholder.md` | `backlog/bug-fix-wasm-fingerprint-placeholder-plan.md` | `bugfix` | `无` |
| `implementation-artifacts/spec-git-version-from-tag.md` | `backlog/story-git-version-from-tag.md` | `backlog/story-git-version-from-tag-plan.md` | `feature` | `无` |
| `implementation-artifacts/spec-harden-error-handling.md` | `backlog/bug-harden-error-handling.md` | `backlog/bug-harden-error-handling-plan.md` | `bugfix` | `8af18a966ca5f2c5af5ebefefe9ad280c809e1a1` |
| `implementation-artifacts/spec-move-admin-link-to-settings.md` | `backlog/story-move-admin-link-to-settings.md` | `backlog/story-move-admin-link-to-settings-plan.md` | `refactor` | `无` |
| `implementation-artifacts/spec-recovery-codes-bulk-actions.md` | `backlog/story-recovery-codes-bulk-actions.md` | `backlog/story-recovery-codes-bulk-actions-plan.md` | `feature` | `无` |
| `implementation-artifacts/spec-refactor-path-resolution.md` | `backlog/story-refactor-path-resolution.md` | `backlog/story-refactor-path-resolution-plan.md` | `refactor` | `9f9e6b35c06032271b160240b1013a59336477e4` |
| `implementation-artifacts/spec-smtp-config-test-email.md` | `backlog/story-smtp-config-test-email.md` | `backlog/story-smtp-config-test-email-plan.md` | `feature` | `abbe26d44b435aee1722236dc4f818eb7e572074` |
| `implementation-artifacts/spec-tech-debt-cleanup-2.md` | `backlog/story-tech-debt-cleanup-2.md` | `backlog/story-tech-debt-cleanup-2-plan.md` | `chore` | `5a23bbf3d25a024596c1ccceb370c229f9231287` |
| `implementation-artifacts/spec-tech-debt-epic6.md` | `backlog/story-tech-debt-epic6.md` | `backlog/story-tech-debt-epic6-plan.md` | `chore` | `f22d2338c4371147a1fd10c806f28c3ed4f8a203` |

## 逐文件去向（143 个源文件）

| 源路径 | 目标路径 | 处理 |
| --- | --- | --- |
| `brainstorming/brainstorming-session-2026-05-28-171053.md` | `initiative-boxwise/brainstorming-session-2026-05-28-171053/brainstorming-session-2026-05-28-171053.md` | 账户安全改进脑暴；按 session 建同名目录（扁平映射修正） |
| `implementation-artifacts/1-1-project-scaffolding.md` | `initiative-boxwise/tickets/epic-project-setup-and-authentication/tickets/story-project-scaffolding-plan.md` | 合并验收条件；保留实施和审查历史 |
| `implementation-artifacts/1-2-identity-auth.md` | `initiative-boxwise/tickets/epic-project-setup-and-authentication/tickets/story-identity-auth-plan.md` | 合并验收条件；保留实施和审查历史 |
| `implementation-artifacts/1-3-admin-account-management.md` | `initiative-boxwise/tickets/epic-project-setup-and-authentication/tickets/story-admin-account-management-plan.md` | 合并验收条件；保留实施和审查历史 |
| `implementation-artifacts/10-1-scaffold-identity-pages.md` | `initiative-boxwise/tickets/epic-identity-scaffold-and-authentication/tickets/story-scaffold-identity-pages-plan.md` | 合并验收条件；保留实施和审查历史 |
| `implementation-artifacts/10-2-iemailsender-adapter.md` | `initiative-boxwise/tickets/epic-identity-scaffold-and-authentication/tickets/story-iemailsender-adapter-plan.md` | 合并验收条件；保留实施和审查历史 |
| `implementation-artifacts/10-3-cookie-auth-bridge.md` | `initiative-boxwise/tickets/epic-identity-scaffold-and-authentication/tickets/story-cookie-auth-bridge-plan.md` | 合并验收条件；保留实施和审查历史 |
| `implementation-artifacts/10-4-2fa-login-workaround.md` | `initiative-boxwise/tickets/epic-identity-scaffold-and-authentication/tickets/story-2fa-login-workaround-plan.md` | 合并验收条件；保留实施和审查历史 |
| `implementation-artifacts/11-1-settings-link-migration.md` | `initiative-boxwise/tickets/epic-identity-adaptation-and-retirement/tickets/story-settings-link-migration-plan.md` | 合并验收条件；保留实施和审查历史 |
| `implementation-artifacts/11-2-passkey-login-retention.md` | `initiative-boxwise/tickets/epic-identity-adaptation-and-retirement/tickets/story-passkey-login-retention-plan.md` | 合并验收条件；保留实施和审查历史 |
| `implementation-artifacts/11-3-decommission-legacy-code.md` | `initiative-boxwise/tickets/epic-identity-adaptation-and-retirement/tickets/story-decommission-legacy-code-plan.md` | 合并验收条件；保留实施和审查历史 |
| `implementation-artifacts/11-4-samesite-docs-update.md` | `initiative-boxwise/tickets/epic-identity-adaptation-and-retirement/tickets/story-samesite-docs-update-plan.md` | 合并验收条件；保留实施和审查历史 |
| `implementation-artifacts/12-1-frontend-direct-api.md` | `initiative-boxwise/tickets/epic-ai-recognition-architecture/tickets/story-frontend-direct-api-plan.md` | 合并验收条件；保留实施和审查历史 |
| `implementation-artifacts/12-2-decommission-server-ai.md` | `initiative-boxwise/tickets/epic-ai-recognition-architecture/tickets/story-decommission-server-ai-plan.md` | 合并验收条件；保留实施和审查历史 |
| `implementation-artifacts/12-3-update-tests-docs.md` | `initiative-boxwise/tickets/epic-ai-recognition-architecture/tickets/story-update-tests-docs-plan.md` | 合并验收条件；保留实施和审查历史 |
| `implementation-artifacts/13-1-llm-config-backend.md` | `initiative-boxwise/tickets/epic-llm-configuration-security/tickets/story-llm-config-backend-plan.md` | 合并验收条件；保留实施和审查历史 |
| `implementation-artifacts/13-2-ai-service-refactor.md` | `initiative-boxwise/tickets/epic-llm-configuration-security/tickets/story-ai-service-refactor-plan.md` | 合并验收条件；保留实施和审查历史 |
| `implementation-artifacts/13-3-tests-admin-docs.md` | `initiative-boxwise/tickets/epic-llm-configuration-security/tickets/story-tests-admin-docs-plan.md` | 合并验收条件；保留实施和审查历史 |
| `implementation-artifacts/2-1-location-entity-crud.md` | `initiative-boxwise/tickets/epic-locations-and-tags/tickets/story-location-entity-crud-plan.md` | 合并验收条件；保留实施和审查历史 |
| `implementation-artifacts/2-2-location-tree-api.md` | `initiative-boxwise/tickets/epic-locations-and-tags/tickets/story-location-tree-api-plan.md` | 合并验收条件；保留实施和审查历史 |
| `implementation-artifacts/2-3-tag-system.md` | `initiative-boxwise/tickets/epic-locations-and-tags/tickets/story-tag-system-plan.md` | 合并验收条件；保留实施和审查历史 |
| `implementation-artifacts/2-4-location-tag-selector.md` | `initiative-boxwise/tickets/epic-locations-and-tags/tickets/story-location-tag-selector-plan.md` | 合并验收条件；保留实施和审查历史 |
| `implementation-artifacts/3-1-item-image-upload.md` | `initiative-boxwise/tickets/epic-item-entry-and-recognition/tickets/story-item-image-upload-plan.md` | 合并验收条件；保留实施和审查历史 |
| `implementation-artifacts/3-2-item-entry-api.md` | `initiative-boxwise/tickets/epic-item-entry-and-recognition/tickets/story-item-entry-api-plan.md` | 合并验收条件；保留实施和审查历史 |
| `implementation-artifacts/3-3-ai-recognition.md` | `initiative-boxwise/tickets/epic-item-entry-and-recognition/tickets/story-ai-recognition-plan.md` | 合并验收条件；保留实施和审查历史 |
| `implementation-artifacts/3-4-item-entry-ui.md` | `initiative-boxwise/tickets/epic-item-entry-and-recognition/tickets/story-item-entry-ui-plan.md` | 合并验收条件；保留实施和审查历史 |
| `implementation-artifacts/3-5-item-detail.md` | `initiative-boxwise/tickets/epic-item-entry-and-recognition/tickets/story-item-detail-plan.md` | 合并验收条件；保留实施和审查历史 |
| `implementation-artifacts/4-1-search-functionality.md` | `initiative-boxwise/tickets/epic-browse-and-deployment/tickets/story-search-functionality-plan.md` | 合并验收条件；保留实施和审查历史 |
| `implementation-artifacts/4-2-thumbnail-grid-browse.md` | `initiative-boxwise/tickets/epic-browse-and-deployment/tickets/story-thumbnail-grid-browse-plan.md` | 合并验收条件；保留实施和审查历史 |
| `implementation-artifacts/4-3-location-tag-filter.md` | `initiative-boxwise/tickets/epic-browse-and-deployment/tickets/story-location-tag-filter-plan.md` | 合并验收条件；保留实施和审查历史 |
| `implementation-artifacts/4-4-item-detail-delete.md` | `initiative-boxwise/tickets/epic-browse-and-deployment/tickets/story-item-detail-delete-plan.md` | 合并验收条件；保留实施和审查历史 |
| `implementation-artifacts/4-5-pwa-offline.md` | `initiative-boxwise/tickets/epic-browse-and-deployment/tickets/story-pwa-offline-plan.md` | 合并验收条件；保留实施和审查历史 |
| `implementation-artifacts/4-6-docker-deployment.md` | `initiative-boxwise/tickets/epic-browse-and-deployment/tickets/story-docker-deployment-plan.md` | 合并验收条件；保留实施和审查历史 |
| `implementation-artifacts/6-1-test-cleanup-quality.md` | `initiative-boxwise/tickets/epic-unit-test-coverage/tickets/story-test-cleanup-quality-plan.md` | 合并验收条件；保留实施和审查历史 |
| `implementation-artifacts/6-2-repo-coverage-gap-fill.md` | `initiative-boxwise/tickets/epic-unit-test-coverage/tickets/story-repo-coverage-gap-fill-plan.md` | 合并验收条件；保留实施和审查历史 |
| `implementation-artifacts/6-3-service-layer-tests.md` | `initiative-boxwise/tickets/epic-unit-test-coverage/tickets/story-service-layer-tests-plan.md` | 合并验收条件；保留实施和审查历史 |
| `implementation-artifacts/6-4-endpoint-layer-tests.md` | `initiative-boxwise/tickets/epic-unit-test-coverage/tickets/story-endpoint-layer-tests-plan.md` | 合并验收条件；保留实施和审查历史 |
| `implementation-artifacts/6-5-admin-pagemodel-tests.md` | `initiative-boxwise/tickets/epic-unit-test-coverage/tickets/story-admin-pagemodel-tests-plan.md` | 合并验收条件；保留实施和审查历史 |
| `implementation-artifacts/7-1-item-edit-backend.md` | `initiative-boxwise/tickets/epic-item-editing/tickets/story-item-edit-backend-plan.md` | 合并验收条件；保留实施和审查历史 |
| `implementation-artifacts/7-2-item-edit-ui.md` | `initiative-boxwise/tickets/epic-item-editing/tickets/story-item-edit-ui-plan.md` | 合并验收条件；保留实施和审查历史 |
| `implementation-artifacts/9-1-2fa-modify-settings.md` | `initiative-boxwise/tickets/epic-two-factor-settings/tickets/story-2fa-modify-settings-plan.md` | 合并验收条件；保留实施和审查历史 |
| `implementation-artifacts/code-review-acceptance-auditor-4-1.md` | `initiative-boxwise/tickets/epic-browse-and-deployment/code-review-acceptance-auditor-4-1.md` | Epic 4 的评审证据 |
| `implementation-artifacts/code-review-blind-hunter-4-1.md` | `initiative-boxwise/tickets/epic-browse-and-deployment/code-review-blind-hunter-4-1.md` | Epic 4 的评审证据 |
| `implementation-artifacts/code-review-edge-case-hunter-4-1.md` | `initiative-boxwise/tickets/epic-browse-and-deployment/code-review-edge-case-hunter-4-1.md` | Epic 4 的评审证据 |
| `implementation-artifacts/deferred-work.md` | `initiative-boxwise/deferred-work.md` | 项目级延期工作或实施证据，不执行 |
| `implementation-artifacts/diff-consolidate-email.txt` | `initiative-boxwise/diff-consolidate-email.txt` | 项目级延期工作或实施证据，不执行 |
| `implementation-artifacts/epic-1-retro-2026-05-24.md` | `initiative-boxwise/tickets/epic-project-setup-and-authentication/epic-project-setup-and-authentication-retrospective.md` | 同 Epic 回顾合并，保留原文与日期；只保留已有结论 |
| `implementation-artifacts/epic-10-retro-2026-06-01.md` | `initiative-boxwise/tickets/epic-identity-scaffold-and-authentication/epic-identity-scaffold-and-authentication-retrospective.md` | 同 Epic 回顾合并，保留原文与日期；只保留已有结论 |
| `implementation-artifacts/epic-11-retro-2026-06-02.md` | `initiative-boxwise/tickets/epic-identity-adaptation-and-retirement/epic-identity-adaptation-and-retirement-retrospective.md` | 同 Epic 回顾合并，保留原文与日期；只保留已有结论 |
| `implementation-artifacts/epic-12-retro-2026-06-06.md` | `initiative-boxwise/tickets/epic-ai-recognition-architecture/epic-ai-recognition-architecture-retrospective.md` | 同 Epic 回顾合并，保留原文与日期；只保留已有结论 |
| `implementation-artifacts/epic-13-retro-2026-06-06.md` | `initiative-boxwise/tickets/epic-llm-configuration-security/epic-llm-configuration-security-retrospective.md` | 同 Epic 回顾合并，保留原文与日期；只保留已有结论 |
| `implementation-artifacts/epic-15-forgot-password-plan.md` | `initiative-boxwise/tickets/epic-forgot-password/tickets/story-forgot-password-reset-plan.md` | 合并验收条件；保留实施和审查历史 |
| `implementation-artifacts/epic-15-manual-test-guide.md` | `initiative-boxwise/tickets/epic-forgot-password/epic-15-manual-test-guide.md` | 历史验证指南，不视为已执行 |
| `implementation-artifacts/epic-15-retro-2026-08-20.md` | `initiative-boxwise/tickets/epic-forgot-password/epic-forgot-password-retrospective.md` | 同 Epic 回顾合并，保留原文与日期；只保留已有结论 |
| `implementation-artifacts/epic-2-retro-2026-05-24.md` | `initiative-boxwise/tickets/epic-locations-and-tags/epic-locations-and-tags-retrospective.md` | 同 Epic 回顾合并，保留原文与日期；只保留已有结论 |
| `implementation-artifacts/epic-3-retro-2026-05-24.md` | `initiative-boxwise/tickets/epic-item-entry-and-recognition/epic-item-entry-and-recognition-retrospective.md` | 同 Epic 回顾合并，保留原文与日期；只保留已有结论 |
| `implementation-artifacts/epic-4-retro-2026-05-24.md` | `initiative-boxwise/tickets/epic-browse-and-deployment/epic-browse-and-deployment-retrospective.md` | 同 Epic 回顾合并，保留原文与日期；只保留已有结论 |
| `implementation-artifacts/epic-5-retro-2026-05-27.md` | `initiative-boxwise/tickets/epic-user-management-and-branding/epic-user-management-and-branding-retrospective.md` | 同 Epic 回顾合并，保留原文与日期；只保留已有结论 |
| `implementation-artifacts/epic-6-retro-2026-05-27.md` | `initiative-boxwise/tickets/epic-unit-test-coverage/epic-unit-test-coverage-retrospective.md` | 同 Epic 回顾合并，保留原文与日期；只保留已有结论 |
| `implementation-artifacts/epic-7-retro-2026-05-28.md` | `initiative-boxwise/tickets/epic-item-editing/epic-item-editing-retrospective.md` | 同 Epic 回顾合并，保留原文与日期；只保留已有结论 |
| `implementation-artifacts/epic-8-retro-2026-05-29.md` | `initiative-boxwise/tickets/epic-account-security/epic-account-security-retrospective.md` | 同 Epic 回顾合并，保留原文与日期；只保留已有结论 |
| `implementation-artifacts/epic-9-retro-2026-05-30.md` | `initiative-boxwise/archive-v6/implementation-artifacts/epic-9-retro-2026-05-30.md` | 保留原件和 Git 历史；原文同时并入 initiative-boxwise/tickets/epic-two-factor-settings/epic-two-factor-settings-retrospective.md |
| `implementation-artifacts/epic-9-retro-2026-05-31.md` | `initiative-boxwise/tickets/epic-two-factor-settings/epic-two-factor-settings-retrospective.md` | 同 Epic 回顾合并，保留原文与日期；只保留已有结论 |
| `implementation-artifacts/investigations/2fa-code-invalid-investigation.md` | `initiative-boxwise/investigations/2fa-code-invalid-investigation.md` | 跨 Epic 诊断证据成组保留，不执行其中脚本 |
| `implementation-artifacts/investigations/2fa-gettwofactoruserasync-null-investigation.md` | `initiative-boxwise/investigations/2fa-gettwofactoruserasync-null-investigation.md` | 跨 Epic 诊断证据成组保留，不执行其中脚本 |
| `implementation-artifacts/investigations/admin-2fa-empty-display-investigation.md` | `initiative-boxwise/investigations/admin-2fa-empty-display-investigation.md` | 跨 Epic 诊断证据成组保留，不执行其中脚本 |
| `implementation-artifacts/investigations/ai-recognition-timeout-investigation.md` | `initiative-boxwise/investigations/ai-recognition-timeout-investigation.md` | 跨 Epic 诊断证据成组保留，不执行其中脚本 |
| `implementation-artifacts/investigations/app-loading-stuck-investigation.md` | `initiative-boxwise/investigations/app-loading-stuck-investigation.md` | 跨 Epic 诊断证据成组保留，不执行其中脚本 |
| `implementation-artifacts/investigations/confirmemail-missing-statusmessage-investigation.md` | `initiative-boxwise/investigations/confirmemail-missing-statusmessage-investigation.md` | 跨 Epic 诊断证据成组保留，不执行其中脚本 |
| `implementation-artifacts/investigations/cors-test.js` | `initiative-boxwise/investigations/cors-test.js` | 跨 Epic 诊断证据成组保留，不执行其中脚本 |
| `implementation-artifacts/investigations/cors-test2.js` | `initiative-boxwise/investigations/cors-test2.js` | 跨 Epic 诊断证据成组保留，不执行其中脚本 |
| `implementation-artifacts/investigations/home-page-refresh-redirect-investigation.md` | `initiative-boxwise/investigations/home-page-refresh-redirect-investigation.md` | 跨 Epic 诊断证据成组保留，不执行其中脚本 |
| `implementation-artifacts/investigations/homepage-search-not-working-investigation.md` | `initiative-boxwise/investigations/homepage-search-not-working-investigation.md` | 跨 Epic 诊断证据成组保留，不执行其中脚本 |
| `implementation-artifacts/investigations/identity-login-404-production-investigation.md` | `initiative-boxwise/investigations/identity-login-404-production-investigation.md` | 跨 Epic 诊断证据成组保留，不执行其中脚本 |
| `implementation-artifacts/investigations/identity-manage-no-sidebar-investigation.md` | `initiative-boxwise/investigations/identity-manage-no-sidebar-investigation.md` | 跨 Epic 诊断证据成组保留，不执行其中脚本 |
| `implementation-artifacts/investigations/login-redirect-loop-investigation.md` | `initiative-boxwise/investigations/login-redirect-loop-investigation.md` | 跨 Epic 诊断证据成组保留，不执行其中脚本 |
| `implementation-artifacts/investigations/passkey-production-error-investigation.md` | `initiative-boxwise/investigations/passkey-production-error-investigation.md` | 跨 Epic 诊断证据成组保留，不执行其中脚本 |
| `implementation-artifacts/investigations/passkey-stale-credentials-investigation.md` | `initiative-boxwise/investigations/passkey-stale-credentials-investigation.md` | 跨 Epic 诊断证据成组保留，不执行其中脚本 |
| `implementation-artifacts/investigations/production-images-not-displaying-investigation.md` | `initiative-boxwise/investigations/production-images-not-displaying-investigation.md` | 跨 Epic 诊断证据成组保留，不执行其中脚本 |
| `implementation-artifacts/investigations/test_ai_api.sh` | `initiative-boxwise/investigations/test_ai_api.sh` | 跨 Epic 诊断证据成组保留，不执行其中脚本 |
| `implementation-artifacts/investigations/upload_speed_test.py` | `initiative-boxwise/investigations/upload_speed_test.py` | 跨 Epic 诊断证据成组保留，不执行其中脚本 |
| `implementation-artifacts/migration-consolidate-email.sql` | `initiative-boxwise/migration-consolidate-email.sql` | 项目级延期工作或实施证据，不执行 |
| `implementation-artifacts/review-acceptance-auditor-4-4.md` | `initiative-boxwise/tickets/epic-browse-and-deployment/review-acceptance-auditor-4-4.md` | Epic 4 的评审证据 |
| `implementation-artifacts/review-blind-hunter-4-4.md` | `initiative-boxwise/tickets/epic-browse-and-deployment/review-blind-hunter-4-4.md` | Epic 4 的评审证据 |
| `implementation-artifacts/review-edge-case-hunter-4-4.md` | `initiative-boxwise/tickets/epic-browse-and-deployment/review-edge-case-hunter-4-4.md` | Epic 4 的评审证据 |
| `implementation-artifacts/spec-about-remove-hardcoded-versions.md` | `backlog/story-about-remove-hardcoded-versions-plan.md` | 独立交付记录；同时建立同 stem 的 backlog 票据 |
| `implementation-artifacts/spec-admin-create-user-email.md` | `backlog/story-admin-create-user-email-plan.md` | 独立交付记录；同时建立同 stem 的 backlog 票据 |
| `implementation-artifacts/spec-admin-edit-user-email.md` | `backlog/story-admin-edit-user-email-plan.md` | 独立交付记录；同时建立同 stem 的 backlog 票据 |
| `implementation-artifacts/spec-complete-webauthn-frontend.md` | `backlog/story-complete-webauthn-frontend-plan.md` | 独立交付记录；同时建立同 stem 的 backlog 票据 |
| `implementation-artifacts/spec-consolidate-email-settings.md` | `backlog/story-consolidate-email-settings-plan.md` | 独立交付记录；同时建立同 stem 的 backlog 票据 |
| `implementation-artifacts/spec-docker-standalone-deploy.md` | `backlog/story-docker-standalone-deploy-plan.md` | 独立交付记录；同时建立同 stem 的 backlog 票据 |
| `implementation-artifacts/spec-fix-ci-skia-package-version.md` | `backlog/bug-fix-ci-skia-package-version-plan.md` | 独立交付记录；同时建立同 stem 的 backlog 票据 |
| `implementation-artifacts/spec-fix-deferred-bugs-group1.md` | `backlog/bug-fix-deferred-bugs-group1-plan.md` | 独立交付记录；同时建立同 stem 的 backlog 票据 |
| `implementation-artifacts/spec-fix-thumbnail-generation-resilience.md` | `backlog/bug-fix-thumbnail-generation-resilience-plan.md` | 独立交付记录；同时建立同 stem 的 backlog 票据 |
| `implementation-artifacts/spec-fix-wasm-fingerprint-placeholder.md` | `backlog/bug-fix-wasm-fingerprint-placeholder-plan.md` | 独立交付记录；同时建立同 stem 的 backlog 票据 |
| `implementation-artifacts/spec-git-version-from-tag.md` | `backlog/story-git-version-from-tag-plan.md` | 独立交付记录；同时建立同 stem 的 backlog 票据 |
| `implementation-artifacts/spec-harden-error-handling.md` | `backlog/bug-harden-error-handling-plan.md` | 独立交付记录；同时建立同 stem 的 backlog 票据 |
| `implementation-artifacts/spec-move-admin-link-to-settings.md` | `backlog/story-move-admin-link-to-settings-plan.md` | 独立交付记录；同时建立同 stem 的 backlog 票据 |
| `implementation-artifacts/spec-recovery-codes-bulk-actions.md` | `backlog/story-recovery-codes-bulk-actions-plan.md` | 独立交付记录；同时建立同 stem 的 backlog 票据 |
| `implementation-artifacts/spec-refactor-path-resolution.md` | `backlog/story-refactor-path-resolution-plan.md` | 独立交付记录；同时建立同 stem 的 backlog 票据 |
| `implementation-artifacts/spec-smtp-config-test-email.md` | `backlog/story-smtp-config-test-email-plan.md` | 独立交付记录；同时建立同 stem 的 backlog 票据 |
| `implementation-artifacts/spec-tech-debt-cleanup-2.md` | `backlog/story-tech-debt-cleanup-2-plan.md` | 独立交付记录；同时建立同 stem 的 backlog 票据 |
| `implementation-artifacts/spec-tech-debt-epic6.md` | `backlog/story-tech-debt-epic6-plan.md` | 独立交付记录；同时建立同 stem 的 backlog 票据 |
| `implementation-artifacts/sprint-status.yaml` | `initiative-boxwise/archive-v6/implementation-artifacts/sprint-status.yaml` | v6 跟踪原件，字节不变 |
| `planning-artifacts/2026-07-02-forgot-password-design.md` | `initiative-boxwise/2026-07-02-forgot-password-design/2026-07-02-forgot-password-design.md` | 按 flat file → 同名目录修正；作为 Epic 15 设计来源 |
| `planning-artifacts/architecture.md` | `initiative-boxwise/architecture-boxwise/architecture-boxwise.md` | 规划目录整体进入 initiative；识别的文档按成套目录归档或改名 |
| `planning-artifacts/brainstorming/brainstorming-session-2026-05-21-151754.md` | `initiative-boxwise/brainstorming-session-2026-05-21-151754/brainstorming-session-2026-05-21-151754.md` | 按 session 建同名目录（扁平映射修正） |
| `planning-artifacts/epics-identity-scaffold-migration.md` | `initiative-boxwise/epics-identity-scaffold-migration/epics-identity-scaffold-migration.md` | 按 flat file → 同名目录修正；作为 Epic 10/11 补充规划来源 |
| `planning-artifacts/epics.md` | `initiative-boxwise/archive-v6/planning-artifacts/epics.md` | v6 跟踪原件，字节不变 |
| `planning-artifacts/prds/prd-BoxWise-2026-05-21/.decision-log.md` | `initiative-boxwise/prd-BoxWise-2026-05-21/.decision-log.md` | 规划目录整体进入 initiative；识别的文档按成套目录归档或改名 |
| `planning-artifacts/prds/prd-BoxWise-2026-05-21/prd.md` | `initiative-boxwise/prd-BoxWise-2026-05-21/prd-BoxWise-2026-05-21.md` | 规划目录整体进入 initiative；识别的文档按成套目录归档或改名 |
| `planning-artifacts/research/technical-identity-scaffold-hybrid-migration-research-2026-05-31.md` | `initiative-boxwise/technical-identity-scaffold-hybrid-migration-research-2026-05-31/technical-identity-scaffold-hybrid-migration-research-2026-05-31.md` | 规划目录整体进入 initiative；识别的文档按成套目录归档或改名 |
| `planning-artifacts/sprint-change-proposal-2026-05-27.md` | `initiative-boxwise/sprint-change-proposal-2026-05-27/sprint-change-proposal-2026-05-27.md` | 规划目录整体进入 initiative；识别的文档按成套目录归档或改名 |
| `planning-artifacts/sprint-change-proposal-2026-05-28-security.md` | `initiative-boxwise/sprint-change-proposal-2026-05-28-security/sprint-change-proposal-2026-05-28-security.md` | 规划目录整体进入 initiative；识别的文档按成套目录归档或改名 |
| `planning-artifacts/sprint-change-proposal-2026-05-28.md` | `initiative-boxwise/sprint-change-proposal-2026-05-28/sprint-change-proposal-2026-05-28.md` | 规划目录整体进入 initiative；识别的文档按成套目录归档或改名 |
| `planning-artifacts/sprint-change-proposal-2026-06-05.md` | `initiative-boxwise/sprint-change-proposal-2026-06-05/sprint-change-proposal-2026-06-05.md` | 规划目录整体进入 initiative；识别的文档按成套目录归档或改名 |
| `planning-artifacts/sprint-change-proposal-2026-06-06.md` | `initiative-boxwise/sprint-change-proposal-2026-06-06/sprint-change-proposal-2026-06-06.md` | 规划目录整体进入 initiative；识别的文档按成套目录归档或改名 |
| `planning-artifacts/sprint-change-proposal-2026-06-07-tech-debt-3.md` | `initiative-boxwise/sprint-change-proposal-2026-06-07-tech-debt-3/sprint-change-proposal-2026-06-07-tech-debt-3.md` | 规划目录整体进入 initiative；识别的文档按成套目录归档或改名 |
| `planning-artifacts/ux-design-directions.html` | `initiative-boxwise/ux-design-specification/ux-design-directions.html` | 规划目录整体进入 initiative；识别的文档按成套目录归档或改名 |
| `planning-artifacts/ux-design-specification.md` | `initiative-boxwise/ux-design-specification/ux-design-specification.md` | 规划目录整体进入 initiative；识别的文档按成套目录归档或改名 |
| `project-context.md` | `project-context.md` | 原地保留，后续由 bmad-project-context 独立处理 |
| `specs/spec-2fa-modify-settings/.decision-log.md` | `initiative-boxwise/spec-2fa-modify-settings/.decision-log.md` | BoxWise 的经典 SPEC，成套搬迁，无 stories.yaml |
| `specs/spec-2fa-modify-settings/SPEC.md` | `initiative-boxwise/spec-2fa-modify-settings/spec-2fa-modify-settings.md` | BoxWise 的经典 SPEC，成套搬迁，无 stories.yaml |
| `specs/spec-2fa-modify-settings/modify-flow.md` | `initiative-boxwise/spec-2fa-modify-settings/modify-flow.md` | BoxWise 的经典 SPEC，成套搬迁，无 stories.yaml |
| `specs/spec-2fa-multi-method-login/.decision-log.md` | `initiative-boxwise/spec-2fa-multi-method-login/.decision-log.md` | BoxWise 的经典 SPEC，成套搬迁，无 stories.yaml |
| `specs/spec-2fa-multi-method-login/SPEC.md` | `initiative-boxwise/spec-2fa-multi-method-login/spec-2fa-multi-method-login.md` | BoxWise 的经典 SPEC，成套搬迁，无 stories.yaml |
| `specs/spec-2fa-multi-method-login/data-model.md` | `initiative-boxwise/spec-2fa-multi-method-login/data-model.md` | BoxWise 的经典 SPEC，成套搬迁，无 stories.yaml |
| `specs/spec-2fa-multi-method-login/login-flow.md` | `initiative-boxwise/spec-2fa-multi-method-login/login-flow.md` | BoxWise 的经典 SPEC，成套搬迁，无 stories.yaml |
| `specs/spec-client-unit-tests/.decision-log.md` | `specs/spec-client-unit-tests/.decision-log.md` | 未找到纳入该 initiative 的直接规划/Story 引用；保留独立经典 SPEC |
| `specs/spec-client-unit-tests/SPEC.md` | `specs/spec-client-unit-tests/SPEC.md` | 未找到纳入该 initiative 的直接规划/Story 引用；保留独立经典 SPEC |
| `specs/spec-fix-thumbnail-generation-resilience/.decision-log.md` | `initiative-boxwise/spec-fix-thumbnail-generation-resilience/.decision-log.md` | BoxWise 的经典 SPEC，成套搬迁，无 stories.yaml |
| `specs/spec-fix-thumbnail-generation-resilience/SPEC.md` | `initiative-boxwise/spec-fix-thumbnail-generation-resilience/spec-fix-thumbnail-generation-resilience.md` | BoxWise 的经典 SPEC，成套搬迁，无 stories.yaml |
| `specs/spec-identity-scaffold-migration/.decision-log.md` | `initiative-boxwise/spec-identity-scaffold-migration/.decision-log.md` | BoxWise 的经典 SPEC，成套搬迁，无 stories.yaml |
| `specs/spec-identity-scaffold-migration/SPEC.md` | `initiative-boxwise/spec-identity-scaffold-migration/spec-identity-scaffold-migration.md` | BoxWise 的经典 SPEC，成套搬迁，无 stories.yaml |
| `specs/spec-identity-scaffold-migration/decommission-checklist.md` | `initiative-boxwise/spec-identity-scaffold-migration/decommission-checklist.md` | BoxWise 的经典 SPEC，成套搬迁，无 stories.yaml |
| `specs/spec-identity-scaffold-migration/migration-phases.md` | `initiative-boxwise/spec-identity-scaffold-migration/migration-phases.md` | BoxWise 的经典 SPEC，成套搬迁，无 stories.yaml |
| `specs/spec-passkey-stale-credential-ux/SPEC.md` | `specs/spec-passkey-stale-credential-ux/SPEC.md` | 保留独立经典 SPEC；仅其 `context:` 一行按“已知断链与留置链接”做路径例外，其余原文不变 |
| `specs/spec-tech-debt-cleanup/.decision-log.md` | `initiative-boxwise/spec-tech-debt-cleanup/.decision-log.md` | BoxWise 的经典 SPEC，成套搬迁，无 stories.yaml |
| `specs/spec-tech-debt-cleanup/SPEC.md` | `initiative-boxwise/spec-tech-debt-cleanup/spec-tech-debt-cleanup.md` | BoxWise 的经典 SPEC，成套搬迁，无 stories.yaml |
| `specs/spec-tech-debt-cleanup/debt-inventory.md` | `initiative-boxwise/spec-tech-debt-cleanup/debt-inventory.md` | BoxWise 的经典 SPEC，成套搬迁，无 stories.yaml |
| `specs/spec-test-coverage/.decision-log.md` | `initiative-boxwise/spec-test-coverage/.decision-log.md` | BoxWise 的经典 SPEC，成套搬迁，无 stories.yaml |
| `specs/spec-test-coverage/SPEC.md` | `initiative-boxwise/spec-test-coverage/spec-test-coverage.md` | BoxWise 的经典 SPEC，成套搬迁，无 stories.yaml |
| `specs/spec-test-coverage/test-inventory.md` | `initiative-boxwise/spec-test-coverage/test-inventory.md` | BoxWise 的经典 SPEC，成套搬迁，无 stories.yaml |

（台账说明：本次扁平映射修正只改 4 个目标路径——两个脑暴 session、`2026-07-02-forgot-password-design`、`epics-identity-scaffold-migration`；源文件数仍为 143，台账仍为一源一行。`investigations/investigations.md` 是新建 router，不是源文件行。）

## 盘点校验

- 精确同名或确认替代的 sprint 实施记录：41（40 条编号记录 + Epic 15.1 的 1 份任务式计划）；缺少独立编号实施记录：19。
- 旧独立实施文件仍写 `review` 的 Story：15 条；v7 状态按“状态例外（待批准）”A/B 处理，不把 `review` 静默改写成验收结论。
- 有可验证明确基线：sprint 21 条，backlog 10 条；缺失或未验证逐行显示。60 条 sprint 中 39 条、18 条 backlog 中 8 条无基线，不填当前 HEAD。
