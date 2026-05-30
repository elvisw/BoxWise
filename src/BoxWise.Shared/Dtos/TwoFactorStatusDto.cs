namespace BoxWise.Shared.Dtos;

public record TwoFactorStatusDto(
    bool TwoFactorEnabled,
    string? TwoFactorMethod,  // "TOTP", "Email", "WebAuthn" — 保留向后兼容
    List<string> AvailableMethods,  // 可用于设置的方法（SMTP 配置时含 Email）
    List<string> ConfiguredMethods,  // 用户已配置的方法列表，如 ["TOTP", "Email"]
    bool HasRecoveryCodes,
    DateTime? GracePeriodEnd,
    DateTime? SetupCompletedAt
);
