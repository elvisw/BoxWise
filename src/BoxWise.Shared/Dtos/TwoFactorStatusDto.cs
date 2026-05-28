namespace BoxWise.Shared.Dtos;

public record TwoFactorStatusDto(
    bool TwoFactorEnabled,
    string? TwoFactorMethod,  // "TOTP", "Email", "WebAuthn"
    List<string> AvailableMethods,  // 根据配置动态返回
    bool HasRecoveryCodes,
    DateTime? GracePeriodEnd,
    DateTime? SetupCompletedAt
);
