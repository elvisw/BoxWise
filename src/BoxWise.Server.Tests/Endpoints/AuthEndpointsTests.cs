using System.Reflection;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.AspNetCore.DataProtection;
using BoxWise.Server.Data;
using BoxWise.Server.Endpoints;
using BoxWise.Server.Models;
using BoxWise.Server.Services;
using BoxWise.Shared.Dtos;
using Moq;

namespace BoxWise.Server.Tests.Endpoints;

public class AuthEndpointsTests : IAsyncLifetime
{
    private TestIdentityContext _ctx = null!;
    private UserManager<AppUser> _userManager = null!;
    private IConfiguration _config = null!;
    private ILoggerFactory _loggerFactory = NullLoggerFactory.Instance;
    private EmailTwoFactorService _emailTwoFactorService = null!;

    public async Task InitializeAsync()
    {
        _ctx = await TestIdentityFactory.CreateAsync();
        _userManager = _ctx.UserManager;
        _config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>()).Build();

        // Set up EmailTwoFactorService for tests that need it
        var dataProtection = _ctx.Provider.GetRequiredService<IDataProtectionProvider>();
        var smtpConfigMock = new Mock<ISmtpConfigurationService>();
        smtpConfigMock.Setup(x => x.IsConfigured()).Returns(false);
        smtpConfigMock.Setup(x => x.GetSnapshot())
            .Returns(new SmtpConfigDto(string.Empty, 587, null, null, null, null));
        _emailTwoFactorService = new EmailTwoFactorService(
            dataProtection, smtpConfigMock.Object, NullLogger<EmailTwoFactorService>.Instance);
    }

    public async Task DisposeAsync() => await _ctx.DisposeAsync();

    private static async Task<int> InvokeAsync(string methodName, params object?[] args)
    {
        var method = typeof(AuthEndpoints).GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Static)!;
        var task = (Task)method.Invoke(null, args)!;
        await task;
        var httpResult = task.GetType().GetProperty("Result")!.GetValue(task)!;
        var executeMethod = httpResult.GetType().GetMethod("ExecuteAsync", [typeof(HttpContext)])!;
        var s = new ServiceCollection(); s.AddLogging(); s.AddDataProtection();
        using var sp = s.BuildServiceProvider();
        var hc = new DefaultHttpContext { RequestServices = sp };
        hc.Response.Body = new MemoryStream();
        await (Task)executeMethod.Invoke(httpResult, [hc])!;
        return hc.Response.StatusCode;
    }

    private static async Task<(int StatusCode, string Body)> InvokeWithBodyAsync(string methodName, params object?[] args)
    {
        var method = typeof(AuthEndpoints).GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Static)!;
        var task = (Task)method.Invoke(null, args)!;
        await task;
        var httpResult = task.GetType().GetProperty("Result")!.GetValue(task)!;
        var executeMethod = httpResult.GetType().GetMethod("ExecuteAsync", [typeof(HttpContext)])!;
        var s = new ServiceCollection(); s.AddLogging(); s.AddDataProtection();
        using var sp = s.BuildServiceProvider();
        var hc = new DefaultHttpContext { RequestServices = sp };
        hc.Response.Body = new MemoryStream();
        await (Task)executeMethod.Invoke(httpResult, [hc])!;
        hc.Response.Body.Seek(0, SeekOrigin.Begin);
        var body = await new StreamReader(hc.Response.Body).ReadToEndAsync();
        return (hc.Response.StatusCode, body);
    }

    // Creates a HttpContext with DataProtection + EmailTwoFactorService for email change tests.
    // sp is intentionally not disposed (kept alive by HttpContext.Items).
    private DefaultHttpContext CreateHttpContextForEmailTest(ClaimsPrincipal user)
    {
        var s = new ServiceCollection();
        s.AddLogging();
        s.AddDataProtection();
        s.AddSingleton(_emailTwoFactorService);
        var sp = s.BuildServiceProvider();
        var hc = new DefaultHttpContext { RequestServices = sp, User = user, Response = { Body = new MemoryStream() } };
        hc.Items["__ServiceProvider"] = sp; // Keep alive for the duration of the test
        return hc;
    }

    private string GenerateOperationToken(string userId, string email)
    {
        var dp = _ctx.Provider.GetRequiredService<IDataProtectionProvider>();
        var protector = dp.CreateProtector(EmailVerificationEndpoints.OperationTokenPurpose);
        return protector.Protect($"{userId}|{email}|{DateTime.UtcNow.AddMinutes(5):O}");
    }

    [Fact] public async Task GetCurrentUserAsync_Unauthenticated_ReturnsOk() { var hc = new DefaultHttpContext(); Assert.Equal(200, await InvokeAsync("GetCurrentUserAsync", _userManager, hc, _config)); }
    [Fact] public async Task GetCurrentUserAsync_Authenticated_ReturnsUser() { var u = new AppUser { UserName = "cu" }; await _userManager.CreateAsync(u, "Test1234!"); var hc = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, u.Id)], "test")) }; Assert.Equal(200, await InvokeAsync("GetCurrentUserAsync", _userManager, hc, _config)); }
    [Fact] public async Task UpdateProfileAsync_ValidName_Succeeds() { var u = new AppUser { UserName = "on" }; await _userManager.CreateAsync(u, "Test1234!"); var hc = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, u.Id)], "test")) }; Assert.Equal(200, await InvokeAsync("UpdateProfileAsync", new UpdateProfileRequest("nn"), _userManager, hc, _config, _loggerFactory)); }
    [Fact] public async Task UpdateProfileAsync_DuplicateName_Fails() { var u1 = new AppUser { UserName = "un1" }; var u2 = new AppUser { UserName = "un2" }; await _userManager.CreateAsync(u1, "Test1234!"); await _userManager.CreateAsync(u2, "Test1234!"); var hc = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, u1.Id)], "test")) }; Assert.Equal(400, await InvokeAsync("UpdateProfileAsync", new UpdateProfileRequest("un2"), _userManager, hc, _config, _loggerFactory)); }
    [Fact] public async Task ChangePasswordAsync_CorrectPassword_Succeeds() { var u = new AppUser { UserName = "pu" }; await _userManager.CreateAsync(u, "OldPass1!"); var hc = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, u.Id)], "test")) }; Assert.Equal(200, await InvokeAsync("ChangePasswordAsync", new ChangePasswordRequest("OldPass1!", "NewPass2!"), _userManager, hc)); }
    [Fact] public async Task ChangePasswordAsync_WrongCurrent_Fails() { var u = new AppUser { UserName = "pu2" }; await _userManager.CreateAsync(u, "OldPass1!"); var hc = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, u.Id)], "test")) }; Assert.Equal(400, await InvokeAsync("ChangePasswordAsync", new ChangePasswordRequest("WrongOld!", "NewPass2!"), _userManager, hc)); }

    [Fact]
    public async Task LoginAsync_ValidCredentials_ReturnsOk()
    {
        var u = new AppUser { UserName = "loginuser" };
        await _userManager.CreateAsync(u, "Test1234!");
        var (status, body) = await InvokeWithBodyAsync("LoginAsync", new LoginRequest("loginuser", "Test1234!"),
            _ctx.SignInManager, _userManager, _config, _loggerFactory, new DefaultHttpContext());
        Assert.Equal(200, status);

        var dto = JsonSerializer.Deserialize<LoginResponse>(body,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        Assert.NotNull(dto);
        Assert.Equal("loginuser", dto.Username);
        Assert.False(dto.RequiresTwoFactor);
    }

    [Fact]
    public async Task LoginAsync_InvalidCredentials_ReturnsValidationProblem()
    {
        var u = new AppUser { UserName = "wrongpwuser" };
        await _userManager.CreateAsync(u, "Test1234!");
        var (status, body) = await InvokeWithBodyAsync("LoginAsync", new LoginRequest("wrongpwuser", "WrongPass1!"),
            _ctx.SignInManager, _userManager, _config, _loggerFactory, new DefaultHttpContext());
        Assert.Equal(400, status);
        Assert.Contains("credentials", body);
    }

    [Fact]
    public async Task LoginAsync_EmptyUsername_ReturnsValidationProblem()
    {
        var (status, body) = await InvokeWithBodyAsync("LoginAsync", new LoginRequest("", "Test1234!"),
            _ctx.SignInManager, _userManager, _config, _loggerFactory, new DefaultHttpContext());
        Assert.Equal(400, status);
        Assert.Contains("credentials", body);
    }

    [Fact]
    public async Task LoginAsync_NonexistentUser_ReturnsValidationProblem()
    {
        var (status, body) = await InvokeWithBodyAsync("LoginAsync", new LoginRequest("nouser", "Test1234!"),
            _ctx.SignInManager, _userManager, _config, _loggerFactory, new DefaultHttpContext());
        Assert.Equal(400, status);
        Assert.Contains("credentials", body);
    }

    [Fact]
    public async Task LogoutAsync_Authenticated_ReturnsOk()
    {
        var result = await InvokeAsync("LogoutAsync", _ctx.SignInManager);
        Assert.Equal(200, result);
    }

    // ===== Email Change Tests =====

    [Fact]
    public async Task UpdateProfile_UsernameOnly_NoTokenRequired()
    {
        var user = new AppUser { UserName = "notokenuser", Email = "old@test.com" };
        await _userManager.CreateAsync(user, "Test1234!");
        var userClaim = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, user.Id)], "test"));
        var hc = CreateHttpContextForEmailTest(userClaim);
        var (status, body) = await InvokeWithHttpContextAsync("UpdateProfileAsync",
            new UpdateProfileRequest("newname"), // NewEmail = null
            _userManager, hc, _config, _loggerFactory);
        Assert.Equal(200, status);

        var dto = JsonSerializer.Deserialize<AuthUserDto>(body,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        Assert.NotNull(dto);
        Assert.Equal("newname", dto.UserName);
        Assert.Equal("old@test.com", dto.Email); // Email unchanged
    }

    [Fact]
    public async Task UpdateProfile_EmailChange_WithoutToken_Rejected()
    {
        var user = new AppUser { UserName = "emailnotoken" };
        await _userManager.CreateAsync(user, "Test1234!");
        var userClaim = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, user.Id)], "test"));
        var hc = CreateHttpContextForEmailTest(userClaim);
        var status = await InvokeWithHttpContextStatusAsync("UpdateProfileAsync",
            new UpdateProfileRequest("emailnotoken", "new@test.com"), // NewEmail set, no OperationToken
            _userManager, hc, _config, _loggerFactory);
        Assert.Equal(400, status);
    }

    [Fact]
    public async Task UpdateProfile_EmailChange_SyncsEmailForTwoFactor()
    {
        var user = new AppUser { UserName = "emailsync" };
        await _userManager.CreateAsync(user, "Test1234!");

        var operationToken = GenerateOperationToken(user.Id, "new@test.com");

        var userClaim = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, user.Id)], "test"));
        var hc = CreateHttpContextForEmailTest(userClaim);

        var (status, body) = await InvokeWithHttpContextAsync("UpdateProfileAsync",
            new UpdateProfileRequest("emailsync", "new@test.com", operationToken),
            _userManager, hc, _config, _loggerFactory);
        Assert.Equal(200, status);

        // Verify both Email and EmailForTwoFactor are synced
        var updatedUser = await _userManager.FindByIdAsync(user.Id);
        Assert.NotNull(updatedUser);
        Assert.Equal("new@test.com", updatedUser.Email);
        Assert.Equal("new@test.com", updatedUser.EmailForTwoFactor);
    }

    // Helper that invokes the method with a pre-built HttpContext and returns (status, body)
    private static async Task<(int StatusCode, string Body)> InvokeWithHttpContextAsync(
        string methodName, params object?[] args)
    {
        // Extract the HttpContext from args (last user-provided args before LoggerFactory)
        var method = typeof(AuthEndpoints).GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Static)!;
        var task = (Task)method.Invoke(null, args)!;
        await task;
        var httpResult = task.GetType().GetProperty("Result")!.GetValue(task)!;
        var executeMethod = httpResult.GetType().GetMethod("ExecuteAsync", [typeof(HttpContext)])!;

        // Find the HttpContext from the args list - it's passed directly
        var httpContext = args.OfType<DefaultHttpContext>().FirstOrDefault()
            ?? throw new InvalidOperationException("No HttpContext found in args");

        httpContext.Response.Body.Seek(0, SeekOrigin.Begin);
        await (Task)executeMethod.Invoke(httpResult, [httpContext])!;
        httpContext.Response.Body.Seek(0, SeekOrigin.Begin);
        var body = await new StreamReader(httpContext.Response.Body).ReadToEndAsync();
        return (httpContext.Response.StatusCode, body);
    }

    // Helper that invokes the method with a pre-built HttpContext and returns only status
    private static async Task<int> InvokeWithHttpContextStatusAsync(
        string methodName, params object?[] args)
    {
        var method = typeof(AuthEndpoints).GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Static)!;
        var task = (Task)method.Invoke(null, args)!;
        await task;
        var httpResult = task.GetType().GetProperty("Result")!.GetValue(task)!;
        var executeMethod = httpResult.GetType().GetMethod("ExecuteAsync", [typeof(HttpContext)])!;

        var httpContext = args.OfType<DefaultHttpContext>().FirstOrDefault()
            ?? throw new InvalidOperationException("No HttpContext found in args");

        await (Task)executeMethod.Invoke(httpResult, [httpContext])!;
        return httpContext.Response.StatusCode;
    }
}
