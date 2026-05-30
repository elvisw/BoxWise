namespace BoxWise.Shared.Dtos;

public record LoginResponse(
    string? Username,
    bool? IsAdmin,
    bool? IsSpecificAdmin,
    bool PasswordRequiresChange,
    bool RequiresTwoFactor,
    bool RequiresTwoFactorSetup = false,
    string? Email = null
);
