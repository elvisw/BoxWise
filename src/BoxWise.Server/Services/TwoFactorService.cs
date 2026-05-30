using System.Globalization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Caching.Memory;
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
    private readonly IMemoryCache _cache;

    public TwoFactorService(
        UserManager<AppUser> userManager,
        IDataProtectionProvider protectionProvider,
        EmailTwoFactorService emailTwoFactorService,
        RecoveryCodeService recoveryCodeService,
        IMemoryCache cache)
    {
        _userManager = userManager;
        _protector = protectionProvider.CreateProtector("BoxWise.TwoFactor");
        _emailTwoFactorService = emailTwoFactorService;
        _recoveryCodeService = recoveryCodeService;
        _cache = cache;
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
        if (user is null)
            return false;

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

        if (!totp.VerifyTotp(code, out long timeStepMatched, new VerificationWindow(1, 1)))
            return false;

        // 防重放：同一用户 + 同一时间步长 2 分钟内不可重复使用
        var replayKey = $"totp_replay_{user.Id}:{timeStepMatched}";
        if (_cache.TryGetValue(replayKey, out _))
            return false;
        _cache.Set(replayKey, true, TimeSpan.FromMinutes(2));

        user.TwoFactorEnabled = true;
        user.ConfiguredMethods |= TwoFactorMethod.TOTP;
        user.TwoFactorSetupCompletedAt = DateTime.UtcNow;

        var result = await _userManager.UpdateAsync(user);
        return result.Succeeded;
    }

    /// <summary>
    /// 验证 TOTP 挑战（登录阶段二）。
    /// 支持双密钥窗口：优先验证 TotpSecretKey，失败时回退到 PendingTotpSecretKey。
    /// 注意：速率限制由端点层配合 RateLimit 配置节实现（Story 8-4）。
    /// </summary>
    public Task<bool> VerifyTotpChallengeAsync(AppUser user, string code)
    {
        if (user is null)
            return Task.FromResult(false);

        // 先尝试主密钥
        if (!string.IsNullOrWhiteSpace(user.TotpSecretKey))
        {
            string base32;
            try
            {
                base32 = _protector.Unprotect(user.TotpSecretKey);
            }
            catch
            {
                // 解密失败，尝试 PendingTotpSecretKey
                return Task.FromResult(TryVerifyWithPendingKey(user, code));
            }

            var secretBytes = Base32Encoding.ToBytes(base32);
            var totp = new Totp(secretBytes);

            if (!totp.VerifyTotp(code, out long timeStepMatched, new VerificationWindow(1, 1)))
            {
                // TOTP 不匹配，尝试 pending key
                return Task.FromResult(TryVerifyWithPendingKey(user, code));
            }

            // 防重放
            var replayKey = $"totp_replay_{user.Id}:{timeStepMatched}";
            if (_cache.TryGetValue(replayKey, out _))
                return Task.FromResult(false);
            _cache.Set(replayKey, true, TimeSpan.FromMinutes(2));

            return Task.FromResult(true);
        }

        // 主密钥为空，直接尝试 pending key
        return Task.FromResult(TryVerifyWithPendingKey(user, code));
    }

    /// <summary>
    /// 尝试使用 PendingTotpSecretKey 验证 TOTP 码。
    /// </summary>
    private bool TryVerifyWithPendingKey(AppUser user, string code)
    {
        if (string.IsNullOrWhiteSpace(user.PendingTotpSecretKey))
            return false;

        string base32;
        try
        {
            base32 = _protector.Unprotect(user.PendingTotpSecretKey);
        }
        catch
        {
            return false;
        }

        var secretBytes = Base32Encoding.ToBytes(base32);
        var totp = new Totp(secretBytes);

        if (!totp.VerifyTotp(code, out long timeStepMatched, new VerificationWindow(1, 1)))
            return false;

        // 独立的防重放缓存（与主密钥使用不同的前缀，以便允许两个密钥在相同时步共存）
        var replayKey = $"totp_replay_pending_{user.Id}:{timeStepMatched}";
        if (_cache.TryGetValue(replayKey, out _))
            return false;
        _cache.Set(replayKey, true, TimeSpan.FromMinutes(2));

        return true;
    }

    /// <summary>
    /// 获取用户 2FA 状态。
    /// </summary>
    public async Task<TwoFactorStatusDto> GetTwoFactorStatusAsync(AppUser user)
    {
        if (user is null)
            throw new ArgumentNullException(nameof(user));

        var availableMethods = new List<string>();
        var configuredMethods = new List<string>();

        // TOTP 始终可用于设置（客户端本地生成，无需服务端配置）
        availableMethods.Add("TOTP");

        // Email 在 SMTP 已配置时可用于设置（无需用户已配置该标志）
        if (_emailTwoFactorService.IsSmtpConfigured())
            availableMethods.Add("Email");

        // 已配置的方法列表（用于前端显示当前状态）
        if (user.ConfiguredMethods.HasFlag(TwoFactorMethod.TOTP))
            configuredMethods.Add("TOTP");
        if (user.ConfiguredMethods.HasFlag(TwoFactorMethod.Email))
            configuredMethods.Add("Email");

        var hasRecoveryCodes = await _recoveryCodeService.HasRecoveryCodesAsync(user);

        return new TwoFactorStatusDto(
            TwoFactorEnabled: user.TwoFactorEnabled,
            TwoFactorMethod: user.ConfiguredMethods == TwoFactorMethod.None ? null : user.ConfiguredMethods.ToString(),
            AvailableMethods: availableMethods,
            ConfiguredMethods: configuredMethods,
            HasRecoveryCodes: hasRecoveryCodes,
            GracePeriodEnd: user.TwoFactorGracePeriodUntil,
            SetupCompletedAt: user.TwoFactorSetupCompletedAt
        );
    }

    /// <summary>
    /// 已废弃：在 [Flags] 多方法模型下语义不明确，由独立 setup/verify 端点替代。
    /// </summary>
    public Task<bool> SwitchMethodAsync(AppUser user, TwoFactorMethod newMethod, string sessionToken)
    {
        // 已废弃：在 [Flags] 多方法模型下语义不明确，由独立 setup/verify 端点替代。
        return Task.FromResult(false);
    }

    /// <summary>
    /// 生成 SessionToken（Data Protection 自包含令牌）。
    /// purpose="2fa-setup" 时 5 分钟有效期，purpose="2fa-modify" 时 15 分钟有效期。
    /// 包含客户端 IP 以绑定会话到特定来源（可选，不强制验证）。
    /// </summary>
    public string GenerateSessionToken(string userId, string? clientIp = null, string purpose = "2fa-setup")
    {
        var expiry = purpose == "2fa-modify" ? DateTime.UtcNow.AddMinutes(15) : DateTime.UtcNow.AddMinutes(5);
        var payload = $"{userId}|{expiry:O}|{purpose}|{clientIp ?? ""}";
        return _protector.Protect(payload);
    }

    /// <summary>
    /// 验证 SessionToken：解密并校验 userId 匹配 + 未过期 + purpose 匹配。
    /// expectedPurpose 默认为 "2fa-setup" 以保持向后兼容。
    /// 如果令牌中包含 IP 地址，会记录不匹配警告但不拒绝（兼容旧令牌）。
    /// </summary>
    public bool ValidateSessionToken(string token, string expectedUserId, string? clientIp = null, string expectedPurpose = "2fa-setup")
    {
        var parsed = TryParseToken(token);
        if (parsed is null)
            return false;

        var (userId, expiresAt, purpose, tokenIp) = parsed.Value;

        // 如果令牌中包含 IP 且调用方提供了 IP，记录不匹配日志
        // 不拒绝请求，以兼容升级前生成的令牌
        if (!string.IsNullOrEmpty(tokenIp) && !string.IsNullOrEmpty(clientIp)
            && !string.Equals(tokenIp, clientIp, StringComparison.OrdinalIgnoreCase))
        {
            // IP 不匹配 — 建议在后续版本中改为拒绝
        }

        return userId == expectedUserId
            && purpose == expectedPurpose
            && expiresAt.AddSeconds(30) > DateTime.UtcNow; // 30s 时钟偏差容差
    }

    /// <summary>
    /// 解析 SessionToken 令牌的组成部分。返回 null 表示令牌无效。
    /// </summary>
    private (string UserId, DateTime ExpiresAt, string Purpose, string? Ip)? TryParseToken(string token)
    {
        try
        {
            var payload = _protector.Unprotect(token);
            var parts = payload.Split('|');
            if (parts.Length < 3)
                return null;

            var userId = parts[0];
            var expiresAt = DateTime.Parse(parts[1], null, DateTimeStyles.RoundtripKind);
            var purpose = parts[2];
            var tokenIp = parts.Length >= 4 ? parts[3] : null;

            return (userId, expiresAt, purpose, tokenIp);
        }
        catch
        {
            return null;
        }
    }

    // ===== Story 9.1: Pending TOTP methods =====

    /// <summary>
    /// 生成暂存 TOTP 密钥（TOTP 修改流程用）。存储到 PendingTotpSecretKey，
    /// 不覆盖已存在的 TotpSecretKey。
    /// </summary>
    public async Task<(string SecretKey, string QrCodeUri)> GeneratePendingTotpSecretAsync(string userId)
    {
        var user = await _userManager.FindByIdAsync(userId)
            ?? throw new KeyNotFoundException("User not found.");

        var key = KeyGeneration.GenerateRandomKey(20);
        var base32 = Base32Encoding.ToString(key);
        var qrCodeUri = $"otpauth://totp/BoxWise:{Uri.EscapeDataString(user.UserName ?? userId)}?secret={base32}&issuer=BoxWise";

        user.PendingTotpSecretKey = _protector.Protect(base32);
        await _userManager.UpdateAsync(user);

        return (base32, qrCodeUri);
    }

    /// <summary>
    /// 验证 TOTP 修改设置。需要 purpose="2fa-modify" 的 sessionToken。
    /// 验证通过后：TotpSecretKey = PendingTotpSecretKey，清除 PendingTotpSecretKey。
    /// 不改变 TwoFactorEnabled 或 ConfiguredMethods。
    /// </summary>
    public async Task<bool> VerifyPendingTotpSetupAsync(AppUser user, string code, string sessionToken)
    {
        if (user is null)
            return false;

        if (!ValidateSessionToken(sessionToken, user.Id, expectedPurpose: "2fa-modify"))
            return false;

        if (string.IsNullOrWhiteSpace(user.PendingTotpSecretKey))
            return false;

        string base32;
        try
        {
            base32 = _protector.Unprotect(user.PendingTotpSecretKey);
        }
        catch
        {
            return false;
        }

        var secretBytes = Base32Encoding.ToBytes(base32);
        var totp = new Totp(secretBytes);

        if (!totp.VerifyTotp(code, out long timeStepMatched, new VerificationWindow(1, 1)))
            return false;

        // 防重放：同一用户 + 同一时间步长 2 分钟内不可重复使用
        var replayKey = $"totp_replay_{user.Id}:{timeStepMatched}";
        if (_cache.TryGetValue(replayKey, out _))
            return false;
        _cache.Set(replayKey, true, TimeSpan.FromMinutes(2));

        // 将 pending key 提升为主 key
        user.TotpSecretKey = user.PendingTotpSecretKey;
        user.PendingTotpSecretKey = null;

        var result = await _userManager.UpdateAsync(user);
        return result.Succeeded;
    }
}
