using System.Collections.Concurrent;
using SkiaSharp;

namespace BoxWise.Server.Services;

public class ThumbnailService
{
    internal static readonly ConcurrentDictionary<int, SemaphoreSlim> Locks = new();

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
