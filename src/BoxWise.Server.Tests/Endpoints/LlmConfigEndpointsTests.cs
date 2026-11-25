using System.Reflection;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using BoxWise.Server.Data;
using BoxWise.Server.Endpoints;
using BoxWise.Server.Models;
using BoxWise.Shared.Dtos;

namespace BoxWise.Server.Tests.Endpoints;

public class LlmConfigEndpointsTests
{
    private static async Task<(int StatusCode, T? Body)> InvokeAndReadAsync<T>(
        HttpContext httpContext, string methodName, params object?[] args)
    {
        var httpResult = await InvokeResultAsync(methodName, args);
        await ExecuteAsync(httpResult, httpContext);

        T? body = default;
        if (httpContext.Response.Body.Length > 0)
        {
            httpContext.Response.Body.Position = 0;
            body = await JsonSerializer.DeserializeAsync<T>(httpContext.Response.Body,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }

        return (httpContext.Response.StatusCode, body);
    }

    private static async Task<int> InvokeStatusAsync(
        HttpContext httpContext, string methodName, params object?[] args)
    {
        var httpResult = await InvokeResultAsync(methodName, args);
        await ExecuteAsync(httpResult, httpContext);
        return httpContext.Response.StatusCode;
    }

    private static async Task<(int StatusCode, string Body)> InvokeStatusAndBodyAsync(
        HttpContext httpContext, string methodName, params object?[] args)
    {
        var httpResult = await InvokeResultAsync(methodName, args);
        await ExecuteAsync(httpResult, httpContext);
        httpContext.Response.Body.Position = 0;
        using var reader = new StreamReader(httpContext.Response.Body, leaveOpen: true);
        return (httpContext.Response.StatusCode, await reader.ReadToEndAsync());
    }

    private static async Task<object> InvokeResultAsync(string methodName, params object?[] args)
    {
        var method = typeof(LlmConfigEndpoints).GetMethod(methodName,
            BindingFlags.NonPublic | BindingFlags.Static)!;
        var task = (Task)method.Invoke(null, args)!;
        await task;
        var result = task.GetType().GetProperty("Result")!.GetValue(task)!;

        // Results<...> unions expose the selected IResult through Result.
        var inner = result.GetType().GetProperty("Result", BindingFlags.Public | BindingFlags.Instance);
        if (inner?.GetValue(result) is { } selected && !ReferenceEquals(selected, result))
            return selected;

        return result;
    }

    private static async Task ExecuteAsync(object httpResult, HttpContext httpContext)
    {
        var executeMethod = httpResult.GetType().GetMethod("ExecuteAsync",
            [typeof(HttpContext)])!;
        await (Task)executeMethod.Invoke(httpResult, [httpContext])!;
    }

    private static HttpContext CreateHttpContext(ClaimsPrincipal? user = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var httpContext = new DefaultHttpContext
        {
            RequestServices = services.BuildServiceProvider()
        };
        httpContext.Response.Body = new MemoryStream();
        if (user is not null)
            httpContext.User = user;
        return httpContext;
    }

    private static HttpContext CreateHttpContext(bool authenticated)
        => CreateHttpContext(authenticated ? new ClaimsPrincipal(new ClaimsIdentity("Test")) : null);

    private static async Task<(TestIdentityContext Context, ClaimsPrincipal Principal)> CreateUserContextAsync(
        bool admin)
    {
        var context = await TestIdentityFactory.CreateAsync();
        var user = new AppUser
        {
            UserName = admin ? "admin" : "member",
            Email = admin ? "admin@example.com" : "member@example.com",
            EmailConfirmed = true
        };
        var created = await context.UserManager.CreateAsync(user, "pass1234");
        Assert.True(created.Succeeded, string.Join("; ", created.Errors.Select(e => e.Description)));

        if (admin)
        {
            var added = await context.UserManager.AddToRoleAsync(user, "Admin");
            Assert.True(added.Succeeded, string.Join("; ", added.Errors.Select(e => e.Description)));
        }

        var identity = new ClaimsIdentity(IdentityConstants.ApplicationScheme);
        identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, user.Id));
        identity.AddClaim(new Claim(ClaimTypes.Name, user.UserName));
        return (context, new ClaimsPrincipal(identity));
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

        var httpContext = CreateHttpContext(authenticated: true);
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

        var httpContext = CreateHttpContext(authenticated: true);
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

    [Fact]
    public async Task UpdateConfig_OriginChangeWithoutKey_Returns400AndLeavesStoredConfig()
    {
        var identity = await CreateUserContextAsync(admin: true);
        await using var identityContext = identity.Context;
        await using var database = await SqliteTestDatabase.CreateAsync();
        await database.SeedConfigAsync("https://api.deepseek.com", "old-key");
        await using var db = database.CreateContext();

        var httpContext = CreateHttpContext(identity.Principal);
        var request = new LlmConfigDto("https://api.openai.com/v1", "", "new-model", 60);

        var status = await InvokeStatusAsync(
            httpContext, "UpdateLlmConfigAsync", httpContext, db, identity.Context.UserManager, request);

        Assert.Equal(400, status);
        await AssertStoredAsync(database, "https://api.deepseek.com", "old-key", "old-model", 30);
    }

    [Fact]
    public async Task UpdateConfig_OriginChangeWithKey_SavesNewValues()
    {
        var identity = await CreateUserContextAsync(admin: true);
        await using var identityContext = identity.Context;
        await using var database = await SqliteTestDatabase.CreateAsync();
        await database.SeedConfigAsync("https://api.deepseek.com", "old-key");
        await using var db = database.CreateContext();

        var httpContext = CreateHttpContext(identity.Principal);
        var request = new LlmConfigDto("https://api.openai.com/v1", "new-key", "new-model", 60);

        var (status, body) = await InvokeAndReadAsync<LlmConfigDto>(
            httpContext, "UpdateLlmConfigAsync", httpContext, db, identity.Context.UserManager, request);

        Assert.Equal(200, status);
        Assert.NotNull(body);
        Assert.Equal("https://api.openai.com/v1", body.BaseUrl);
        Assert.Equal("new-key", body.ApiKey);
        await AssertStoredAsync(database, "https://api.openai.com/v1", "new-key", "new-model", 60);
    }

    [Fact]
    public async Task UpdateConfig_SameOriginPathChangeWithoutKey_RetainsStoredKey()
    {
        var identity = await CreateUserContextAsync(admin: true);
        await using var identityContext = identity.Context;
        await using var database = await SqliteTestDatabase.CreateAsync();
        await database.SeedConfigAsync("https://api.deepseek.com/v1", "old-key");
        await using var db = database.CreateContext();

        var httpContext = CreateHttpContext(identity.Principal);
        var request = new LlmConfigDto("https://api.deepseek.com/chat/completions?profile=vision", "", "new-model", 60);

        var (status, body) = await InvokeAndReadAsync<LlmConfigDto>(
            httpContext, "UpdateLlmConfigAsync", httpContext, db, identity.Context.UserManager, request);

        Assert.Equal(200, status);
        Assert.NotNull(body);
        Assert.Equal("https://api.deepseek.com/chat/completions?profile=vision", body.BaseUrl);
        Assert.Equal("old-key", body.ApiKey);
        await AssertStoredAsync(database, "https://api.deepseek.com/chat/completions?profile=vision", "old-key", "new-model", 60);
    }

    [Fact]
    public async Task UpdateConfig_UnchangedInvalidLegacyUrlWithoutKey_RetainsStoredKey()
    {
        var identity = await CreateUserContextAsync(admin: true);
        await using var identityContext = identity.Context;
        await using var database = await SqliteTestDatabase.CreateAsync();
        await database.SeedConfigAsync("not a valid url", "old-key");
        await using var db = database.CreateContext();

        var httpContext = CreateHttpContext(identity.Principal);
        var request = new LlmConfigDto("not a valid url", "", "new-model", 60);

        var (status, body) = await InvokeAndReadAsync<LlmConfigDto>(
            httpContext, "UpdateLlmConfigAsync", httpContext, db, identity.Context.UserManager, request);

        Assert.Equal(200, status);
        Assert.NotNull(body);
        Assert.Equal("not a valid url", body.BaseUrl);
        Assert.Equal("old-key", body.ApiKey);
        await AssertStoredAsync(database, "not a valid url", "old-key", "new-model", 60);
    }

    [Fact]
    public async Task UpdateConfig_ChangedInvalidLegacyUrlWithoutKey_Returns400()
    {
        var identity = await CreateUserContextAsync(admin: true);
        await using var identityContext = identity.Context;
        await using var database = await SqliteTestDatabase.CreateAsync();
        await database.SeedConfigAsync("not a valid url", "old-key");
        await using var db = database.CreateContext();

        var httpContext = CreateHttpContext(identity.Principal);
        var request = new LlmConfigDto("https://api.deepseek.com", "", "new-model", 60);

        var status = await InvokeStatusAsync(
            httpContext, "UpdateLlmConfigAsync", httpContext, db, identity.Context.UserManager, request);

        Assert.Equal(400, status);
        await AssertStoredAsync(database, "not a valid url", "old-key", "old-model", 30);
    }

    [Fact]
    public async Task UpdateConfig_ConcurrentOriginChangeWithoutKey_Returns400AndKeepsConcurrentPair()
    {
        var identity = await CreateUserContextAsync(admin: true);
        await using var identityContext = identity.Context;
        await using var database = await SqliteTestDatabase.CreateAsync();
        await database.SeedConfigAsync("https://old.example.com/v1", "old-key");

        var interceptor = new ConcurrentUpdateInterceptor(async () =>
        {
            await using var concurrent = database.CreateContext();
            var row = await concurrent.LlmConfigs.SingleAsync(c => c.Id == 1);
            row.BaseUrl = "https://new.example.com/v1";
            row.ApiKey = "new-key";
            row.Model = "new-model";
            row.TimeoutSeconds = 60;
            await concurrent.SaveChangesAsync();
        });
        await using var db = database.CreateContext(interceptor);

        var httpContext = CreateHttpContext(identity.Principal);
        var request = new LlmConfigDto("https://old.example.com/v2", "", "request-model", 30);

        var status = await InvokeStatusAsync(
            httpContext, "UpdateLlmConfigAsync", httpContext, db, identity.Context.UserManager, request);

        Assert.Equal(400, status);
        await AssertStoredAsync(database, "https://new.example.com/v1", "new-key", "new-model", 60);
    }

    [Fact]
    public async Task UpdateConfig_PersistentConflict_Returns409WithoutSensitiveValues()
    {
        var identity = await CreateUserContextAsync(admin: true);
        await using var identityContext = identity.Context;
        await using var database = await SqliteTestDatabase.CreateAsync();
        await database.SeedConfigAsync("https://old.example.com/v1", "old-key");

        var writes = 0;
        var interceptor = new ConcurrentUpdateInterceptor(async () =>
        {
            writes++;
            await using var concurrent = database.CreateContext();
            var row = await concurrent.LlmConfigs.SingleAsync(c => c.Id == 1);
            row.ApiKey = $"concurrent-key-{writes}";
            row.Model = $"concurrent-model-{writes}";
            await concurrent.SaveChangesAsync();
        }, maxFires: int.MaxValue);
        await using var db = database.CreateContext(interceptor);

        var httpContext = CreateHttpContext(identity.Principal);
        var request = new LlmConfigDto("https://old.example.com/v2", "", "request-model", 30);

        var (status, body) = await InvokeStatusAndBodyAsync(
            httpContext, "UpdateLlmConfigAsync", httpContext, db, identity.Context.UserManager, request);

        Assert.Equal(409, status);
        Assert.True(writes > 1);
        Assert.DoesNotContain("old-key", body);
        Assert.DoesNotContain("concurrent-key", body);
        Assert.DoesNotContain("request-model", body);

        var stored = await database.ReadConfigAsync();
        Assert.NotNull(stored);
        Assert.Equal("https://old.example.com/v1", stored!.BaseUrl);
        Assert.StartsWith("concurrent-key-", stored.ApiKey);
        Assert.StartsWith("concurrent-model-", stored.Model);
    }

    [Fact]
    public async Task UpdateConfig_NonAdmin_Returns403()
    {
        var identity = await CreateUserContextAsync(admin: false);
        await using var identityContext = identity.Context;
        await using var database = await SqliteTestDatabase.CreateAsync();
        await using var db = database.CreateContext();
        var request = new LlmConfigDto("https://api.deepseek.com", "new-key", "new-model", 60);

        var httpContext = CreateHttpContext(identity.Principal);
        var result = await InvokeResultAsync(
            "UpdateLlmConfigAsync", httpContext, db, identity.Context.UserManager, request);

        Assert.IsType<ForbidHttpResult>(result);
    }

    [Fact]
    public async Task UpdateConfig_Unauthenticated_Returns401()
    {
        var identity = await CreateUserContextAsync(admin: true);
        await using var identityContext = identity.Context;
        await using var database = await SqliteTestDatabase.CreateAsync();
        await using var db = database.CreateContext();
        var request = new LlmConfigDto("https://api.deepseek.com", "new-key", "new-model", 60);

        var httpContext = CreateHttpContext(authenticated: false);
        var status = await InvokeStatusAsync(
            httpContext, "UpdateLlmConfigAsync", httpContext, db, identity.Context.UserManager, request);

        Assert.Equal(401, status);
    }

    private static async Task AssertStoredAsync(
        SqliteTestDatabase database, string baseUrl, string? apiKey, string model, int timeoutSeconds)
    {
        var stored = await database.ReadConfigAsync();
        Assert.NotNull(stored);
        Assert.Equal(baseUrl, stored!.BaseUrl);
        Assert.Equal(apiKey, stored.ApiKey);
        Assert.Equal(model, stored.Model);
        Assert.Equal(timeoutSeconds, stored.TimeoutSeconds);
    }
}
