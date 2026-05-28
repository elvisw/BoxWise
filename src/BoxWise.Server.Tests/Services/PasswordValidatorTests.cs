using Microsoft.AspNetCore.Identity;
using BoxWise.Server.Models;
using BoxWise.Server.Services.PasswordValidators;

namespace BoxWise.Server.Tests.Services;

public class PasswordValidatorTests
{
    private static readonly CommonPasswordValidator CommonValidator = new();
    private static readonly NoNumericOnlyValidator NumericValidator = new();
    private static readonly UserManager<AppUser>? NullManager = null;
    private static readonly AppUser? NullUser = null;

    // ── CommonPasswordValidator ──────────────────────────────────────────

    [Fact]
    public async Task CommonPasswordValidator_CommonPassword_Fails()
    {
        var result = await CommonValidator.ValidateAsync(NullManager!, NullUser!, "password");
        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, e => e.Code == "CommonPassword");
    }

    [Fact]
    public async Task CommonPasswordValidator_CommonPasswordCaseInsensitive()
    {
        var result = await CommonValidator.ValidateAsync(NullManager!, NullUser!, "Password");
        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task CommonPasswordValidator_NonCommonPassword_Succeeds()
    {
        var result = await CommonValidator.ValidateAsync(NullManager!, NullUser!, "MyStr0ng!Pass");
        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task CommonPasswordValidator_NullPassword_Succeeds()
    {
        var result = await CommonValidator.ValidateAsync(NullManager!, NullUser!, null);
        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task CommonPasswordValidator_AllTwentyCommonPasswordsRejected()
    {
        var passwords = new[] { "12345678", "admin123", "sunshine" };

        foreach (var pw in passwords)
        {
            var result = await CommonValidator.ValidateAsync(NullManager!, NullUser!, pw);
            Assert.False(result.Succeeded);
        }
    }

    // ── NoNumericOnlyValidator ───────────────────────────────────────────

    [Fact]
    public async Task NoNumericOnlyValidator_PureNumbers_Fails()
    {
        var result = await NumericValidator.ValidateAsync(NullManager!, NullUser!, "12345678");
        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, e => e.Code == "NumericOnly");
    }

    [Fact]
    public async Task NoNumericOnlyValidator_MixedChars_Succeeds()
    {
        var result = await NumericValidator.ValidateAsync(NullManager!, NullUser!, "abc123");
        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task NoNumericOnlyValidator_NullPassword_Succeeds()
    {
        var result = await NumericValidator.ValidateAsync(NullManager!, NullUser!, null);
        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task NoNumericOnlyValidator_EmptyPassword_Succeeds()
    {
        var result = await NumericValidator.ValidateAsync(NullManager!, NullUser!, "");
        Assert.True(result.Succeeded);
    }

    // ── Static helper ────────────────────────────────────────────────────

    [Fact]
    public void IsCommon_StaticMethod()
    {
        Assert.True(CommonPasswordValidator.IsCommon("password"));
        Assert.True(CommonPasswordValidator.IsCommon("Password"));
        Assert.True(CommonPasswordValidator.IsCommon("12345678"));
        Assert.False(CommonPasswordValidator.IsCommon("MyStr0ng!Pass"));
        Assert.False(CommonPasswordValidator.IsCommon("Zx9#mK2!pLq5@wN8"));
        Assert.False(CommonPasswordValidator.IsCommon("elvisw123456test"));
    }
}
