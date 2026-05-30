namespace BoxWise.Shared.Dtos;

/// <summary>
/// 测试邮件发送结果。
/// </summary>
public sealed record SmtpTestResult(bool Success, string? ErrorMessage);
