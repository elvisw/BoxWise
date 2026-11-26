using System.Net;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using BoxWise.Server.Data;
using BoxWise.Server.Models;
using BoxWise.Server.Services;
using OtpNet;

namespace BoxWise.Server.Tests.Endpoints;

/// <summary>
/// A1 管理员重置的 HTTP 契约：真实登录 + 真实 API 路由 + 真实 SQLite。
/// 权限（403）与 Razor 页面路径由既有测试覆盖，这里只补 API 独有行为。
/// </summary>
public class TwoFactorCleanupE2ETests : IDisposable
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly string _tempDir;

    public TwoFactorCleanupE2ETests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"boxwise-2fa-cleanup-{Guid.NewGuid()}");
        Directory.CreateDirectory(_tempDir);
        var sqlitePath = Path.Combine(_tempDir, "test.db");

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("DataDirectory", _tempDir);
            builder.UseSetting("ConnectionStrings:DefaultConnection", $"Data Source={sqlitePath}");

            var commitInterceptor = new FailOnCommitInterceptor();
            var saveInterceptor = new FailOnSaveInterceptor();

            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<IAntiforgery>(_ => new NoOpAntiforgery2fa());

                // 追加 options 配置动作，仅在失败路径测试中武装；其余测试不受影响。
                services.AddDbContext<AppDbContext>(options =>
                    options.AddInterceptors(commitInterceptor, saveInterceptor));
                services.AddSingleton(commitInterceptor);
                services.AddSingleton(saveInterceptor);
            });
        });

        using var scope = _factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.EnsureCreated();
    }

    public void Dispose()
    {
        _factory.Dispose();
        for (var i = 0; i < 3; i++)
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

    [Fact]
    public async Task AdminReset_RevokesPasskey_RotatesAuthenticatorKey_AndClearsEverything()
    {
        var admin = await CreateUserAsync("reset-admin", role: "Admin");
        var target = await CreateTwoFactorUserAsync("reset-target");
        var keyBefore = target.AuthenticatorKey;
        var stampBefore = target.SecurityStamp;

        using var client = CreateClient();
        await PasswordLoginAsync(client, admin.UserName!, "TestPass123!");

        var response = await PostResetAsync(client, target.UserId);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var updated = await userManager.FindByIdAsync(target.UserId);

        Assert.NotNull(updated);
        Assert.False(updated!.TwoFactorEnabled);
        Assert.Equal(TwoFactorMethod.None, updated.ConfiguredMethods);
        Assert.Null(updated.TotpSecretKey);
        Assert.Null(updated.PendingTotpSecretKey);
        Assert.Null(updated.EmailForTwoFactor);
        Assert.Null(updated.TwoFactorSetupCompletedAt);
        Assert.Null(updated.TwoFactorGracePeriodUntil);
        Assert.NotEqual(stampBefore, updated.SecurityStamp);

        Assert.Equal(0, await db.RecoveryCodes.CountAsync(rc => rc.UserId == target.UserId));
        Assert.Equal(0, await db.WebAuthnCredentials.CountAsync(wc => wc.UserId == target.UserId));
        Assert.Equal(0, await userManager.CountRecoveryCodesAsync(updated));

        // 旧验证器密钥不可再用。
        var rotatedKey = await userManager.GetAuthenticatorKeyAsync(updated);
        Assert.NotNull(rotatedKey);
        Assert.NotEqual(keyBefore, rotatedKey);
        var oldCode = new Totp(Base32Encoding.ToBytes(keyBefore!)).ComputeTotp();
        Assert.False(await userManager.VerifyTwoFactorTokenAsync(
            updated, userManager.Options.Tokens.AuthenticatorTokenProvider, oldCode));
    }

    /// <summary>
    /// 保留既有授权边界：非管理员调用重置端点得到 403，目标状态不变。
    /// </summary>
    [Fact]
    public async Task AdminReset_NonAdmin_IsForbidden_AndLeavesStateUntouched()
    {
        var nonAdmin = await CreateUserAsync("reset-nonadmin");
        var target = await CreateTwoFactorUserAsync("reset-protected");
        var stampBefore = target.SecurityStamp;

        using var client = CreateClient();
        await PasswordLoginAsync(client, nonAdmin.UserName!, "TestPass123!");

        var response = await PostResetAsync(client, target.UserId);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var updated = await userManager.FindByIdAsync(target.UserId);

        Assert.NotNull(updated);
        Assert.True(updated!.TwoFactorEnabled);
        Assert.Equal(stampBefore, updated.SecurityStamp);
        Assert.Equal(2, await db.RecoveryCodes.CountAsync(rc => rc.UserId == target.UserId));
        Assert.Equal(1, await db.WebAuthnCredentials.CountAsync(wc => wc.UserId == target.UserId));
    }

    /// <summary>
    /// A1 + 第 5 项：提交失败时 API 必须保持 500，但用不同 detail 区分
    /// “已回滚、未做更改”与“提交结果未知、需先核对状态”。
    /// </summary>
    [Fact]
    public async Task AdminReset_CommitFailure_Returns500WithUnconfirmedDetail()
    {
        var admin = await CreateUserAsync("reset-admin-commit", role: "Admin");
        var target = await CreateTwoFactorUserAsync("reset-commit-target");

        using var client = CreateClient();
        await PasswordLoginAsync(client, admin.UserName!, "TestPass123!");

        _factory.Services.GetRequiredService<FailOnCommitInterceptor>().Arm();

        var response = await PostResetAsync(client, target.UserId);
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("unconfirmed", body);
        Assert.DoesNotContain("no changes were committed", body);
    }

    /// <summary>
    /// 第 5 项的已回滚分支：写入失败时 detail 必须明确“未做任何更改”。
    /// </summary>
    [Fact]
    public async Task AdminReset_SaveFailure_Returns500WithRolledBackDetail()
    {
        var admin = await CreateUserAsync("reset-admin-save", role: "Admin");
        var target = await CreateTwoFactorUserAsync("reset-save-target");

        using var client = CreateClient();
        await PasswordLoginAsync(client, admin.UserName!, "TestPass123!");

        // 第一次保存（删除自定义码 + 关闭 2FA）后失败，事务已回滚。
        _factory.Services.GetRequiredService<FailOnSaveInterceptor>().FailOnSave(2);

        var response = await PostResetAsync(client, target.UserId);
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("no changes were committed", body);
    }

    // ── helpers ─────────────────────────────────────────────────────────

    private HttpClient CreateClient() => _factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        AllowAutoRedirect = false,
        HandleCookies = true
    });

    private static async Task<HttpResponseMessage> PostResetAsync(HttpClient client, string userId)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Post, $"/api/admin/users/{userId}/two-factor/reset");
        request.Headers.Add("X-Requested-With", "XMLHttpRequest");
        return await client.SendAsync(request);
    }

    private static async Task PasswordLoginAsync(HttpClient client, string username, string password)
    {
        var response = await client.PostAsync(
            "/Identity/Account/Login",
            new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("Input.Username", username),
                new KeyValuePair<string, string>("Input.Password", password),
                new KeyValuePair<string, string>("Input.RememberMe", "false")
            }));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/", response.Headers.Location?.OriginalString);
    }

    private async Task<AppUser> CreateUserAsync(string username, string? role = null)
    {
        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();

        var user = new AppUser
        {
            UserName = username,
            Email = $"{username}@example.com",
            EmailConfirmed = true
        };
        var result = await userManager.CreateAsync(user, "TestPass123!");
        Assert.True(result.Succeeded);

        if (role is not null)
            Assert.True((await userManager.AddToRoleAsync(user, role)).Succeeded);

        return user;
    }

    /// <summary>
    /// 通过真实 Identity API 准备 2FA 状态：验证器密钥、Identity 恢复码、自定义恢复码、通行密钥。
    /// </summary>
    private async Task<SeededTarget> CreateTwoFactorUserAsync(string username)
    {
        var user = await CreateUserAsync(username);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();

        var tracked = await userManager.FindByIdAsync(user.Id);
        Assert.NotNull(tracked);

        tracked!.TwoFactorEnabled = true;
        tracked.ConfiguredMethods = TwoFactorMethod.TOTP | TwoFactorMethod.WebAuthn;
        tracked.TotpSecretKey = "legacy-totp-secret";
        tracked.PendingTotpSecretKey = "legacy-pending-totp-secret";
        tracked.EmailForTwoFactor = tracked.Email;
        tracked.TwoFactorSetupCompletedAt = DateTime.UtcNow.AddDays(-1);
        tracked.TwoFactorGracePeriodUntil = DateTime.UtcNow.AddHours(12);
        Assert.True((await userManager.UpdateAsync(tracked)).Succeeded);

        Assert.True((await userManager.ResetAuthenticatorKeyAsync(tracked)).Succeeded);
        Assert.NotNull(await userManager.GenerateNewTwoFactorRecoveryCodesAsync(tracked, 4));
        await new RecoveryCodeService(db).StoreRecoveryCodesAsync(
            tracked, ["CUSTOMCODE1", "CUSTOMCODE2"]);

        db.WebAuthnCredentials.Add(new WebAuthnCredential
        {
            UserId = tracked.Id,
            CredentialId = Convert.ToBase64String(Guid.NewGuid().ToByteArray()),
            PublicKey = Convert.ToBase64String(Guid.NewGuid().ToByteArray()),
            DeviceName = "测试通行密钥",
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        return new SeededTarget(
            tracked.Id,
            await userManager.GetAuthenticatorKeyAsync(tracked),
            tracked.SecurityStamp);
    }

    private sealed record SeededTarget(string UserId, string? AuthenticatorKey, string? SecurityStamp);
}
