using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using BoxWise.Server.Data;
using BoxWise.Server.Pages.Admin;

namespace BoxWise.Server.Tests.Pages;

public class LlmConfigModelTests
{
    private static LlmConfigModel CreateModel(AppDbContext db)
        => new(db)
        {
            PageContext = new PageContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };

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

    [Fact]
    public async Task OnPost_OriginChangeWithoutKey_ShowsErrorAndKeepsStoredConfig()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        await database.SeedConfigAsync("https://api.deepseek.com", "old-key");
        await using var db = database.CreateContext();
        var model = CreateModel(db);
        model.BaseUrl = "https://api.openai.com/v1";
        model.ApiKey = "";
        model.Model = "new-model";
        model.TimeoutSeconds = 60;

        var result = await model.OnPostAsync();

        Assert.IsType<PageResult>(result);
        Assert.NotNull(model.ErrorMessage);
        Assert.Null(model.StatusMessage);
        Assert.True(model.HasApiKey);
        await AssertStoredAsync(database, "https://api.deepseek.com", "old-key", "old-model", 30);
    }

    [Fact]
    public async Task OnPost_OriginChangeWithKey_SavesNewConfig()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        await database.SeedConfigAsync("https://api.deepseek.com", "old-key");
        await using var db = database.CreateContext();
        var model = CreateModel(db);
        model.BaseUrl = "https://api.openai.com/v1";
        model.ApiKey = "new-key";
        model.Model = "new-model";
        model.TimeoutSeconds = 60;

        var result = await model.OnPostAsync();

        Assert.IsType<PageResult>(result);
        Assert.Null(model.ErrorMessage);
        Assert.NotNull(model.StatusMessage);
        await AssertStoredAsync(database, "https://api.openai.com/v1", "new-key", "new-model", 60);
    }

    [Fact]
    public async Task OnPost_SameOriginPathChangeWithoutKey_RetainsStoredKey()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        await database.SeedConfigAsync("https://api.deepseek.com/v1", "old-key");
        await using var db = database.CreateContext();
        var model = CreateModel(db);
        model.BaseUrl = "https://api.deepseek.com/chat/completions";
        model.ApiKey = "";
        model.Model = "new-model";
        model.TimeoutSeconds = 60;

        var result = await model.OnPostAsync();

        Assert.IsType<PageResult>(result);
        Assert.Null(model.ErrorMessage);
        await AssertStoredAsync(database, "https://api.deepseek.com/chat/completions", "old-key", "new-model", 60);
    }

    [Fact]
    public async Task OnPost_UnchangedInvalidLegacyUrlWithoutKey_RetainsStoredKey()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        await database.SeedConfigAsync("not a valid url", "old-key");
        await using var db = database.CreateContext();
        var model = CreateModel(db);
        model.BaseUrl = "not a valid url";
        model.ApiKey = "";
        model.Model = "new-model";
        model.TimeoutSeconds = 60;

        var result = await model.OnPostAsync();

        Assert.IsType<PageResult>(result);
        Assert.Null(model.ErrorMessage);
        await AssertStoredAsync(database, "not a valid url", "old-key", "new-model", 60);
    }

    [Fact]
    public async Task OnPost_BlankBaseUrl_ShowsErrorWithoutChangingConfig()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        await database.SeedConfigAsync("https://api.deepseek.com", "old-key");
        await using var db = database.CreateContext();
        var model = CreateModel(db);
        model.BaseUrl = "  ";
        model.ApiKey = "";
        model.Model = "new-model";
        model.TimeoutSeconds = 60;

        var result = await model.OnPostAsync();

        Assert.IsType<PageResult>(result);
        Assert.NotNull(model.ErrorMessage);
        await AssertStoredAsync(database, "https://api.deepseek.com", "old-key", "old-model", 30);
    }

    [Fact]
    public async Task OnPost_ConcurrentOriginChangeWithoutKey_ShowsErrorAndKeepsConcurrentPair()
    {
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
        var model = CreateModel(db);
        model.BaseUrl = "https://old.example.com/v2";
        model.ApiKey = "";
        model.Model = "request-model";
        model.TimeoutSeconds = 30;

        var result = await model.OnPostAsync();

        Assert.IsType<PageResult>(result);
        Assert.NotNull(model.ErrorMessage);
        Assert.DoesNotContain("new-key", model.ErrorMessage);
        await AssertStoredAsync(database, "https://new.example.com/v1", "new-key", "new-model", 60);
    }

    [Fact]
    public async Task OnPost_PersistentConflict_ShowsGenericErrorAndKeepsConcurrentPair()
    {
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
        var model = CreateModel(db);
        model.BaseUrl = "https://old.example.com/v2";
        model.ApiKey = "";
        model.Model = "request-model";
        model.TimeoutSeconds = 30;

        var result = await model.OnPostAsync();

        Assert.IsType<PageResult>(result);
        Assert.Equal("配置已被其他管理员修改，请刷新后重试", model.ErrorMessage);
        Assert.DoesNotContain("concurrent-key", model.ErrorMessage);

        var stored = await database.ReadConfigAsync();
        Assert.NotNull(stored);
        Assert.Equal("https://old.example.com/v1", stored!.BaseUrl);
        Assert.StartsWith("concurrent-key-", stored.ApiKey);
        Assert.StartsWith("concurrent-model-", stored.Model);
    }

    [Fact]
    public void PageModel_KeepsAdminOnlyAuthorization()
    {
        var attribute = Assert.Single(
            typeof(LlmConfigModel).GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
                .Cast<AuthorizeAttribute>());

        Assert.Equal("AdminOnly", attribute.Policy);
    }
}
