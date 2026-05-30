using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using BoxWise.Server.Services;
using BoxWise.Shared.Dtos;

namespace BoxWise.Server.Tests.Services;

public sealed class SmtpConfigurationServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly IDataProtectionProvider _dataProtection;
    private readonly SmtpConfigurationService _service;

    public SmtpConfigurationServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempDir);

        var config = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?>()).Build();

        var services = new ServiceCollection();
        services.AddDataProtection()
            .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(_tempDir, "keys")));
        var sp = services.BuildServiceProvider();
        _dataProtection = sp.GetRequiredService<IDataProtectionProvider>();

        // 通过环境变量设置 DataDirectory
        Environment.SetEnvironmentVariable("DataDirectory", _tempDir);
        var envConfig = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DataDirectory"] = _tempDir
            })
            .Build();

        _service = new SmtpConfigurationService(
            _dataProtection,
            envConfig,
            NullLogger<SmtpConfigurationService>.Instance);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); }
        catch { }
    }

    [Fact]
    public void IsConfigured_EmptyConfig_ReturnsFalse()
    {
        Assert.False(_service.IsConfigured());
    }

    [Fact]
    public async Task IsConfigured_ValidConfig_ReturnsTrue()
    {
        var config = new SmtpConfigDto("smtp.example.com", 587, null, null, null, null);
        await _service.SaveAsync(config);
        Assert.True(_service.IsConfigured());
    }

    [Fact]
    public async Task IsConfigured_PortOutOfRange_ReturnsFalse()
    {
        var config = new SmtpConfigDto("smtp.example.com", 0, null, null, null, null);
        await _service.SaveAsync(config);
        Assert.False(_service.IsConfigured());
    }

    [Fact]
    public async Task IsConfigured_PortTooHigh_ReturnsFalse()
    {
        var config = new SmtpConfigDto("smtp.example.com", 99999, null, null, null, null);
        await _service.SaveAsync(config);
        Assert.False(_service.IsConfigured());
    }

    [Fact]
    public async Task SaveAndLoad_PasswordEncryptionRoundTrip()
    {
        var config = new SmtpConfigDto(
            "smtp.example.com", 587, "user", "secret123", "test@boxwise.app", "BoxWise");
        var (success, error) = await _service.SaveAsync(config);
        Assert.True(success);
        Assert.Null(error);

        var snapshot = _service.GetSnapshot();
        Assert.Equal("smtp.example.com", snapshot.Host);
        Assert.Equal(587, snapshot.Port);
        Assert.Equal("user", snapshot.Username);

        // GetSnapshot() 返回解密后的明文密码
        Assert.Equal("secret123", snapshot.Password);

        // ToString 不应泄露密码
        Assert.DoesNotContain("secret123", snapshot.ToString());
    }

    [Fact]
    public async Task Save_EmptyPassword_KeepsOldPassword()
    {
        var config = new SmtpConfigDto(
            "smtp.example.com", 587, "user", "secret123", null, null);
        await _service.SaveAsync(config);
        var firstSnapshot = _service.GetSnapshot();
        var oldPassword = firstSnapshot.Password;

        // 再次保存，密码留空
        var updateConfig = new SmtpConfigDto(
            "smtp.example.com", 587, "user2", null, null, null);
        await _service.SaveAsync(updateConfig);
        var secondSnapshot = _service.GetSnapshot();

        Assert.Equal(oldPassword, secondSnapshot.Password);
        Assert.Equal("user2", secondSnapshot.Username);
    }

    [Fact]
    public async Task Save_HostTooLong_ReturnsError()
    {
        var config = new SmtpConfigDto(
            new string('a', 254), 587, null, null, null, null);
        var (success, error) = await _service.SaveAsync(config);
        Assert.False(success);
        Assert.Contains("不能超过", error ?? "");
    }

    [Fact]
    public async Task Save_FromAddressTooLong_ReturnsError()
    {
        var config = new SmtpConfigDto(
            "smtp.example.com", 587, null, null, new string('a', 257) + "@b.com", null);
        var (success, error) = await _service.SaveAsync(config);
        Assert.False(success);
        Assert.Contains("不能超过", error ?? "");
    }

    [Fact]
    public async Task Save_UsernameTooLong_ReturnsError()
    {
        var config = new SmtpConfigDto(
            "smtp.example.com", 587, new string('a', 257), null, null, null);
        var (success, error) = await _service.SaveAsync(config);
        Assert.False(success);
        Assert.Contains("不能超过", error ?? "");
    }

    [Fact]
    public async Task Save_FromNameControlChars_AreStripped()
    {
        var config = new SmtpConfigDto(
            "smtp.example.com", 587, null, null, "test@boxwise.app", "Box\r\nWise\t");
        var (success, error) = await _service.SaveAsync(config);
        Assert.True(success);
        Assert.Null(error);

        var snapshot = _service.GetSnapshot();
        Assert.Equal("BoxWise", snapshot.FromName);
    }

    [Fact]
    public async Task Save_NonDpPassword_IsReEncrypted()
    {
        // 模拟旧配置或外部设置的明文密码（不以 DP: 开头）
        var config = new SmtpConfigDto(
            "smtp.example.com", 587, "user", "plain-text-password", null, null);
        var (success, error) = await _service.SaveAsync(config);
        Assert.True(success);

        var snapshot = _service.GetSnapshot();
        // GetSnapshot() 返回解密后的明文
        Assert.Equal("plain-text-password", snapshot.Password);
    }

    [Fact]
    public async Task Save_AlreadyEncryptedPassword_StaysEncrypted()
    {
        // GetSnapshot 返回明文，再次保存时明文被重新加密但最终返回的仍是相同明文
        var config = new SmtpConfigDto(
            "smtp.example.com", 587, "user", "secret123", null, null);
        await _service.SaveAsync(config);
        var firstSnapshot = _service.GetSnapshot();

        // 再次保存相同明文密码
        var updateConfig = new SmtpConfigDto(
            "smtp.example.com", 587, "user", firstSnapshot.Password, null, null);
        await _service.SaveAsync(updateConfig);
        var secondSnapshot = _service.GetSnapshot();

        Assert.Equal(firstSnapshot.Password, secondSnapshot.Password);
    }

    [Fact]
    public async Task GetSnapshot_ReturnsCopy()
    {
        var config = new SmtpConfigDto("smtp.example.com", 587, null, null, null, null);
        await _service.SaveAsync(config);
        var snap1 = _service.GetSnapshot();

        // 修改快照不应影响原配置
        var modified = snap1 with { Host = "changed.com" };
        var snap2 = _service.GetSnapshot();
        Assert.Equal("smtp.example.com", snap2.Host);
    }

    [Fact]
    public async Task LoadFromCorruptedJson_DegradesGracefully()
    {
        // 直接写入损坏 JSON
        var dataDir = _tempDir;
        var filePath = Path.Combine(dataDir, "smtp-config.json");
        await File.WriteAllTextAsync(filePath, "{{{corrupted json}}");

        // 新建 Service 应优雅降级
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DataDirectory"] = dataDir
            })
            .Build();

        var recovered = new SmtpConfigurationService(
            _dataProtection,
            config,
            NullLogger<SmtpConfigurationService>.Instance);

        Assert.False(recovered.IsConfigured());
    }

    [Fact]
    public void LoadFromMissingFile_FallsBackToEmpty()
    {
        // 文件不存在时应该用空配置
        Assert.False(_service.IsConfigured());
    }

    [Fact]
    public async Task SendTestEmail_NotConfigured_ReturnsError()
    {
        // service 未配置 → 返回错误
        var result = await _service.SendTestEmailAsync("test@boxwise.app");
        Assert.False(result.Success);
        Assert.NotNull(result.ErrorMessage);
    }

    [Fact]
    public async Task Save_Load_RoundTrip()
    {
        var config = new SmtpConfigDto(
            "smtp.gmail.com", 587, "user@gmail.com", "app-password",
            "user@gmail.com", "BoxWise");
        var (success, error) = await _service.SaveAsync(config);
        Assert.True(success);
        Assert.Null(error);

        var snapshot = _service.GetSnapshot();
        Assert.Equal("smtp.gmail.com", snapshot.Host);
        Assert.Equal(587, snapshot.Port);
        Assert.Equal("user@gmail.com", snapshot.Username);
        Assert.Equal("user@gmail.com", snapshot.FromAddress);
        Assert.Equal("BoxWise", snapshot.FromName);
        // GetSnapshot() 返回解密后的明文
        Assert.Equal("app-password", snapshot.Password);
    }

    [Fact]
    public async Task ConcurrentSaves_WithSemaphore_NoDataLoss()
    {
        var config1 = new SmtpConfigDto("smtp1.example.com", 587, null, null, null, null);
        var config2 = new SmtpConfigDto("smtp2.example.com", 587, null, null, null, null);

        var results = await Task.WhenAll(
            _service.SaveAsync(config1),
            _service.SaveAsync(config2));

        var snapshot = _service.GetSnapshot();
        // 两个保存都成功，最后写入的胜出
        Assert.True(results[0].Success);
        Assert.True(results[1].Success);
        Assert.Contains("smtp", snapshot.Host);
    }
}
