namespace BoxWise.Shared.Dtos;

public record WebAuthnAvailableResponse(bool Available, string Origin, string? UserHandle = null);
