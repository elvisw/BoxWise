namespace BoxWise.Shared.Dtos;

public record AuthUserDto(string UserName, bool IsAdmin, bool PasswordManagedByEnv = false, bool PasswordRequiresChange = false);
