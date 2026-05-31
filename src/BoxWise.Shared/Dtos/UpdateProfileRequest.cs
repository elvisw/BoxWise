namespace BoxWise.Shared.Dtos;

public record UpdateProfileRequest(string NewUsername, string? NewEmail = null, string? OperationToken = null);
