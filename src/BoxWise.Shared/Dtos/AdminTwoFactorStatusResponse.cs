namespace BoxWise.Shared.Dtos;

public record AdminTwoFactorStatusResponse(
    string UserName,
    TwoFactorStatusDto Status
);
