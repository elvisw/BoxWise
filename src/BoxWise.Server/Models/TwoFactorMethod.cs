namespace BoxWise.Server.Models;

public enum TwoFactorMethod
{
    None = 0,
    TOTP = 1,
    Email = 2,
    WebAuthn = 3  // 前向预留，对应逻辑在 8-3 实现
}
