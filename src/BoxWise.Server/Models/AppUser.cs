using Microsoft.AspNetCore.Identity;

namespace BoxWise.Server.Models;

public class AppUser : IdentityUser
{
    /// <summary>
    /// 已配置的 2FA 方法集合（[Flags] 枚举，支持多方法并存）。
    /// </summary>
    public TwoFactorMethod ConfiguredMethods { get; set; } = TwoFactorMethod.None;
    public string? TotpSecretKey { get; set; }
    /// <summary>
    /// 暂存的新 TOTP 密钥（TOTP 修改流程中使用）。
    /// 用户扫描新 QR 码后，verify 前暂存于此，verify 成功时覆盖 TotpSecretKey。
    /// </summary>
    public string? PendingTotpSecretKey { get; set; }
    // EmailForTwoFactor 与 user.Email 通过 AuthEndpoints.UpdateProfileAsync 保持同步（原子更新）。
    // 登录阶段 2FA 验证码优先读取 user.Email，fallback EmailForTwoFactor（向后兼容）。
    // 部署迁移脚本同步已有的 Email ≠ EmailForTwoFactor 分歧数据。
    public string? EmailForTwoFactor { get; set; }

    /// <summary>
    /// 计算属性：优先返回 Email，回退到 EmailForTwoFactor（向后兼容）。
    /// 消除 !string.IsNullOrEmpty(Email) ? Email : EmailForTwoFactor 模式的重复。
    /// </summary>
    public string? EffectiveEmailForTwoFactor => !string.IsNullOrEmpty(Email) ? Email : EmailForTwoFactor;
    public DateTime? TwoFactorSetupCompletedAt { get; set; }
    public DateTime? TwoFactorGracePeriodUntil { get; set; }
    public ICollection<RecoveryCode> RecoveryCodes { get; set; } = new List<RecoveryCode>();
}
