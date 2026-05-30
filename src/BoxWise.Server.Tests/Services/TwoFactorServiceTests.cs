using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.AspNetCore.DataProtection;
using BoxWise.Server.Data;
using BoxWise.Server.Models;
using BoxWise.Server.Services;
using BoxWise.Shared.Dtos;
using Moq;
using OtpNet;

namespace BoxWise.Server.Tests.Services;

public class TwoFactorServiceTests : IAsyncLifetime
{
    private TestIdentityContext _ctx = null!;
    private UserManager<AppUser> _userManager = null!;
    private TwoFactorService _twoFactorService = null!;
    private IDataProtector _totpProtector = null!;
    private Mock<ISmtpConfigurationService> _smtpConfigMock = null!;

    public async Task InitializeAsync()
    {
        _ctx = await TestIdentityFactory.CreateAsync();
        _userManager = _ctx.UserManager;

        _smtpConfigMock = new Mock<ISmtpConfigurationService>();
        _smtpConfigMock.Setup(x => x.IsConfigured()).Returns(false);
        _smtpConfigMock.Setup(x => x.GetSnapshot())
            .Returns(new SmtpConfigDto(string.Empty, 587, null, null, null, null));

        var dataProtection = _ctx.Provider.GetRequiredService<IDataProtectionProvider>();
        _totpProtector = dataProtection.CreateProtector("BoxWise.TwoFactor");
        var db = _ctx.Scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var cache = new MemoryCache(new MemoryCacheOptions());
        var emailTwoFactorService = new EmailTwoFactorService(
            dataProtection, _smtpConfigMock.Object, NullLogger<EmailTwoFactorService>.Instance);
        var recoveryService = new RecoveryCodeService(db);
        _twoFactorService = new TwoFactorService(
            _userManager, dataProtection, emailTwoFactorService, recoveryService, cache);
    }

    public async Task DisposeAsync() => await _ctx.DisposeAsync();

    // ────────────── SessionToken purpose parameterization ──────────────

    [Fact]
    public void GenerateSessionToken_DefaultPurpose_CanBeValidated()
    {
        var token = _twoFactorService.GenerateSessionToken("user1");

        Assert.True(_twoFactorService.ValidateSessionToken(token, "user1"));
        Assert.True(_twoFactorService.ValidateSessionToken(token, "user1", expectedPurpose: "2fa-setup"));
    }

    [Fact]
    public void GenerateSessionToken_ModifyPurpose_CanBeValidated()
    {
        var token = _twoFactorService.GenerateSessionToken("user1", purpose: "2fa-modify");

        Assert.False(_twoFactorService.ValidateSessionToken(token, "user1")); // defaults to "2fa-setup"
        Assert.True(_twoFactorService.ValidateSessionToken(token, "user1", expectedPurpose: "2fa-modify"));
    }

    [Fact]
    public void ValidateSessionToken_WrongPurpose_Fails()
    {
        var token = _twoFactorService.GenerateSessionToken("user1", purpose: "2fa-setup");

        Assert.False(_twoFactorService.ValidateSessionToken(token, "user1", expectedPurpose: "2fa-modify"));
    }

    [Fact]
    public void ValidateSessionToken_WrongUserId_Fails()
    {
        var token = _twoFactorService.GenerateSessionToken("user1");

        Assert.False(_twoFactorService.ValidateSessionToken(token, "wrong-user"));
    }

    [Fact]
    public void ValidateSessionToken_MalformedToken_Fails()
    {
        Assert.False(_twoFactorService.ValidateSessionToken("garbage-token", "user1"));
    }

    [Fact]
    public void ValidateSessionToken_BackwardCompatible_DefaultPurpose()
    {
        // Calling ValidateSessionToken without expectedPurpose defaults to "2fa-setup"
        var token = _twoFactorService.GenerateSessionToken("user1");

        Assert.True(_twoFactorService.ValidateSessionToken(token, "user1"));
        Assert.True(_twoFactorService.ValidateSessionToken(token, "user1", expectedPurpose: "2fa-setup"));
    }

    [Fact]
    public void GenerateSessionToken_DefaultPurpose_FiveMinExpiry()
    {
        // The token should be valid immediately after creation (within the 5-minute window)
        var token = _twoFactorService.GenerateSessionToken("user1");

        Assert.True(_twoFactorService.ValidateSessionToken(token, "user1"));
    }

    [Fact]
    public void GenerateSessionToken_ModifyPurpose_FifteenMinExpiry()
    {
        // The modify token should be valid immediately after creation (within the 15-minute window)
        var token = _twoFactorService.GenerateSessionToken("user1", purpose: "2fa-modify");

        Assert.True(_twoFactorService.ValidateSessionToken(token, "user1", expectedPurpose: "2fa-modify"));
    }

    // ────────────── GeneratePendingTotpSecretAsync ──────────────

    [Fact]
    public async Task GeneratePendingTotpSecretAsync_StoresInPendingNotTotp()
    {
        var user = new AppUser { UserName = "pending-storage" };
        await _userManager.CreateAsync(user, "Test1234!");

        // Set an existing TotpSecretKey (as if user already has TOTP configured)
        var existingKey = KeyGeneration.GenerateRandomKey(20);
        var existingBase32 = Base32Encoding.ToString(existingKey);
        user.TotpSecretKey = _totpProtector.Protect(existingBase32);
        await _userManager.UpdateAsync(user);

        // Generate pending secret
        var (secretKey, qrCodeUri) = await _twoFactorService.GeneratePendingTotpSecretAsync(user.Id);

        Assert.False(string.IsNullOrEmpty(secretKey));
        Assert.Contains("otpauth://totp/BoxWise:", qrCodeUri);

        // Reload user
        var reloaded = await _userManager.FindByIdAsync(user.Id);
        Assert.NotNull(reloaded);

        // PendingTotpSecretKey should be set
        Assert.NotNull(reloaded.PendingTotpSecretKey);
        Assert.NotEmpty(reloaded.PendingTotpSecretKey);

        // TotpSecretKey should be unchanged
        Assert.Equal(user.TotpSecretKey, reloaded.TotpSecretKey);
    }

    [Fact]
    public async Task GeneratePendingTotpSecretAsync_UserNotFound_ThrowsKeyNotFoundException()
    {
        var ex = await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            _twoFactorService.GeneratePendingTotpSecretAsync("nonexistent-id"));

        Assert.Equal("User not found.", ex.Message);
    }

    // ────────────── VerifyPendingTotpSetupAsync ──────────────

    [Fact]
    public async Task VerifyPendingTotpSetupAsync_NullUser_Fails()
    {
        var result = await _twoFactorService.VerifyPendingTotpSetupAsync(null!, "123456", "some-token");
        Assert.False(result);
    }

    [Fact]
    public async Task VerifyPendingTotpSetupAsync_NoPendingSecret_Fails()
    {
        var user = new AppUser { UserName = "no-pending" };
        await _userManager.CreateAsync(user, "Test1234!");

        var token = _twoFactorService.GenerateSessionToken(user.Id, purpose: "2fa-modify");
        var result = await _twoFactorService.VerifyPendingTotpSetupAsync(user, "123456", token);

        Assert.False(result);
    }

    [Fact]
    public async Task VerifyPendingTotpSetupAsync_WrongSessionPurpose_Fails()
    {
        var user = new AppUser { UserName = "wrong-purpose" };
        await _userManager.CreateAsync(user, "Test1234!");

        // Generate pending secret
        var key = KeyGeneration.GenerateRandomKey(20);
        var base32 = Base32Encoding.ToString(key);
        user.PendingTotpSecretKey = _totpProtector.Protect(base32);
        await _userManager.UpdateAsync(user);

        // Use "2fa-setup" session token (wrong purpose for modify)
        var token = _twoFactorService.GenerateSessionToken(user.Id, purpose: "2fa-setup");
        var validCode = new Totp(key).ComputeTotp();

        var result = await _twoFactorService.VerifyPendingTotpSetupAsync(user, validCode, token);

        Assert.False(result);
    }

    [Fact]
    public async Task VerifyPendingTotpSetupAsync_Success()
    {
        var user = new AppUser { UserName = "pending-success" };
        await _userManager.CreateAsync(user, "Test1234!");

        // Set existing TOTP (as if user already has 2FA)
        var existingKey = KeyGeneration.GenerateRandomKey(20);
        user.TotpSecretKey = _totpProtector.Protect(Base32Encoding.ToString(existingKey));
        user.ConfiguredMethods = TwoFactorMethod.TOTP;
        user.TwoFactorEnabled = true;
        await _userManager.UpdateAsync(user);

        // Generate pending secret via service
        var (secretKey, _) = await _twoFactorService.GeneratePendingTotpSecretAsync(user.Id);

        // Capture the encrypted PendingTotpSecretKey before verification clears it
        var reloadedBefore = await _userManager.FindByIdAsync(user.Id);
        Assert.NotNull(reloadedBefore);
        var capturedPendingKey = reloadedBefore.PendingTotpSecretKey;

        // Create a valid TOTP code from the pending secret
        var secretBytes = Base32Encoding.ToBytes(secretKey);
        var validCode = new Totp(secretBytes).ComputeTotp();

        // Create a modify session token
        var modifyToken = _twoFactorService.GenerateSessionToken(user.Id, purpose: "2fa-modify");

        // Verify the pending setup
        var result = await _twoFactorService.VerifyPendingTotpSetupAsync(user, validCode, modifyToken);

        Assert.True(result);

        // Reload user and verify state
        var reloaded = await _userManager.FindByIdAsync(user.Id);
        Assert.NotNull(reloaded);

        // TotpSecretKey should now be the captured pending value
        Assert.Equal(capturedPendingKey, reloaded.TotpSecretKey);

        // PendingTotpSecretKey should be cleared
        Assert.Null(reloaded.PendingTotpSecretKey);

        // TwoFactorEnabled should remain true
        Assert.True(reloaded.TwoFactorEnabled);

        // ConfiguredMethods should remain unchanged
        Assert.Equal(TwoFactorMethod.TOTP, reloaded.ConfiguredMethods);
    }

    [Fact]
    public async Task VerifyPendingTotpSetupAsync_AntiReplay()
    {
        var user = new AppUser { UserName = "pending-replay" };
        await _userManager.CreateAsync(user, "Test1234!");

        // Generate pending secret
        var (secretKey, _) = await _twoFactorService.GeneratePendingTotpSecretAsync(user.Id);

        var secretBytes = Base32Encoding.ToBytes(secretKey);
        var validCode = new Totp(secretBytes).ComputeTotp();

        var modifyToken = _twoFactorService.GenerateSessionToken(user.Id, purpose: "2fa-modify");

        // First call should succeed
        var firstResult = await _twoFactorService.VerifyPendingTotpSetupAsync(user, validCode, modifyToken);
        Assert.True(firstResult);

        // Reload user and re-set PendingTotpSecretKey + clear TotpSecretKey for second attempt
        var reloaded = await _userManager.FindByIdAsync(user.Id);
        Assert.NotNull(reloaded);
        reloaded.PendingTotpSecretKey = _totpProtector.Protect(secretKey);
        reloaded.TotpSecretKey = null;
        await _userManager.UpdateAsync(reloaded);

        // Second call with same TOTP code and fresh session token should fail (anti-replay)
        var modifyToken2 = _twoFactorService.GenerateSessionToken(reloaded.Id, purpose: "2fa-modify");
        var secondResult = await _twoFactorService.VerifyPendingTotpSetupAsync(reloaded, validCode, modifyToken2);
        Assert.False(secondResult);
    }

    // ────────────── VerifyTotpChallengeAsync dual-key window ──────────────

    [Fact]
    public async Task VerifyTotpChallengeAsync_PendingKeyFallback_Success()
    {
        var user = new AppUser { UserName = "pending-fallback" };
        await _userManager.CreateAsync(user, "Test1234!");

        // TotpSecretKey is null (or invalid), PendingTotpSecretKey has a valid key
        var key = KeyGeneration.GenerateRandomKey(20);
        var base32 = Base32Encoding.ToString(key);
        user.PendingTotpSecretKey = _totpProtector.Protect(base32);
        user.TwoFactorEnabled = true;
        user.ConfiguredMethods = TwoFactorMethod.TOTP;
        await _userManager.UpdateAsync(user);

        // Generate valid TOTP code from the pending key
        var validCode = new Totp(key).ComputeTotp();

        var result = await _twoFactorService.VerifyTotpChallengeAsync(user, validCode);

        Assert.True(result);
    }

    [Fact]
    public async Task VerifyTotpChallengeAsync_OriginalKeyStillWorks()
    {
        var user = new AppUser { UserName = "original-key" };
        await _userManager.CreateAsync(user, "Test1234!");

        // Set up a valid TotpSecretKey
        var key = KeyGeneration.GenerateRandomKey(20);
        var base32 = Base32Encoding.ToString(key);
        user.TotpSecretKey = _totpProtector.Protect(base32);
        user.TwoFactorEnabled = true;
        user.ConfiguredMethods = TwoFactorMethod.TOTP;
        await _userManager.UpdateAsync(user);

        // Generate valid TOTP code from the original key
        var validCode = new Totp(key).ComputeTotp();

        var result = await _twoFactorService.VerifyTotpChallengeAsync(user, validCode);

        Assert.True(result);
    }

    [Fact]
    public async Task VerifyTotpChallengeAsync_NoKey_ReturnsFalse()
    {
        var user = new AppUser { UserName = "no-key" };
        await _userManager.CreateAsync(user, "Test1234!");

        // No keys set at all
        var result = await _twoFactorService.VerifyTotpChallengeAsync(user, "123456");

        Assert.False(result);
    }
}
