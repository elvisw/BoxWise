using System.Reflection;
using System.Security.Claims;
using System.Text.Json;
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

namespace BoxWise.Server.Tests.Endpoints;

public class EmailVerificationEndpointsTests : IAsyncLifetime
{
    private TestIdentityContext _ctx = null!;
    private UserManager<AppUser> _userManager = null!;
    private TwoFactorService _twoFactorService = null!;
    private EmailTwoFactorService _emailTwoFactorService = null!;
    private IDataProtectionProvider _dataProtection = null!;
    private Mock<ISmtpConfigurationService> _smtpConfigMock = null!;

    public async Task InitializeAsync()
    {
        _ctx = await TestIdentityFactory.CreateAsync();
        _userManager = _ctx.UserManager;

        _smtpConfigMock = new Mock<ISmtpConfigurationService>();
        _smtpConfigMock.Setup(x => x.IsConfigured()).Returns(false);
        _smtpConfigMock.Setup(x => x.GetSnapshot())
            .Returns(new SmtpConfigDto(string.Empty, 587, null, null, null, null));

        _dataProtection = _ctx.Provider.GetRequiredService<IDataProtectionProvider>();
        var db = _ctx.Scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var cache = new MemoryCache(new MemoryCacheOptions());
        _emailTwoFactorService = new EmailTwoFactorService(
            _dataProtection, _smtpConfigMock.Object, NullLogger<EmailTwoFactorService>.Instance);
        var recoveryService = new RecoveryCodeService(db);
        _twoFactorService = new TwoFactorService(
            _userManager, _dataProtection, _emailTwoFactorService, recoveryService, cache);
    }

    public async Task DisposeAsync() => await _ctx.DisposeAsync();

    private static async Task<int> InvokeEmailAsync(string methodName, params object?[] args)
    {
        var method = typeof(EmailVerificationEndpoints).GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Static)!;
        var task = (Task)method.Invoke(null, args)!;
        await task;
        var httpResult = task.GetType().GetProperty("Result")!.GetValue(task)!;
        var executeMethod = httpResult.GetType().GetMethod("ExecuteAsync", [typeof(HttpContext)])!;
        var s = new ServiceCollection(); s.AddLogging();
        using var sp = s.BuildServiceProvider();
        var hc = new DefaultHttpContext { RequestServices = sp };
        hc.Response.Body = new MemoryStream();
        await (Task)executeMethod.Invoke(httpResult, [hc])!;
        return hc.Response.StatusCode;
    }

    private static async Task<(int StatusCode, string Body)> InvokeEmailWithBodyAsync(string methodName, params object?[] args)
    {
        var method = typeof(EmailVerificationEndpoints).GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Static)!;
        var task = (Task)method.Invoke(null, args)!;
        await task;
        var httpResult = task.GetType().GetProperty("Result")!.GetValue(task)!;
        var executeMethod = httpResult.GetType().GetMethod("ExecuteAsync", [typeof(HttpContext)])!;
        var s = new ServiceCollection(); s.AddLogging();
        using var sp = s.BuildServiceProvider();
        var hc = new DefaultHttpContext { RequestServices = sp };
        hc.Response.Body = new MemoryStream();
        await (Task)executeMethod.Invoke(httpResult, [hc])!;
        hc.Response.Body.Seek(0, SeekOrigin.Begin);
        var body = await new StreamReader(hc.Response.Body).ReadToEndAsync();
        return (hc.Response.StatusCode, body);
    }

    [Fact]
    public async Task SendCode_NoSessionToken_ReturnsValidationProblem()
    {
        var user = new AppUser { UserName = "sendnotoken" };
        await _userManager.CreateAsync(user, "Test1234!");
        var hc = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, user.Id)], "test"))
        };
        var status = await InvokeEmailAsync("SendCodeAsync",
            new SendEmailCodeRequest("new@test.com"), hc, _userManager, _twoFactorService, _emailTwoFactorService);
        Assert.Equal(400, status);
    }

    [Fact]
    public async Task SendCode_Unauthenticated_ReturnsUnauthorized()
    {
        var hc = new DefaultHttpContext();
        hc.Request.Headers["X-Session-Token"] = "some-token";
        var status = await InvokeEmailAsync("SendCodeAsync",
            new SendEmailCodeRequest("new@test.com"), hc, _userManager, _twoFactorService, _emailTwoFactorService);
        Assert.Equal(401, status);
    }

    [Fact]
    public async Task SendCode_InvalidEmail_ReturnsValidationProblem()
    {
        var user = new AppUser { UserName = "sendinvalid" };
        await _userManager.CreateAsync(user, "Test1234!");
        var hc = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, user.Id)], "test"))
        };
        var token = _twoFactorService.GenerateSessionToken(user.Id);
        hc.Request.Headers["X-Session-Token"] = token;

        var status = await InvokeEmailAsync("SendCodeAsync",
            new SendEmailCodeRequest("not-an-email"), hc, _userManager, _twoFactorService, _emailTwoFactorService);
        Assert.Equal(400, status);
    }

    [Fact]
    public async Task SendCode_ExistingEmailConflict_ReturnsValidationProblem()
    {
        var user1 = new AppUser { UserName = "user1", Email = "existing@test.com" };
        var user2 = new AppUser { UserName = "user2" };
        await _userManager.CreateAsync(user1, "Test1234!");
        await _userManager.CreateAsync(user2, "Test1234!");

        var hc = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, user2.Id)], "test"))
        };
        var token = _twoFactorService.GenerateSessionToken(user2.Id);
        hc.Request.Headers["X-Session-Token"] = token;

        var status = await InvokeEmailAsync("SendCodeAsync",
            new SendEmailCodeRequest("existing@test.com"), hc, _userManager, _twoFactorService, _emailTwoFactorService);
        Assert.Equal(400, status);
    }

    [Fact]
    public async Task VerifyCode_Unauthenticated_ReturnsUnauthorized()
    {
        var hc = new DefaultHttpContext();
        var status = await InvokeEmailAsync("VerifyCodeAsync",
            new VerifyEmailCodeRequest("123456", "some-token"), hc, _userManager, _emailTwoFactorService, _dataProtection);
        Assert.Equal(401, status);
    }

    [Fact]
    public async Task VerifyCode_InvalidCode_ReturnsValidationProblem()
    {
        var user = new AppUser { UserName = "verifyinvalid" };
        await _userManager.CreateAsync(user, "Test1234!");
        var hc = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, user.Id)], "test"))
        };

        var status = await InvokeEmailAsync("VerifyCodeAsync",
            new VerifyEmailCodeRequest("wrong", "some-token"), hc, _userManager, _emailTwoFactorService, _dataProtection);
        Assert.Equal(400, status);
    }

    [Fact]
    public async Task VerifyCode_ValidCode_ReturnsOperationToken()
    {
        // Enable SMTP so send works (otherwise code isn't sent)
        _smtpConfigMock.Setup(x => x.IsConfigured()).Returns(true);
        _smtpConfigMock.Setup(x => x.GetSnapshot())
            .Returns(new SmtpConfigDto("smtp.test.com", 587, "user", "pass", "from@test.com", "Test"));

        var user = new AppUser { UserName = "verifyvalid" };
        await _userManager.CreateAsync(user, "Test1234!");

        // Generate a real token and code
        var (code, token) = _emailTwoFactorService.GenerateCode(user.Id, "new@test.com");

        var hc = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, user.Id)], "test"))
        };

        var (status, body) = await InvokeEmailWithBodyAsync("VerifyCodeAsync",
            new VerifyEmailCodeRequest(code, token), hc, _userManager, _emailTwoFactorService, _dataProtection);
        Assert.Equal(200, status);

        var response = JsonSerializer.Deserialize<EmailVerifyCodeResponse>(body,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        Assert.NotNull(response);
        Assert.False(string.IsNullOrEmpty(response.OperationToken));
        Assert.Equal("new@test.com", response.VerifiedEmail);
    }

    [Fact]
    public async Task VerifyCode_TokenReuse_ReturnsValidationProblem()
    {
        _smtpConfigMock.Setup(x => x.IsConfigured()).Returns(true);
        _smtpConfigMock.Setup(x => x.GetSnapshot())
            .Returns(new SmtpConfigDto("smtp.test.com", 587, "user", "pass", "from@test.com", "Test"));

        var user = new AppUser { UserName = "verifyreuse" };
        await _userManager.CreateAsync(user, "Test1234!");

        var (code, token) = _emailTwoFactorService.GenerateCode(user.Id, "new@test.com");

        var hc = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, user.Id)], "test"))
        };

        // First call should succeed
        var (status1, _) = await InvokeEmailWithBodyAsync("VerifyCodeAsync",
            new VerifyEmailCodeRequest(code, token), hc, _userManager, _emailTwoFactorService, _dataProtection);
        Assert.Equal(200, status1);

        // Second call with same code+token should fail (already consumed via VerifyCodeOnce)
        var (status2, body2) = await InvokeEmailWithBodyAsync("VerifyCodeAsync",
            new VerifyEmailCodeRequest(code, token), hc, _userManager, _emailTwoFactorService, _dataProtection);
        Assert.Equal(400, status2);
        Assert.Contains("请重新发送", body2);
    }
}
