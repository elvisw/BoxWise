using System.Globalization;
using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using BoxWise.Shared.Dtos;
using MailKit.Net.Smtp;
using MimeKit;

namespace BoxWise.Server.Services;

public class EmailTwoFactorService
{
    private readonly ISmtpConfigurationService _smtpConfig;
    private readonly ILogger<EmailTwoFactorService> _logger;
    private readonly IDataProtector _protector;

    public EmailTwoFactorService(
        IDataProtectionProvider protectionProvider,
        ISmtpConfigurationService smtpConfig,
        ILogger<EmailTwoFactorService> logger)
    {
        _protector = protectionProvider.CreateProtector("BoxWise.EmailTwoFactor");
        _smtpConfig = smtpConfig;
        _logger = logger;
    }

    /// <summary>
    /// 生成 6 位验证码 + 自包含加密令牌（Data Protection，零服务端存储）。
    /// 令牌内含 userId + email + code + 过期时间，验证时解密比对。
    /// </summary>
    public (string Code, string Token) GenerateCode(string userId, string email)
    {
        var code = RandomNumberGenerator.GetInt32(100000, 1000000).ToString();
        var payload = $"{userId}|{email}|{code}|{DateTime.UtcNow.AddMinutes(5):O}";
        var token = _protector.Protect(payload);
        return (code, token);
    }

    /// <summary>
    /// 验证邮箱验证码：解密令牌，校验 userId/email/code 匹配且未过期。
    /// 自包含令牌，无需服务端缓存。
    /// </summary>
    public bool VerifyCode(string userId, string email, string code, string token)
    {
        try
        {
            var payload = _protector.Unprotect(token);
            var parts = payload.Split('|');
            if (parts.Length < 4)
                return false;

            var tokenUserId = parts[0];
            var tokenEmail = parts[1];
            var tokenCode = parts[2];
            var expiresAt = DateTime.Parse(parts[3], null, DateTimeStyles.RoundtripKind);

            return tokenUserId == userId
                && tokenEmail == email
                && tokenCode == code
                && expiresAt > DateTime.UtcNow;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 使用 MailKit SmtpClient 发送验证码邮件。
    /// 从 SmtpConfigurationService 快照读取配置。如果 SMTP 未配置则返回 false（静默失败）。
    /// </summary>
    public async Task<bool> SendVerificationEmailAsync(string toEmail, string code, string? userName)
    {
        var config = _smtpConfig.GetSnapshot();
        if (string.IsNullOrWhiteSpace(config.Host))
        {
            _logger.LogWarning("SMTP 未配置，无法发送验证码邮件到 {Email}", toEmail);
            return false;
        }

        // 默认值回退，防止 FromAddress/FromName 为空时邮件发送失败
        var fromAddress = string.IsNullOrWhiteSpace(config.FromAddress)
            ? "noreply@boxwise.app"
            : config.FromAddress;
        var fromName = string.IsNullOrWhiteSpace(config.FromName)
            ? "BoxWise"
            : config.FromName;

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
            client.Timeout = 30000;
            await client.ConnectAsync(config.Host, config.Port, useSsl: config.Port == 465);
            if (!string.IsNullOrWhiteSpace(config.Username))
                await client.AuthenticateAsync(config.Username, config.Password ?? "");
            await client.SendAsync(message);
            await TryDisconnectAsync(client);

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
    /// 检查 SMTP 是否已配置。委托给 SmtpConfigurationService。
    /// </summary>
    public bool IsSmtpConfigured()
        => _smtpConfig.IsConfigured();

    /// <summary>
    /// 独立 try/catch 包裹 DisconnectAsync，防止断开连接异常影响主流程。
    /// </summary>
    private static async Task TryDisconnectAsync(SmtpClient client)
    {
        try
        {
            if (client.IsConnected)
                await client.DisconnectAsync(true);
        }
        catch
        {
            // 断开连接异常不影响主流程
        }
    }
}
