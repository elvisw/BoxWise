using System.Reflection;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using BoxWise.Server.Endpoints;
using BoxWise.Server.Models;
using BoxWise.Shared.Dtos;

namespace BoxWise.Server.Tests.Endpoints;

public class LlmConfigEndpointsTests
{
    private static async Task<(int StatusCode, T? Body)> InvokeAndReadAsync<T>(
        HttpContext httpContext, string methodName, params object?[] args)
    {
        var method = typeof(LlmConfigEndpoints).GetMethod(methodName,
            BindingFlags.NonPublic | BindingFlags.Static)!;
        var task = (Task)method.Invoke(null, args)!;
        await task;
        var httpResult = task.GetType().GetProperty("Result")!.GetValue(task)!;
        var executeMethod = httpResult.GetType().GetMethod("ExecuteAsync",
            [typeof(HttpContext)])!;
        await (Task)executeMethod.Invoke(httpResult, [httpContext])!;

        T? body = default;
        if (httpContext.Response.Body.Length > 0)
        {
            httpContext.Response.Body.Position = 0;
            body = await JsonSerializer.DeserializeAsync<T>(httpContext.Response.Body,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }

        return (httpContext.Response.StatusCode, body);
    }

    private static HttpContext CreateHttpContext(bool authenticated = true)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var httpContext = new DefaultHttpContext
        {
            RequestServices = services.BuildServiceProvider()
        };
        httpContext.Response.Body = new MemoryStream();
        if (authenticated)
            httpContext.User = new ClaimsPrincipal(new ClaimsIdentity("Test"));
        return httpContext;
    }

    [Fact]
    public async Task GetConfig_WhenConfigured_ReturnsOkWithDto()
    {
        using var db = TestDbContextFactory.Create();
        db.LlmConfigs.Add(new LlmConfig
        {
            Id = 1,
            BaseUrl = "https://api.example.com",
            ApiKey = "sk-test-key",
            Model = "test-model",
            TimeoutSeconds = 60
        });
        await db.SaveChangesAsync();

        var httpContext = CreateHttpContext();
        var (statusCode, body) = await InvokeAndReadAsync<LlmConfigDto>(
            httpContext, "GetLlmConfigAsync", httpContext, db);

        Assert.Equal(200, statusCode);
        Assert.NotNull(body);
        Assert.Equal("https://api.example.com", body.BaseUrl);
        Assert.Equal("sk-test-key", body.ApiKey);
        Assert.Equal("test-model", body.Model);
        Assert.Equal(60, body.TimeoutSeconds);
    }

    [Fact]
    public async Task GetConfig_WhenNotConfigured_ReturnsOkWithEmptyDto()
    {
        using var db = TestDbContextFactory.Create();

        var httpContext = CreateHttpContext();
        var (statusCode, body) = await InvokeAndReadAsync<LlmConfigDto>(
            httpContext, "GetLlmConfigAsync", httpContext, db);

        Assert.Equal(200, statusCode);
        Assert.NotNull(body);
        Assert.Null(body.ApiKey);
        Assert.Null(body.BaseUrl);
        Assert.Equal("doubao-seed-2-0-pro-260215", body.Model);
        Assert.Equal(30, body.TimeoutSeconds);
    }

    [Fact]
    public async Task GetConfig_WhenUnauthenticated_Returns401()
    {
        using var db = TestDbContextFactory.Create();
        var httpContext = CreateHttpContext(authenticated: false);

        var (statusCode, _) = await InvokeAndReadAsync<LlmConfigDto>(
            httpContext, "GetLlmConfigAsync", httpContext, db);

        Assert.Equal(401, statusCode);
    }

    [Fact]
    public void GetConfig_ApiKeyMaskedInToString()
    {
        var dto = new LlmConfigDto(
            "https://api.example.com",
            "sk-secret-key-12345",
            "test-model",
            30);

        var str = dto.ToString();

        Assert.DoesNotContain("sk-secret-key-12345", str);
        Assert.Contains("***", str);
        Assert.Contains("https://api.example.com", str);
    }
}
