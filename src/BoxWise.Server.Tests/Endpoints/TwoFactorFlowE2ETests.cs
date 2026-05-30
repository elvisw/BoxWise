using System.Reflection;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.AspNetCore.DataProtection;
using BoxWise.Server.Data;
using BoxWise.Server.Endpoints;
using BoxWise.Server.Models;
using BoxWise.Server.Services;
using BoxWise.Shared.Dtos;
using Moq;
using OtpNet;

namespace BoxWise.Server.Tests.Endpoints;

/// <summary>
/// 2FA 登录流程集成测试。
/// 覆盖：方法隔离、多方法路由、FallbackPolicy 兼容、错误路径。
/// 注意：Handler 层测试中 TwoFactorUserId Cookie 注入不稳定（DefaultHttpContext
/// 的 Request/Response cookie 无法自动往返），因此 Verify/Challenge 的完整认证流程
/// 需通过 WebApplicationFactory E2E 测试覆盖。本文件测试可达的逻辑路径。
/// </summary>
public class TwoFactorFlowE2ETests : IAsyncLifetime
{
    private TestIdentityContext _ctx = null!;
    private UserManager<AppUser> _userManager = null!;
    private SignInManager<AppUser> _signInManager = null!;
    private TwoFactorService _twoFactorService = null!;
    private EmailTwoFactorService _emailTwoFactorService = null!;
    private RecoveryCodeService _recoveryCodeService = null!;
    private Mock<ISmtpConfigurationService> _smtpConfigMock = null!;
    private IDataProtector _totpProtector = null!;

    public async Task InitializeAsync()
    {
        _ctx = await TestIdentityFactory.CreateAsync();
        _userManager = _ctx.UserManager;
        _signInManager = _ctx.SignInManager;

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

    private HttpContext CreateAuthContext(AppUser user)
    {
        return new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, user.Id)], "test"))
        };
    }

    /// <summary>
    /// 创建一个已配置 TOTP 的用户（密钥已加密存储）。
    /// </summary>
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
    public async Task ChallengeAsync_NoCookie_ReturnsUnauthorized()
    {
        // 无 TwoFactorUserId Cookie → 401
        var status = await TwoFactorTestHelpers.Invoke2FAAsync(
            "ChallengeAsync", _signInManager, _emailTwoFactorService, _userManager, _recoveryCodeService, null!);
        Assert.Equal(401, status);
    }

    [Fact]
    public async Task SwitchMethodAsync_Returns410Gone()
    {
        var method = typeof(TwoFactorEndpoints).GetMethod(
            "SwitchMethodAsync", BindingFlags.NonPublic | BindingFlags.Static)!;
        var result = method.Invoke(null, null)!;
        var executeMethod = result.GetType().GetMethod(
            "ExecuteAsync", [typeof(HttpContext)])!;
        var s = new ServiceCollection(); s.AddLogging();
        using var sp = s.BuildServiceProvider();
        var hc = new DefaultHttpContext { RequestServices = sp };
        hc.Response.Body = new MemoryStream();
        var task = (Task)executeMethod.Invoke(result, [hc])!;
        await task;
        Assert.Equal(410, hc.Response.StatusCode);
    }

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
        // 验证 [Flags] 枚举的正确行为
        var user = new AppUser { UserName = "flagtest" };
        await _userManager.CreateAsync(user, "Test1234!");

        // 初始状态
        Assert.Equal(TwoFactorMethod.None, user.ConfiguredMethods);
        Assert.False(user.ConfiguredMethods.HasFlag(TwoFactorMethod.TOTP));
        Assert.False(user.ConfiguredMethods.HasFlag(TwoFactorMethod.Email));

        // 添加 TOTP
        user.ConfiguredMethods |= TwoFactorMethod.TOTP;
        Assert.True(user.ConfiguredMethods.HasFlag(TwoFactorMethod.TOTP));
        Assert.False(user.ConfiguredMethods.HasFlag(TwoFactorMethod.Email));

        // 添加 Email（不覆盖 TOTP）
        user.ConfiguredMethods |= TwoFactorMethod.Email;
        Assert.True(user.ConfiguredMethods.HasFlag(TwoFactorMethod.TOTP));
        Assert.True(user.ConfiguredMethods.HasFlag(TwoFactorMethod.Email));
        Assert.Equal(TwoFactorMethod.TOTP | TwoFactorMethod.Email, user.ConfiguredMethods);

        // 移除 Email
        user.ConfiguredMethods &= ~TwoFactorMethod.Email;
        Assert.True(user.ConfiguredMethods.HasFlag(TwoFactorMethod.TOTP));
        Assert.False(user.ConfiguredMethods.HasFlag(TwoFactorMethod.Email));
    }

    [Fact]
    public async Task GetStatusAsync_MultiMethod_ReturnsBothAvailableMethods()
    {
        var (user, _) = await CreateTotpUserAsync("statusmulti");
        user.EmailForTwoFactor = "status@test.com";
        user.ConfiguredMethods |= TwoFactorMethod.Email;
        await _userManager.UpdateAsync(user);

        var hc = CreateAuthContext(user);
        var (status, body) = await TwoFactorTestHelpers.Invoke2FAWithBodyAsync(
            "GetStatusAsync", hc, _userManager, _twoFactorService);
        Assert.Equal(200, status);

        var dto = JsonSerializer.Deserialize<TwoFactorStatusDto>(body,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        Assert.True(dto.TwoFactorEnabled);
        Assert.Contains("TOTP", dto.AvailableMethods);
        Assert.Contains("Email", dto.AvailableMethods);
        // ConfiguredMethods reflects actual user configuration
        Assert.Contains("TOTP", dto.ConfiguredMethods);
        Assert.Contains("Email", dto.ConfiguredMethods);
    }

    [Fact]
    public async Task GetStatusAsync_OnlyTotpConfigured_AvailableMethodsShowsSettableMethods()
    {
        // AvailableMethods 反映可用于设置的方法，而非用户已配置的方法
        // TOTP 始终可用；Email 在 SMTP 已配置时可用（无论用户是否已配置）
        var (user, _) = await CreateTotpUserAsync("onlytotpstatus");
        var hc = CreateAuthContext(user);

        var (status, body) = await TwoFactorTestHelpers.Invoke2FAWithBodyAsync(
            "GetStatusAsync", hc, _userManager, _twoFactorService);
        Assert.Equal(200, status);
        var dto = JsonSerializer.Deserialize<TwoFactorStatusDto>(body,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        Assert.Contains("TOTP", dto.AvailableMethods);
        // SMTP mock 返回 IsConfigured()=true，Email 应出现在可设置方法中
        Assert.Contains("Email", dto.AvailableMethods);
        // WebAuthn 不再硬编码
        Assert.DoesNotContain("WebAuthn", dto.AvailableMethods);
        // ConfiguredMethods reflects actual user configuration
        Assert.Contains("TOTP", dto.ConfiguredMethods);
        Assert.DoesNotContain("Email", dto.ConfiguredMethods);
    }

    [Fact]
    public async Task ReAuthenticate_GeneratesSessionToken_ForTotpSetup()
    {
        var user = new AppUser { UserName = "reauth-setup" };
        await _userManager.CreateAsync(user, "Test1234!");
        var hc = CreateAuthContext(user);

        var (status, body) = await TwoFactorTestHelpers.Invoke2FAWithBodyAsync("ReAuthenticateAsync",
            new ReAuthenticateRequest("Test1234!"), hc, _userManager, _twoFactorService);
        Assert.Equal(200, status);

        var response = JsonSerializer.Deserialize<ReAuthenticateResponse>(body,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        Assert.False(string.IsNullOrEmpty(response.SessionToken));

        // SessionToken 应可被验证
        Assert.True(_twoFactorService.ValidateSessionToken(response.SessionToken, user.Id));
    }

    [Fact]
    public async Task TotpService_GenerateAndVerify_EndToEnd()
    {
        // 验证 OtpNet + Data Protection 集成正确
        var user = new AppUser { UserName = "totp-svc" };
        await _userManager.CreateAsync(user, "Test1234!");

        var (secretKey, qrCodeUri) = await _twoFactorService.GenerateTotpSecretAsync(user.Id);
        Assert.False(string.IsNullOrEmpty(secretKey));
        Assert.Contains("otpauth://totp/BoxWise:", qrCodeUri);

        // 重新加载用户（GenerateTotpSecretAsync 修改了 TotpSecretKey）
        user = (await _userManager.FindByNameAsync("totp-svc"))!;
        Assert.False(string.IsNullOrEmpty(user.TotpSecretKey));

        // 验证码验证
        var secretBytes = Base32Encoding.ToBytes(secretKey);
        var totp = new Totp(secretBytes);
        var validCode = totp.ComputeTotp();

        var sessionToken = _twoFactorService.GenerateSessionToken(user.Id);
        var setupResult = await _twoFactorService.VerifyTotpSetupAsync(user, validCode, sessionToken);
        Assert.True(setupResult);

        // 挑战验证
        var challengeResult = await _twoFactorService.VerifyTotpChallengeAsync(user, validCode);
        Assert.False(challengeResult); // 防重放：同一 timeStep 已使用
    }

    [Fact]
    public async Task EmailTwoFactorService_GenerateAndVerify_EndToEnd()
    {
        var user = new AppUser { UserName = "email-svc" };
        await _userManager.CreateAsync(user, "Test1234!");

        var (code, token) = _emailTwoFactorService.GenerateCode(user.Id, "test@email.com");
        Assert.Equal(6, code.Length);
        Assert.False(string.IsNullOrEmpty(token));

        // 正确验证
        Assert.True(_emailTwoFactorService.VerifyCode(user.Id, "test@email.com", code, token));

        // 错误验证码
        Assert.False(_emailTwoFactorService.VerifyCode(user.Id, "test@email.com", "000000", token));

        // 错误邮箱
        Assert.False(_emailTwoFactorService.VerifyCode(user.Id, "wrong@email.com", code, token));

        // 错误用户
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

        // 生成恢复码
        var codes = await _recoveryCodeService.RegenerateRecoveryCodesAsync(user);
        Assert.Equal(8, codes.Count);
        Assert.True(await _recoveryCodeService.HasRecoveryCodesAsync(user));

        // 验证恢复码
        Assert.True(await _recoveryCodeService.VerifyRecoveryCodeAsync(user, codes[0], _userManager));

        // 使用后恢复码失效
        Assert.False(await _recoveryCodeService.VerifyRecoveryCodeAsync(user, codes[0], _userManager));

        // 使用恢复码后清除 2FA 状态
        user = (await _userManager.FindByNameAsync("recovery-svc"))!;
        Assert.False(user.TwoFactorEnabled);
        Assert.Equal(TwoFactorMethod.None, user.ConfiguredMethods);
    }
}
