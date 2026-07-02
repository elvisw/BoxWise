using System.Net;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using BoxWise.Server.Areas.Identity.Pages.Account;
using BoxWise.Server.Data;
using BoxWise.Server.Models;
using Moq;

namespace BoxWise.Server.Tests.Endpoints;

/// <summary>
/// E2E tests for the forgot password flow.
/// Uses WebApplicationFactory for HTTP-level tests (redirects, GET),
/// and PageModel-direct tests for form POST validation (bypassing antiforgery
/// and form content serialization issues).
/// </summary>
public class ForgotPasswordE2ETests : IDisposable
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly Mock<IEmailSender> _emailSenderMock;
    private readonly string _tempDir;

    public ForgotPasswordE2ETests()
    {
        _emailSenderMock = new Mock<IEmailSender>();

        _tempDir = Path.Combine(Path.GetTempPath(), $"boxwise-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(_tempDir);
        var sqlitePath = Path.Combine(_tempDir, "test.db");

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("DataDirectory", _tempDir);
            builder.UseSetting("ConnectionStrings:DefaultConnection",
                $"Data Source={sqlitePath}");

            builder.ConfigureTestServices(services =>
            {
                services.PostConfigure<AuthorizationOptions>(options =>
                {
                    options.FallbackPolicy = new AuthorizationPolicyBuilder()
                        .RequireAssertion(_ => true)
                        .Build();
                });

                services.AddSingleton(_emailSenderMock.Object);

                services.AddSingleton<IAntiforgery>(_ => new NoOpAntiforgery());
            });
        });

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _factory?.Dispose();
        for (int i = 0; i < 3; i++)
        {
            try
            {
                if (Directory.Exists(_tempDir))
                    Directory.Delete(_tempDir, true);
                break;
            }
            catch (IOException)
            {
                Thread.Sleep(200);
            }
        }
    }

    /// <summary>
    /// Create a properly-seeded user via UserManager so Identity operations work.
    /// </summary>
    private async Task<AppUser> CreateUserAsync(
        string username = "testuser",
        string email = "test@example.com",
        string password = "TestPass123!",
        bool emailConfirmed = true)
    {
        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();

        var existing = await userManager.FindByNameAsync(username);
        if (existing is not null)
            return existing;

        var user = new AppUser
        {
            UserName = username,
            Email = email,
            EmailConfirmed = emailConfirmed
        };
        var result = await userManager.CreateAsync(user, password);
        if (!result.Succeeded)
            throw new InvalidOperationException(
                $"Failed to create test user: {string.Join("; ", result.Errors.Select(e => e.Description))}");

        return user;
    }

    private HttpClient CreateClient()
    {
        return _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
    }

    private async Task<HttpResponseMessage> PostForgotPasswordAsync(HttpClient client, string username)
    {
        var content = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("Input.Username", username)
        });
        return await client.PostAsync("/Identity/Account/ForgotPassword", content);
    }

    #region ForgotPassword — HTTP Tests

    [Fact]
    public async Task ForgotPassword_ExistingUser_RedirectsToConfirmationAndSendsEmail()
    {
        var user = await CreateUserAsync();
        var client = CreateClient();
        _emailSenderMock.Reset();

        var response = await PostForgotPasswordAsync(client, user.UserName!);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("ForgotPasswordConfirmation", response.Headers.Location?.OriginalString ?? "");

        _emailSenderMock.Verify(e => e.SendEmailAsync(
            user.Email!,
            It.Is<string>(s => s.Contains("密码重置")),
            It.Is<string>(b => b.Contains("重置密码"))
        ), Times.Once);
    }

    [Fact]
    public async Task ForgotPassword_NonexistentUser_StillRedirectsToConfirmation()
    {
        var client = CreateClient();
        _emailSenderMock.Reset();

        var response = await PostForgotPasswordAsync(client, "nonexistentuser");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("ForgotPasswordConfirmation", response.Headers.Location?.OriginalString ?? "");

        _emailSenderMock.Verify(e => e.SendEmailAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()
        ), Times.Never);
    }

    [Fact]
    public async Task ForgotPassword_EmptyUsername_ReturnsPageWithValidationError()
    {
        var client = CreateClient();

        var response = await PostForgotPasswordAsync(client, "");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("请输入用户名", body);
    }

    [Fact]
    public async Task ForgotPassword_UnconfirmedEmail_RedirectsWithoutSending()
    {
        var user = await CreateUserAsync("unconfirmed", "unconfirmed@example.com", emailConfirmed: false);
        var client = CreateClient();
        _emailSenderMock.Reset();

        var response = await PostForgotPasswordAsync(client, user.UserName!);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        _emailSenderMock.Verify(e => e.SendEmailAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()
        ), Times.Never);
    }

    #endregion

    #region ResetPassword — HTTP GET Tests

    [Fact]
    public async Task ResetPassword_Get_ValidUserIdAndCode_ShowsForm()
    {
        var user = await CreateUserAsync();
        var client = CreateClient();

        var code = "dGVzdC10b2tlbi1lbmNvZGVk";
        var response = await client.GetAsync(
            $"/Identity/Account/ResetPassword?userId={user.Id}&code={code}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("重置密码", body);
        Assert.Contains("t***t@example.com", body);
    }

    [Fact]
    public async Task ResetPassword_Get_InvalidUserId_RedirectsToConfirmation()
    {
        var client = CreateClient();

        var response = await client.GetAsync(
            "/Identity/Account/ResetPassword?userId=not-found&code=abc");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("ResetPasswordConfirmation", response.Headers.Location?.OriginalString ?? "");
    }

    [Fact]
    public async Task ResetPassword_Get_MissingCode_ReturnsBadRequest()
    {
        var user = await CreateUserAsync();
        var client = CreateClient();

        var response = await client.GetAsync(
            $"/Identity/Account/ResetPassword?userId={user.Id}");

        Assert.Equal(HttpStatusCode.BadRequest, (HttpStatusCode)response.StatusCode);
    }

    #endregion

    #region ResetPassword — PageModel Direct Tests (POST validation)

    private ResetPasswordModel CreateResetPasswordModel(UserManager<AppUser> userManager)
    {
        return new ResetPasswordModel(userManager, _emailSenderMock.Object);
    }

    [Fact]
    public async Task ResetPassword_Post_ValidPassword_Succeeds()
    {
        var user = await CreateUserAsync();
        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        _emailSenderMock.Reset();

        var realUser = await userManager.FindByIdAsync(user.Id);
        Assert.NotNull(realUser);
        var rawToken = await userManager.GeneratePasswordResetTokenAsync(realUser!);
        var encodedToken = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(rawToken));

        var model = CreateResetPasswordModel(userManager);
        model.Input = new ResetPasswordModel.InputModel
        {
            UserId = user.Id,
            Code = rawToken,
            Password = "NewPass123!",
            ConfirmPassword = "NewPass123!"
        };

        var result = await model.OnPostAsync();

        var redirect = Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal("./ResetPasswordConfirmation", redirect.PageName);

        _emailSenderMock.Verify(e => e.SendEmailAsync(
            user.Email!,
            It.Is<string>(s => s.Contains("密码已重置")),
            It.IsAny<string>()
        ), Times.Once);
    }

    [Fact]
    public async Task ResetPassword_Post_TooShortPassword_ReturnsValidationError()
    {
        var user = await CreateUserAsync();
        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();

        var model = CreateResetPasswordModel(userManager);
        model.Input = new ResetPasswordModel.InputModel
        {
            UserId = user.Id,
            Code = "anycode",
            Password = "12345",
            ConfirmPassword = "12345"
        };

        // Trigger model validation manually
        var validationResults = new List<System.ComponentModel.DataAnnotations.ValidationResult>();
        var ctx = new System.ComponentModel.DataAnnotations.ValidationContext(model.Input);
        System.ComponentModel.DataAnnotations.Validator.TryValidateObject(model.Input, ctx, validationResults, true);

        foreach (var vr in validationResults)
            model.ModelState.AddModelError(vr.MemberNames.FirstOrDefault() ?? "", vr.ErrorMessage ?? "");

        var result = await model.OnPostAsync();

        Assert.IsType<PageResult>(result);
        Assert.False(model.ModelState.IsValid);

        var messages = string.Join("; ", model.ModelState.Values
            .SelectMany(v => v.Errors).Select(e => e.ErrorMessage));
        Assert.Contains("密码长度至少为 8", messages);
    }

    [Fact]
    public async Task ResetPassword_Post_PasswordMismatch_ReturnsValidationError()
    {
        var user = await CreateUserAsync();
        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();

        var model = CreateResetPasswordModel(userManager);
        model.Input = new ResetPasswordModel.InputModel
        {
            UserId = user.Id,
            Code = "anycode",
            Password = "NewPass123!",
            ConfirmPassword = "DifferentPass!"
        };

        var validationResults = new List<System.ComponentModel.DataAnnotations.ValidationResult>();
        var ctx = new System.ComponentModel.DataAnnotations.ValidationContext(model.Input);
        System.ComponentModel.DataAnnotations.Validator.TryValidateObject(model.Input, ctx, validationResults, true);

        foreach (var vr in validationResults)
            model.ModelState.AddModelError(vr.MemberNames.FirstOrDefault() ?? "", vr.ErrorMessage ?? "");

        var result = await model.OnPostAsync();

        Assert.IsType<PageResult>(result);
        Assert.False(model.ModelState.IsValid);

        var messages = string.Join("; ", model.ModelState.Values
            .SelectMany(v => v.Errors).Select(e => e.ErrorMessage));
        Assert.Contains("密码和确认密码不匹配", messages);
    }

    [Fact]
    public async Task ResetPassword_Post_InvalidToken_ReturnsError()
    {
        var user = await CreateUserAsync();
        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();

        var model = CreateResetPasswordModel(userManager);
        model.Input = new ResetPasswordModel.InputModel
        {
            UserId = user.Id,
            Code = "InvalidTokenData",
            Password = "NewPass123!",
            ConfirmPassword = "NewPass123!"
        };

        var result = await model.OnPostAsync();

        Assert.IsType<PageResult>(result);
        Assert.False(model.ModelState.IsValid);

        var messages = string.Join("; ", model.ModelState.Values
            .SelectMany(v => v.Errors).Select(e => e.ErrorMessage));
        Assert.Contains("Invalid token", messages);
    }

    #endregion
}

internal sealed class NoOpAntiforgery : IAntiforgery
{
    public AntiforgeryTokenSet GetAndStoreTokens(HttpContext httpContext)
        => new("", "", "NoOpCookie", "NoOpForm");

    public AntiforgeryTokenSet GetTokens(HttpContext httpContext)
        => new("", "", "NoOpCookie", "NoOpForm");

    public Task<bool> IsRequestValidAsync(HttpContext httpContext)
        => Task.FromResult(true);

    public void SetCookieTokenAndHeader(HttpContext httpContext) { }

    public Task ValidateRequestAsync(HttpContext httpContext)
        => Task.CompletedTask;
}
