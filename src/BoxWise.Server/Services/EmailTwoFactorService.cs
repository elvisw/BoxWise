using System.Collections.Concurrent;
using System.Globalization;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using BoxWise.Shared.Dtos;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace BoxWise.Server.Services;

public class EmailTwoFactorService
{
    private readonly ISmtpConfigurationService _smtpConfig;
    private readonly ILogger<EmailTwoFactorService> _logger;
    private readonly IDataProtector _protector;

    // 一次性 token 消费追踪（ConcurrentDictionary + 惰性清理）
    private static readonly ConcurrentDictionary<string, DateTime> _consumedTokens = new();
    private static readonly TimeSpan _tokenTtl = TimeSpan.FromMinutes(5);
    private static DateTime _lastCleanup = DateTime.UtcNow;
    private static readonly object _cleanupLock = new();

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
    /// 一次性验证码校验：TryAdd 原子操作防止 TOCTOU 竞态。
    /// 验证失败时移除 tokenHash 允许重试；验证成功时惰性清理过期条目。
    /// </summary>
    public bool VerifyCodeOnce(string userId, string email, string code, string token)
    {
        var tokenHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

        // TryAdd 是原子操作 —— 修复 TOCTOU 竞态
        if (!_consumedTokens.TryAdd(tokenHash, DateTime.UtcNow.Add(_tokenTtl)))
            return false; // 已存在 → 已消费

        bool result;
        try
        {
            result = VerifyCode(userId, email, code, token);
        }
        catch
        {
            // VerifyCode 抛出异常时清理 tokenHash，防止 token hash 泄漏（无法被清理）
            _consumedTokens.TryRemove(tokenHash, out _);
            throw;
        }

        if (!result)
            _consumedTokens.TryRemove(tokenHash, out _); // 验证失败 → 允许重试
        else
            CleanupExpiredTokens();
        return result;
    }

    private static void CleanupExpiredTokens()
    {
        var now = DateTime.UtcNow;
        if (now - _lastCleanup < TimeSpan.FromMinutes(2)) return;
        lock (_cleanupLock)
        {
            if (now - _lastCleanup < TimeSpan.FromMinutes(2)) return;
            var expired = _consumedTokens.Where(kv => kv.Value < now).Select(kv => kv.Key).ToList();
            foreach (var key in expired) _consumedTokens.TryRemove(key, out _);
            _lastCleanup = now;
        }
    }

    /// <summary>
    /// 标记 operation token 为已消费，防止重放攻击。
    /// 与 VerifyCodeOnce 共享同一 _consumedTokens 字典，TTL 5 分钟。
    /// </summary>
    public static bool TryConsumeOperationToken(string token)
    {
        var tokenHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
        var consumed = !_consumedTokens.TryAdd(tokenHash, DateTime.UtcNow.Add(_tokenTtl));
        if (consumed) return false; // 已消费 → 拒绝
        CleanupExpiredTokens();
        return true; // 首次消费 → 允许
    }

    /// <summary>
    /// 使用 MailKit SmtpClient 发送验证码邮件。
    /// 从 SmtpConfigurationService 快照读取配置。如果 SMTP 未配置则返回 false（静默失败）。
    /// purpose 参数区分邮件模板："2fa"（默认）或 "email-change"。
    /// </summary>
    public async Task<bool> SendVerificationEmailAsync(string toEmail, string code, string? userName, string purpose = "2fa")
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

        var purposeText = purpose == "email-change" ? "邮箱修改" : "双因素认证";
        var subject = purpose == "email-change"
            ? "【BoxWise】邮箱修改验证码"
            : "【BoxWise】您的验证码";

        try
        {
            var message = new MimeMessage();
            message.From.Add(new MailboxAddress(fromName, fromAddress));
            message.To.Add(new MailboxAddress(userName ?? "用户", toEmail));
            message.Subject = subject;

            var body = new TextPart("plain")
            {
                Text = $@"您好{(!string.IsNullOrEmpty(userName) ? $" {userName}，" : "，")}

您的 BoxWise {purposeText}验证码为：

    {code}

此验证码有效期为 5 分钟，请勿泄露给他人。

如果您未请求此验证码，请忽略此邮件。

此致，
BoxWise 安全团队"
            };

            message.Body = body;

            using var client = new SmtpClient();
            client.Timeout = 30000;
            client.SslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13;
            await client.ConnectAsync(config.Host, config.Port,
                config.Port == 465 ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTlsWhenAvailable);
            if (!string.IsNullOrWhiteSpace(config.Username))
                await client.AuthenticateAsync(config.Username, config.Password ?? "");
            await client.SendAsync(message);
            await TryDisconnectAsync(client);

            _logger.LogInformation("{Purpose} 验证码邮件已发送到 {Email}", purpose, toEmail);
            return true;
        }
        catch (MailKit.Security.AuthenticationException ex)
        {
            _logger.LogError(ex, "SMTP 认证失败，无法发送验证码邮件到 {Email}", toEmail);
            return false;
        }
        catch (SmtpCommandException ex)
        {
            _logger.LogError(ex, "SMTP 命令错误，无法发送验证码邮件到 {Email}", toEmail);
            return false;
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or SocketException)
        {
            _logger.LogError(ex, "SMTP 连接失败，无法发送验证码邮件到 {Email}", toEmail);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "发送验证码邮件到 {Email} 时发生未知错误", toEmail);
            return false;
        }
    }

    /// <summary>
    /// 发送邮箱变更通知到旧邮箱（异步，失败不影响主流程）。
    /// </summary>
    public async Task SendChangeNotificationAsync(string oldEmail, string? userName)
    {
        var config = _smtpConfig.GetSnapshot();
        if (string.IsNullOrWhiteSpace(config.Host))
        {
            _logger.LogWarning("SMTP 未配置，无法发送邮箱变更通知到 {Email}", oldEmail);
            return;
        }

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
            message.To.Add(new MailboxAddress(userName ?? "用户", oldEmail));
            message.Subject = "【BoxWise】邮箱地址已变更";

            var body = new TextPart("plain")
            {
                Text = $@"您好{(!string.IsNullOrEmpty(userName) ? $" {userName}，" : "，")}

您的 BoxWise 账户邮箱地址已成功修改。如果这不是您本人的操作，请立即联系管理员。

此致，
BoxWise 安全团队"
            };

            message.Body = body;

            using var client = new SmtpClient();
            client.Timeout = 30000;
            client.SslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13;
            await client.ConnectAsync(config.Host, config.Port,
                config.Port == 465 ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTlsWhenAvailable);
            if (!string.IsNullOrWhiteSpace(config.Username))
                await client.AuthenticateAsync(config.Username, config.Password ?? "");
            await client.SendAsync(message);
            await TryDisconnectAsync(client);

            _logger.LogInformation("邮箱变更通知已发送到 {OldEmail}", oldEmail);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "发送邮箱变更通知到 {OldEmail} 失败（不影响主流程）", oldEmail);
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
