using System.Net;
using System.Text;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using BoxWise.Server.Data;
using BoxWise.Server.Models;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace BoxWise.Server.Tests.Endpoints;

/// <summary>
/// E2E 验证 dotnet/aspnetcore#66929：去掉 workaround 后，
/// GetTwoFactorAuthenticationUserAsync 在 .NET 10.0.11 是否能正确返回用户。
/// 流程：POST /Identity/Account/Login（密码正确 + 2FA 启用）→ 302 到 LoginWith2fa
///      → GET /Identity/Account/LoginWith2fa（框架原生 GetTwoFactorAuthenticationUserAsync）
///      → 200 表示用户成功获取；302 回 Login 表示 bug 复现（返回 null）。
/// </summary>
public class TwoFactorLoginE2ETests : IDisposable
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly string _tempDir;

    public TwoFactorLoginE2ETests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"boxwise-2fa-login-{Guid.NewGuid()}");
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

                services.AddSingleton<IAntiforgery>(_ => new NoOpAntiforgery2fa());
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
    /// 创建启用 TOTP 2FA 的用户（通过 UserManager 种子，与生产路径一致）。
    /// </summary>
    private async Task<AppUser> CreateTwoFactorUserAsync(
        string username = "2fauser",
        string password = "TestPass123!")
    {
        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();

        var user = new AppUser
        {
            UserName = username,
            Email = $"{username}@example.com",
            EmailConfirmed = true,
            TwoFactorEnabled = true,
            ConfiguredMethods = TwoFactorMethod.TOTP
        };
        var result = await userManager.CreateAsync(user, password);
        if (!result.Succeeded)
            throw new InvalidOperationException(
                $"Failed to create 2FA user: {string.Join("; ", result.Errors.Select(e => e.Description))}");

        await userManager.ResetAuthenticatorKeyAsync(user);
        return user;
    }

    private HttpClient CreateClient()
    {
        return _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true
        });
    }

    private static async Task<HttpResponseMessage> PostLoginAsync(HttpClient client, string username, string password)
    {
        var content = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("Input.Username", username),
            new KeyValuePair<string, string>("Input.Password", password),
            new KeyValuePair<string, string>("Input.RememberMe", "false")
        });
        return await client.PostAsync("/Identity/Account/Login", content);
    }

    /// <summary>
    /// 核心：密码正确 + 2FA 启用 → Login 页 302 到 LoginWith2fa →
    /// GET LoginWith2fa 应返回 200（GetTwoFactorAuthenticationUserAsync 成功）。
    /// 若 bug 复现，OnGetAsync 会 RedirectToPage("./Login") → 302。
    /// </summary>
    [Fact]
    public async Task LoginWith2fa_Get_Returns200_WhenTwoFactorCookiePresent()
    {
        var user = await CreateTwoFactorUserAsync();
        var client = CreateClient();

        // 1. POST login → 期望 302 到 LoginWith2fa（RequiresTwoFactor）
        var loginResponse = await PostLoginAsync(client, user.UserName!, "TestPass123!");
        Assert.Equal(HttpStatusCode.Redirect, loginResponse.StatusCode);
        var location = loginResponse.Headers.Location?.OriginalString ?? "";
        Assert.Contains("LoginWith2fa", location);

        // 2. GET LoginWith2fa — 这是关键验证点。
        //    框架原生 GetTwoFactorAuthenticationUserAsync 必须从 Identity.TwoFactorUserId
        //    cookie 返回用户。成功 → 200；返回 null（bug）→ 302 回 Login。
        var twoFaResponse = await client.GetAsync(location);

        Assert.Equal(HttpStatusCode.OK, twoFaResponse.StatusCode);
        var body = await twoFaResponse.Content.ReadAsStringAsync();
        Assert.Contains("验证器", body);
    }

    /// <summary>
    /// 反向验证：无 TwoFactorUserId cookie 直接访问 LoginWith2fa → 应 302 回 Login。
    /// </summary>
    [Fact]
    public async Task LoginWith2fa_Get_RedirectsToLogin_WhenNoCookie()
    {
        var client = CreateClient();

        var response = await client.GetAsync("/Identity/Account/LoginWith2fa?rememberMe=false&returnUrl=%2F");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("Login", response.Headers.Location?.OriginalString ?? "");
        Assert.DoesNotContain("LoginWith2fa", response.Headers.Location?.OriginalString ?? "");
    }

    /// <summary>
    /// 验证 LoginWithRecoveryCode 页面也能正确获取 2FA 用户（同样使用
    /// GetTwoFactorAuthenticationUserAsync，去掉 workaround 后的路径）。
    /// </summary>
    [Fact]
    public async Task LoginWithRecoveryCode_Get_Returns200_WhenTwoFactorCookiePresent()
    {
        var user = await CreateTwoFactorUserAsync();
        var client = CreateClient();

        var loginResponse = await PostLoginAsync(client, user.UserName!, "TestPass123!");
        Assert.Equal(HttpStatusCode.Redirect, loginResponse.StatusCode);
        Assert.Contains("LoginWith2fa", loginResponse.Headers.Location?.OriginalString ?? "");

        // 导航到恢复码页面（带 2FA cookie）
        var recoveryResponse = await client.GetAsync("/Identity/Account/LoginWithRecoveryCode?returnUrl=%2F");

        Assert.Equal(HttpStatusCode.OK, recoveryResponse.StatusCode);
        var body = await recoveryResponse.Content.ReadAsStringAsync();
        Assert.Contains("恢复码", body);
    }
}

internal sealed class NoOpAntiforgery2fa : IAntiforgery
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