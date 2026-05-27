using Microsoft.AspNetCore.Identity;
using BoxWise.Server.Models;

namespace BoxWise.Server.Tests;

public class AuthEndpointsTests
{
    [Fact]
    public async Task UpdateProfile_ValidUsername_Succeeds()
    {
        await using var ctx = await TestIdentityFactory.CreateAsync();
        var user = await CreateUserAsync(ctx, "alice", "pass1234");

        var result = await ctx.UserManager.SetUserNameAsync(user, "alice_new");

        Assert.True(result.Succeeded);
        Assert.Equal("alice_new", user.UserName);
        Assert.Equal("ALICE_NEW", user.NormalizedUserName);
    }

    [Fact]
    public async Task UpdateProfile_DuplicateUsername_Fails()
    {
        await using var ctx = await TestIdentityFactory.CreateAsync();
        await CreateUserAsync(ctx, "bob", "pass1234");
        var alice = await CreateUserAsync(ctx, "alice", "pass1234");

        alice.UserName = "bob";
        var result = await ctx.UserManager.UpdateAsync(alice);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task UpdateProfile_SameName_Succeeds()
    {
        await using var ctx = await TestIdentityFactory.CreateAsync();
        var user = await CreateUserAsync(ctx, "alice", "pass1234");

        var result = await ctx.UserManager.SetUserNameAsync(user, "alice");

        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task ChangePassword_CorrectCurrent_Succeeds()
    {
        await using var ctx = await TestIdentityFactory.CreateAsync();
        var user = await CreateUserAsync(ctx, "alice", "pass1234");

        var result = await ctx.UserManager.ChangePasswordAsync(user, "pass1234", "newpass1");

        Assert.True(result.Succeeded);
        var check = await ctx.UserManager.CheckPasswordAsync(user, "newpass1");
        Assert.True(check);
    }

    [Fact]
    public async Task ChangePassword_WrongCurrent_Fails()
    {
        await using var ctx = await TestIdentityFactory.CreateAsync();
        var user = await CreateUserAsync(ctx, "alice", "pass1234");

        var result = await ctx.UserManager.ChangePasswordAsync(user, "wrongpass", "newpass1");

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task ChangePassword_TooShort_Fails()
    {
        await using var ctx = await TestIdentityFactory.CreateAsync();
        var user = await CreateUserAsync(ctx, "alice", "pass1234");

        var result = await ctx.UserManager.ChangePasswordAsync(user, "pass1234", "ab");

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task AdminResetPassword_UpdatesSecurityStamp()
    {
        await using var ctx = await TestIdentityFactory.CreateAsync();
        var user = await CreateUserAsync(ctx, "user1", "oldpass1");
        var oldStamp = user.SecurityStamp;

        var token = await ctx.UserManager.GeneratePasswordResetTokenAsync(user);
        await ctx.UserManager.ResetPasswordAsync(user, token, "newpass2");
        await ctx.UserManager.UpdateSecurityStampAsync(user);

        var updated = await ctx.UserManager.FindByIdAsync(user.Id);
        Assert.NotNull(updated);
        Assert.NotEqual(oldStamp, updated!.SecurityStamp);
        var check = await ctx.UserManager.CheckPasswordAsync(updated, "newpass2");
        Assert.True(check);
    }

    private static async Task<AppUser> CreateUserAsync(TestIdentityContext ctx, string name, string password)
    {
        var user = new AppUser { UserName = name };
        await ctx.UserManager.CreateAsync(user, password);
        return user;
    }
}
