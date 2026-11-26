using System.Net;
using System.Text;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using BoxWise.Server.Data;
using BoxWise.Server.Models;
using BoxWise.Server.Services;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OtpNet;

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

    private async Task<List<string>> StoreRecoveryCodesAsync(string userId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var service = new RecoveryCodeService(db);

        var user = await userManager.FindByIdAsync(userId)
            ?? throw new InvalidOperationException("Test user not found");
        var codes = RecoveryCodeService.GenerateRecoveryCodes();
        await service.StoreRecoveryCodesAsync(user, codes);
        return codes;
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

    /// <summary>
    /// 恢复码验证成功后：2FA 关闭、全部恢复码删除、SecurityStamp 变化，
    /// 且用户可以用密码重新登录（不建立恢复码登录会话）。
    /// </summary>
    [Fact]
    public async Task RecoveryCode_Post_DisablesTwoFactor_AndAllowsPasswordRelogin()
    {
        var user = await CreateTwoFactorUserAsync("recovery-ok");
        var codes = await StoreRecoveryCodesAsync(user.Id);
        var stampBefore = user.SecurityStamp;
        var client = CreateClient();

        var loginResponse = await PostLoginAsync(client, user.UserName!, "TestPass123!");
        Assert.Equal(HttpStatusCode.Redirect, loginResponse.StatusCode);
        Assert.Contains("LoginWith2fa", loginResponse.Headers.Location?.OriginalString ?? "");

        // 小写 + 前后空格应被规范化后接受。
        var codeResponse = await client.PostAsync(
            "/Identity/Account/LoginWithRecoveryCode",
            new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("Input.RecoveryCode", $"  {codes[0].ToLowerInvariant()}  ")
            }));

        Assert.Equal(HttpStatusCode.Redirect, codeResponse.StatusCode);
        Assert.Contains("Login", codeResponse.Headers.Location?.OriginalString ?? "");
        Assert.DoesNotContain("LoginWithRecoveryCode", codeResponse.Headers.Location?.OriginalString ?? "");

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            var updated = await userManager.FindByIdAsync(user.Id);

            Assert.NotNull(updated);
            Assert.False(updated!.TwoFactorEnabled);
            Assert.Equal(TwoFactorMethod.None, updated.ConfiguredMethods);
            Assert.Null(updated.TotpSecretKey);
            Assert.Null(updated.EmailForTwoFactor);
            Assert.Null(updated.TwoFactorSetupCompletedAt);
            Assert.NotNull(updated.TwoFactorGracePeriodUntil);
            Assert.NotEqual(stampBefore, updated.SecurityStamp);
            Assert.Equal(0, await db.RecoveryCodes.CountAsync(rc => rc.UserId == user.Id));
        }

        // 恢复码不建立登录会话：重新用密码登录应直接成功。
        var reloginResponse = await PostLoginAsync(client, user.UserName!, "TestPass123!");
        Assert.Equal(HttpStatusCode.Redirect, reloginResponse.StatusCode);
        Assert.Equal("/", reloginResponse.Headers.Location?.OriginalString);
    }

    /// <summary>
    /// Identity token store 的恢复码（TOTP 用户在 Manage 页生成）也应在恢复码页关闭 2FA：
    /// 本次码被消耗后，关闭 2FA 会清空剩余 Identity 码与全部自定义码、SecurityStamp 变化，
    /// 随后密码可重登。
    /// </summary>
    [Fact]
    public async Task IdentityRecoveryCode_Post_DisablesTwoFactor_ClearsAllCodes_AndAllowsPasswordRelogin()
    {
        var user = await CreateTwoFactorUserAsync("recovery-identity");
        string identityCode;
        string stampBefore;

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            var fresh = await userManager.FindByIdAsync(user.Id);
            Assert.NotNull(fresh);

            var codes = await userManager.GenerateNewTwoFactorRecoveryCodesAsync(fresh!, 10);
            Assert.NotNull(codes);
            identityCode = codes!.First();
            stampBefore = fresh!.SecurityStamp!;

            Assert.True(fresh.TwoFactorEnabled);
            Assert.Equal(10, await userManager.CountRecoveryCodesAsync(fresh));
            Assert.Equal(0, await db.RecoveryCodes.CountAsync(rc => rc.UserId == user.Id));
        }

        var client = CreateClient();
        var loginResponse = await PostLoginAsync(client, user.UserName!, "TestPass123!");
        Assert.Equal(HttpStatusCode.Redirect, loginResponse.StatusCode);
        Assert.Contains("LoginWith2fa", loginResponse.Headers.Location?.OriginalString ?? "");

        // Identity 恢复码含连字符；小写 + 前后空格应被规范化后接受。
        var codeResponse = await client.PostAsync(
            "/Identity/Account/LoginWithRecoveryCode",
            new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("Input.RecoveryCode", $"  {identityCode.ToLowerInvariant()}  ")
            }));

        Assert.Equal(HttpStatusCode.Redirect, codeResponse.StatusCode);
        Assert.Contains("Login", codeResponse.Headers.Location?.OriginalString ?? "");
        Assert.DoesNotContain("LoginWithRecoveryCode", codeResponse.Headers.Location?.OriginalString ?? "");

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            var updated = await userManager.FindByIdAsync(user.Id);

            Assert.NotNull(updated);
            Assert.False(updated!.TwoFactorEnabled);
            Assert.Equal(TwoFactorMethod.None, updated.ConfiguredMethods);
            Assert.Null(updated.TotpSecretKey);
            Assert.Null(updated.EmailForTwoFactor);
            Assert.Null(updated.TwoFactorSetupCompletedAt);
            Assert.NotNull(updated.TwoFactorGracePeriodUntil);
            Assert.NotEqual(stampBefore, updated.SecurityStamp);
            Assert.Equal(0, await userManager.CountRecoveryCodesAsync(updated));
            Assert.Equal(0, await db.RecoveryCodes.CountAsync(rc => rc.UserId == user.Id));
        }

        var reloginResponse = await PostLoginAsync(client, user.UserName!, "TestPass123!");
        Assert.Equal(HttpStatusCode.Redirect, reloginResponse.StatusCode);
        Assert.Equal("/", reloginResponse.Headers.Location?.OriginalString);
    }

    /// <summary>
    /// 错误恢复码不应产生任何状态变更（2FA、恢复码、安全戳均保持不变）。
    /// </summary>
    [Fact]
    public async Task RecoveryCode_Post_InvalidCode_DoesNotChangeState()
    {
        var user = await CreateTwoFactorUserAsync("recovery-bad");
        await StoreRecoveryCodesAsync(user.Id);
        var client = CreateClient();

        var loginResponse = await PostLoginAsync(client, user.UserName!, "TestPass123!");
        Assert.Equal(HttpStatusCode.Redirect, loginResponse.StatusCode);
        Assert.Contains("LoginWith2fa", loginResponse.Headers.Location?.OriginalString ?? "");

        string? stampBefore;
        using (var scope = _factory.Services.CreateScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            stampBefore = (await userManager.FindByIdAsync(user.Id))!.SecurityStamp;
        }

        var codeResponse = await client.PostAsync(
            "/Identity/Account/LoginWithRecoveryCode",
            new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("Input.RecoveryCode", "ZZZZZZZZZZ")
            }));

        Assert.Equal(HttpStatusCode.OK, codeResponse.StatusCode);
        var body = System.Net.WebUtility.HtmlDecode(
            await codeResponse.Content.ReadAsStringAsync());
        Assert.Contains("恢复码无效", body);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            var unchanged = await userManager.FindByIdAsync(user.Id);

            Assert.NotNull(unchanged);
            Assert.True(unchanged!.TwoFactorEnabled);
            Assert.Equal(TwoFactorMethod.TOTP, unchanged.ConfiguredMethods);
            Assert.Equal(stampBefore, unchanged.SecurityStamp);
            Assert.Equal(8, await db.RecoveryCodes.CountAsync(rc => rc.UserId == user.Id));
        }
    }

    /// <summary>
    /// A2：登录态下自助关闭 2FA 保留 Identity 验证器密钥、通行密钥和既有宽限期，
    /// 清空两套恢复码并轮换安全戳。
    /// </summary>
    [Fact]
    public async Task Disable2fa_Post_PreservesKeyPasskeysAndGrace_AndRotatesStamp()
    {
        var user = await CreateTwoFactorUserAsync("self-disable-e2e");
        var grace = DateTime.UtcNow.AddHours(12);
        string? keyBefore;
        string? stampBefore;

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            var fresh = await userManager.FindByIdAsync(user.Id);
            Assert.NotNull(fresh);

            // 认证器密钥来自 Identity token store；同时补一套自定义恢复码和通行密钥。
            keyBefore = await userManager.GetAuthenticatorKeyAsync(fresh!);
            Assert.False(string.IsNullOrEmpty(keyBefore));
            await userManager.GenerateNewTwoFactorRecoveryCodesAsync(fresh!, 10);

            var service = new RecoveryCodeService(db);
            await service.StoreRecoveryCodesAsync(fresh!, RecoveryCodeService.GenerateRecoveryCodes());

            db.WebAuthnCredentials.Add(new WebAuthnCredential
            {
                UserId = fresh!.Id,
                CredentialId = Convert.ToBase64String(Guid.NewGuid().ToByteArray()),
                PublicKey = Convert.ToBase64String(Guid.NewGuid().ToByteArray()),
                DeviceName = "测试通行密钥"
            });

            fresh.TwoFactorGracePeriodUntil = grace;
            await userManager.UpdateAsync(fresh);
            stampBefore = fresh.SecurityStamp;
        }

        var client = CreateClient();
        var loginResponse = await PostLoginAsync(client, user.UserName!, "TestPass123!");
        Assert.Equal(HttpStatusCode.Redirect, loginResponse.StatusCode);
        Assert.Contains("LoginWith2fa", loginResponse.Headers.Location?.OriginalString ?? "");

        // 用真实 TOTP 完成第二步登录，才能以登录态访问 Manage 页面。
        var totp = new Totp(Base32Encoding.ToBytes(keyBefore!));
        var twoFactorResponse = await client.PostAsync(
            loginResponse.Headers.Location!.OriginalString,
            new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("Input.TwoFactorCode", totp.ComputeTotp()),
                new KeyValuePair<string, string>("Input.RememberMachine", "false")
            }));
        Assert.Equal(HttpStatusCode.Redirect, twoFactorResponse.StatusCode);

        var disable = await client.PostAsync("/Identity/Account/Manage/Disable2fa", content: null);
        Assert.Equal(HttpStatusCode.Redirect, disable.StatusCode);
        Assert.Contains("TwoFactorAuthentication", disable.Headers.Location?.OriginalString ?? "");

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            var updated = await userManager.FindByIdAsync(user.Id);

            Assert.NotNull(updated);
            Assert.False(updated!.TwoFactorEnabled);
            Assert.Equal(TwoFactorMethod.None, updated.ConfiguredMethods);
            Assert.Null(updated.TotpSecretKey);
            Assert.Equal(grace, updated.TwoFactorGracePeriodUntil);
            Assert.NotEqual(stampBefore, updated.SecurityStamp);

            Assert.Equal(keyBefore, await userManager.GetAuthenticatorKeyAsync(updated));
            Assert.Equal(1, await db.WebAuthnCredentials.CountAsync(c => c.UserId == user.Id));
            Assert.Equal(0, await db.RecoveryCodes.CountAsync(rc => rc.UserId == user.Id));
            Assert.Equal(0, await userManager.CountRecoveryCodesAsync(updated));
        }
    }

    /// <summary>
    /// 验收标准：A2 关闭后重新设置 TOTP，用户会拿到新的恢复码，旧恢复码不可再用。
    /// 走真实 EnableAuthenticator 路由，不另建夹具。
    /// </summary>
    [Fact]
    public async Task EnableAuthenticator_AfterDisable_IssuesNewCodes_AndOldCodesFail()
    {
        var user = await CreateTwoFactorUserAsync("re-setup-e2e");
        string keyBefore;
        string oldIdentityCode;

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            var fresh = await userManager.FindByIdAsync(user.Id);
            Assert.NotNull(fresh);

            keyBefore = (await userManager.GetAuthenticatorKeyAsync(fresh!))!;
            Assert.False(string.IsNullOrEmpty(keyBefore));

            // 两套旧恢复码都在。
            oldIdentityCode = (await userManager.GenerateNewTwoFactorRecoveryCodesAsync(fresh!, 10))!.First();
            await new RecoveryCodeService(db)
                .StoreRecoveryCodesAsync(fresh!, ["CUSTOMCODE1", "CUSTOMCODE2"]);
        }

        var client = CreateClient();
        await CompleteTwoFactorLoginAsync(client, keyBefore);

        var disable = await client.PostAsync("/Identity/Account/Manage/Disable2fa", content: null);
        Assert.Equal(HttpStatusCode.Redirect, disable.StatusCode);

        // 真实 EnableAuthenticator POST：沿用保留的验证器密钥验证成功。
        var totp = new Totp(Base32Encoding.ToBytes(keyBefore));
        var enable = await client.PostAsync(
            "/Identity/Account/Manage/EnableAuthenticator",
            new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("Input.Code", totp.ComputeTotp())
            }));

        Assert.Equal(HttpStatusCode.Redirect, enable.StatusCode);
        Assert.Contains(
            "GenerateRecoveryCodes",
            enable.Headers.Location?.OriginalString ?? "");

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            var updated = await userManager.FindByIdAsync(user.Id);

            Assert.NotNull(updated);
            Assert.True(updated!.TwoFactorEnabled);
            Assert.True(updated.ConfiguredMethods.HasFlag(TwoFactorMethod.TOTP));

            // 关闭 2FA 已把计数清零，因此重新启用会发新的 10 个 Identity 码。
            Assert.Equal(10, await userManager.CountRecoveryCodesAsync(updated));

            // 旧 Identity 码与旧自定义码都不可再用。
            var oldIdentity = await userManager.RedeemTwoFactorRecoveryCodeAsync(updated, oldIdentityCode);
            Assert.False(oldIdentity.Succeeded);
            var service = new RecoveryCodeService(db);
            Assert.False(await service.ValidateRecoveryCodeAsync(updated, "CUSTOMCODE1"));
        }
    }

    private static async Task CompleteTwoFactorLoginAsync(HttpClient client, string authenticatorKey)
    {
        var loginResponse = await PostLoginAsync(client, "re-setup-e2e", "TestPass123!");
        Assert.Equal(HttpStatusCode.Redirect, loginResponse.StatusCode);
        Assert.Contains("LoginWith2fa", loginResponse.Headers.Location?.OriginalString ?? "");

        var totp = new Totp(Base32Encoding.ToBytes(authenticatorKey));
        var twoFactorResponse = await client.PostAsync(
            loginResponse.Headers.Location!.OriginalString,
            new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("Input.TwoFactorCode", totp.ComputeTotp()),
                new KeyValuePair<string, string>("Input.RememberMachine", "false")
            }));
        Assert.Equal(HttpStatusCode.Redirect, twoFactorResponse.StatusCode);
    }

    private static async Task PasswordLoginAsync(HttpClient client, string username, string password)
    {
        var loginResponse = await PostLoginAsync(client, username, password);
        Assert.Equal(HttpStatusCode.Redirect, loginResponse.StatusCode);
        Assert.Equal("/", loginResponse.Headers.Location?.OriginalString);
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