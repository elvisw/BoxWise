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

    // ============ GenerateAsync ============

    [Fact]
    public async Task GenerateAsync_ValidItem_UpdatesDbPaths()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"boxwise-test-{Guid.NewGuid()}");
        try
        {
            var dbName = Guid.NewGuid().ToString();

            // Arrange: 种子数据（AppUser + Item）
            using (var db = TestDbContextFactory.Create(dbName))
            {
                db.Users.Add(new AppUser { Id = "user-1", UserName = "test" });
                db.Items.Add(new Item
                {
                    Name = "测试物品",
                    CreatedByUserId = "user-1",
                    CreatedAt = DateTime.UtcNow
                });
                db.SaveChanges();
            }

            // Arrange: 配置真实 DI 容器的 IServiceScopeFactory（共享 InMemory 数据库名）
            var services = new ServiceCollection();
            services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(dbName));
            services.AddLogging();
            using var serviceProvider = services.BuildServiceProvider();
            var scopeFactory = serviceProvider.GetRequiredService<IServiceScopeFactory>();

            // Arrange: 创建 ImageStorageService + ThumbnailService
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["DataDirectory"] = tempDir
                }).Build();
            var storage = new ImageStorageService(config);
            var thumbnailService = new ThumbnailService(storage, Mock.Of<ILogger<ThumbnailService>>());

            // Arrange: 创建测试用原始图片（600x800）
            var originalPath = storage.GetOriginalPath(1);
            CreateTestImage(originalPath);

            // Act
            await thumbnailService.GenerateAsync(1, scopeFactory);

            // Assert: 数据库路径字段已更新（使用 Path.Combine 适配平台分隔符）
            using (var verifyDb = TestDbContextFactory.Create(dbName))
            {
                var item = verifyDb.Items.Find(1);
                Assert.NotNull(item);
                Assert.Equal(Path.Combine("1", "original.jpg"), item.PhotoPath);
                Assert.Equal(Path.Combine("1", "thumb.jpg"), item.ThumbPath);
                Assert.Equal(Path.Combine("1", "medium.jpg"), item.MediumPath);
            }

            // Assert: 缩略图文件存在且尺寸正确
            var thumbPath = storage.GetThumbPath(1);
            var mediumPath = storage.GetMediumPath(1);
            Assert.True(File.Exists(thumbPath), "缩略图文件应存在");
            Assert.True(File.Exists(mediumPath), "中等图文件应存在");

            using var thumbBitmap = SKBitmap.Decode(thumbPath);
            Assert.NotNull(thumbBitmap);
            Assert.Equal(300, thumbBitmap.Width);
            Assert.Equal(400, thumbBitmap.Height);

            using var mediumBitmap = SKBitmap.Decode(mediumPath);
            Assert.NotNull(mediumBitmap);
            Assert.Equal(1200, mediumBitmap.Width);
            Assert.Equal(1600, mediumBitmap.Height);
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

    [Fact]
    public async Task GenerateAsync_ItemNotFound_NoOp()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"boxwise-test-{Guid.NewGuid()}");
        try
        {
            var dbName = Guid.NewGuid().ToString();

            // Arrange: 空数据库（无 Item）
            var services = new ServiceCollection();
            services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(dbName));
            services.AddLogging();
            using var serviceProvider = services.BuildServiceProvider();
            var scopeFactory = serviceProvider.GetRequiredService<IServiceScopeFactory>();

            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["DataDirectory"] = tempDir
                }).Build();
            var storage = new ImageStorageService(config);
            var thumbnailService = new ThumbnailService(storage, Mock.Of<ILogger<ThumbnailService>>());

            // Act: 不存在的 Item ID，应静默返回不抛异常
            var ex = await Record.ExceptionAsync(() =>
                thumbnailService.GenerateAsync(999, scopeFactory));

            // Assert
            Assert.Null(ex);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }
}
