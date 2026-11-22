# Epic 5 回顾 — 用户管理增强 + 品牌收尾

**日期:** 2026-05-27
**Epic:** 5 — 用户管理增强 + 品牌收尾
**状态:** 完成（3/3 Stories）

## 交付总结

| Story | 内容 | CR 发现 | 关键模式 |
|-------|------|---------|---------|
| 5.1 | Admin 用户管理（编辑/删除/角色/改密） | HIGH: NormalizedUserName + SecurityStamp | UserManager.SetUserNameAsync + 管理员自我保护 |
| 5.2 | 用户自助服务（改信息/改密码） | HIGH: async void 崩溃 + 对话框状态 | TypedResults.ValidationProblem + MudDialog |
| 5.3 | 品牌与版权（图标/Footer/关于页） | LOW: favicon.ico 缺失 + 版本号格式 | SVG favicon + OG 标签 + AssemblyInformationalVersion |

## 关键指标

- **构建:** 0 错误 0 警告
- **测试:** 58/58 通过（新增 15 个）
- **CR 发现:** 31 → 修复 15，延期 5，驳回 11
- **新增文件:** 22 个
- **修改文件:** 16 个

## 关键教训

### 1. Identity 内部机制不可忽视
`NormalizedUserName` 和 `SecurityStamp` 是 Identity 的内部约定。直接赋值 `user.UserName = newName` 而非使用 `SetUserNameAsync` 会导致登录失败。管理员改密后必须调用 `UpdateSecurityStampAsync`，否则旧会话仍然有效。

### 2. TDD 先行的价值
规格要求 5-8 个新测试，但测试在 CR 之后才补上。如果先写测试，CRITICAL 级别的 bug（NormalizedUserName）在开发阶段就能发现。

### 3. CR 三层审查互补
Blind Hunter 发现代码模式问题，Edge Case Hunter 发现边界条件（NormalizedUserName, SecurityStamp），Acceptance Auditor 发现遗漏（测试缺失）。三层各自覆盖了其他层看不到的盲区。

### 4. 轻量规格的局限性
变更提案 `sprint-change-proposal-2026-05-27.md` 作为指导文档发挥了作用，但 CR 发现无法写回 Story 文件（因为没有正式的 Story 文件）。正式走 `bmad-create-story` 流程可改善可追溯性。

### 5. ASP.NET Core Identity 内置 API 的高度完备性
Admin 用户管理的所有需求（编辑/删除/角色/密码重置）和用户自助服务（改信息/改密码）均由 `UserManager<T>` 内置 API 提供，无需额外存储过程或自定义逻辑。

## Epic 4 回顾承诺跟踪

| Epic 4 教训 | Epic 5 表现 |
|-------------|------------|
| MudBlazor 9.x API 兼容性 | ✅ 正确使用 `IMudDialogInstance` |
| CR 价值不可替代 | ✅ 三层审查，31 项发现 |
| 客户端生命周期陷阱 | ✅ 无新增生命周期问题 |
| Repository 统一抽象 | ✅ 直接使用 UserManager |
| 基础设施配置需审查 | N/A |

## 行动项

### 技术债务
1. **延期项跟进** (5 项): SVG 优化、GetCurrentUserAsync 语义、Unauthorized 语义、Logo 尺寸、TOCTOU
   - 负责人: Elvis | 优先级: 低
2. **Identity 知识沉淀**: CLAUDE.md 补充 `NormalizedUserName`、`SecurityStamp` 注意事项
   - 负责人: Elvis | 优先级: 中

### 流程改进
3. **TDD 先行**: 下个 Epic 先写测试再写实现
   - 负责人: Elvis
4. **正式 Story 流程**: 使用 `bmad-create-story`，CR 发现可回写
   - 负责人: Elvis

## 下一步

- 提交 Epic 5 + 品牌完善所有变更
- 打 v1.0.0 tag
- 决定下一步方向：维护 / 新功能 / 重构
