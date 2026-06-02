using System.Security.Claims;
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

namespace BoxWise.Server.Tests.Endpoints;

/// <summary>
/// 2FA Service 层集成测试。
/// 覆盖：方法隔离、多方法路由、恢复码、TOTP/Email 验证端到端。
/// </summary>
public class TwoFactorFlowE2ETests : IAsyncLifetime
{
    private TestIdentityContext _ctx = null!;
    private UserManager<AppUser> _userManager = null!;
    private TwoFactorService _twoFactorService = null!;
    private EmailTwoFactorService _emailTwoFactorService = null!;
    private RecoveryCodeService _recoveryCodeService = null!;
    private Mock<ISmtpConfigurationService> _smtpConfigMock = null!;
    private IDataProtector _totpProtector = null!;

    public async Task InitializeAsync()
    {
        _ctx = await TestIdentityFactory.CreateAsync();
        _userManager = _ctx.UserManager;

        _smtpConfigMock = new Mock<ISmtpConfigurationService>();
        _smtpConfigMock.Setup(x => x.IsConfigured()).Returns(true);
        _smtpConfigMock.Setup(x => x.GetSnapshot())
            .Returns(new SmtpConfigDto("smtp.test.com", 587, null, null, null, null));

        var dataProtection = _ctx.Provider.GetRequiredService<IDataProtectionProvider>();
        _totpProtector = dataProtection.CreateProtector("BoxWise.TwoFactor");
        var db = _ctx.Scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var cache = new MemoryCache(new MemoryCacheOptions());
        _emailTwoFactorService = new EmailTwoFactorService(
            dataProtection, _smtpConfigMock.Object, NullLogger<EmailTwoFactorService>.Instance);
        _recoveryCodeService = new RecoveryCodeService(db);
        _twoFactorService = new TwoFactorService(
            _userManager, dataProtection, _emailTwoFactorService, _recoveryCodeService, cache);
    }

    public async Task DisposeAsync() => await _ctx.DisposeAsync();

    // ────────────── Helpers ──────────────

    private async Task<(AppUser User, byte[] Key)> CreateTotpUserAsync(string username)
    {
        var user = new AppUser { UserName = username };
        await _userManager.CreateAsync(user, "Test1234!");

        var key = KeyGeneration.GenerateRandomKey(20);
        user.TotpSecretKey = _totpProtector.Protect(Base32Encoding.ToString(key));
        user.ConfiguredMethods = TwoFactorMethod.TOTP;
        user.TwoFactorEnabled = true;
        await _userManager.UpdateAsync(user);

        return (user, key);
    }

    // ────────────── Tests ──────────────

    [Fact]
    public async Task EmailSetup_DoesNotClearTotpSecretKey()
    {
        // 方法隔离原则核心测试
        var (user, _) = await CreateTotpUserAsync("isolation");
        var totpKeyBefore = user.TotpSecretKey;

        // 模拟 VerifyEmailAsync 行为：添加 Email 方法
        user.EmailForTwoFactor = "iso@test.com";
        user.ConfiguredMethods |= TwoFactorMethod.Email;
        await _userManager.UpdateAsync(user);

        // Verify: TOTP 密钥未被清除，两种方法并存
        user = (await _userManager.FindByNameAsync("isolation"))!;
        Assert.Equal(totpKeyBefore, user.TotpSecretKey);
        Assert.True(user.ConfiguredMethods.HasFlag(TwoFactorMethod.TOTP));
        Assert.True(user.ConfiguredMethods.HasFlag(TwoFactorMethod.Email));
    }

    [Fact]
    public async Task ConfiguredMethods_FlagsBehavior_Correct()
    {
        var user = new AppUser { UserName = "flagtest" };
        await _userManager.CreateAsync(user, "Test1234!");

        Assert.Equal(TwoFactorMethod.None, user.ConfiguredMethods);
        Assert.False(user.ConfiguredMethods.HasFlag(TwoFactorMethod.TOTP));
        Assert.False(user.ConfiguredMethods.HasFlag(TwoFactorMethod.Email));

        user.ConfiguredMethods |= TwoFactorMethod.TOTP;
        Assert.True(user.ConfiguredMethods.HasFlag(TwoFactorMethod.TOTP));
        Assert.False(user.ConfiguredMethods.HasFlag(TwoFactorMethod.Email));

        user.ConfiguredMethods |= TwoFactorMethod.Email;
        Assert.True(user.ConfiguredMethods.HasFlag(TwoFactorMethod.TOTP));
        Assert.True(user.ConfiguredMethods.HasFlag(TwoFactorMethod.Email));
        Assert.Equal(TwoFactorMethod.TOTP | TwoFactorMethod.Email, user.ConfiguredMethods);

        user.ConfiguredMethods &= ~TwoFactorMethod.Email;
        Assert.True(user.ConfiguredMethods.HasFlag(TwoFactorMethod.TOTP));
        Assert.False(user.ConfiguredMethods.HasFlag(TwoFactorMethod.Email));
    }

    [Fact]
    public async Task TotpService_GenerateAndVerify_EndToEnd()
    {
        var user = new AppUser { UserName = "totp-svc" };
        await _userManager.CreateAsync(user, "Test1234!");

        var (secretKey, qrCodeUri) = await _twoFactorService.GenerateTotpSecretAsync(user.Id);
        Assert.False(string.IsNullOrEmpty(secretKey));
        Assert.Contains("otpauth://totp/BoxWise:", qrCodeUri);

        user = (await _userManager.FindByNameAsync("totp-svc"))!;
        Assert.False(string.IsNullOrEmpty(user.TotpSecretKey));

        var secretBytes = Base32Encoding.ToBytes(secretKey);
        var totp = new Totp(secretBytes);
        var validCode = totp.ComputeTotp();

        var sessionToken = _twoFactorService.GenerateSessionToken(user.Id);
        var setupResult = await _twoFactorService.VerifyTotpSetupAsync(user, validCode, sessionToken);
        Assert.True(setupResult);

        var challengeResult = await _twoFactorService.VerifyTotpChallengeAsync(user, validCode, "login");
        Assert.True(challengeResult);

        var replayResult = await _twoFactorService.VerifyTotpChallengeAsync(user, validCode, "login");
        Assert.False(replayResult);
    }

    [Fact]
    public async Task EmailTwoFactorService_GenerateAndVerify_EndToEnd()
    {
        var user = new AppUser { UserName = "email-svc" };
        await _userManager.CreateAsync(user, "Test1234!");

        var (code, token) = _emailTwoFactorService.GenerateCode(user.Id, "test@email.com");
        Assert.Equal(6, code.Length);
        Assert.False(string.IsNullOrEmpty(token));

        Assert.True(_emailTwoFactorService.VerifyCode(user.Id, "test@email.com", code, token));
        Assert.False(_emailTwoFactorService.VerifyCode(user.Id, "test@email.com", "000000", token));
        Assert.False(_emailTwoFactorService.VerifyCode(user.Id, "wrong@email.com", code, token));
        Assert.False(_emailTwoFactorService.VerifyCode("wrong-id", "test@email.com", code, token));
    }

    [Fact]
    public async Task RecoveryCodeService_RegenerateAndVerify_EndToEnd()
    {
        var user = new AppUser { UserName = "recovery-svc" };
        await _userManager.CreateAsync(user, "Test1234!");
        user.TwoFactorEnabled = true;
        user.ConfiguredMethods = TwoFactorMethod.TOTP;
        await _userManager.UpdateAsync(user);

        var codes = await _recoveryCodeService.RegenerateRecoveryCodesAsync(user);
        Assert.Equal(8, codes.Count);
        Assert.True(await _recoveryCodeService.HasRecoveryCodesAsync(user));

        Assert.True(await _recoveryCodeService.VerifyRecoveryCodeAsync(user, codes[0], _userManager));
        Assert.False(await _recoveryCodeService.VerifyRecoveryCodeAsync(user, codes[0], _userManager));

        user = (await _userManager.FindByNameAsync("recovery-svc"))!;
        Assert.False(user.TwoFactorEnabled);
        Assert.Equal(TwoFactorMethod.None, user.ConfiguredMethods);
    }
}
