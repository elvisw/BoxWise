using BoxWise.Shared.Dtos;

namespace BoxWise.Server.Services;

/// <summary>
/// SMTP 配置管理接口，便于测试 Mock。
/// </summary>
public interface ISmtpConfigurationService
{
    /// <summary>检查 SMTP 是否已配置（Host 非空 + Port 有效）。</summary>
    bool IsConfigured();

    /// <summary>返回当前配置的不可变快照（浅拷贝）。</summary>
    SmtpConfigDto GetSnapshot();

    /// <summary>保存配置（加密密码 → 原子写入 JSON）。</summary>
    Task<(bool Success, string? Error)> SaveAsync(SmtpConfigDto config);

    /// <summary>发送测试邮件验证当前配置。</summary>
    Task<SmtpTestResult> SendTestEmailAsync(string toEmail);
}
