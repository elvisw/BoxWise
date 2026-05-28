namespace BoxWise.Shared.Dtos;

public record TwoFactorSetupResponse(string SecretKey, string QrCodeUri);
