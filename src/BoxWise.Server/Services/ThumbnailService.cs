using SkiaSharp;

namespace BoxWise.Server.Services;

public class ThumbnailService
{
    private readonly ImageStorageService _storage;
    private readonly ILogger<ThumbnailService> _logger;

    public ThumbnailService(ImageStorageService storage, ILogger<ThumbnailService> logger)
    {
        _storage = storage;
        _logger = logger;
    }

    public void GenerateInBackground(int itemId, IServiceScopeFactory scopeFactory)
    {
        _ = Task.Run(async () =>
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
        });
    }

    private static void GenerateThumb(string sourcePath, string destPath, int width)
    {
        using var original = SKBitmap.Decode(sourcePath)
            ?? throw new InvalidOperationException($"无法解码图片: {sourcePath}");

        if (original.Width <= 0 || original.Height <= 0)
            throw new InvalidOperationException($"图片尺寸无效: {sourcePath}");

        var ratio = (float)width / original.Width;
        var height = (int)(original.Height * ratio);

        using var resized = original.Resize(new SKSizeI(width, height), new SKSamplingOptions(SKFilterMode.Linear));
        using var image = SKImage.FromBitmap(resized);
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, 85);

        using var file = File.Create(destPath);
        data.SaveTo(file);
    }
}
