namespace BoxWise.Server.Services;

/// <summary>
/// Keeps the stored LLM API key bound to its origin. A submitted key always wins; a blank
/// key may only be retained when the URL is unchanged or provably points at the same origin.
/// </summary>
public static class LlmConfigKeyPolicy
{
    public static bool CanRetainKey(
        string? existingBaseUrl,
        string? existingApiKey,
        string? newBaseUrl,
        string? newApiKey)
    {
        if (!string.IsNullOrWhiteSpace(newApiKey))
            return true;

        if (string.IsNullOrWhiteSpace(existingApiKey))
            return true;

        var oldUrl = existingBaseUrl?.Trim();
        var targetUrl = newBaseUrl?.Trim();
        if (string.IsNullOrEmpty(targetUrl))
            return false;

        // Legacy values may be invalid URLs; an unchanged value is still the same destination.
        if (string.Equals(oldUrl, targetUrl, StringComparison.Ordinal))
            return true;

        return TryGetOrigin(oldUrl, out var oldOrigin)
            && TryGetOrigin(targetUrl, out var newOrigin)
            && string.Equals(oldOrigin, newOrigin, StringComparison.Ordinal);
    }

    private static bool TryGetOrigin(string? url, out string origin)
    {
        origin = "";
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return false;

        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.Ordinal)
            && !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal))
        {
            return false;
        }

        if (string.IsNullOrEmpty(uri.Host) || !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Fragment))
            return false;

        origin = $"{uri.Scheme}://{uri.IdnHost.ToLowerInvariant()}:{uri.Port}";
        return true;
    }
}
