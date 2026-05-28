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
        "Qwerty123", "1234567890", "password1", "welcome1", "sunshine",
        "football", "baseball", "abc12345", "qwertyui", "trustno1",
        "princess", "12345678910", "superman", "qazwsxedc", "michael",
        "starwars", "shadow1", "12121212", "69696969", "hunter2",
        "batman1", "summer1", "access1", "zxcvbnm1", "charlie",
        "donald1", "flower1", "hottie1", "lovely1", "ranger1",
        "thomas1", "george1", "soccer1", "arsenal", "liverpool",
        "chelsea", "manchester", "banana1", "monkey1", "cheese1",
        "pepper1", "muffin1", "cookie1", "ginger1", "jordan1",
        "buster1", "tigger1", "peanut1", "snoopy1", "charlie1",
        "friends", "hello123", "chocolate", "internet", "computer",
        "samsung", "android", "iphone5", "windows", "linkedin",
        "facebook", "twitter", "youtube", "pokemon", "nintendo",
        "playstation", "xbox360", "callofduty", "minecraft", "fortnite",
        "destiny2", "halo123", "skyrim", "assassin", "fifa2019",
        "ncc1701", "startrek", "starwars1", "matrix1", "merlin1",
        "qwerty12345", "password1234", "admin2019", "letmein1", "welcome2020",
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
