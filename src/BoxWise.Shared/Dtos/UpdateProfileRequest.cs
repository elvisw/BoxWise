namespace BoxWise.Shared.Dtos;

public record UpdateProfileRequest(string? NewUsername = null, string? NewEmail = null, string? OperationToken = null);
