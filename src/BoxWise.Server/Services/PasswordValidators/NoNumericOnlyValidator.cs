using Microsoft.AspNetCore.Identity;
using BoxWise.Server.Models;

namespace BoxWise.Server.Services.PasswordValidators;

public class NoNumericOnlyValidator : IPasswordValidator<AppUser>
{
    public Task<IdentityResult> ValidateAsync(UserManager<AppUser> manager, AppUser user, string? password)
    {
        if (password is not null && password.Length > 0 && password.All(char.IsDigit))
        {
            return Task.FromResult(IdentityResult.Failed(new IdentityError
            {
                Code = "NumericOnly",
                Description = "密码不能为纯数字。"
            }));
        }

        return Task.FromResult(IdentityResult.Success);
    }
}
