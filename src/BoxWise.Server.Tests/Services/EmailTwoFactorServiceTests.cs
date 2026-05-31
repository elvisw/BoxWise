using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using BoxWise.Server.Services;
using BoxWise.Shared.Dtos;
using Moq;

namespace BoxWise.Server.Tests.Services;

public class EmailTwoFactorServiceTests
{
    private readonly EmailTwoFactorService _service;
    private readonly Mock<ISmtpConfigurationService> _smtpConfigMock;

    public EmailTwoFactorServiceTests()
    {
        _smtpConfigMock = new Mock<ISmtpConfigurationService>();
        _smtpConfigMock.Setup(x => x.IsConfigured()).Returns(false);
        _smtpConfigMock.Setup(x => x.GetSnapshot())
            .Returns(new SmtpConfigDto(string.Empty, 587, null, null, null, null));

        // Create a real DataProtection provider for test isolation
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        services.AddDataProtection();
        using var sp = services.BuildServiceProvider();
        var dataProtection = sp.GetRequiredService<IDataProtectionProvider>();

        _service = new EmailTwoFactorService(
            dataProtection, _smtpConfigMock.Object, NullLogger<EmailTwoFactorService>.Instance);
    }

    [Fact]
    public void VerifyCodeOnce_TokenReuse_ReturnsFalse()
    {
        var (code, token) = _service.GenerateCode("user1", "test@test.com");

        // First call should succeed
        var firstResult = _service.VerifyCodeOnce("user1", "test@test.com", code, token);
        Assert.True(firstResult);

        // Second call with same token+code should fail (already consumed)
        var secondResult = _service.VerifyCodeOnce("user1", "test@test.com", code, token);
        Assert.False(secondResult);
    }

    [Fact]
    public void VerifyCodeOnce_WrongCode_AllowsRetry()
    {
        var (code, token) = _service.GenerateCode("user1", "test@test.com");

        // Wrong code should return false but NOT consume the token
        var wrongResult = _service.VerifyCodeOnce("user1", "test@test.com", "000000", token);
        Assert.False(wrongResult);

        // Same token with correct code should still work (token not consumed by failed attempt)
        var retryResult = _service.VerifyCodeOnce("user1", "test@test.com", code, token);
        Assert.True(retryResult);
    }

    [Fact]
    public void VerifyCodeOnce_WrongUserId_Fails()
    {
        var (code, token) = _service.GenerateCode("user1", "test@test.com");

        var result = _service.VerifyCodeOnce("wronguser", "test@test.com", code, token);
        Assert.False(result);
    }

    [Fact]
    public void VerifyCodeOnce_WrongEmail_Fails()
    {
        var (code, token) = _service.GenerateCode("user1", "test@test.com");

        var result = _service.VerifyCodeOnce("user1", "wrong@test.com", code, token);
        Assert.False(result);
    }

    [Fact]
    public void VerifyCodeOnce_MalformedToken_Fails()
    {
        var result = _service.VerifyCodeOnce("user1", "test@test.com", "123456", "garbage-token");
        Assert.False(result);
    }

    [Fact]
    public void VerifyCodeOnce_VerifyCode_BackwardCompatible()
    {
        // VerifyCodeOnce should match the behavior of VerifyCode for the first call
        var (code, token) = _service.GenerateCode("user1", "test@test.com");

        var onceResult = _service.VerifyCodeOnce("user1", "test@test.com", code, token);
        var verifyResult = _service.VerifyCode("user1", "test@test.com", code, token);

        Assert.True(onceResult);
        Assert.True(verifyResult); // VerifyCode is not impacted by VerifyCodeOnce
    }
}
