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
                UserVerification = UserVerificationRequirement.Preferred
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
}
