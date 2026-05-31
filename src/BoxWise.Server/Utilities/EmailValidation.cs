namespace BoxWise.Server.Utilities;

/// <summary>
/// 邮箱验证共享辅助方法。
/// 对邮箱进行空值、长度和格式校验。
/// 返回 null 表示验证通过，否则返回错误消息。
/// </summary>
public static class EmailValidation
{
    public static string? Validate(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
            return "邮箱不能为空";

        if (email.Length > 256 || !EmailValidator.IsValid(email))
            return "请输入有效的邮箱地址";

        return null;
    }
}
