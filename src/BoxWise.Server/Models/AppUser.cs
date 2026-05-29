using Microsoft.AspNetCore.Identity;

namespace BoxWise.Server.Models;

public class AppUser : IdentityUser
{
    public TwoFactorMethod TwoFactorMethod { get; set; } = TwoFactorMethod.None;
    public string? TotpSecretKey { get; set; }
    // 预留: Email 2FA (Story 8-2b)
    // 已知限制：修改 Email 不会自动同步 EmailForTwoFactor。
    // 当用户通过 UpdateProfile 修改主邮箱后，2FA 邮箱需独立更新。
    // 这是跨 Story 关注点，当前用户量下风险可控。
    public string? EmailForTwoFactor { get; set; }
    public DateTime? TwoFactorSetupCompletedAt { get; set; }
    public DateTime? TwoFactorGracePeriodUntil { get; set; }
    public ICollection<RecoveryCode> RecoveryCodes { get; set; } = new List<RecoveryCode>();
}
