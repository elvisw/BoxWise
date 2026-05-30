namespace BoxWise.Shared.Dtos;

/// <summary>
/// 2FA 挑战响应，返回用户可用的验证方法及邮箱验证令牌。
/// </summary>
/// <param name="AllowedMethods">可用的 2FA 验证方式列表，如 "TOTP"、"Email"。</param>
/// <param name="Token">邮箱验证令牌，仅在 Email 方式可用时非 null；用于后续验证（VerifyAsync）中验证码绑定。</param>
/// <param name="HasRecoveryCodes">用户当前是否存在有效的恢复码。</param>
public record TwoFactorChallengeResponse(List<string> AllowedMethods, string? Token = null, bool HasRecoveryCodes = false);
