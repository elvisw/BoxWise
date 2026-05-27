namespace BoxWise.Shared.Dtos;

public record ChangePasswordRequest(string CurrentPassword, string NewPassword);
