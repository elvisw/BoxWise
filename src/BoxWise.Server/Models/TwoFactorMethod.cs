namespace BoxWise.Server.Models;

/// <summary>
/// 2FA 方法 [Flags] 枚举。值变更需同步数据迁移。
/// WebAuthn 从 3 改为 4 以避免破坏 TOTP|Email 组合（011→100）。
/// WebAuthn 已于 Story 8-3 部署，由 WebAuthnEndpoints.cs 管理其标志位。
/// </summary>
[Flags]
public enum TwoFactorMethod
{
    None = 0,
    TOTP = 1,
    Email = 2,
    WebAuthn = 4  // 前向预留，对应逻辑在 8-3 实现。从 3 改为 4 以保护 [Flags] 组合。
}
