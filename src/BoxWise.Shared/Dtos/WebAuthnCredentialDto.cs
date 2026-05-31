namespace BoxWise.Shared.Dtos;

public record WebAuthnCredentialDto(int Id, string DeviceName, DateTime CreatedAt, string CredentialId);
