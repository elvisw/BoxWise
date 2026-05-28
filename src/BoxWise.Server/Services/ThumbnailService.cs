using System.Collections.Concurrent;
using SkiaSharp;

namespace BoxWise.Server.Services;

public class ThumbnailService
{
    private readonly ImageStorageService _storage;
    private readonly ILogger<ThumbnailService> _logger;
    private static readonly ConcurrentDictionary<int, SemaphoreSlim> _locks = new();

    public ThumbnailService(ImageStorageService storage, ILogger<ThumbnailService> logger)
    {
        _storage = storage;
        _logger = logger;
    }

    public void GenerateInBackground(int itemId, IServiceScopeFactory scopeFactory)
        => _ = Task.Run(async () =>
        {
            var semaphore = _locks.GetOrAdd(itemId, _ => new SemaphoreSlim(1, 1));
            await semaphore.WaitAsync();
            try
            {
                await GenerateAsync(itemId, scopeFactory);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Background thumbnail generation failed for item {ItemId}", itemId);
            }
            finally
            {
                semaphore.Release();
            }
        });

    internal async Task GenerateAsync(int itemId, IServiceScopeFactory scopeFactory)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<Data.AppDbContext>();
            var item = await db.Items.FindAsync(itemId);
            if (item is null) return;

            var original = _storage.GetOriginalPath(itemId);

            GenerateThumb(original, _storage.GetThumbPath(itemId), 300);
            GenerateThumb(original, _storage.GetMediumPath(itemId), 1200);

            item.PhotoPath = Path.Combine(itemId.ToString(), "original.jpg");
            item.ThumbPath = Path.Combine(itemId.ToString(), "thumb.jpg");
            item.MediumPath = Path.Combine(itemId.ToString(), "medium.jpg");
            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to generate thumbnails for item {ItemId}", itemId);
        }
    }

    internal static void GenerateThumb(string sourcePath, string destPath, int width)
    {
        using var original = SKBitmap.Decode(sourcePath)
            ?? throw new InvalidOperationException($"无法解码图片: {sourcePath}");

        if (original.Width <= 0)
            throw new InvalidOperationException($"图片宽度无效 ({original.Width}): {sourcePath}");
        if (original.Height <= 0)
            throw new InvalidOperationException($"图片高度无效 ({original.Height}): {sourcePath}");

        var ratio = (float)width / original.Width;
        var height = (int)(original.Height * ratio);

        using var resized = original.Resize(new SKSizeI(width, height), new SKSamplingOptions(SKFilterMode.Linear))
            ?? throw new InvalidOperationException($"无法缩放图片: {sourcePath}");
        using var image = SKImage.FromBitmap(resized);
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, 85);

        var dir = Path.GetDirectoryName(destPath);
        if (dir is not null && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);
        using var file = File.Create(destPath);
        data.SaveTo(file);
    }
}
