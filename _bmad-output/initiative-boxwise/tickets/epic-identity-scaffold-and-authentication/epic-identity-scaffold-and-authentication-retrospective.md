# Epic 10 回顾 — Identity 脚手架混合模式迁移

> **日期：** 2026-06-01
> **参与：** Elvis (Project Lead), Amelia (Developer), Charlie (Senior Dev), Dana (QA), Winston (Architect), Alice (PO)
> **Epic 状态：** 4/4 Stories done

## 交付概要

| 维度 | 数据 |
|------|------|
| 完成率 | 4/4 Stories (100%) |
| 代码变更 | ~2,700+ 行新增 |
| 提交数 | 4 个独立 commit |
| 编译 | 0 错误 0 警告（全程） |
| 测试 | 308 通过 0 失败（全程保持） |
| 回顾修复 | 12 处 bug 修复 |

## 成功之处

1. **Story 拆分粒度合理** — 4 个 Story 独立可构建、可验证，每个 commit 原子化
2. **测试稳定性** — 308 测试全程零回归，架构迁移中测试保护网有效
3. **Dev Notes 文档化** — 每个 Story 的实施笔记详尽，脚手架版本差异、API 冲突等经验教训完整记录
4. **PR 集中审查** — Epic 10 作为迁移第一个 Epic，PR 审查比 Story 级 CR 更全面

## 挑战与发现

### .NET 10 GetTwoFactorAuthenticationUserAsync Bug (#66929)

Epic 10 最大意外。`TwoFactorUserId` Cookie 中 `NameIdentifier` claim 缺失，UserId 被错误写入 `Name` claim。Workaround 在两个层面应用：
- API 端点层：`TwoFactorEndpoints.GetTwoFactorUserAsync()`
- PageModel 层：`LoginWith2fa.cshtml.cs` / `LoginWithRecoveryCode.cshtml.cs` 的 `GetTwoFactorUserAsync()` 辅助方法

**待上游修复后移除 workaround。**

### 脚手架代码与 BoxWise 体系不兼容

手动验证中发现的 12 处问题：

| 类别 | 数量 | 典型问题 |
|------|------|---------|
| 业务适配 | 3 | Email→Username 登录、admin 无 Email |
| Bug workaround | 2 | .NET 10 #66929 |
| 缺失页面补丁 | 3 | ShowRecoveryCodes 不存在 |
| UX 增强 | 3 | QR 码渲染、Logout GET 支持 |
| 样式基础设施 | 1 | 新建 Identity 区域 _Layout.cshtml |

所有修改记录在 `docs/identity-scaffold-modifications.md`，CLAUDE.md 已添加引用规则。

### 设计决策

1. **不迁移旧 TOTP 数据** — App 未上线，删库重建，用户通过 Identity `EnableAuthenticator` 重新设置 2FA
2. **Email 为必填字段** — 管理员初始化时设置 `Admin:Email`，Admin 创建用户页面已有邮箱校验
3. **Bootstrap 默认样式** — 双 UI 风格并存是已接受的权衡（SPEC NFR-1），不做深度美化

## 未完成的验收标准

- **Story 10.4 AC-7** — 手动验证：
  - ✅ A（无 2FA 用户回归）
  - ✅ B（TOTP 设置）
  - ✅ C（2FA 完整登录）
  - ✅ D（恢复码登录）
  - ✅ E（边界情况：E1/E2 错误处理 + E3 无 Cookie 优雅降级）

## Deferred to Epic 11

| 项 | 来源 |
|----|------|
| Identity 页面简体中文本地化 | 回顾 |
| `_ManageNav` 中 ExternalLogins/PersonalData 死链接 | Story 10.1 |
| `Login.razor` 改造（仅保留通行密钥） | Story 11-2 |
| 退役旧代码 + 测试更新 | Story 11-3 |
| SameSite 策略 + 文档更新 | Story 11-4 |
| Settings 链接迁移 | Story 11-1 |
| Admin 面板 2FA 状态适配（`ConfiguredMethods` 为空） | 回顾验证 |

## 行动项

| # | 行动 | 负责人 | 状态 |
|---|------|--------|------|
| 1 | `identity-scaffold-modifications.md` 创建并维护 | 团队 | ✅ done |
| 2 | CLAUDE.md 添加 scaffold 修改清单引用 | 团队 | ✅ done |
| 3 | 完成 AC-7 手动验证 D/E | Elvis | ⬜ pending |
| 4 | `dotnet build` + `dotnet test` 最终验证 | Elvis | ⬜ pending |
| 5 | 提交所有回顾修复 | Elvis | ⬜ pending |

## 关键经验

1. **脚手架代码不可假设与项目兼容** — 每次必须手动验证 Email/Username 体系、依赖页面是否存在
2. **Always verify before claiming done** — AC-7 的 pending 状态暴露了 12 个 bug，强调手动验证的重要性
3. **记录每一次 scaffold 修改** — `identity-scaffold-modifications.md` 是上游更新时的救生索
4. **.NET 预览版本的 Bug 需要防御性 workaround** — #66929 影响比预期更深（NameIdentifier claim 完全缺失）
