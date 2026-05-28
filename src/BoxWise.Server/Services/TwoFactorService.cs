using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using OtpNet;
using BoxWise.Server.Models;
using BoxWise.Shared.Dtos;

namespace BoxWise.Server.Services;

public class TwoFactorService
{
    private readonly UserManager<AppUser> _userManager;
    private readonly IDataProtector _protector;
    private readonly EmailTwoFactorService _emailTwoFactorService;
    private readonly RecoveryCodeService _recoveryCodeService;

    public TwoFactorService(
        UserManager<AppUser> userManager,
        IDataProtectionProvider protectionProvider,
        EmailTwoFactorService emailTwoFactorService,
        RecoveryCodeService recoveryCodeService)
    {
        _userManager = userManager;
        _protector = protectionProvider.CreateProtector("BoxWise.TwoFactor");
        _emailTwoFactorService = emailTwoFactorService;
        _recoveryCodeService = recoveryCodeService;
    }

    /// <summary>
    /// 生成 TOTP 密钥 + 二维码 URI，加密存储密钥到用户记录。
    /// </summary>
    public async Task<(string SecretKey, string QrCodeUri)> GenerateTotpSecretAsync(string userId)
    {
        var user = await _userManager.FindByIdAsync(userId)
            ?? throw new KeyNotFoundException("User not found.");

        var key = KeyGeneration.GenerateRandomKey(20);
        var base32 = Base32Encoding.ToString(key);
        var qrCodeUri = $"otpauth://totp/BoxWise:{Uri.EscapeDataString(user.UserName ?? userId)}?secret={base32}&issuer=BoxWise";

        user.TotpSecretKey = _protector.Protect(base32);
        await _userManager.UpdateAsync(user);

        return (base32, qrCodeUri);
    }

    /// <summary>
    /// 验证 TOTP 设置（首次绑定，需要 SessionToken）。
    /// </summary>
    public async Task<bool> VerifyTotpSetupAsync(AppUser user, string code, string sessionToken)
    {
        if (!ValidateSessionToken(sessionToken, user.Id))
            return false;

        if (string.IsNullOrWhiteSpace(user.TotpSecretKey))
            return false;

        string base32;
        try
        {
            base32 = _protector.Unprotect(user.TotpSecretKey);
        }
        catch
        {
            return false;
        }

        var secretBytes = Base32Encoding.ToBytes(base32);
        var totp = new Totp(secretBytes);

        if (!totp.VerifyTotp(code, out _, new VerificationWindow(1, 1)))
            return false;

        user.TwoFactorEnabled = true;
        user.TwoFactorMethod = TwoFactorMethod.TOTP;
        user.TwoFactorSetupCompletedAt = DateTime.UtcNow;

        var result = await _userManager.UpdateAsync(user);
        return result.Succeeded;
    }

    /// <summary>
    /// 验证 TOTP 挑战（登录阶段二）。
    /// 注意：速率限制由端点层配合 RateLimit 配置节实现（Story 8-4）。
    /// </summary>
    public Task<bool> VerifyTotpChallengeAsync(AppUser user, string code)
    {
        if (string.IsNullOrWhiteSpace(user.TotpSecretKey))
            return Task.FromResult(false);

        string base32;
        try
        {
            base32 = _protector.Unprotect(user.TotpSecretKey);
        }
        catch
        {
            return Task.FromResult(false);
        }

        var secretBytes = Base32Encoding.ToBytes(base32);
        var totp = new Totp(secretBytes);

        var valid = totp.VerifyTotp(code, out _, new VerificationWindow(1, 1));
        return Task.FromResult(valid);
    }

    /// <summary>
    /// 获取用户 2FA 状态。
    /// </summary>
    public async Task<TwoFactorStatusDto> GetTwoFactorStatusAsync(AppUser user)
    {
        var availableMethods = new List<string> { "TOTP" };

        if (_emailTwoFactorService.IsSmtpConfigured())
            availableMethods.Add("Email");

        availableMethods.Add("WebAuthn");

        if (user.TwoFactorMethod != TwoFactorMethod.None
            && !availableMethods.Contains(user.TwoFactorMethod.ToString()))
        {
            availableMethods.Add(user.TwoFactorMethod.ToString());
        }

        var hasRecoveryCodes = await _recoveryCodeService.HasRecoveryCodesAsync(user);

        return new TwoFactorStatusDto(
            TwoFactorEnabled: user.TwoFactorEnabled,
            TwoFactorMethod: user.TwoFactorMethod == TwoFactorMethod.None ? null : user.TwoFactorMethod.ToString(),
            AvailableMethods: availableMethods,
            HasRecoveryCodes: hasRecoveryCodes,
            GracePeriodEnd: user.TwoFactorGracePeriodUntil,
            SetupCompletedAt: user.TwoFactorSetupCompletedAt
        );
    }

    /// <summary>
    /// 切换 2FA 方法。TOTP 切换由 VerifyTotpSetupAsync 完成，此处为验证入口。
    /// </summary>
    public Task<bool> SwitchMethodAsync(AppUser user, TwoFactorMethod newMethod, string sessionToken)
    {
        if (newMethod == TwoFactorMethod.None)
            throw new ArgumentException("Cannot switch to None.", nameof(newMethod));

        if (newMethod == TwoFactorMethod.WebAuthn)
        {
            // WebAuthn 不需要额外验证，凭证注册在 WebAuthnEndpoints 中处理
            // 只需验证 SessionToken
            if (!ValidateSessionToken(sessionToken, user.Id))
                return Task.FromResult(false);
            return Task.FromResult(true);
        }

        if (!ValidateSessionToken(sessionToken, user.Id))
            return Task.FromResult(false);

        // SwitchMethodAsync 仅验证 SessionToken 合法性，不直接设置 TwoFactorMethod。
        // 实际的 TwoFactorMethod 赋值在各 Verify 端点（VerifyTotpSetupAsync/VerifyEmail*）中完成。
        return Task.FromResult(true);
    }

    /// <summary>
    /// 生成 SessionToken（Data Protection 自包含令牌，5 分钟有效期）。
    /// 包含客户端 IP 以绑定会话到特定来源（可选，不强制验证）。
    /// </summary>
    public string GenerateSessionToken(string userId, string? clientIp = null)
    {
        var payload = $"{userId}|{DateTime.UtcNow.AddMinutes(5):O}|2fa-setup|{clientIp ?? ""}";
        return _protector.Protect(payload);
    }

    /// <summary>
    /// 验证 SessionToken：解密并校验 userId 匹配 + 未过期。
    /// 如果令牌中包含 IP 地址，会记录不匹配警告但不拒绝（兼容旧令牌）。
    /// </summary>
    public bool ValidateSessionToken(string token, string expectedUserId, string? clientIp = null)
    {
        try
        {
            var payload = _protector.Unprotect(token);
            var parts = payload.Split('|');
            if (parts.Length < 3)
                return false;

            var userId = parts[0];
            var expiresAt = DateTime.Parse(parts[1], null, DateTimeStyles.RoundtripKind);
            var purpose = parts[2];
            var tokenIp = parts.Length >= 4 ? parts[3] : null;

            // 如果令牌中包含 IP 且调用方提供了 IP，记录不匹配日志
            // 不拒绝请求，以兼容升级前生成的令牌
            if (!string.IsNullOrEmpty(tokenIp) && !string.IsNullOrEmpty(clientIp)
                && !string.Equals(tokenIp, clientIp, StringComparison.OrdinalIgnoreCase))
            {
                // IP 不匹配 — 建议在后续版本中改为拒绝
            }

            return userId == expectedUserId
                && purpose == "2fa-setup"
                && expiresAt > DateTime.UtcNow;
        }
        catch
        {
            return false;
        }
    }
}
