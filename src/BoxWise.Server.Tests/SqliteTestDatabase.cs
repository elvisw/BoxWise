using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using BoxWise.Server.Data;
using BoxWise.Server.Models;

namespace BoxWise.Server.Tests;

/// <summary>
/// A real, isolated SQLite database for tests that need provider-accurate conditional writes.
/// </summary>
public sealed class SqliteTestDatabase : IAsyncDisposable
{
    private readonly string _path;
    private readonly string _connectionString;

    private SqliteTestDatabase(string path, string connectionString)
    {
        _path = path;
        _connectionString = connectionString;
    }

    public static async Task<SqliteTestDatabase> CreateAsync()
    {
        var path = Path.Combine(Path.GetTempPath(), $"boxwise-test-{Guid.NewGuid():N}.db");
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Pooling = false
        }.ToString();

        var database = new SqliteTestDatabase(path, connectionString);
        await using var db = database.CreateContext();
        await db.Database.EnsureCreatedAsync();
        return database;
    }

    public AppDbContext CreateContext(params IInterceptor[] interceptors)
    {
        var builder = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connectionString);

        if (interceptors.Length > 0)
            builder.AddInterceptors(interceptors);

        return new AppDbContext(builder.Options);
    }

    public async Task SeedConfigAsync(
        string baseUrl, string? apiKey, string model = "old-model", int timeoutSeconds = 30)
    {
        await using var db = CreateContext();
        db.LlmConfigs.Add(new LlmConfig
        {
            Id = 1,
            BaseUrl = baseUrl,
            ApiKey = apiKey,
            Model = model,
            TimeoutSeconds = timeoutSeconds
        });
        await db.SaveChangesAsync();
    }

    public async Task<LlmConfig?> ReadConfigAsync()
    {
        await using var db = CreateContext();
        return await db.LlmConfigs
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == 1);
    }

    public ValueTask DisposeAsync()
    {
        DeleteIfExists(_path);
        DeleteIfExists(_path + "-wal");
        DeleteIfExists(_path + "-shm");
        DeleteIfExists(_path + "-journal");
        return ValueTask.CompletedTask;
    }

    private static void DeleteIfExists(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}

/// <summary>
/// Runs an independent write immediately before the first UPDATE issued by the writer,
/// making the stale-snapshot race deterministic without production hooks.
/// </summary>
public sealed class ConcurrentUpdateInterceptor : DbCommandInterceptor
{
    private readonly Func<Task> _concurrentUpdate;
    private readonly int _maxFires;
    private int _fired;

    public ConcurrentUpdateInterceptor(Func<Task> concurrentUpdate, int maxFires = 1)
    {
        _concurrentUpdate = concurrentUpdate;
        _maxFires = maxFires;
    }

    public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (!IsConfigUpdate(command))
            return result;

        if (Interlocked.Increment(ref _fired) <= _maxFires)
            await _concurrentUpdate();

        return result;
    }

    private static bool IsConfigUpdate(DbCommand command)
        => command.CommandText.TrimStart().StartsWith("UPDATE", StringComparison.OrdinalIgnoreCase)
           && command.CommandText.Contains("LlmConfigs", StringComparison.Ordinal);
}

/// <summary>
/// Inserts the singleton through an independent context immediately before this context's
/// first save, making the duplicate-insert race deterministic.
/// </summary>
public sealed class ConcurrentInsertInterceptor : SaveChangesInterceptor
{
    private readonly Func<Task> _concurrentInsert;
    private int _fired;

    public ConcurrentInsertInterceptor(Func<Task> concurrentInsert)
    {
        _concurrentInsert = concurrentInsert;
    }

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref _fired, 1) == 0)
            await _concurrentInsert();

        return result;
    }
}
