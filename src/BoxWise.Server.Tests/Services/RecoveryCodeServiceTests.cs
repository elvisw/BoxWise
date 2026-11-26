using BoxWise.Server.Data;
using BoxWise.Server.Models;
using BoxWise.Server.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace BoxWise.Server.Tests.Services;

/// <summary>
/// RecoveryCodeService 测试跑在真实 SQLite 上：清理依赖事务语义，
/// InMemory provider 不支持事务，无法验证回滚承诺。
/// </summary>
public class RecoveryCodeServiceTests
{
    // ── Static GenerateRecoveryCodes tests ───────────────────────────────

    [Fact]
    public void GenerateRecoveryCodes_ReturnsEightCodes()
    {
        Assert.Equal(8, RecoveryCodeService.GenerateRecoveryCodes().Count);
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
        Assert.All(RecoveryCodeService.GenerateRecoveryCodes(), c => Assert.Equal(10, c.Length));
    }

    // ── Store / Validate ────────────────────────────────────────────────

    [Fact]
    public async Task ValidateRecoveryCodeAsync_ValidCode_DoesNotConsume()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        await using var ctx = await TestIdentityFactory.CreateAsync(database);
        var service = new RecoveryCodeService(ctx.Db);
        var user = await CreateUserAsync(ctx, "validate-code");

        var codes = RecoveryCodeService.GenerateRecoveryCodes();
        await service.StoreRecoveryCodesAsync(user, codes);

        Assert.True(await service.ValidateRecoveryCodeAsync(user, codes[0]));
        Assert.True(await service.ValidateRecoveryCodeAsync(user, codes[0]));

        await using var fresh = database.CreateContext();
        Assert.Equal(8, await fresh.RecoveryCodes.CountAsync(rc => rc.UserId == user.Id));
    }

    [Fact]
    public async Task ValidateRecoveryCodeAsync_InvalidCode_ReturnsFalse()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        await using var ctx = await TestIdentityFactory.CreateAsync(database);
        var service = new RecoveryCodeService(ctx.Db);
        var user = await CreateUserAsync(ctx, "validate-invalid");

        await service.StoreRecoveryCodesAsync(user, RecoveryCodeService.GenerateRecoveryCodes());

        Assert.False(await service.ValidateRecoveryCodeAsync(user, "INVALIDCODE123"));
    }

    [Fact]
    public async Task RegenerateRecoveryCodes_OldCodesInvalid()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        await using var ctx = await TestIdentityFactory.CreateAsync(database);
        var service = new RecoveryCodeService(ctx.Db);
        var user = await CreateUserAsync(ctx, "regenerate");

        var original = RecoveryCodeService.GenerateRecoveryCodes();
        await service.StoreRecoveryCodesAsync(user, original);

        var replacement = await service.RegenerateRecoveryCodesAsync(user);

        Assert.Equal(8, replacement.Count);
        Assert.False(await service.ValidateRecoveryCodeAsync(user, original[0]));
        Assert.True(await service.ValidateRecoveryCodeAsync(user, replacement[0]));
    }

    // ── Commit paths (A1–A3) ────────────────────────────────────────────

    [Theory]
    [InlineData("  customcode1  ")]
    [InlineData("CUSTOMCODE1")]
    public async Task CustomRecoveryCode_IsNormalized_AndWins(string input)
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        await using var ctx = await TestIdentityFactory.CreateAsync(database);
        var service = new RecoveryCodeService(ctx.Db);
        var user = await SeedTwoFactorUserAsync(ctx, service, "normalize-code");

        var result = await service.DisableWithRecoveryCodeAsync(user, input, ctx.UserManager);

        Assert.True(result.Succeeded);
        Assert.True(result.OutcomeConfirmed);
    }

    [Fact]
    public async Task RecoveryCode_Commit_PersistsCleanupEverywhere()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        await using var ctx = await TestIdentityFactory.CreateAsync(database);
        var service = new RecoveryCodeService(ctx.Db);
        var user = await SeedTwoFactorUserAsync(ctx, service, "commit-cleanup");
        var stampBefore = user.SecurityStamp;

        var result = await service.DisableWithRecoveryCodeAsync(
            user, "CUSTOMCODE1", ctx.UserManager);

        Assert.True(result.Succeeded);

        await using var fresh = database.CreateContext();
        var persisted = await fresh.Users.SingleAsync(u => u.Id == user.Id);
        Assert.False(persisted.TwoFactorEnabled);
        Assert.Equal(TwoFactorMethod.None, persisted.ConfiguredMethods);
        Assert.Null(persisted.TotpSecretKey);
        Assert.Null(persisted.PendingTotpSecretKey);
        Assert.Null(persisted.EmailForTwoFactor);
        Assert.Null(persisted.TwoFactorSetupCompletedAt);
        Assert.NotNull(persisted.TwoFactorGracePeriodUntil);
        Assert.NotEqual(stampBefore, persisted.SecurityStamp);
        Assert.Equal(0, await fresh.RecoveryCodes.CountAsync(rc => rc.UserId == user.Id));
        Assert.Equal(0, await ctx.UserManager.CountRecoveryCodesAsync(persisted));
    }

    [Fact]
    public async Task SelfDisable_PreservesKeyPasskeyAndGrace_AndRotatesStamp()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        await using var ctx = await TestIdentityFactory.CreateAsync(database);
        var service = new RecoveryCodeService(ctx.Db);
        var grace = DateTime.UtcNow.AddHours(12);
        var user = await SeedTwoFactorUserAsync(ctx, service, "self-disable", grace);
        var stampBefore = user.SecurityStamp;
        var keyBefore = await ctx.UserManager.GetAuthenticatorKeyAsync(user);

        var result = await service.DisableTwoFactorAsync(user, ctx.UserManager);

        Assert.True(result.Succeeded);

        await using var fresh = database.CreateContext();
        var persisted = await fresh.Users.SingleAsync(u => u.Id == user.Id);
        Assert.False(persisted.TwoFactorEnabled);
        Assert.Equal(grace, persisted.TwoFactorGracePeriodUntil);
        Assert.NotEqual(stampBefore, persisted.SecurityStamp);
        Assert.Equal(keyBefore, await ctx.UserManager.GetAuthenticatorKeyAsync(persisted));
        Assert.Equal(1, await fresh.WebAuthnCredentials.CountAsync(wc => wc.UserId == user.Id));
        Assert.Equal(0, await fresh.RecoveryCodes.CountAsync(rc => rc.UserId == user.Id));
        Assert.Equal(0, await ctx.UserManager.CountRecoveryCodesAsync(persisted));
    }

    [Fact]
    public async Task AdminReset_RevokesPasskey_ClearsGrace_AndRotatesAuthenticatorKey()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        await using var ctx = await TestIdentityFactory.CreateAsync(database);
        var service = new RecoveryCodeService(ctx.Db);
        var user = await SeedTwoFactorUserAsync(
            ctx, service, "admin-reset", DateTime.UtcNow.AddHours(12));
        var keyBefore = await ctx.UserManager.GetAuthenticatorKeyAsync(user);

        var result = await service.ResetForAdminAsync(user, ctx.UserManager);

        Assert.True(result.Succeeded);

        await using var fresh = database.CreateContext();
        var persisted = await fresh.Users.SingleAsync(u => u.Id == user.Id);
        Assert.Null(persisted.TwoFactorGracePeriodUntil);
        Assert.Equal(0, await fresh.WebAuthnCredentials.CountAsync(wc => wc.UserId == user.Id));
        Assert.Equal(0, await fresh.RecoveryCodes.CountAsync(rc => rc.UserId == user.Id));
        Assert.Equal(0, await ctx.UserManager.CountRecoveryCodesAsync(persisted));

        // 旧验证器密钥不可再用，新密钥与旧值不同。
        var rotated = await ctx.UserManager.GetAuthenticatorKeyAsync(persisted);
        Assert.NotNull(rotated);
        Assert.NotEqual(keyBefore, rotated);
        var oldCode = new OtpNet.Totp(OtpNet.Base32Encoding.ToBytes(keyBefore!)).ComputeTotp();
        Assert.False(await ctx.UserManager.VerifyTwoFactorTokenAsync(
            persisted, ctx.UserManager.Options.Tokens.AuthenticatorTokenProvider, oldCode));
    }

    [Fact]
    public async Task AdminReset_WithoutAuthenticatorKey_DoesNotCreateOne()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        await using var ctx = await TestIdentityFactory.CreateAsync(database);
        var service = new RecoveryCodeService(ctx.Db);
        var user = await CreateUserAsync(ctx, "admin-reset-no-key");

        var result = await service.ResetForAdminAsync(user, ctx.UserManager);

        Assert.True(result.Succeeded);
        Assert.Null(await ctx.UserManager.GetAuthenticatorKeyAsync(user));
    }

    // ── Failure branches (A4) ───────────────────────────────────────────

    [Fact]
    public async Task InvalidCode_LeavesBothStoresAndStateUntouched()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        await using var ctx = await TestIdentityFactory.CreateAsync(database);
        var service = new RecoveryCodeService(ctx.Db);
        var user = await SeedTwoFactorUserAsync(ctx, service, "invalid-code");
        var stampBefore = user.SecurityStamp;

        // 该值在自定义表与 Identity token store 中都不存在 → 只有“未命中”这一分类。
        var result = await service.DisableWithRecoveryCodeAsync(
            user, "AAAAAAAAAA", ctx.UserManager);

        Assert.Equal(TwoFactorCleanupStatus.InvalidCode, result.Status);
        Assert.True(result.OutcomeConfirmed);

        await using var fresh = database.CreateContext();
        var persisted = await fresh.Users.SingleAsync(u => u.Id == user.Id);
        Assert.True(persisted.TwoFactorEnabled);
        Assert.Equal(stampBefore, persisted.SecurityStamp);
        Assert.Equal(2, await fresh.RecoveryCodes.CountAsync(rc => rc.UserId == user.Id));
        Assert.Equal(2, await ctx.UserManager.CountRecoveryCodesAsync(persisted));
    }

    [Fact]
    public async Task FailureAfterEarlierSave_RollsBack_AndCustomCodeStaysUsable()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();

        // 第一次保存（删除自定义码 + 关闭 2FA）已写入事务，第二次保存失败。
        var interceptor = new FailOnSaveInterceptor();
        await using (var ctx = await TestIdentityFactory.CreateAsync(database, interceptor))
        {
            var service = new RecoveryCodeService(ctx.Db);
            var user = await SeedTwoFactorUserAsync(ctx, service, "rollback-custom");
            interceptor.FailOnSave(2);

            var result = await service.DisableWithRecoveryCodeAsync(
                user, "CUSTOMCODE1", ctx.UserManager);

            Assert.Equal(TwoFactorCleanupStatus.StorageFailure, result.Status);
            Assert.True(result.OutcomeConfirmed);
            Assert.True(interceptor.CompletedSaves >= 1);
        }

        await using var verify = await TestIdentityFactory.CreateAsync(database);
        var persisted = await verify.Db.Users.SingleAsync(u => u.UserName == "rollback-custom");
        Assert.True(persisted.TwoFactorEnabled);
        Assert.Equal(2, await verify.Db.RecoveryCodes.CountAsync(rc => rc.UserId == persisted.Id));

        // 确认回滚后原始恢复码仍可用。
        var retry = await new RecoveryCodeService(verify.Db)
            .DisableWithRecoveryCodeAsync(persisted, "CUSTOMCODE1", verify.UserManager);
        Assert.True(retry.Succeeded);
    }

    [Fact]
    public async Task IdentityRedemptionFailure_RollsBackRedemption_AndCodeStaysUsable()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();

        // Identity 码先被消耗（保存 #1），随后清理（保存 #2）失败。
        var interceptor = new FailOnSaveInterceptor();
        string identityCode;
        await using (var ctx = await TestIdentityFactory.CreateAsync(database, interceptor))
        {
            var service = new RecoveryCodeService(ctx.Db);
            var user = await CreateUserAsync(ctx, "rollback-identity");
            user.TwoFactorEnabled = true;
            user.ConfiguredMethods = TwoFactorMethod.TOTP;
            await ctx.UserManager.UpdateAsync(user);
            await ctx.UserManager.ResetAuthenticatorKeyAsync(user);
            var codes = await ctx.UserManager.GenerateNewTwoFactorRecoveryCodesAsync(user, 2);
            identityCode = codes!.First();
            interceptor.FailOnSave(2);

            var result = await service.DisableWithRecoveryCodeAsync(
                user, identityCode, ctx.UserManager);

            Assert.Equal(TwoFactorCleanupStatus.StorageFailure, result.Status);
            Assert.True(result.OutcomeConfirmed);
            Assert.True(interceptor.CompletedSaves >= 1);
        }

        await using var verify = await TestIdentityFactory.CreateAsync(database);
        var persisted = await verify.Db.Users.SingleAsync(u => u.UserName == "rollback-identity");
        Assert.True(persisted.TwoFactorEnabled);
        Assert.Equal(2, await verify.UserManager.CountRecoveryCodesAsync(persisted));

        // 已消耗的 Identity 码随事务回滚恢复，仍可再次使用。
        var retry = await new RecoveryCodeService(verify.Db)
            .DisableWithRecoveryCodeAsync(persisted, identityCode, verify.UserManager);
        Assert.True(retry.Succeeded);
    }

    [Fact]
    public async Task StaleConcurrencyStamp_ReportsConflict_AndRollsBack()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();

        var interceptor = new StaleConcurrencyStampInterceptor();
        await using var ctx = await TestIdentityFactory.CreateAsync(database, interceptor);
        var service = new RecoveryCodeService(ctx.Db);
        var user = await SeedTwoFactorUserAsync(ctx, service, "conflict-cleanup");

        // 播种完成后才武装：让并发令牌冲突落在清理的 UPDATE 上。
        interceptor.Arm();

        var result = await service.DisableTwoFactorAsync(user, ctx.UserManager);

        Assert.Equal(TwoFactorCleanupStatus.Conflict, result.Status);
        Assert.True(result.OutcomeConfirmed);

        await using var fresh = database.CreateContext();
        var persisted = await fresh.Users.SingleAsync(u => u.Id == user.Id);
        Assert.True(persisted.TwoFactorEnabled);
        Assert.Equal(2, await fresh.RecoveryCodes.CountAsync(rc => rc.UserId == user.Id));
    }

    [Fact]
    public async Task TransactionStartLockFailure_ReportsConflict(){
        await using var database = await SqliteTestDatabase.CreateAsync();
        var interceptor = new FailOnTransactionStartInterceptor();
        await using var ctx = await TestIdentityFactory.CreateAsync(database, interceptor);
        var service = new RecoveryCodeService(ctx.Db);
        var user = await SeedTwoFactorUserAsync(ctx, service, "lock-start");

        interceptor.Arm();

        var result = await service.DisableTwoFactorAsync(user, ctx.UserManager);

        // 包装在 DbUpdateException 内的 SQLITE_BUSY 也属于冲突，不能降级为存储失败。
        Assert.Equal(TwoFactorCleanupStatus.Conflict, result.Status);
        Assert.True(result.OutcomeConfirmed);

        await using var fresh = database.CreateContext();
        var persisted = await fresh.Users.SingleAsync(u => u.Id == user.Id);
        Assert.True(persisted.TwoFactorEnabled);
        Assert.Equal(2, await fresh.RecoveryCodes.CountAsync(rc => rc.UserId == user.Id));
    }

    [Fact]
    public async Task CommitFailure_ReportsUnconfirmedOutcome()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var interceptor = new FailOnCommitInterceptor();
        await using var ctx = await TestIdentityFactory.CreateAsync(database, interceptor);
        var service = new RecoveryCodeService(ctx.Db);
        var user = await SeedTwoFactorUserAsync(ctx, service, "commit-unknown");

        interceptor.Arm();

        var result = await service.DisableTwoFactorAsync(user, ctx.UserManager);

        // 提交失败后无法确认最终状态：既不能报告成功，也不能断言清理未生效。
        Assert.Equal(TwoFactorCleanupStatus.StorageFailure, result.Status);
        Assert.False(result.OutcomeConfirmed);
        Assert.False(result.Succeeded);
    }

    // ── helpers ─────────────────────────────────────────────────────────

    private static async Task<AppUser> CreateUserAsync(TestIdentityContext ctx, string userName)
    {
        var user = new AppUser { UserName = userName, Email = $"{userName}@example.com" };
        await ctx.UserManager.CreateAsync(user, "Test1234!");
        return user;
    }

    /// <summary>
    /// 同时准备两套恢复码、Identity 验证器密钥、通行密钥和既有宽限期。
    /// </summary>
    private static async Task<AppUser> SeedTwoFactorUserAsync(
        TestIdentityContext ctx,
        RecoveryCodeService service,
        string userName,
        DateTime? gracePeriod = null)
    {
        var user = await CreateUserAsync(ctx, userName);
        user.TwoFactorEnabled = true;
        user.ConfiguredMethods = TwoFactorMethod.TOTP | TwoFactorMethod.WebAuthn;
        user.TotpSecretKey = "legacy-totp-secret";
        user.PendingTotpSecretKey = "legacy-pending-totp-secret";
        user.EmailForTwoFactor = $"{userName}@example.com";
        user.TwoFactorSetupCompletedAt = DateTime.UtcNow.AddDays(-1);
        user.TwoFactorGracePeriodUntil = gracePeriod;
        await ctx.UserManager.UpdateAsync(user);

        await ctx.UserManager.ResetAuthenticatorKeyAsync(user);
        await ctx.UserManager.GenerateNewTwoFactorRecoveryCodesAsync(user, 2);
        await service.StoreRecoveryCodesAsync(user, ["CUSTOMCODE1", "CUSTOMCODE2"]);

        ctx.Db.WebAuthnCredentials.Add(new WebAuthnCredential
        {
            UserId = user.Id,
            CredentialId = Convert.ToBase64String(Guid.NewGuid().ToByteArray()),
            PublicKey = Convert.ToBase64String(Guid.NewGuid().ToByteArray()),
            DeviceName = "测试通行密钥",
            CreatedAt = DateTime.UtcNow
        });
        await ctx.Db.SaveChangesAsync();

        return user;
    }
}
