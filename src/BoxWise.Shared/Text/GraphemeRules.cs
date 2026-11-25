using System.Globalization;

namespace BoxWise.Shared.Text;

/// <summary>
/// 位置描述的用户感知字符（Unicode 字素簇）规则，客户端与服务端共用。
/// </summary>
public static class GraphemeRules
{
    public const int MaxDescriptionGraphemes = 300;

    public const string DescriptionTooLongMessage = "描述不能超过 300 字。";

    /// <summary>
    /// 统计 Unicode 字素簇数量（不按 UTF-16 单元或码点计数）。
    /// </summary>
    public static int CountGraphemes(string? value)
    {
        if (string.IsNullOrEmpty(value)) return 0;

        var enumerator = StringInfo.GetTextElementEnumerator(value);
        var count = 0;
        while (enumerator.MoveNext()) count++;
        return count;
    }

    /// <summary>
    /// 去除首尾空白；去除后为空视为无描述（null）。内部换行保留。
    /// </summary>
    public static string? NormalizeDescription(string? value)
    {
        if (value is null) return null;

        var trimmed = value.Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }

    public static bool IsDescriptionWithinLimit(string? normalizedDescription) =>
        CountGraphemes(normalizedDescription) <= MaxDescriptionGraphemes;

    /// <summary>
    /// 规范化并校验描述。返回 false 时不修改输入，也不截断。
    /// </summary>
    public static bool TryNormalizeDescription(
        string? value,
        out string? normalizedDescription,
        out string? error)
    {
        normalizedDescription = NormalizeDescription(value);
        error = null;

        if (normalizedDescription is not null
            && !IsDescriptionWithinLimit(normalizedDescription))
        {
            error = DescriptionTooLongMessage;
            return false;
        }

        return true;
    }
}
