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

    /// <summary>
    /// 连接字符串，供需要在其上构建独立 DI 容器（Identity 等）的测试使用。
    /// </summary>
    public string ConnectionString => _connectionString;

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

/// <summary>
/// Throws before the configured save number so a multi-save operation can be interrupted
/// after an earlier save already ran inside its transaction.
/// <see cref="CompletedSaves"/> records how many saves succeeded before the failure.
/// </summary>
public sealed class FailOnSaveInterceptor : SaveChangesInterceptor
{
    private int _attemptedSaves;
    private int _failOnSave = -1;

    public int CompletedSaves { get; private set; }

    public void FailOnSave(int saveNumber)
    {
        _attemptedSaves = 0;
        CompletedSaves = 0;
        _failOnSave = saveNumber;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDue();
        return new ValueTask<InterceptionResult<int>>(result);
    }

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        ThrowIfDue();
        return result;
    }

    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        CompletedSaves++;
        return result;
    }

    public override ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
    {
        CompletedSaves++;
        return new ValueTask<int>(result);
    }

    private void ThrowIfDue()
    {
        var save = ++_attemptedSaves;
        if (_failOnSave > 0 && save >= _failOnSave)
        {
            _failOnSave = -1;
            throw new InvalidOperationException("Injected SaveChanges failure for rollback verification.");
        }
    }
}

/// <summary>
/// Fails the transaction commit once, so the "commit outcome unknown" branch can be verified
/// without corrupting the file.
/// </summary>
public sealed class FailOnCommitInterceptor : DbTransactionInterceptor
{
    private int _armed;

    public void Arm() => _armed = 1;

    public override ValueTask<InterceptionResult> TransactionCommittingAsync(
        DbTransaction transaction,
        TransactionEventData eventData,
        InterceptionResult result,
        CancellationToken cancellationToken = default)
    {
        ThrowIfArmed();
        return new ValueTask<InterceptionResult>(result);
    }

    public override InterceptionResult TransactionCommitting(
        DbTransaction transaction,
        TransactionEventData eventData,
        InterceptionResult result)
    {
        ThrowIfArmed();
        return result;
    }

    private void ThrowIfArmed()
    {
        if (Interlocked.Exchange(ref _armed, 0) == 0)
            return;

        throw new InvalidOperationException("Simulated commit failure.");
    }
}

/// <summary>
/// Fails <c>BeginTransaction</c> once with a wrapped SQLite busy exception, so the
/// transaction-start failure path and the wrapped-exception classification can be verified
/// without waiting on a real file lock.
/// </summary>
public sealed class FailOnTransactionStartInterceptor : DbTransactionInterceptor
{
    private int _armed;

    public void Arm() => _armed = 1;

    public override ValueTask<InterceptionResult<DbTransaction>> TransactionStartingAsync(
        DbConnection connection,
        TransactionStartingEventData eventData,
        InterceptionResult<DbTransaction> result,
        CancellationToken cancellationToken = default)
    {
        ThrowIfArmed();
        return new ValueTask<InterceptionResult<DbTransaction>>(result);
    }

    public override InterceptionResult<DbTransaction> TransactionStarting(
        DbConnection connection,
        TransactionStartingEventData eventData,
        InterceptionResult<DbTransaction> result)
    {
        ThrowIfArmed();
        return result;
    }

    private void ThrowIfArmed()
    {
        if (Interlocked.Exchange(ref _armed, 0) == 0)
            return;

        throw new DbUpdateException(
            "Simulated write lock.",
            new SqliteException("database is locked", 5));
    }
}

/// <summary>
/// Marks the tracked user's ConcurrencyStamp original value as stale once, so the next UPDATE
/// affects 0 rows and Identity reports a concurrency failure — the provider-accurate equivalent
/// of a concurrent cleanup winning the race.
/// <see cref="Arm"/> targets the next modified-user save; a larger value skips earlier ones.
/// </summary>
public sealed class StaleConcurrencyStampInterceptor : SaveChangesInterceptor
{
    private int _armedOnSave;
    private int _modifiedUserSaves;

    public void Arm(int modifiedUserSave = 1)
    {
        _modifiedUserSaves = 0;
        _armedOnSave = modifiedUserSave;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Apply(eventData);
        return new ValueTask<InterceptionResult<int>>(result);
    }

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData, InterceptionResult<int> result)
    {
        Apply(eventData);
        return result;
    }

    private void Apply(DbContextEventData eventData)
    {
        if (_armedOnSave == 0 || eventData.Context is null)
            return;

        var entry = eventData.Context.ChangeTracker
            .Entries<AppUser>()
            .FirstOrDefault(e => e.State == EntityState.Modified);
        if (entry is null)
            return;

        if (++_modifiedUserSaves != _armedOnSave)
            return;

        _armedOnSave = 0;
        entry.Property(u => u.ConcurrencyStamp).OriginalValue = "stale-" + Guid.NewGuid();
    }
}
