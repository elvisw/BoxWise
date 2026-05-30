using System.Net.Mail;

namespace BoxWise.Server.Utilities;

/// <summary>
/// RFC 5322 合规的邮箱格式校验，使用 MailAddress 严格验证。
/// </summary>
public static class EmailValidator
{
    public static bool IsValid(string? email)
    {
        if (email is null) return false;
        try
        {
            var addr = new MailAddress(email);
            return addr.Address == email;
        }
        catch (FormatException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
