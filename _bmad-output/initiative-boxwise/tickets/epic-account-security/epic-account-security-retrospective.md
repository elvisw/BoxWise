# Epic 8 回顾 — 账户安全加固

**日期:** 2026-05-29
**Epic:** 8: 账户安全加固
**状态:** 全部 9 个 Story 完成

---

## 交付摘要

| 指标 | 数值 |
|------|------|
| Story 完成 | 9 / 9 |
| Phase | 3 (P1: 3路并行, P2: 关键路径+3路并行, P3: 2路并行) |
| 提交 | `290e19c`, `395441b`, `51ebd41` |
| 文件变更 | 70+ |
| 测试 | 205 / 205 通过 |
| 构建 | 0 警告, 0 错误 |
| 审查轮次 | 7 轮 (v1-v5 提案 + Phase 1 + Phase 2) |
| 审查发现 | 48 条 (6 阻塞 + 17 重要 + 25 建议)，全部修复 |

## 策略成果

**安全加固优先级：** 2FA > 速率限制 > 密码规则

**实施的安全能力：**

| 能力 | 实现 |
|------|------|
| 密码规则 | 8位 + 禁纯数字 + 拒绝 top 20 常见密码 |
| TOTP 2FA | Otp.NET，Data Protection 加密密钥，QR 码设置向导 |
| 邮箱验证码 | MailKit SMTP，Data Protection 自包含令牌（零存储） |
| 恢复码 | SHA-256 哈希，首次使用即全部清除 |
| WebAuthn | fido2-net-lib v4，localhost/域名双模式 |
| 速率限制 | ASP.NET Core Rate Limiting（5 策略）+ fail2ban 模板 |
| CSRF | X-Requested-With header 验证 |
| 会话安全 | 两阶段登录 + amr claim + SecurityStamp 刷新 |
| 管理员 2FA 管理 | Admin 面板 + CLI 重置工具 + 审计日志 |

## 成功点

1. **多轮审查机制** — 提案 5 轮 + 代码 2 轮，从架构到 API 签名到配置键名逐步收敛
2. **并行 Agent 实施** — Phase 1/2 的并行策略有效加速交付
3. **find-docs 技能** — 在 Fido2 v4 API 不熟悉时快速找到正确文档
4. **Data Protection 自包含令牌** — SessionToken 和邮箱验证码统一零存储方案，简洁可靠
5. **fido2-net-lib v4 参数对象 API** — 通过 ctx7 docs 找到正确的 RequestNewCredentialParams 等封装类

## 教训

1. **WebAuthn 复杂度被低估** — fido2-net-lib v4 API 与 v3 差异大，Agent 首次实施未创建文件
2. **并行修改冲突** — Program.cs/AppUser.cs 被多 Agent 同时修改，需手动整合
3. **NuGet 兼容性验证** — Fido2 仅 net8.0 target，虽兼容但 API 需要实际查文档确认

## 后续建议

- Epic 9 尚未规划，可考虑的方向：物品分享/导出、批量操作、通知提醒
- 部署前验证：SMTP 配置、WebAuthn origin 配置、fail2ban 部署
