namespace BoxWise.Shared.Dtos;

/// <summary>
/// 登录响应 DTO。
/// ⚠️ 部署注意事项：Blazor WASM 客户端缓存可能造成版本偏差 ——
/// 旧版客户端对新字段会做 JSON 反序列化忽略，不会报错。
/// 新增安全关键字段（如 RequiresTwoFactor）时，旧客户端可能跳过 2FA 流程。
/// 建议：部署时清空 CDN 缓存，或使用 service worker 版本检查强制刷新。
/// EmailForTwoFactor 清除逻辑已统一：RecoveryCodeService、AdminTwoFactorEndpoints、
/// ResetTwoFactor.cshtml.cs、Program.cs CLI 均清除 EmailForTwoFactor。
/// </summary>
public record LoginResponse(
    string? Username,
    bool? IsAdmin,
    bool? IsSpecificAdmin,
    bool PasswordRequiresChange,
    bool RequiresTwoFactor,
    bool RequiresTwoFactorSetup = false,
    string? Email = null,
    bool PasswordManagedByEnv = false
);
