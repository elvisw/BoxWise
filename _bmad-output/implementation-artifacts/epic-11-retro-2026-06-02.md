---
epic: 11
title: Identity 脚手架混合模式迁移 — 前端适配 + 退役 + 文档更新
date: 2026-06-02
stories_completed: [11-1, 11-2, 11-3, 11-4]
stories_remaining: []
---

# Epic 11 回顾

## 完成情况

| Story | 标题 | 状态 |
|-------|------|:--:|
| 11.1 | Settings.razor 替换为跳转链接 | ✅ done |
| 11.2 | Login.razor 通行密钥专用页面 | ✅ done |
| 11.3 | 退役旧代码 + 测试更新 | ✅ done |
| 11.4 | SameSite 策略 + Architecture/UX 文档更新 | ✅ done |

## 成功经验

1. **6 代理审核机制** — Story 11.3 创建过程中 3+3 代理并行审核发现并修复了 5 个 P0 问题（UpdateProfileAsync 零调用方、RecoveryCodeService 不可删除、Settings.razor GetTwoFactorStatusAsync 冲突等），确保了退役清单的准确性。
2. **Spec 驱动的退役清单** — `decommission-checklist.md` 提供了精确的退役/保留边界，避免了过度删除。
3. **预留标记模式** — Story 11.1/11.2 中标记"Story 11.3 处理"的代码（如 LoginModel 延期清理）有效避免了跨 Story git 冲突。
4. **退役顺序依赖** — 常量提取 → 文件删除 → 方法退役 → 测试清理的严格顺序防止了编译失败。

## 已知限制

### Email 2FA 登录路径不可用

**影响：** 仅配置了 Email 2FA（ConfiguredMethods=Email, TwoFactorEnabled=true）的用户无法通过 Identity 登录页面完成登录。

**根因：**
- Identity 脚手架 `LoginWith2fa.cshtml.cs` 仅处理 TOTP 验证（`TwoFactorAuthenticatorSignInAsync`）
- Identity 不提供 `SendCode.cshtml` 用于发送 Email 验证码
- Story 11.2 移除了 Login.razor 中的 Email 2FA 客户端 UI
- Story 11.3 退役了 Server 端 Email 2FA 登录端点（ChallengeAsync/SendChallengeCodeAsync/VerifyAsync）

**当前状态：** 无用户受到影响——BoxWise 用户均使用 TOTP 或通行密钥。Email 2FA 部署路径不存在。

**解决方案：** 如需支持 Email 2FA 登录，需自定义 Identity 脚手架页面（`SendCode.cshtml` + 修改 `LoginWith2fa.cshtml.cs` 支持 Email）。记录为技术债务，待需求出现时再处理。

### ConfiguredMethods 与 Identity 页面同步

**影响：** Identity 的 EnableAuthenticator/Disable2fa/ResetAuthenticator 页面操作 Identity 内置 `TwoFactorEnabled` 标志，但不更新自定义字段 `ConfiguredMethods`。
- 通过 Identity 启用 TOTP 的用户在 Admin 面板显示 ConfiguredMethods=None
- 禁用 2FA 后 ConfiguredMethods 不清零

**严重度：** 低。`GetTwoFactorStatusAsync`（Admin 后台使用）依赖该字段。用户端的"管理账户设置"按钮已跳转到 Identity Manage 页面。

**建议：** 在 Identity 脚手架页面的 PageModel 中添加 `ConfiguredMethods` 同步逻辑（Epic 10 预存问题）。

## 退役统计

| 指标 | 值 |
|------|:--|
| DELETE 文件 | 15 |
| MODIFY 文件 | 6 |
| NEW 文件 | 1 |
| 退役代码行数 | ~1600 |
| 退役 Rate Limit 策略 | 5 |
| AuthEndpoints.cs 精简 | 414→37 行 |
| AuthService.cs 精简 | 609→144 行 |
| 测试通过数 | 261（29 Client + 232 Server） |

## 经验教训

1. **规划文档与实际代码的偏差** — epics 文件中标记 `UpdateProfileAsync` 为"保留"，但实际代码中零调用方。审计代理纠正了此错误。
2. **Rate Limit 策略的全量清理** — epics 仅计划清理 2 个策略，审计发现了额外 3 个死策略。
3. **Client 组件退役需验证整条调用链** — AccountInfoDialog → AuthService methods → Server endpoints 三层均需退役，缺一不可。
4. **测试文件的反射依赖** — `TwoFactorTestHelpers.cs` 通过反射调用 `TwoFactorEndpoints` 私有方法，源文件删除后测试文件必须同步删除。
