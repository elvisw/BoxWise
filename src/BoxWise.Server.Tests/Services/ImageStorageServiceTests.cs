using BoxWise.Server.Services;
using Microsoft.Extensions.Configuration;

namespace BoxWise.Server.Tests.Services;

public class ImageStorageServiceTests
{
    private static (ImageStorageService service, string tempDir) CreateService()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"boxwise-test-{Guid.NewGuid()}");
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DataDirectory"] = tempDir
            }).Build();
        return (new ImageStorageService(config), tempDir);
    }

    [Fact]
    public async Task SaveOriginalAsync_SavesToCorrectPath()
    {
        var (service, tempDir) = CreateService();
        try
        {
            var content = "test-image-data"u8.ToArray();
            using var stream = new MemoryStream(content);

            var relativePath = await service.SaveOriginalAsync(42, stream);

            var expectedRelative = Path.Combine("42", "original.jpg");
            Assert.Equal(expectedRelative, relativePath);
            var fullPath = Path.Combine(tempDir, relativePath);
            Assert.True(File.Exists(fullPath));
            var saved = await File.ReadAllBytesAsync(fullPath);
            Assert.Equal(content, saved);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void GetItemDirectory_CreatesAndReturnsPath()
    {
        var (service, tempDir) = CreateService();
        try
        {
            var dir = service.GetItemDirectory(7);

            var expectedDir = Path.Combine(tempDir, "7");
            Assert.Equal(expectedDir, dir);
            Assert.True(Directory.Exists(dir));
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void DeleteItemFiles_RemovesDirectory()
    {
        var (service, tempDir) = CreateService();
        try
        {
            var dir = service.GetItemDirectory(3);
            File.WriteAllText(Path.Combine(dir, "original.jpg"), "data");

            service.DeleteItemFiles(3);

            Assert.False(Directory.Exists(dir));
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void GetPaths_ReturnCorrectPaths()
    {
        var (service, tempDir) = CreateService();
        try
        {
            var original = service.GetOriginalPath(5);
            var thumb = service.GetThumbPath(5);
            var medium = service.GetMediumPath(5);

            Assert.Equal(Path.Combine(tempDir, "5", "original.jpg"), original);
            Assert.Equal(Path.Combine(tempDir, "5", "thumb.jpg"), thumb);
            Assert.Equal(Path.Combine(tempDir, "5", "medium.jpg"), medium);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }
}
