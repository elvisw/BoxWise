using Microsoft.AspNetCore.Identity;
using BoxWise.Server.Models;

namespace BoxWise.Server.Services.PasswordValidators;

public class CommonPasswordValidator : IPasswordValidator<AppUser>
{
    internal static readonly HashSet<string> CommonPasswords = new(StringComparer.OrdinalIgnoreCase)
    {
        "password", "12345678", "123456789", "qwerty123", "admin123",
        "password123", "iloveyou", "monkey", "dragon", "master",
        "11111111", "letmein", "123123123", "abcdefgh", "Password1",
        "Qwerty123", "1234567890", "password1", "welcome1", "sunshine"
    };

    public static bool IsCommon(string password) => CommonPasswords.Contains(password);

    public Task<IdentityResult> ValidateAsync(UserManager<AppUser> manager, AppUser user, string? password)
    {
        if (password is not null && CommonPasswords.Contains(password))
        {
            return Task.FromResult(IdentityResult.Failed(new IdentityError
            {
                Code = "CommonPassword",
                Description = "密码过于常见，请选择更复杂的密码。"
            }));
        }

        return Task.FromResult(IdentityResult.Success);
    }
}
