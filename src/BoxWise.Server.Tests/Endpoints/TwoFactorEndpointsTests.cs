using System.Reflection;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.AspNetCore.DataProtection;
using BoxWise.Server.Data;
using BoxWise.Server.Endpoints;
using BoxWise.Server.Models;
using BoxWise.Server.Services;
using BoxWise.Shared.Dtos;
using Moq;

namespace BoxWise.Server.Tests.Endpoints;

public class TwoFactorEndpointsTests : IAsyncLifetime
{
    private TestIdentityContext _ctx = null!;
    private UserManager<AppUser> _userManager = null!;
    private TwoFactorService _twoFactorService = null!;
    private EmailTwoFactorService _emailTwoFactorService = null!;
    private RecoveryCodeService _recoveryCodeService = null!;
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
        var db = _ctx.Scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var cache = new MemoryCache(new MemoryCacheOptions());
        _emailTwoFactorService = new EmailTwoFactorService(
            dataProtection, _smtpConfigMock.Object, NullLogger<EmailTwoFactorService>.Instance);
        var recoveryService = new RecoveryCodeService(db);
        _recoveryCodeService = recoveryService;
        _twoFactorService = new TwoFactorService(
            _userManager, dataProtection, _emailTwoFactorService, recoveryService, cache);
    }

    public async Task DisposeAsync() => await _ctx.DisposeAsync();

    private static async Task<int> Invoke2FAAsync(string methodName, params object?[] args)
    {
        var method = typeof(TwoFactorEndpoints).GetMethod(
            methodName, BindingFlags.NonPublic | BindingFlags.Static)!;
        var task = (Task)method.Invoke(null, args)!;
        await task;
        var httpResult = task.GetType().GetProperty("Result")!.GetValue(task)!;
        var executeMethod = httpResult.GetType().GetMethod(
            "ExecuteAsync", [typeof(HttpContext)])!;
        var s = new ServiceCollection(); s.AddLogging();
        using var sp = s.BuildServiceProvider();
        var hc = new DefaultHttpContext { RequestServices = sp };
        hc.Response.Body = new MemoryStream();
        await (Task)executeMethod.Invoke(httpResult, [hc])!;
        return hc.Response.StatusCode;
    }

    private static async Task<(int StatusCode, string Body)> Invoke2FAWithBodyAsync(
        string methodName, params object?[] args)
    {
        var method = typeof(TwoFactorEndpoints).GetMethod(
            methodName, BindingFlags.NonPublic | BindingFlags.Static)!;
        var task = (Task)method.Invoke(null, args)!;
        await task;
        var httpResult = task.GetType().GetProperty("Result")!.GetValue(task)!;
        var executeMethod = httpResult.GetType().GetMethod(
            "ExecuteAsync", [typeof(HttpContext)])!;
        var s = new ServiceCollection(); s.AddLogging();
        using var sp = s.BuildServiceProvider();
        var hc = new DefaultHttpContext { RequestServices = sp };
        hc.Response.Body = new MemoryStream();
        await (Task)executeMethod.Invoke(httpResult, [hc])!;
        hc.Response.Body.Seek(0, SeekOrigin.Begin);
        var body = await new StreamReader(hc.Response.Body).ReadToEndAsync();
        return (hc.Response.StatusCode, body);
    }

    // ────────────── Tests ──────────────

    [Fact]
    public async Task GetStatusAsync_Unauthenticated_ReturnsUnauthorized()
    {
        var hc = new DefaultHttpContext();
        var status = await Invoke2FAAsync("GetStatusAsync", hc, _userManager, _twoFactorService);
        Assert.Equal(401, status);
    }

    [Fact]
    public async Task GetStatusAsync_NoTwoFactor_ReturnsNone()
    {
        var user = new AppUser { UserName = "statusnone" };
        await _userManager.CreateAsync(user, "Test1234!");
        var hc = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, user.Id)], "test"))
        };
        var (status, body) = await Invoke2FAWithBodyAsync(
            "GetStatusAsync", hc, _userManager, _twoFactorService);
        Assert.Equal(200, status);

        var dto = JsonSerializer.Deserialize<TwoFactorStatusDto>(body,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        Assert.NotNull(dto);
        Assert.False(dto.TwoFactorEnabled);
        Assert.Null(dto.TwoFactorMethod);
        // TOTP 始终可用；SMTP 未配置时 Email 不可用
        Assert.Contains("TOTP", dto.AvailableMethods);
        Assert.DoesNotContain("Email", dto.AvailableMethods);
        Assert.False(dto.HasRecoveryCodes);
        // ConfiguredMethods reflects actual user configuration
        Assert.Empty(dto.ConfiguredMethods);
    }

    [Fact]
    public async Task ReAuthenticateAsync_WrongPassword_ReturnsValidationProblem()
    {
        var user = new AppUser { UserName = "reautherr" };
        await _userManager.CreateAsync(user, "Test1234!");
        var hc = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, user.Id)], "test"))
        };
        var status = await Invoke2FAAsync("ReAuthenticateAsync",
            new ReAuthenticateRequest("WrongPass1!"), hc, _userManager, _twoFactorService);
        Assert.Equal(400, status);
    }

    [Fact]
    public async Task ReAuthenticateAsync_CorrectPassword_ReturnsSessionToken()
    {
        var user = new AppUser { UserName = "reauthok" };
        await _userManager.CreateAsync(user, "Test1234!");
        var hc = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, user.Id)], "test"))
        };
        var (status, body) = await Invoke2FAWithBodyAsync("ReAuthenticateAsync",
            new ReAuthenticateRequest("Test1234!"), hc, _userManager, _twoFactorService);
        Assert.Equal(200, status);

        var response = JsonSerializer.Deserialize<ReAuthenticateResponse>(body,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        Assert.NotNull(response);
        Assert.False(string.IsNullOrEmpty(response.SessionToken));
    }

    [Fact]
    public async Task SetupTotpAsync_NoSessionToken_ReturnsValidationProblem()
    {
        var user = new AppUser { UserName = "setuptotp" };
        await _userManager.CreateAsync(user, "Test1234!");
        var hc = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, user.Id)], "test"))
        };
        // No X-Session-Token header → method returns ValidationProblem before
        // checking user or touching TwoFactorService
        var status = await Invoke2FAAsync("SetupTotpAsync", hc, _userManager, _twoFactorService);
        Assert.Equal(400, status);
    }

    [Fact]
    public async Task ChallengeAsync_NoTwoFactorCookie_ReturnsUnauthorized()
    {
        // SignInManager has no TwoFactorUserId cookie, so
        // GetTwoFactorAuthenticationUserAsync() returns null → 401
        var status = await Invoke2FAAsync(
            "ChallengeAsync", _ctx.SignInManager, _emailTwoFactorService, _userManager, _recoveryCodeService, NullLoggerFactory.Instance);
        Assert.Equal(401, status);
    }

    [Fact]
    public async Task ChallengeAsync_WithRecoveryCodes_ReturnsHasRecoveryCodesTrue()
    {
        // Arrange
        var user = new AppUser { UserName = "challengerecovery" };
        await _userManager.CreateAsync(user, "Test1234!");

        // Store recovery codes for user
        var codes = RecoveryCodeService.GenerateRecoveryCodes();
        await _recoveryCodeService.StoreRecoveryCodesAsync(user, codes);

        // Issue TwoFactorUserId cookie so ChallengeAsync can find the user
        var httpContext = _ctx.SignInManager.Context;
        var twoFactorIdentity = new ClaimsIdentity(IdentityConstants.TwoFactorUserIdScheme);
        twoFactorIdentity.AddClaim(new Claim(ClaimTypes.NameIdentifier, user.Id));
        twoFactorIdentity.AddClaim(new Claim(ClaimTypes.Name, user.UserName ?? ""));
        twoFactorIdentity.AddClaim(new Claim("SessionToken", Guid.NewGuid().ToString()));

        // Use a mock authentication service that returns success for the
        // TwoFactorUserId scheme, bypassing the cookie handler which doesn't
        // work in unit-test context (Data Protection key isolation issue).
        var mockAuthService = new Mock<IAuthenticationService>();
        mockAuthService.Setup(x => x.AuthenticateAsync(httpContext, IdentityConstants.TwoFactorUserIdScheme))
            .ReturnsAsync(AuthenticateResult.Success(new AuthenticationTicket(
                new ClaimsPrincipal(twoFactorIdentity),
                IdentityConstants.TwoFactorUserIdScheme)));

        // Override the authentication service in the HttpContext's service provider
        var originalProvider = httpContext.RequestServices;
        var mockServices = new ServiceCollection();
        mockServices.AddSingleton<IAuthenticationService>(mockAuthService.Object);
        var mockProvider = mockServices.BuildServiceProvider();
        httpContext.RequestServices = mockProvider;

        // Act
        var (status, body) = await Invoke2FAWithBodyAsync(
            "ChallengeAsync", _ctx.SignInManager, _emailTwoFactorService, _userManager, _recoveryCodeService, NullLoggerFactory.Instance);

        // Assert
        Assert.Equal(200, status);
        var response = JsonSerializer.Deserialize<TwoFactorChallengeResponse>(body,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        Assert.NotNull(response);
        Assert.True(response.HasRecoveryCodes);
    }
}
