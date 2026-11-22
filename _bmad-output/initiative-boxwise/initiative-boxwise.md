---
type: initiative
title: "BoxWise"
status: done
risk: low
sources: ["_bmad-output/initiative-boxwise/prd-BoxWise-2026-05-21/prd-BoxWise-2026-05-21.md"]
companions: ["_bmad-output/initiative-boxwise/architecture-boxwise/architecture-boxwise.md", "_bmad-output/initiative-boxwise/ux-design-specification/ux-design-specification.md"]
---
# BoxWise

## Description

箱知 · BoxWise 是一个家庭物品管理 PWA，让家庭成员记录物品放在哪里，并在需要时快速找到。

## Outcome

家庭成员在收纳后能持久记录物品位置，并在 30 秒内通过搜索或浏览定位到实物；全家人共享同一物品库。

## Requirements

- R1 (prd FR-1): 拍照采集（可选）：用户可以选择拍摄物品照片或跳过拍照直接录入。照片为物品记录的可选字段。
- R2 (prd FR-2): AI 识别预填：系统将物品照片 base64 编码后，通过客户端浏览器直接发送至火山引擎 ARK API（OpenAI 兼容），自动识别物品名称并生成简要描述，预填至备注字段。服务端不参与 AI 调用。v1 仅支持火山 ARK（doubao-seed-2-0-pro-260215）。
- R3 (prd FR-3): 手动输入兜底：当 AI API 调用失败、超时或不可用时，系统自动切换为纯手动输入模式。
- R4 (prd FR-4): 位置分配：用户为物品分配一个层级位置（房间 → 柜子 → 箱子）。
- R5 (prd FR-5): 标签附加：用户可以为物品添加多个自定义标签。
- R6 (prd FR-6): 入库保存：用户确认所有信息后保存物品记录。
- R7 (prd FR-7): 位置继承：保存一件物品后，下一件物品录入时位置字段默认填充为上一件的位置。
- R8 (prd FR-8): 连续模式提示：UI 清晰标识当前处于"连续收纳"状态及继承的位置。
- R9 (prd FR-9): 模糊搜索：用户可以通过物品名称关键词搜索物品，系统执行模糊匹配。
- R10 (prd FR-10): 搜索结果展示：搜索结果以列表或网格形式展示，包含物品缩略图、名称、位置、标签。
- R11 (prd FR-11): 缩略图网格：所有物品以缩略图网格形式展示，便于视觉浏览。
- R12 (prd FR-12): 位置筛选：用户可以按位置层级筛选物品显示范围。
- R13 (prd FR-13): 标签筛选：用户可以按标签筛选物品。
- R14 (prd FR-14): 层级位置创建：用户可以创建、命名、编辑、删除层级位置节点，层级深度由用户自定义（如房间 → 柜子 → 箱子，或房间 → 柜子 → 抽屉 → 分隔盒等更深层级）。
- R15 (prd FR-15): 层级浏览：用户可以在浏览视图中按位置层级导航查看物品。
- R16 (prd FR-16): 物品删除：用户可以在物品详情页删除物品记录。
- R17 (prd FR-17): 用户注册：管理员通过后台管理界面创建家庭成员账户。不提供面向最终用户的自助注册功能（后续版本也不做）。
- R18 (prd FR-18): 用户登录：已注册用户可以通过用户名和密码登录系统。
- R19 (prd FR-19): 登录保护：所有物品相关页面（浏览、搜索、录入、详情）需要登录后才能访问。
- R20 (prd FR-20): 录入者标识：物品记录显示录入者信息，便于家人知道"这是谁收的"。
- R21 (sprint-change-proposal-2026-05-28 FR-21): 用户可以在物品详情页编辑已录入物品的名称、位置、标签和备注。
- R22a (sprint-change-proposal-2026-05-28-security FR-22a): 密码规则升级：系统必须强制执行以下密码规则：
- R22b (sprint-change-proposal-2026-05-28-security FR-22b): 邮箱验证码：系统提供邮箱验证码作为备选 2FA 方式。
- R22c (sprint-change-proposal-2026-05-28-security FR-22c): WebAuthn/Passkey 支持：WebAuthn 作为推荐的 2FA 方式（需要 HTTPS + 域名）。
- R22d (sprint-change-proposal-2026-05-28-security FR-22d): 恢复码：用户设置 2FA 后生成一次性恢复码，用于 2FA 设备丢失时的账户恢复。
- R22e (sprint-change-proposal-2026-05-28-security FR-22e): 速率限制与暴力破解防护：Consequences (testable):
- R22f (sprint-change-proposal-2026-05-28-security FR-22f): 管理员 2FA 管理：Consequences (testable):
- R22g (sprint-change-proposal-2026-05-28-security FR-22g): 管理员 2FA CLI 恢复：管理员丢失 2FA 设备时，可通过服务器本地 CLI 强制重置。

## Constraints

- 家庭内部使用，用户数不超过 5；部署目标为 1C1G Linux VPS。
- 所有家庭成员共享同一物品库，v1 不做权限细分。
- 不做自助注册；账户由管理员创建。
- 不做推送、搬家、资产、断舍离、平面图或统计面板。
- 使用 SQLite、ASP.NET Core Identity、Blazor WASM 与 HTTPS。

## Done when

- SM-1：搬家或换季整理后至少两个家庭成员实际使用。
- SM-2：需要找东西时，30 秒内定位到实物；非首次场景成功率 > 90%。
- 物品录入、搜索、浏览、位置/标签管理和账户认证在部署环境可用。

## Boundaries

- 不做搬家工具、资产管理、断舍离助手、社交平台、推送、平面图或统计面板。
- 不做物品编辑之外的 v2 功能扩张；历史 Story 不重新验收。

## References

- prd — _bmad-output/initiative-boxwise/prd-BoxWise-2026-05-21/prd-BoxWise-2026-05-21.md
- architecture — _bmad-output/initiative-boxwise/architecture-boxwise/architecture-boxwise.md
- design — _bmad-output/initiative-boxwise/ux-design-specification/ux-design-specification.md

## Notes

- Action item: 恢复标准 Story 文件格式（# Story N.M: 标题）——Epic 15 采用任务式计划文件导致解析器无法解析、状态需手动补录；owner Elvis，epic 15。
- Action item: 执行 epic-15-manual-test-guide.md 的 7 组手动测试场景并记录结果；owner Elvis，epic 15。
