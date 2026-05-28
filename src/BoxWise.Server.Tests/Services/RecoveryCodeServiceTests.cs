using BoxWise.Server.Data;
using BoxWise.Server.Models;
using BoxWise.Server.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BoxWise.Server.Tests.Services;

public class RecoveryCodeServiceTests
{
    private static AppDbContext CreateDb() => TestDbContextFactory.Create();

    // ── Static GenerateRecoveryCodes tests ───────────────────────────────

    [Fact]
    public void GenerateRecoveryCodes_ReturnsEightCodes()
    {
        var codes = RecoveryCodeService.GenerateRecoveryCodes();

        Assert.Equal(8, codes.Count);
    }

    [Fact]
    public void GenerateRecoveryCodes_CodesAreUnique()
    {
        var codes = RecoveryCodeService.GenerateRecoveryCodes();

        Assert.Equal(8, codes.Distinct().Count());
    }

    [Fact]
    public void GenerateRecoveryCodes_CodeLength()
    {
        var codes = RecoveryCodeService.GenerateRecoveryCodes();

        Assert.All(codes, c => Assert.Equal(10, c.Length));
    }

    // ── Indirect HashCode tests via Store+Verify flow ────────────────────

    [Fact]
    public async Task HashCode_SameInput_SameOutput()
    {
        await using var ctx = await TestIdentityFactory.CreateAsync();
        var db = ctx.Scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var service = new RecoveryCodeService(db);
        var user = new AppUser { UserName = "same-input@example.com" };
        await ctx.UserManager.CreateAsync(user);

        var codes = new List<string> { "test" };
        await service.StoreRecoveryCodesAsync(user, codes);

        var result = await service.VerifyRecoveryCodeAsync(user, "test", ctx.UserManager);

        Assert.True(result);
    }

    [Fact]
    public async Task HashCode_DifferentInput_DifferentOutput()
    {
        await using var ctx = await TestIdentityFactory.CreateAsync();
        var db = ctx.Scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var service = new RecoveryCodeService(db);
        var user = new AppUser { UserName = "diff-input@example.com" };
        await ctx.UserManager.CreateAsync(user);

        var codes = new List<string> { "a" };
        await service.StoreRecoveryCodesAsync(user, codes);

        var result = await service.VerifyRecoveryCodeAsync(user, "b", ctx.UserManager);

        Assert.False(result);
    }

    // ── Store & Verify tests ─────────────────────────────────────────────

    [Fact]
    public async Task StoreAndVerify_Success()
    {
        await using var ctx = await TestIdentityFactory.CreateAsync();
        var db = ctx.Scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var service = new RecoveryCodeService(db);
        var user = new AppUser { UserName = "store-verify@example.com" };
        await ctx.UserManager.CreateAsync(user);

        var codes = RecoveryCodeService.GenerateRecoveryCodes();
        await service.StoreRecoveryCodesAsync(user, codes);

        var result = await service.VerifyRecoveryCodeAsync(user, codes[0], ctx.UserManager);

        Assert.True(result);
    }

    [Fact]
    public async Task Verify_WrongCode_Fails()
    {
        await using var ctx = await TestIdentityFactory.CreateAsync();
        var db = ctx.Scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var service = new RecoveryCodeService(db);
        var user = new AppUser { UserName = "wrong-code@example.com" };
        await ctx.UserManager.CreateAsync(user);

        var codes = RecoveryCodeService.GenerateRecoveryCodes();
        await service.StoreRecoveryCodesAsync(user, codes);

        // Try verifying with a code NOT in the stored set
        var result = await service.VerifyRecoveryCodeAsync(user, "AAAAAAAAAA", ctx.UserManager);

        Assert.False(result);
    }

    [Fact]
    public async Task Verify_UsedCodeDeleted()
    {
        await using var ctx = await TestIdentityFactory.CreateAsync();
        var db = ctx.Scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var service = new RecoveryCodeService(db);
        var user = new AppUser { UserName = "used-deleted@example.com" };
        await ctx.UserManager.CreateAsync(user);

        var codes = RecoveryCodeService.GenerateRecoveryCodes();
        await service.StoreRecoveryCodesAsync(user, codes);

        // Verify codes exist before verification
        Assert.Equal(8, await db.RecoveryCodes.CountAsync());

        // Successful verification
        var result = await service.VerifyRecoveryCodeAsync(user, codes[0], ctx.UserManager);
        Assert.True(result);

        // All recovery codes should be deleted after successful use
        Assert.Equal(0, await db.RecoveryCodes.CountAsync());

        // 2FA settings cleared on the user object
        Assert.False(user.TwoFactorEnabled);
        Assert.Equal(TwoFactorMethod.None, user.TwoFactorMethod);
        Assert.Null(user.TotpSecretKey);
        Assert.Null(user.EmailForTwoFactor);
        Assert.Null(user.TwoFactorSetupCompletedAt);
        Assert.NotNull(user.TwoFactorGracePeriodUntil);
    }

    [Fact]
    public async Task RegenerateRecoveryCodes_OldCodesInvalid()
    {
        await using var ctx = await TestIdentityFactory.CreateAsync();
        var db = ctx.Scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var service = new RecoveryCodeService(db);
        var user = new AppUser { UserName = "regenerate@example.com" };
        await ctx.UserManager.CreateAsync(user);

        var originalCodes = RecoveryCodeService.GenerateRecoveryCodes();
        await service.StoreRecoveryCodesAsync(user, originalCodes);

        // Regenerate (old codes are cleared from DB)
        var newCodes = await service.RegenerateRecoveryCodesAsync(user);

        // Old codes should no longer work
        var oldResult = await service.VerifyRecoveryCodeAsync(user, originalCodes[0], ctx.UserManager);
        Assert.False(oldResult);

        // New codes are stored and should work (verifying succeeds)
        Assert.NotEmpty(newCodes);
        Assert.Equal(8, newCodes.Count);

        // New codes should still be valid (verify by checking HasRecoveryCodesAsync)
        var hasCodes = await service.HasRecoveryCodesAsync(user);
        Assert.True(hasCodes);
    }
}
