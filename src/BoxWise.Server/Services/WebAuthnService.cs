using System.Text;
using Fido2NetLib;
using Fido2NetLib.Objects;
using Microsoft.EntityFrameworkCore;
using BoxWise.Server.Data;
using BoxWise.Server.Models;

namespace BoxWise.Server.Services;

public class WebAuthnService
{
    private readonly IFido2 _fido2;
    private readonly AppDbContext _db;

    public WebAuthnService(IFido2 fido2, AppDbContext db)
    {
        _fido2 = fido2;
        _db = db;
    }

    public static bool IsOriginSupported(string origin)
    {
        if (string.IsNullOrWhiteSpace(origin)) return false;
        if (origin.StartsWith("https://localhost", StringComparison.OrdinalIgnoreCase)) return true;
        if (!origin.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return false;
        try
        {
            var uri = new Uri(origin);
            return Uri.CheckHostName(uri.Host) == UriHostNameType.Dns;
        }
        catch { return false; }
    }

    public async Task<CredentialCreateOptions> StartRegistration(AppUser user)
    {
        var existing = await _db.WebAuthnCredentials
            .Where(c => c.UserId == user.Id).ToListAsync();

        if (existing.Count >= 10)
            throw new InvalidOperationException("已达凭证数量上限（10个）。");

        var excludeCredentials = existing
            .Select(c => new PublicKeyCredentialDescriptor(Convert.FromBase64String(c.CredentialId)))
            .ToList();

        return _fido2.RequestNewCredential(new RequestNewCredentialParams
        {
            User = new Fido2User
            {
                Id = Encoding.UTF8.GetBytes(user.Id),
                Name = user.UserName ?? user.Id,
                DisplayName = user.UserName ?? user.Id
            },
            ExcludeCredentials = excludeCredentials,
            AuthenticatorSelection = new AuthenticatorSelection
            {
                UserVerification = UserVerificationRequirement.Preferred,
                ResidentKey = ResidentKeyRequirement.Required
            },
            AttestationPreference = AttestationConveyancePreference.None
        });
    }

    public async Task<bool> CompleteRegistration(
        AppUser user,
        AuthenticatorAttestationRawResponse attestation,
        CredentialCreateOptions options,
        string deviceName)
    {
        try
        {
            var result = await _fido2.MakeNewCredentialAsync(new MakeNewCredentialParams
            {
                AttestationResponse = attestation,
                OriginalOptions = options,
                IsCredentialIdUniqueToUserCallback = (args, ct) =>
                    Task.FromResult(!_db.WebAuthnCredentials.Any(c =>
                        c.UserId == user.Id
                        && c.CredentialId == Convert.ToBase64String(args.CredentialId)))
            });

            _db.WebAuthnCredentials.Add(new WebAuthnCredential
            {
                UserId = user.Id,
                CredentialId = Convert.ToBase64String(result.Id),
                PublicKey = Convert.ToBase64String(result.PublicKey),
                SignCount = (int)result.SignCount,
                DeviceName = deviceName,
                CreatedAt = DateTime.UtcNow
            });

            await _db.SaveChangesAsync();
            return true;
        }
        catch { return false; }
    }

    public async Task<AssertionOptions?> StartVerification(AppUser user)
    {
        var credentials = await _db.WebAuthnCredentials
            .Where(c => c.UserId == user.Id).ToListAsync();

        if (credentials.Count == 0) return null;

        var allowedCredentials = credentials
            .Select(c => new PublicKeyCredentialDescriptor(Convert.FromBase64String(c.CredentialId)))
            .ToList();

        return _fido2.GetAssertionOptions(new GetAssertionOptionsParams
        {
            AllowedCredentials = allowedCredentials,
            UserVerification = UserVerificationRequirement.Discouraged
        });
    }

    public async Task<bool> CompleteVerification(
        AppUser user,
        AuthenticatorAssertionRawResponse assertion,
        AssertionOptions options)
    {
        try
        {
            var credentials = await _db.WebAuthnCredentials
                .Where(c => c.UserId == user.Id).ToListAsync();

            foreach (var credential in credentials)
            {
                try
                {
                    var storedPublicKey = Convert.FromBase64String(credential.PublicKey);
                    var result = await _fido2.MakeAssertionAsync(new MakeAssertionParams
                    {
                        AssertionResponse = assertion,
                        OriginalOptions = options,
                        StoredPublicKey = storedPublicKey,
                        StoredSignatureCounter = (uint)credential.SignCount,
                        IsUserHandleOwnerOfCredentialIdCallback = (args, ct) =>
                            Task.FromResult(credential.CredentialId
                                == Convert.ToBase64String(args.CredentialId))
                    });

                    credential.SignCount = (int)result.SignCount;
                    await _db.SaveChangesAsync();
                    return true;
                }
                catch { /* try next credential */ }
            }
            return false;
        }
        catch { return false; }
    }

    public async Task<List<WebAuthnCredential>> GetCredentialsAsync(AppUser user)
    {
        return await _db.WebAuthnCredentials
            .Where(c => c.UserId == user.Id)
            .OrderByDescending(c => c.CreatedAt)
            .ToListAsync();
    }

    public async Task<bool> RemoveCredentialAsync(AppUser user, int id)
    {
        var credential = await _db.WebAuthnCredentials
            .FirstOrDefaultAsync(c => c.Id == id && c.UserId == user.Id);
        if (credential is null) return false;
        _db.WebAuthnCredentials.Remove(credential);
        await _db.SaveChangesAsync();
        return true;
    }

    // ===== Passkey 无密码登录 =====

    public AssertionOptions StartLogin()
    {
        return _fido2.GetAssertionOptions(new GetAssertionOptionsParams
        {
            // 不传 AllowedCredentials → 浏览器弹出通行密钥选择器
            UserVerification = UserVerificationRequirement.Preferred
        });
    }

    public async Task<AppUser?> CompleteLoginAsync(
        AuthenticatorAssertionRawResponse assertion,
        AssertionOptions options)
    {
        // 按 credentialId 精确查询（避免全表遍历 + SignCount 竞争）
        // assertion.Id 来自浏览器端 Base64url 格式，需转为标准 Base64 与数据库匹配
        var rawId = assertion.Id.Replace('-', '+').Replace('_', '/');
        switch (rawId.Length % 4)
        {
            case 2: rawId += "=="; break;
            case 3: rawId += "="; break;
        }
        var credential = await _db.WebAuthnCredentials
            .Include(c => c.User)
            .FirstOrDefaultAsync(c => c.CredentialId == rawId);

        if (credential is null) return null;

        try
        {
            var result = await _fido2.MakeAssertionAsync(new MakeAssertionParams
            {
                AssertionResponse = assertion,
                OriginalOptions = options,
                StoredPublicKey = Convert.FromBase64String(credential.PublicKey),
                StoredSignatureCounter = (uint)credential.SignCount,
                IsUserHandleOwnerOfCredentialIdCallback = (args, ct) =>
                    Task.FromResult(credential.CredentialId
                        == Convert.ToBase64String(args.CredentialId))
            });

            // 乐观并发控制：用 OriginalValue 防止 SignCount 竞争
            var oldSignCount = credential.SignCount;
            credential.SignCount = (int)result.SignCount;
            _db.Entry(credential).Property(nameof(credential.SignCount)).OriginalValue = oldSignCount;
            await _db.SaveChangesAsync();
            return credential.User;
        }
        catch (Fido2VerificationException)
        {
            return null;
        }
        catch (DbUpdateConcurrencyException)
        {
            return null;
        }
    }
}
