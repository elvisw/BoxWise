using Microsoft.AspNetCore.Identity;

namespace BoxWise.Server.Models;

public class AppUser : IdentityUser
{
    public TwoFactorMethod TwoFactorMethod { get; set; } = TwoFactorMethod.None;
    public string? TotpSecretKey { get; set; }
    // 预留: Email 2FA (Story 8-2b)
    public string? EmailForTwoFactor { get; set; }
    public DateTime? TwoFactorSetupCompletedAt { get; set; }
    public DateTime? TwoFactorGracePeriodUntil { get; set; }
    public ICollection<RecoveryCode> RecoveryCodes { get; set; } = new List<RecoveryCode>();
}
