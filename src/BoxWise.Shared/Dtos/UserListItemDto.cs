namespace BoxWise.Shared.Dtos;

public record UserListItemDto(
    string Id,
    string UserName,
    bool IsAdmin,
    bool TwoFactorEnabled = false,
    string? TwoFactorMethod = null
);
