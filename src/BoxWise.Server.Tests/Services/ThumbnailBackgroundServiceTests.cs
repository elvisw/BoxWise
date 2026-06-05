using BoxWise.Server.Data;
using BoxWise.Server.Models;
using BoxWise.Server.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using SkiaSharp;

namespace BoxWise.Server.Tests.Services;

public class ThumbnailBackgroundServiceTests : IDisposable
{
    private readonly List<string> _tempDirs = [];

    public void Dispose()
    {
        foreach (var dir in _tempDirs)
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, true);
        }
    }

    private static void CreateTestImage(string path, int width = 600, int height = 800)
    {
        var dir = Path.GetDirectoryName(path);
        if (dir is { Length: > 0 } && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        using var bitmap = new SKBitmap(width, height);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(new SKColor(255, 0, 0));
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, 85);
        using var file = File.Create(path);
        data.SaveTo(file);
    }

    private (ThumbnailBackgroundService Service, string TempDir, string DbName) CreateService()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"boxwise-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);
        _tempDirs.Add(tempDir);

        var dbName = Guid.NewGuid().ToString();

        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(dbName));
        services.AddSingleton<ImageStorageService>(_ =>
        {
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["DataDirectory"] = tempDir
                })!.Build();
            return new ImageStorageService(config);
        });
        services.AddLogging();

        var serviceProvider = services.BuildServiceProvider();
        var scopeFactory = serviceProvider.GetRequiredService<IServiceScopeFactory>();
        var logger = Mock.Of<ILogger<ThumbnailBackgroundService>>();
        var storage = serviceProvider.GetRequiredService<ImageStorageService>();

        var bgService = new ThumbnailBackgroundService(scopeFactory, logger, storage);
        return (bgService, tempDir, dbName);
    }

    /// <summary>
    /// Seed the test user (idempotent) and an item.
    /// </summary>
    private async Task SeedItemAsync(string dbName, int itemId, bool createOriginalImage, string tempDir)
    {
        using var db = TestDbContextFactory.Create(dbName);
        if (!db.Users.Any(u => u.Id == "user-1"))
        {
            db.Users.Add(new AppUser { Id = "user-1", UserName = "test" });
            await db.SaveChangesAsync();
        }

        db.Items.Add(new Item
        {
            Id = itemId,
            Name = $"Item {itemId}",
            CreatedByUserId = "user-1",
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        if (createOriginalImage)
        {
            var originalPath = Path.Combine(tempDir, itemId.ToString(), "original.jpg");
            CreateTestImage(originalPath);
        }
    }

    // ==================== Startup Recovery Scan ====================

    [Fact]
    public async Task ScanForMissingThumbnails_ItemsWithoutThumbnailsWithOriginal_CreatesThumbnails()
    {
        // Arrange
        var (service, tempDir, dbName) = CreateService();
        await SeedItemAsync(dbName, itemId: 1, createOriginalImage: true, tempDir);
        await SeedItemAsync(dbName, itemId: 2, createOriginalImage: true, tempDir);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        // Act
        await service.ScanForMissingThumbnailsAsync(cts.Token);

        // Assert: thumbnails created
        Assert.True(File.Exists(Path.Combine(tempDir, "1", "thumb.jpg")));
        Assert.True(File.Exists(Path.Combine(tempDir, "1", "medium.jpg")));
        Assert.True(File.Exists(Path.Combine(tempDir, "2", "thumb.jpg")));
        Assert.True(File.Exists(Path.Combine(tempDir, "2", "medium.jpg")));

        // Assert: DB paths updated
        using var verifyDb = TestDbContextFactory.Create(dbName);
        var item1 = await verifyDb.Items.FindAsync(1);
        Assert.NotNull(item1);
        Assert.Equal(Path.Combine("1", "thumb.jpg"), item1.ThumbPath);
        Assert.Equal(Path.Combine("1", "medium.jpg"), item1.MediumPath);

        // Assert: correct dimensions
        using var thumb = SKBitmap.Decode(Path.Combine(tempDir, "1", "thumb.jpg"));
        Assert.NotNull(thumb);
        Assert.Equal(300, thumb.Width);
        Assert.Equal(400, thumb.Height);

        using var medium = SKBitmap.Decode(Path.Combine(tempDir, "1", "medium.jpg"));
        Assert.NotNull(medium);
        Assert.Equal(1200, medium.Width);
        Assert.Equal(1600, medium.Height);
    }

    [Fact]
    public async Task ScanForMissingThumbnails_ItemWithoutOriginalFile_SilentlySkips()
    {
        // Arrange: item with ThumbPath=null but no original.jpg
        var (service, tempDir, dbName) = CreateService();
        await SeedItemAsync(dbName, itemId: 1, createOriginalImage: false, tempDir);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        // Act - should not throw
        await service.ScanForMissingThumbnailsAsync(cts.Token);

        // Assert: no thumbnails created
        Assert.False(File.Exists(Path.Combine(tempDir, "1", "thumb.jpg")));
        Assert.False(File.Exists(Path.Combine(tempDir, "1", "medium.jpg")));

        // Assert: DB still has null paths
        using var verifyDb = TestDbContextFactory.Create(dbName);
        var item = await verifyDb.Items.FindAsync(1);
        Assert.NotNull(item);
        Assert.Null(item.ThumbPath);
    }

    [Fact]
    public async Task ScanForMissingThumbnails_CorruptOriginal_LogsError_SkipsItem()
    {
        // Arrange: item with corrupt original.jpg (garbage bytes)
        var (service, tempDir, dbName) = CreateService();
        await SeedItemAsync(dbName, itemId: 1, createOriginalImage: true, tempDir);

        // Overwrite with garbage
        var originalPath = Path.Combine(tempDir, "1", "original.jpg");
        await File.WriteAllBytesAsync(originalPath, [0xFF, 0x00, 0xFF, 0x00, 0xFF]);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        // Act - should not throw
        await service.ScanForMissingThumbnailsAsync(cts.Token);

        // Assert: no thumbnails created, DB still null
        Assert.False(File.Exists(Path.Combine(tempDir, "1", "thumb.jpg")));
        using var verifyDb = TestDbContextFactory.Create(dbName);
        var item = await verifyDb.Items.FindAsync(1);
        Assert.NotNull(item);
        Assert.Null(item.ThumbPath);
    }

    // ==================== TryEnqueue ====================

    [Fact]
    public void TryEnqueue_UnderCapacity_ReturnsTrue()
    {
        var (service, _, _) = CreateService();

        var result = service.TryEnqueue(42);

        Assert.True(result);
    }

    [Fact]
    public void TryEnqueue_AlwaysReturnsTrue_EvenWhenChannelFull()
    {
        var (service, _, _) = CreateService();

        // Fill the channel to capacity (100)
        for (int i = 0; i < 100; i++)
            Assert.True(service.TryEnqueue(1000 + i));

        // DropWrite mode: TryWrite always returns true even when full
        // (item is silently dropped; CAP-2 recovery scan will pick it up)
        var result = service.TryEnqueue(9999);
        Assert.True(result);
    }

    // ==================== Cancellation ====================

    [Fact]
    public async Task ExecuteAsync_CancelsGracefully()
    {
        var (service, tempDir, dbName) = CreateService();

        await SeedItemAsync(dbName, itemId: 1, createOriginalImage: true, tempDir);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));

        // Start the service, then cancel and verify no exception
        var executeTask = service.StartAsync(cts.Token);
        await Task.Delay(100);
        await cts.CancelAsync();
        await executeTask;
        await service.StopAsync(CancellationToken.None);
    }

    // ==================== Per-item Lock Safety ====================

    [Fact]
    public async Task ScanForMissingThumbnails_PerItemLock_PreventsConcurrentProcessing()
    {
        // Arrange: create item with original
        var (service, tempDir, dbName) = CreateService();
        await SeedItemAsync(dbName, itemId: 1, createOriginalImage: true, tempDir);

        // Pre-hold the per-item lock for item 1
        var locks = ThumbnailService.Locks;
        var heldSemaphore = locks.GetOrAdd(1, _ => new SemaphoreSlim(1, 1));
        await heldSemaphore.WaitAsync();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        // Start scan on a background task (it will try to process item 1 and block on the lock)
        var scanTask = service.ScanForMissingThumbnailsAsync(cts.Token);

        // Poll until the scan acquires the per-item lock (max 2 seconds)
        var lockAcquired = false;
        for (int attempt = 0; attempt < 20; attempt++)
        {
            await Task.Delay(100);
            if (heldSemaphore.CurrentCount == 0)
            {
                lockAcquired = true;
                break;
            }
        }
        Assert.True(lockAcquired, "Scan should have acquired the per-item lock and blocked");

        // Item should NOT have thumbnails yet (lock held — scan is blocked)
        Assert.False(File.Exists(Path.Combine(tempDir, "1", "thumb.jpg")),
            "Item 1 should not have thumbnails while per-item lock is held");

        // Release the held lock so the scan can proceed
        heldSemaphore.Release();

        // Wait for scan to complete
        await scanTask;

        // Now item 1 should have thumbnails
        Assert.True(File.Exists(Path.Combine(tempDir, "1", "thumb.jpg")),
            "Item 1 should have thumbnails after per-item lock released");
    }
}
