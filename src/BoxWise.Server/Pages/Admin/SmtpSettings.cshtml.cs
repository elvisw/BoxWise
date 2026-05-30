using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using BoxWise.Server.Services;
using BoxWise.Shared.Dtos;

namespace BoxWise.Server.Pages.Admin;

[Authorize(Policy = "AdminOnly")]
public class SmtpSettingsModel : PageModel
{
    private readonly ISmtpConfigurationService _smtpConfig;
    private readonly ILogger<SmtpSettingsModel> _logger;

    public SmtpSettingsModel(
        ISmtpConfigurationService smtpConfig,
        ILogger<SmtpSettingsModel> logger)
    {
        _smtpConfig = smtpConfig;
        _logger = logger;
    }

    // 表单字段
    [BindProperty]
    public string Host { get; set; } = string.Empty;

    [BindProperty]
    public int Port { get; set; } = 587;

    [BindProperty]
    public string? Username { get; set; }

    [BindProperty]
    public string? Password { get; set; }

    [BindProperty]
    public string? FromAddress { get; set; }

    [BindProperty]
    public string? FromName { get; set; }

    [BindProperty]
    public string? TestEmail { get; set; }

    // UI 状态
    public bool HasPassword { get; private set; }
    public bool IsConfigured { get; private set; }
    public string? ConfigStatus { get; private set; }
    public string? SuccessMessage { get; private set; }
    public string? ErrorMessage { get; private set; }

    public void OnGet()
    {
        LoadFromSnapshot();
    }

    public async Task<IActionResult> OnPostSaveAsync()
    {
        // FromAddress 非空校验
        if (string.IsNullOrWhiteSpace(Host))
        {
            ErrorMessage = "SMTP 服务器地址不能为空";
            LoadFromSnapshot();
            return Page();
        }

        if (string.IsNullOrWhiteSpace(FromAddress))
        {
            ErrorMessage = "发件人地址不能为空";
            LoadFromSnapshot();
            return Page();
        }

        if (Port is < 1 or > 65535)
        {
            ErrorMessage = "端口号必须为 1-65535 之间的数字";
            LoadFromSnapshot();
            return Page();
        }

        var config = new SmtpConfigDto(
            Host.Trim(),
            Port,
            Username?.Trim(),
            Password,
            FromAddress.Trim(),
            FromName?.Trim()
        );

        var (success, error) = await _smtpConfig.SaveAsync(config);

        if (!success)
        {
            ErrorMessage = error ?? "保存失败";
            LoadFromSnapshot();
            return Page();
        }

        _logger.LogInformation("Admin '{AdminName}' 更新了 SMTP 配置", User.Identity?.Name);
        SuccessMessage = "SMTP 配置已保存";
        LoadFromSnapshot();
        return Page();
    }

    public async Task<IActionResult> OnPostTestAsync()
    {
        if (string.IsNullOrWhiteSpace(TestEmail))
        {
            ErrorMessage = "请输入有效的邮箱地址";
            LoadFromSnapshot();
            return Page();
        }

        if (!_smtpConfig.IsConfigured())
        {
            ErrorMessage = "请先配置 SMTP 服务器";
            LoadFromSnapshot();
            return Page();
        }

        var result = await _smtpConfig.SendTestEmailAsync(TestEmail);

        if (result.Success)
        {
            SuccessMessage = $"测试邮件已发送到 {TestEmail}";
        }
        else
        {
            ErrorMessage = result.ErrorMessage ?? "发送失败";
        }

        LoadFromSnapshot();
        return Page();
    }

    private void LoadFromSnapshot()
    {
        var snapshot = _smtpConfig.GetSnapshot();

        // 仅在首次 OnGet 或无错误回退时设置表单值
        if (!Request.Method.Equals("POST", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(Host))
        {
            Host = snapshot.Host;
            Port = snapshot.Port;
            Username = snapshot.Username;
            FromAddress = snapshot.FromAddress;
            FromName = snapshot.FromName;
        }

        HasPassword = !string.IsNullOrWhiteSpace(snapshot.Password);
        IsConfigured = _smtpConfig.IsConfigured();
        ConfigStatus = IsConfigured ? "已配置" : "尚未配置";
    }
}
