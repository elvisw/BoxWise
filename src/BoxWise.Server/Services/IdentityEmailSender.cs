using System.Net.Sockets;
using System.Security.Authentication;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.AspNetCore.Identity.UI.Services;
using MimeKit;

namespace BoxWise.Server.Services;

public class IdentityEmailSender : IEmailSender
{
    private readonly ISmtpConfigurationService _smtpConfig;
    private readonly ILogger<IdentityEmailSender> _logger;

    public IdentityEmailSender(
        ISmtpConfigurationService smtpConfig,
        ILogger<IdentityEmailSender> logger)
    {
        _smtpConfig = smtpConfig;
        _logger = logger;
    }

    public async Task SendEmailAsync(string email, string subject, string htmlMessage)
    {
        var config = _smtpConfig.GetSnapshot();
        if (string.IsNullOrWhiteSpace(config.Host))
        {
            _logger.LogWarning("SMTP 未配置，无法发送邮件到 {Email}", email);
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
            message.To.Add(new MailboxAddress(email, email));
            message.Subject = subject;
            message.Body = new TextPart("html") { Text = htmlMessage };

            using var client = new SmtpClient();
            client.Timeout = 30000;
            client.SslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13;
            await client.ConnectAsync(config.Host, config.Port,
                config.Port == 465 ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTlsWhenAvailable);
            if (!string.IsNullOrWhiteSpace(config.Username))
                await client.AuthenticateAsync(config.Username, config.Password ?? "");
            await client.SendAsync(message);
            await TryDisconnectAsync(client);

            _logger.LogInformation("邮件已发送到 {Email}（主题：{Subject}）", email, subject);
        }
        catch (MailKit.Security.AuthenticationException ex)
        {
            _logger.LogError(ex, "SMTP 认证失败，无法发送邮件到 {Email}", email);
        }
        catch (SmtpCommandException ex)
        {
            _logger.LogError(ex, "SMTP 命令错误，无法发送邮件到 {Email}", email);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or SocketException)
        {
            _logger.LogError(ex, "SMTP 连接失败，无法发送邮件到 {Email}", email);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "发送邮件到 {Email} 时发生未知错误", email);
        }
    }

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
