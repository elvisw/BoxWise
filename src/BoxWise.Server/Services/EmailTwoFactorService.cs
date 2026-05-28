using System.Globalization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Caching.Memory;
using MailKit.Net.Smtp;
using MimeKit;

namespace BoxWise.Server.Services;

public class EmailTwoFactorService
{
    private readonly IDataProtector _protector;
    private readonly IMemoryCache _cache;
    private readonly IConfiguration _config;
    private readonly ILogger<EmailTwoFactorService> _logger;

    public EmailTwoFactorService(
        IDataProtectionProvider protectionProvider,
        IMemoryCache cache,
        IConfiguration config,
        ILogger<EmailTwoFactorService> logger)
    {
        _protector = protectionProvider.CreateProtector("BoxWise.EmailTwoFactor");
        _cache = cache;
        _config = config;
        _logger = logger;
    }

    /// <summary>
    /// 生成 6 位验证码，用 Data Protection 加密后存入内存缓存，返回明文验证码。
    /// 缓存 5 分钟后过期。
    /// </summary>
    public string GenerateAndCacheCode(string userId, string email)
    {
        var code = Random.Shared.Next(100000, 999999).ToString();
        var payload = $"{userId}|{email}|{code}|{DateTime.UtcNow.AddMinutes(5):O}";
        var encrypted = _protector.Protect(payload);
        _cache.Set(CacheKey(userId), encrypted, TimeSpan.FromMinutes(5));
        return code;
    }

    /// <summary>
    /// 验证邮箱验证码：查找缓存、解密、校验 userId/email/code 匹配且未过期。
    /// </summary>
    public bool VerifyCode(string userId, string email, string code)
    {
        var key = CacheKey(userId);
        if (!_cache.TryGetValue(key, out string? encrypted) || encrypted is null)
            return false;

        try
        {
            var payload = _protector.Unprotect(encrypted);
            var parts = payload.Split('|');
            if (parts.Length < 4)
                return false;

            var cachedUserId = parts[0];
            var cachedEmail = parts[1];
            var cachedCode = parts[2];
            var expiresAt = DateTime.Parse(parts[3], null, DateTimeStyles.RoundtripKind);

            if (cachedUserId == userId && cachedEmail == email && cachedCode == code && expiresAt > DateTime.UtcNow)
            {
                _cache.Remove(key);
                return true;
            }
        }
        catch
        {
            // 解密失败视为无效
        }

        return false;
    }

    /// <summary>
    /// 使用 MailKit SmtpClient 发送验证码邮件。
    /// 从 IConfiguration["Smtp:Host"] 等读取配置。如果 SMTP 未配置则返回 false（静默失败）。
    /// </summary>
    public async Task<bool> SendVerificationEmailAsync(string toEmail, string code, string? userName)
    {
        var host = _config["Smtp:Host"];
        if (string.IsNullOrWhiteSpace(host))
        {
            _logger.LogWarning("SMTP 未配置，无法发送验证码邮件到 {Email}", toEmail);
            return false;
        }

        var portStr = _config["Smtp:Port"];
        var port = !string.IsNullOrWhiteSpace(portStr) && int.TryParse(portStr, out var p) ? p : 587;
        var username = _config["Smtp:Username"] ?? "";
        var password = _config["Smtp:Password"] ?? "";
        var fromAddress = _config["Smtp:FromAddress"] ?? "noreply@boxwise.app";
        var fromName = _config["Smtp:FromName"] ?? "BoxWise";

        try
        {
            var message = new MimeMessage();
            message.From.Add(new MailboxAddress(fromName, fromAddress));
            message.To.Add(new MailboxAddress(userName ?? "用户", toEmail));
            message.Subject = "【BoxWise】您的验证码";

            var body = new TextPart("plain")
            {
                Text = $@"您好{(!string.IsNullOrEmpty(userName) ? $" {userName}，" : "，")}

您的 BoxWise 双因素认证验证码为：

    {code}

此验证码有效期为 5 分钟，请勿泄露给他人。

如果您未请求此验证码，请忽略此邮件。

此致，
BoxWise 安全团队"
            };

            message.Body = body;

            using var client = new SmtpClient();
            await client.ConnectAsync(host, port, useSsl: port == 465);
            if (!string.IsNullOrWhiteSpace(username))
                await client.AuthenticateAsync(username, password);
            await client.SendAsync(message);
            await client.DisconnectAsync(true);

            _logger.LogInformation("验证码邮件已发送到 {Email}", toEmail);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "发送验证码邮件到 {Email} 失败", toEmail);
            return false;
        }
    }

    /// <summary>
    /// 检查 SMTP 是否已配置。
    /// </summary>
    public bool IsSmtpConfigured()
    {
        return !string.IsNullOrWhiteSpace(_config["Smtp:Host"]);
    }

    /// <summary>
    /// 清除缓存的验证码（用于手动清理）。
    /// </summary>
    public void ClearCachedCode(string userId)
    {
        _cache.Remove(CacheKey(userId));
    }

    private static string CacheKey(string userId) => $"2fa_email_{userId}";
}
