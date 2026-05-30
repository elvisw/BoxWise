namespace BoxWise.Shared.Dtos;

public record VerifyTwoFactorRequest(string Code, string? Token = null, string? Method = null);
