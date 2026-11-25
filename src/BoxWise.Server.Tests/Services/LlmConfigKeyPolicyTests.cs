using BoxWise.Server.Services;

namespace BoxWise.Server.Tests.Services;

public class LlmConfigKeyPolicyTests
{
    [Theory]
    [InlineData("https://api.example.com/v1", "https://api.example.com/v2", true)]
    [InlineData("https://api.example.com", "https://api.example.com:443/v1", true)]
    [InlineData("http://api.example.com", "http://api.example.com:80/v1", true)]
    [InlineData("https://api.example.com/v1", "http://api.example.com/v1", false)]
    [InlineData("https://api.example.com", "https://other.example.com", false)]
    [InlineData("https://api.example.com", "https://api.example.com:8443", false)]
    [InlineData("https://api.example.com", "https://api.example.com.evil.test", false)]
    [InlineData("not a valid url", "not a valid url", true)]
    [InlineData("not a valid url", "https://api.example.com", false)]
    [InlineData("/relative/path", "/relative/path", true)]
    [InlineData("/relative/path", "https://api.example.com", false)]
    [InlineData("https://user:pass@api.example.com", "https://api.example.com", false)]
    [InlineData("https://api.example.com/v1#fragment", "https://api.example.com/v2", false)]
    public void CanRetainKey_RequiresProvableSameOrigin(
        string existingBaseUrl, string newBaseUrl, bool expected)
    {
        var result = LlmConfigKeyPolicy.CanRetainKey(
            existingBaseUrl, "stored-key", newBaseUrl, "");

        Assert.Equal(expected, result);
    }

    [Fact]
    public void CanRetainKey_SuppliedKeyAlwaysAllowed()
    {
        var result = LlmConfigKeyPolicy.CanRetainKey(
            "https://api.example.com", "stored-key", "https://other.example.com", "new-key");

        Assert.True(result);
    }

    [Fact]
    public void CanRetainKey_NoStoredKey_AllowsBlankKey()
    {
        var result = LlmConfigKeyPolicy.CanRetainKey(
            "https://api.example.com", null, "https://other.example.com", null);

        Assert.True(result);
    }

    [Fact]
    public void CanRetainKey_StoredKeyWithBlankTargetUrl_Rejected()
    {
        var result = LlmConfigKeyPolicy.CanRetainKey(
            "https://api.example.com", "stored-key", "", "");

        Assert.False(result);
    }
}
