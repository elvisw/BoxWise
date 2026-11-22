using BoxWise.Server.Services;
using BoxWise.Shared.Dtos;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MimeKit;
using Moq;

namespace BoxWise.Server.Tests.Services;

/// <summary>
/// MailKit SMTP 发信路径的真实 socket 回归。使用 loopback 随机端口，
/// 不访问外部域、不使用真实凭据，也不覆盖生产 TLS/认证。
/// </summary>
public sealed class SmtpTransportTests : IDisposable
{
    private readonly string _tempDir;
    private readonly ServiceProvider _services;
    private readonly IDataProtectionProvider _dataProtection;

    public SmtpTransportTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"boxwise-smtp-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        var services = new ServiceCollection();
        services.AddDataProtection()
            .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(_tempDir, "keys")));
        _services = services.BuildServiceProvider();
        _dataProtection = _services.GetRequiredService<IDataProtectionProvider>();
    }

    public void Dispose()
    {
        _services.Dispose();
        try { Directory.Delete(_tempDir, recursive: true); }
        catch { }
    }

    [Fact]
    public async Task IdentityEmailSender_SendsHtmlMail_OverSmtp()
    {
        await using var server = new LoopbackSmtpServer();
        var sender = new IdentityEmailSender(
            CreateConfigMock(server.Port, "identity@boxwise.test", "BoxWise Identity").Object,
            NullLogger<IdentityEmailSender>.Instance);

        await sender.SendEmailAsync(
                "user@example.test",
                "【BoxWise】确认邮箱",
                "<p>确认码 123456</p>")
            .WaitAsync(TimeSpan.FromSeconds(10));
        await server.Completion.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal("identity@boxwise.test", server.MailFrom);
        Assert.Equal(new[] { "user@example.test" }, server.Recipients);
        Assert.True(server.QuitReceived);

        var message = ParseMessage(server);
        Assert.Equal("【BoxWise】确认邮箱", message.Subject);
        Assert.Equal("identity@boxwise.test", message.From.Mailboxes.Single().Address);
        Assert.Equal("BoxWise Identity", message.From.Mailboxes.Single().Name);
        Assert.Equal("user@example.test", message.To.Mailboxes.Single().Address);
        Assert.Contains("确认码 123456", message.HtmlBody!);
    }

    [Fact]
    public async Task EmailTwoFactorService_SendVerificationEmail_SendsPlainMail()
    {
        await using var server = new LoopbackSmtpServer();
        var service = CreateEmailTwoFactorService(server.Port, "security@boxwise.test", "BoxWise Security");

        var sent = await service.SendVerificationEmailAsync(
                "user@example.test",
                "654321",
                "张三")
            .WaitAsync(TimeSpan.FromSeconds(10));
        await server.Completion.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.True(sent);
        Assert.Equal("security@boxwise.test", server.MailFrom);
        Assert.Equal(new[] { "user@example.test" }, server.Recipients);

        var message = ParseMessage(server);
        Assert.Equal("【BoxWise】您的验证码", message.Subject);
        Assert.Equal("张三", message.To.Mailboxes.Single().Name);
        Assert.Contains("654321", message.TextBody!);
        Assert.Contains("张三", message.TextBody!);
    }

    [Fact]
    public async Task EmailTwoFactorService_SendChangeNotification_SendsPlainMail()
    {
        await using var server = new LoopbackSmtpServer();
        var service = CreateEmailTwoFactorService(server.Port, "security@boxwise.test", "BoxWise Security");

        await service.SendChangeNotificationAsync("old@example.test", "李四")
            .WaitAsync(TimeSpan.FromSeconds(10));
        await server.Completion.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal("security@boxwise.test", server.MailFrom);
        Assert.Equal(new[] { "old@example.test" }, server.Recipients);

        var message = ParseMessage(server);
        Assert.Equal("【BoxWise】邮箱地址已变更", message.Subject);
        Assert.Equal("李四", message.To.Mailboxes.Single().Name);
        Assert.Contains("已成功修改", message.TextBody!);
    }

    [Fact]
    public async Task SmtpConfigurationService_SendTestEmail_SendsPlainMail()
    {
        await using var server = new LoopbackSmtpServer();
        using var service = CreateSmtpConfigurationService();

        var (saved, saveError) = await service.SaveAsync(new SmtpConfigDto(
            "127.0.0.1",
            server.Port,
            null,
            null,
            "smtp-test@boxwise.test",
            "BoxWise SMTP"));
        Assert.True(saved, saveError);

        var result = await service.SendTestEmailAsync("admin@example.test")
            .WaitAsync(TimeSpan.FromSeconds(10));
        await server.Completion.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal("smtp-test@boxwise.test", server.MailFrom);
        Assert.Equal(new[] { "admin@example.test" }, server.Recipients);

        var message = ParseMessage(server);
        Assert.Equal("【BoxWise】SMTP 配置测试邮件", message.Subject);
        Assert.Contains("SMTP 配置正确", message.TextBody!);
    }

    [Fact]
    public async Task EmailTwoFactorService_SendVerificationEmail_RecipientRejected_ReturnsFalse()
    {
        await using var server = new LoopbackSmtpServer(rejectRecipient: true);
        var service = CreateEmailTwoFactorService(server.Port, "security@boxwise.test", "BoxWise Security");

        var sent = await service.SendVerificationEmailAsync(
                "user@example.test",
                "654321",
                "张三")
            .WaitAsync(TimeSpan.FromSeconds(10));
        await server.Completion.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.False(sent);
        Assert.Equal(new[] { "user@example.test" }, server.Recipients);
        Assert.Null(server.Data);
    }

    [Fact]
    public async Task SmtpConfigurationService_SendTestEmail_RecipientRejected_ReturnsError()
    {
        await using var server = new LoopbackSmtpServer(rejectRecipient: true);
        using var service = CreateSmtpConfigurationService();

        var (saved, saveError) = await service.SaveAsync(new SmtpConfigDto(
            "127.0.0.1",
            server.Port,
            null,
            null,
            "smtp-test@boxwise.test",
            "BoxWise SMTP"));
        Assert.True(saved, saveError);

        var result = await service.SendTestEmailAsync("admin@example.test")
            .WaitAsync(TimeSpan.FromSeconds(10));
        await server.Completion.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.False(result.Success);
        Assert.NotNull(result.ErrorMessage);
        Assert.Contains("SMTP 命令错误", result.ErrorMessage);
        Assert.Null(server.Data);
    }

    private static Mock<ISmtpConfigurationService> CreateConfigMock(
        int port,
        string fromAddress,
        string fromName)
    {
        var config = new SmtpConfigDto("127.0.0.1", port, null, null, fromAddress, fromName);
        var mock = new Mock<ISmtpConfigurationService>();
        mock.Setup(x => x.IsConfigured()).Returns(true);
        mock.Setup(x => x.GetSnapshot()).Returns(config);
        return mock;
    }

    private EmailTwoFactorService CreateEmailTwoFactorService(
        int port,
        string fromAddress,
        string fromName)
        => new(
            _dataProtection,
            CreateConfigMock(port, fromAddress, fromName).Object,
            NullLogger<EmailTwoFactorService>.Instance);

    private SmtpConfigurationService CreateSmtpConfigurationService()
    {
        var dataDir = Path.Combine(_tempDir, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dataDir);

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DataDirectory"] = dataDir,
            })
            .Build();

        return new SmtpConfigurationService(
            _dataProtection,
            configuration,
            NullLogger<SmtpConfigurationService>.Instance);
    }

    private static MimeMessage ParseMessage(LoopbackSmtpServer server)
    {
        Assert.NotNull(server.Data);
        return MimeMessage.Load(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(server.Data)));
    }
}
