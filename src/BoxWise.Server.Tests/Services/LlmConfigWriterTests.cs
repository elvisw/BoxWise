using Microsoft.EntityFrameworkCore;
using BoxWise.Server.Services;

namespace BoxWise.Server.Tests.Services;

public class LlmConfigWriterTests
{
    private static async Task ConcurrentlyWriteAsync(
        SqliteTestDatabase database, string baseUrl, string apiKey, string model, int timeoutSeconds)
    {
        await using var db = database.CreateContext();
        var row = await db.LlmConfigs.SingleAsync(c => c.Id == 1);
        row.BaseUrl = baseUrl;
        row.ApiKey = apiKey;
        row.Model = model;
        row.TimeoutSeconds = timeoutSeconds;
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task SaveAsync_StaleSnapshotAfterOriginChange_RejectsBlankKeyAndKeepsConcurrentPair()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        await database.SeedConfigAsync("https://old.example.com/v1", "old-key");

        var interceptor = new ConcurrentUpdateInterceptor(() =>
            ConcurrentlyWriteAsync(database, "https://new.example.com/v1", "new-key", "new-model", 60));
        await using var db = database.CreateContext(interceptor);

        var result = await LlmConfigWriter.SaveAsync(
            db,
            new LlmConfigUpdate("https://old.example.com/v2", "", "request-model", 30));

        Assert.Equal(LlmConfigWriteStatus.KeyRequired, result.Status);
        var stored = await database.ReadConfigAsync();
        Assert.NotNull(stored);
        Assert.Equal("https://new.example.com/v1", stored!.BaseUrl);
        Assert.Equal("new-key", stored.ApiKey);
        Assert.Equal("new-model", stored.Model);
        Assert.Equal(60, stored.TimeoutSeconds);
    }

    [Fact]
    public async Task SaveAsync_StaleSnapshotWithSuppliedKey_PersistsRequestPairCoherently()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        await database.SeedConfigAsync("https://old.example.com/v1", "old-key");

        var interceptor = new ConcurrentUpdateInterceptor(() =>
            ConcurrentlyWriteAsync(database, "https://new.example.com/v1", "new-key", "new-model", 60));
        await using var db = database.CreateContext(interceptor);

        var result = await LlmConfigWriter.SaveAsync(
            db,
            new LlmConfigUpdate("https://request.example.com/v1", "request-key", "request-model", 30));

        Assert.Equal(LlmConfigWriteStatus.Saved, result.Status);
        var stored = await database.ReadConfigAsync();
        Assert.NotNull(stored);
        Assert.Equal("https://request.example.com/v1", stored!.BaseUrl);
        Assert.Equal("request-key", stored.ApiKey);
        Assert.Equal("request-model", stored.Model);
        Assert.Equal(30, stored.TimeoutSeconds);
    }

    [Fact]
    public async Task SaveAsync_ExistingRowWithNullKey_AllowsBlankKeySameOrigin()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        await database.SeedConfigAsync("https://old.example.com/v1", null);

        await using var db = database.CreateContext();
        var result = await LlmConfigWriter.SaveAsync(
            db,
            new LlmConfigUpdate("https://old.example.com/v2", "", "new-model", 60));

        Assert.Equal(LlmConfigWriteStatus.Saved, result.Status);
        var stored = await database.ReadConfigAsync();
        Assert.NotNull(stored);
        Assert.Equal("https://old.example.com/v2", stored!.BaseUrl);
        Assert.Null(stored.ApiKey);
    }

    [Fact]
    public async Task SaveAsync_PersistentConflicts_ReturnsGenericConflictWithoutSensitiveValues()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        await database.SeedConfigAsync("https://old.example.com/v1", "old-key");

        var writes = 0;
        var interceptor = new ConcurrentUpdateInterceptor(async () =>
        {
            writes++;
            await ConcurrentlyWriteAsync(
                database,
                "https://old.example.com/v1",
                $"concurrent-key-{writes}",
                $"concurrent-model-{writes}",
                30);
        }, maxFires: int.MaxValue);
        await using var db = database.CreateContext(interceptor);

        var result = await LlmConfigWriter.SaveAsync(
            db,
            new LlmConfigUpdate("https://old.example.com/v2", "", "request-model", 60));

        Assert.Equal(LlmConfigWriteStatus.Conflict, result.Status);
        Assert.True(writes > 1);
        Assert.DoesNotContain("concurrent-key", LlmConfigWriter.ConflictMessage);
        Assert.DoesNotContain("request-model", LlmConfigWriter.ConflictMessage);

        var stored = await database.ReadConfigAsync();
        Assert.NotNull(stored);
        Assert.Equal("https://old.example.com/v1", stored!.BaseUrl);
        Assert.StartsWith("concurrent-key-", stored.ApiKey);
        Assert.StartsWith("concurrent-model-", stored.Model);
    }

    [Fact]
    public async Task SaveAsync_ConcurrentInsertAfterRead_RechecksOriginAgainstInsertedRow()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();

        var interceptor = new ConcurrentInsertInterceptor(() =>
            database.SeedConfigAsync("https://other.example.com/v1", "other-key", "other-model", 60));
        await using var db = database.CreateContext(interceptor);

        var result = await LlmConfigWriter.SaveAsync(
            db,
            new LlmConfigUpdate("https://request.example.com/v1", "", "request-model", 30));

        Assert.Equal(LlmConfigWriteStatus.KeyRequired, result.Status);
        var stored = await database.ReadConfigAsync();
        Assert.NotNull(stored);
        Assert.Equal("https://other.example.com/v1", stored!.BaseUrl);
        Assert.Equal("other-key", stored.ApiKey);
        Assert.Equal("other-model", stored.Model);
        Assert.Equal(60, stored.TimeoutSeconds);
    }

    [Fact]
    public async Task SaveAsync_NoExistingRow_InsertsCoherentPair()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        await using var db = database.CreateContext();

        var result = await LlmConfigWriter.SaveAsync(
            db,
            new LlmConfigUpdate("https://new.example.com/v1", "new-key", "new-model", 60));

        Assert.Equal(LlmConfigWriteStatus.Saved, result.Status);
        var stored = await database.ReadConfigAsync();
        Assert.NotNull(stored);
        Assert.Equal("https://new.example.com/v1", stored!.BaseUrl);
        Assert.Equal("new-key", stored.ApiKey);
        Assert.Equal("new-model", stored.Model);
        Assert.Equal(60, stored.TimeoutSeconds);
    }
}
