namespace BoxWise.Shared.Dtos;

/// <summary>
/// SMTP 配置 DTO。序列化前密码已由 SmtpConfigurationService 加密为 DP: 前缀格式，
/// 故无需 [JsonIgnore]。<c>override ToString()</c> 遮蔽密码防止 record 默认实现泄露。
/// </summary>
public sealed record SmtpConfigDto(
    string Host,
    int Port,
    string? Username,
    string? Password,
    string? FromAddress,
    string? FromName)
{
    /// <summary>
    /// 遮蔽密码，防止 record 默认 ToString 泄露。
    /// </summary>
    public override string ToString()
        => $"SmtpConfigDto {{ Host = {Host}, Port = {Port}, Username = {Username}, Password = ***, FromAddress = {FromAddress}, FromName = {FromName} }}";
}
