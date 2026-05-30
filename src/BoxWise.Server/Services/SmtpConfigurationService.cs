using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using BoxWise.Shared.Dtos;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.AspNetCore.DataProtection;
using MimeKit;

namespace BoxWise.Server.Services;

/// <summary>
/// SMTP 配置服务，Singleton。构造时从 JSON 文件加载（不存在则回退 IConfiguration）。
/// Data Protection 加密密码，原子写入 JSON，快照模式供发送方使用。
/// </summary>
public sealed class SmtpConfigurationService : ISmtpConfigurationService, IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip,
    };

    private readonly string _filePath;
    private readonly IDataProtector _protector;
    private readonly IConfiguration _config;
    private readonly ILogger<SmtpConfigurationService> _logger;
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    private SmtpConfigDto _current = new(string.Empty, 587, null, null, null, null);

    /// <summary>DP 加密密码前缀，用于识别加密版本。</summary>
    internal const string DpPrefix = "DP:";

    public SmtpConfigurationService(
        IDataProtectionProvider protectionProvider,
        IConfiguration configuration,
        ILogger<SmtpConfigurationService> logger)
    {
        _protector = protectionProvider.CreateProtector("BoxWise.SmtpConfig");
        _config = configuration;
        _logger = logger;

        var dataDir = configuration["DataDirectory"] ?? "data";
        _filePath = Path.Combine(dataDir, "smtp-config.json");

        LoadFromFileOrFallback();
    }

    public bool IsConfigured()
        => !string.IsNullOrWhiteSpace(_current.Host)
           && _current.Port is >= 1 and <= 65535;

    public SmtpConfigDto GetSnapshot()
    {
        var plaintext = TryDecryptPassword(_current.Password);
        return _current with { Password = plaintext };
    }

    public async Task<(bool Success, string? Error)> SaveAsync(SmtpConfigDto config)
    {
        await _semaphore.WaitAsync();
        try
        {
            // 服务端长度校验
            if (config.Host.Length > 253)
                return (false, "SMTP 服务器地址不能超过 253 个字符");
            if (config.Username?.Length > 256)
                return (false, "用户名不能超过 256 个字符");
            if (config.FromAddress?.Length > 256)
                return (false, "发件人地址不能超过 256 个字符");
            if (config.FromName?.Length > 256)
                return (false, "发件人名称不能超过 256 个字符");

            // 剥离 FromName 控制字符
            var cleanedFromName = config.FromName is not null
                ? string.Concat(config.FromName.Where(c => !char.IsControl(c)))
                : null;

            // 密码处理：空 = 保持旧值，非空且不以 DP: 开头 = 加密
            string? encryptedPassword = _current.Password;
            if (!string.IsNullOrWhiteSpace(config.Password))
            {
                if (!config.Password.StartsWith(DpPrefix, StringComparison.Ordinal))
                {
                    encryptedPassword = DpPrefix + _protector.Protect(config.Password);
                }
                else
                {
                    encryptedPassword = config.Password;
                }
            }

            var toSave = config with
            {
                Password = encryptedPassword,
                FromName = cleanedFromName,
            };

            // 原子写入：先写 .tmp → File.Move 覆盖
            var tmpPath = _filePath + ".tmp";
            var json = JsonSerializer.Serialize(toSave, JsonOptions);
            await File.WriteAllTextAsync(tmpPath, json);
            File.Move(tmpPath, _filePath, overwrite: true);

            // 成功后才更新内存
            _current = toSave;
            return (true, null);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "SMTP 配置 JSON 序列化失败");
            return (false, "配置数据格式错误");
        }
        catch (IOException ex)
        {
            _logger.LogError(ex, "SMTP 配置写入文件失败");
            return (false, "写入配置文件失败，请检查磁盘空间和权限");
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogError(ex, "SMTP 配置写入权限不足");
            return (false, "写入配置文件权限不足");
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task<SmtpTestResult> SendTestEmailAsync(string toEmail)
    {
        var snapshot = GetSnapshot();
        if (!IsConfigured())
            return new SmtpTestResult(false, "请先配置 SMTP 服务器");

        try
        {
            var message = new MimeMessage();
            var fromAddress = string.IsNullOrWhiteSpace(snapshot.FromAddress)
                ? "noreply@boxwise.app"
                : snapshot.FromAddress;
            var fromName = string.IsNullOrWhiteSpace(snapshot.FromName)
                ? "BoxWise"
                : snapshot.FromName;
            message.From.Add(new MailboxAddress(fromName, fromAddress));
            message.To.Add(new MailboxAddress("", toEmail));
            message.Subject = "【BoxWise】SMTP 配置测试邮件";

            var body = new TextPart("plain")
            {
                Text = @"这是一封来自 BoxWise 的 SMTP 配置测试邮件。

如果您收到了此邮件，说明 SMTP 配置正确，邮件发送功能正常运行。

此致，
BoxWise 安全团队"
            };
            message.Body = body;

            using var client = new SmtpClient();
            client.Timeout = 15000;
            client.SslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13;
            await client.ConnectAsync(snapshot.Host, snapshot.Port,
                snapshot.Port == 465 ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTlsWhenAvailable);
            if (!string.IsNullOrWhiteSpace(snapshot.Username))
                await client.AuthenticateAsync(snapshot.Username, snapshot.Password ?? "");
            await client.SendAsync(message);
            await TryDisconnectAsync(client);

            _logger.LogInformation("SMTP 测试邮件已发送到 {Email}", toEmail);
            return new SmtpTestResult(true, null);
        }
        catch (MailKit.Security.AuthenticationException)
        {
            return new SmtpTestResult(false, "SMTP 认证失败，请检查用户名和密码");
        }
        catch (SmtpCommandException ex)
        {
            _logger.LogError(ex, "SMTP 测试发送失败（命令错误）");
            return new SmtpTestResult(false, $"SMTP 命令错误：{ex.Message}");
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or SocketException)
        {
            _logger.LogError(ex, "SMTP 测试发送失败（连接错误）");
            return new SmtpTestResult(false, $"SMTP 连接失败：{ex.Message}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SMTP 测试发送失败（未知错误）");
            return new SmtpTestResult(false, $"发送失败：{ex.Message}");
        }
    }

    public void Dispose() => _semaphore.Dispose();

    // ────────────── Private ──────────────

    /// <summary>
    /// 从 JSON 加载配置。文件不存在时从 IConfiguration 回退迁移。
    /// JSON 损坏时优雅降级（空配置 + 日志警告）。
    /// </summary>
    private void LoadFromFileOrFallback()
    {
        if (File.Exists(_filePath))
        {
            try
            {
                var json = File.ReadAllText(_filePath);
                var loaded = JsonSerializer.Deserialize<SmtpConfigDto>(json, JsonOptions);
                if (loaded is not null)
                {
                    _current = loaded;
                    _logger.LogInformation("SMTP 配置已从 {Path} 加载", _filePath);
                    return;
                }
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "SMTP 配置文件 {Path} 损坏，将使用空配置", _filePath);
            }
            catch (IOException ex)
            {
                _logger.LogWarning(ex, "SMTP 配置文件 {Path} 读取失败，将使用空配置", _filePath);
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogWarning(ex, "SMTP 配置文件 {Path} 权限不足，将使用空配置", _filePath);
            }

            _current = new SmtpConfigDto(string.Empty, 587, null, null, null, null);
            return;
        }

        // 文件不存在：尝试从 IConfiguration 回退迁移
        var host = _config["Smtp:Host"];
        if (!string.IsNullOrWhiteSpace(host))
        {
            var portStr = _config["Smtp:Port"];
            var port = !string.IsNullOrWhiteSpace(portStr) && int.TryParse(portStr, out var p) ? p : 587;
            var username = _config["Smtp:Username"];
            var rawPassword = _config["Smtp:Password"];
            var fromAddress = _config["Smtp:FromAddress"];
            var fromName = _config["Smtp:FromName"];

            string? encryptedPassword = null;
            if (!string.IsNullOrWhiteSpace(rawPassword))
            {
                encryptedPassword = DpPrefix + _protector.Protect(rawPassword);
            }

            _current = new SmtpConfigDto(host, port, username, encryptedPassword, fromAddress, fromName);

            // 原子写入初始配置
            TryWriteInitialFile();
            _logger.LogInformation("SMTP 配置已从 appsettings 迁移到 {Path}", _filePath);
        }
        else
        {
            _current = new SmtpConfigDto(string.Empty, 587, null, null, null, null);
            _logger.LogDebug("SMTP 未配置，使用空配置");
        }
    }

    /// <summary>
    /// 首次迁移时原子写入初始配置（创建时 + 从 IConfiguration 回退时）。
    /// </summary>
    private void TryWriteInitialFile()
    {
        try
        {
            var tmpPath = _filePath + ".tmp";
            var json = JsonSerializer.Serialize(_current, JsonOptions);
            File.WriteAllText(tmpPath, json);
            File.Move(tmpPath, _filePath, overwrite: true);
        }
        catch (IOException ex)
        {
            _logger.LogError(ex, "写入初始 SMTP 配置文件失败");
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogError(ex, "写入初始 SMTP 配置文件权限不足");
        }
    }

    /// <summary>
    /// 尝试解密密码。DP 前缀不存在时返回原值（兼容未加密的旧配置）。
    /// 捕获 CryptographicException（密钥环丢失等）。
    /// </summary>
    private string? TryDecryptPassword(string? password)
    {
        if (string.IsNullOrWhiteSpace(password))
            return null;

        if (!password.StartsWith(DpPrefix, StringComparison.Ordinal))
            return password;

        try
        {
            return _protector.Unprotect(password[DpPrefix.Length..]);
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException)
        {
            _logger.LogWarning(ex, "SMTP 密码解密失败，密钥环可能已丢失");
            return null;
        }
    }

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
