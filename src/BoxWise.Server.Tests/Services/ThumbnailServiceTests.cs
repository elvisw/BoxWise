using BoxWise.Server.Services;
using SkiaSharp;

namespace BoxWise.Server.Tests.Services;

public class ThumbnailServiceTests
{
    /// <summary>
    /// 创建一个 600x800 的测试图片文件
    /// </summary>
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

    // ============ GenerateThumb ============

    [Fact]
    public void GenerateThumb_ValidImage_CreatesResizedFile()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"boxwise-test-{Guid.NewGuid()}");
        try
        {
            Directory.CreateDirectory(tempDir);
            var sourcePath = Path.Combine(tempDir, "source.jpg");
            var destPath = Path.Combine(tempDir, "thumb.jpg");

            // Arrange: 创建 600x800 测试图片
            CreateTestImage(sourcePath);

            // Act: 调用 GenerateThumb，目标宽度 300
            ThumbnailService.GenerateThumb(sourcePath, destPath, 300);

            // Assert: 输出文件存在
            Assert.True(File.Exists(destPath));

            // Assert: 输出图片尺寸正确（300 x 400）
            using var output = SKBitmap.Decode(destPath);
            Assert.NotNull(output);
            Assert.Equal(300, output.Width);
            Assert.Equal(400, output.Height);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void GenerateThumb_ValidImage_CreatesMediumSize()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"boxwise-test-{Guid.NewGuid()}");
        try
        {
            Directory.CreateDirectory(tempDir);
            var sourcePath = Path.Combine(tempDir, "source.jpg");
            var destPath = Path.Combine(tempDir, "medium.jpg");
            CreateTestImage(sourcePath);

            // Act: 目标宽度 1200
            ThumbnailService.GenerateThumb(sourcePath, destPath, 1200);

            // Assert: 1200 x 1600
            Assert.True(File.Exists(destPath));
            using var output = SKBitmap.Decode(destPath);
            Assert.NotNull(output);
            Assert.Equal(1200, output.Width);
            Assert.Equal(1600, output.Height);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void GenerateThumb_SourceNotExists_ThrowsInvalidOperationException()
    {
        // Arrange: 源文件路径不存在，目标文件路径
        var sourcePath = Path.Combine(Path.GetTempPath(), $"nonexistent-{Guid.NewGuid()}.jpg");
        var destPath = Path.Combine(Path.GetTempPath(), $"output-{Guid.NewGuid()}.jpg");

        try
        {
            // Act & Assert: GenerateThumb 抛出 InvalidOperationException
            var ex = Assert.Throws<InvalidOperationException>(() =>
                ThumbnailService.GenerateThumb(sourcePath, destPath, 300));
            Assert.Contains("无法解码", ex.Message);
        }
        finally
        {
            if (File.Exists(destPath)) File.Delete(destPath);
        }
    }

    [Fact]
    public void GenerateThumb_CorruptImage_ThrowsInvalidOperationException()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"boxwise-test-{Guid.NewGuid()}");
        try
        {
            Directory.CreateDirectory(tempDir);
            var sourcePath = Path.Combine(tempDir, "corrupt.jpg");
            var destPath = Path.Combine(tempDir, "thumb.jpg");

            // Arrange: 创建一个无效的垃圾文件
            File.WriteAllBytes(sourcePath, [0x00, 0xFF, 0x00, 0xFF, 0x00]);

            // Act & Assert
            var ex = Assert.Throws<InvalidOperationException>(() =>
                ThumbnailService.GenerateThumb(sourcePath, destPath, 300));
            Assert.Contains("无法解码", ex.Message);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void GenerateThumb_SmallImage_ResizesSuccessfully()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"boxwise-test-{Guid.NewGuid()}");
        try
        {
            Directory.CreateDirectory(tempDir);
            var sourcePath = Path.Combine(tempDir, "source.jpg");
            var destPath = Path.Combine(tempDir, "thumb.jpg");
            CreateTestImage(sourcePath, width: 1, height: 1);

            // Act: 正常 Resize 1x1 图片验证 null check 不影响正常路径
            ThumbnailService.GenerateThumb(sourcePath, destPath, 300);

            Assert.True(File.Exists(destPath));
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void GenerateThumb_DestDirectoryNotExists_CreatesDirectory()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"boxwise-test-{Guid.NewGuid()}");
        try
        {
            Directory.CreateDirectory(tempDir);
            var sourcePath = Path.Combine(tempDir, "source.jpg");
            var destPath = Path.Combine(tempDir, "nonexistent-subdir", "thumb.jpg");
            CreateTestImage(sourcePath);

            // Act: 目录不存在时应自动创建
            ThumbnailService.GenerateThumb(sourcePath, destPath, 300);

            Assert.True(File.Exists(destPath));
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }
}
