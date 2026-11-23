using System.Net;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using BoxWise.Server.Data;
using BoxWise.Server.Models;
using BoxWise.Server.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BoxWise.Server.Tests.Endpoints;

/// <summary>
/// 验证删除最后一个 WebAuthn 凭据时：
/// 若 2FA 因此被关闭，则同时删除全部恢复码并更新 SecurityStamp；
/// 若仍有 TOTP/Email，则恢复码和安全戳保持不变。
/// </summary>
public class WebAuthnCredentialDeleteE2ETests : IDisposable
{
    private const string TestUserId = "test-user-id";

    private readonly WebApplicationFactory<Program> _factory;
    private readonly string _tempDir;

    public WebAuthnCredentialDeleteE2ETests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"boxwise-webauthn-delete-{Guid.NewGuid()}");
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

                services.AddAuthentication(options =>
                {
                    options.DefaultScheme = TestAuthHandler.SchemeName;
                    options.DefaultAuthenticateScheme = TestAuthHandler.SchemeName;
                    options.DefaultChallengeScheme = TestAuthHandler.SchemeName;
                })
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(
                    TestAuthHandler.SchemeName, _ => { });
            });
        });
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

    private async Task<(int CredentialId, string SecurityStamp)> SeedUserAsync(
        TwoFactorMethod configuredMethods)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();

        var user = new AppUser
        {
            Id = TestUserId,
            UserName = "webauthn-delete-user",
            Email = "webauthn-delete@example.com",
            TwoFactorEnabled = true,
            ConfiguredMethods = configuredMethods
        };
        var createResult = await userManager.CreateAsync(user);
        if (!createResult.Succeeded)
            throw new InvalidOperationException(
                $"Failed to create user: {string.Join("; ", createResult.Errors.Select(e => e.Description))}");

        var credential = new WebAuthnCredential
        {
            UserId = user.Id,
            CredentialId = Convert.ToBase64String(Guid.NewGuid().ToByteArray()),
            PublicKey = Convert.ToBase64String(Guid.NewGuid().ToByteArray()),
            DeviceName = "测试密钥",
            CreatedAt = DateTime.UtcNow
        };
        db.WebAuthnCredentials.Add(credential);

        var recoveryCodeService = new RecoveryCodeService(db);
        await recoveryCodeService.StoreRecoveryCodesAsync(
            user, RecoveryCodeService.GenerateRecoveryCodes());

        await db.SaveChangesAsync();
        return (credential.Id, user.SecurityStamp!);
    }

    private HttpClient CreateClient() => _factory.CreateClient();

    private static HttpRequestMessage CreateDeleteRequest(int credentialId)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Delete, $"/api/auth/webauthn/credentials/{credentialId}");
        request.Headers.Add("X-Requested-With", "XMLHttpRequest");
        return request;
    }

    [Fact]
    public async Task DeleteLastCredential_Disables2fa_ClearsRecoveryCodes_AndRotatesSecurityStamp()
    {
        var (credentialId, stampBefore) = await SeedUserAsync(TwoFactorMethod.WebAuthn);
        var client = CreateClient();

        var response = await client.SendAsync(CreateDeleteRequest(credentialId));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();

        var updated = await userManager.FindByIdAsync(TestUserId);
        Assert.NotNull(updated);
        Assert.False(updated!.TwoFactorEnabled);
        Assert.Equal(TwoFactorMethod.None, updated.ConfiguredMethods);
        Assert.NotEqual(stampBefore, updated.SecurityStamp);
        Assert.Equal(0, await db.RecoveryCodes.CountAsync(rc => rc.UserId == TestUserId));
        Assert.Equal(0, await db.WebAuthnCredentials.CountAsync(c => c.UserId == TestUserId));
    }

    [Fact]
    public async Task DeleteLastCredential_WithTotpConfigured_KeepsRecoveryCodesAndSecurityStamp()
    {
        var (credentialId, stampBefore) = await SeedUserAsync(
            TwoFactorMethod.WebAuthn | TwoFactorMethod.TOTP);
        var client = CreateClient();

        var response = await client.SendAsync(CreateDeleteRequest(credentialId));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();

        var updated = await userManager.FindByIdAsync(TestUserId);
        Assert.NotNull(updated);
        Assert.True(updated!.TwoFactorEnabled);
        Assert.Equal(TwoFactorMethod.TOTP, updated.ConfiguredMethods);
        Assert.Equal(stampBefore, updated.SecurityStamp);
        Assert.Equal(8, await db.RecoveryCodes.CountAsync(rc => rc.UserId == TestUserId));
    }
}
