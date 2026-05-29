namespace BoxWise.Shared.Dtos;

public record LoginResponse(
    string? Username,
    bool? IsAdmin,
    bool? IsSpecificAdmin,
    bool PasswordRequiresChange,
    bool RequiresTwoFactor,
    string? Email = null
);
