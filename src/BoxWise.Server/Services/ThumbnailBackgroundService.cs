using System.Threading.Channels;
using BoxWise.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace BoxWise.Server.Services;

public class ThumbnailBackgroundService : BackgroundService
{
    private readonly Channel<ThumbnailRequest> _channel = Channel.CreateBounded<ThumbnailRequest>(
        new BoundedChannelOptions(100)
        {
            FullMode = BoundedChannelFullMode.DropWrite
        });

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ThumbnailBackgroundService> _logger;
    private readonly ImageStorageService _storage;

    private int _scanInProgress;

    public ThumbnailBackgroundService(
        IServiceScopeFactory scopeFactory,
        ILogger<ThumbnailBackgroundService> logger,
        ImageStorageService storage)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _storage = storage;
    }

    /// <summary>
    /// Try to enqueue a thumbnail generation request.
    /// Returns true if enqueued, false if channel was full (request dropped).
    /// </summary>
    public bool TryEnqueue(int itemId)
    {
        if (_channel.Reader.Count >= 100)
        {
            _logger.LogWarning(
                "Thumbnail queue full ({Capacity}), item {ItemId} will be recovered on next recovery scan",
                100, itemId);
            return false;
        }

        _channel.Writer.TryWrite(new ThumbnailRequest(itemId));
        _logger.LogDebug("Enqueued thumbnail generation for item {ItemId}", itemId);
        return true;
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _channel.Writer.TryComplete();
        await base.StopAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("ThumbnailBackgroundService started");

        // 1. Startup scan: recover items with missing thumbnails
        Interlocked.Exchange(ref _scanInProgress, 1);
        try
        {
            await ScanForMissingThumbnailsAsync(stoppingToken);
        }
        finally
        {
            Interlocked.Exchange(ref _scanInProgress, 0);
        }

        // 2. Run channel consumer and periodic timer concurrently
        var consumerTask = ConsumeChannelAsync(stoppingToken);
        var periodicTask = PeriodicScanLoopAsync(stoppingToken);

        await Task.WhenAll(consumerTask, periodicTask);

        _logger.LogInformation("ThumbnailBackgroundService stopped");
    }

    private async Task ConsumeChannelAsync(CancellationToken ct)
    {
        try
        {
            await foreach (var request in _channel.Reader.ReadAllAsync(ct))
            {
                await ProcessItemAsync(request.ItemId, ct);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on graceful shutdown
        }
    }

    private async Task PeriodicScanLoopAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(10));

        try
        {
            while (await timer.WaitForNextTickAsync(ct))
            {
                if (Interlocked.CompareExchange(ref _scanInProgress, 1, 0) != 0)
                {
                    _logger.LogWarning("Periodic scan skipped: previous scan still in progress");
                    continue;
                }

                try
                {
                    await ScanForMissingThumbnailsAsync(ct);
                }
                finally
                {
                    Interlocked.Exchange(ref _scanInProgress, 0);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on graceful shutdown
        }
    }

    internal async Task ScanForMissingThumbnailsAsync(CancellationToken ct)
    {
        _logger.LogInformation("Starting thumbnail recovery scan");

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var itemIds = await db.Items
                .Where(i => i.ThumbPath == null || i.ThumbPath == "")
                .Select(i => i.Id)
                .ToListAsync(ct);

            var recoveredCount = 0;
            foreach (var itemId in itemIds)
            {
                if (ct.IsCancellationRequested)
                    break;

                // Silently skip items that never had an image uploaded
                if (!File.Exists(_storage.GetOriginalPath(itemId)))
                    continue;

                try
                {
                    await ProcessItemAsync(itemId, ct);
                    recoveredCount++;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex, "Failed to recover thumbnail for item {ItemId}", itemId);
                }
            }

            _logger.LogInformation(
                "Thumbnail recovery scan completed: {RecoveredCount}/{TotalCount} items processed",
                recoveredCount, itemIds.Count);
        }
        catch (OperationCanceledException)
        {
            // Expected on shutdown
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Thumbnail recovery scan failed");
        }
    }

    private async Task ProcessItemAsync(int itemId, CancellationToken ct)
    {
        var semaphore = ThumbnailService.Locks.GetOrAdd(itemId, _ => new SemaphoreSlim(1, 1));
        await semaphore.WaitAsync(ct);

        try
        {
            var originalPath = _storage.GetOriginalPath(itemId);
            if (!File.Exists(originalPath))
            {
                return;
            }

            ThumbnailService.GenerateThumb(originalPath, _storage.GetThumbPath(itemId), 300);
            ThumbnailService.GenerateThumb(originalPath, _storage.GetMediumPath(itemId), 1200);

            await UpdateItemPathsAsync(itemId, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Error generating thumbnail for item {ItemId}", itemId);
            // DB paths remain NULL — periodic scan will retry
        }
        finally
        {
            semaphore.Release();
        }
    }

    private async Task UpdateItemPathsAsync(int itemId, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var item = await db.Items.FindAsync(new object[] { itemId }, ct);
        if (item is null) return;

        var prefix = itemId.ToString();
        item.PhotoPath = Path.Combine(prefix, "original.jpg");
        item.ThumbPath = Path.Combine(prefix, "thumb.jpg");
        item.MediumPath = Path.Combine(prefix, "medium.jpg");

        await db.SaveChangesAsync(ct);
    }
}
