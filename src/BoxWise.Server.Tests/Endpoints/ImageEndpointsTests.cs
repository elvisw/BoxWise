using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using BoxWise.Server.Data;
using BoxWise.Server.Models;
using BoxWise.Server.Services;
using SkiaSharp;

namespace BoxWise.Server.Tests.Endpoints;

/// <summary>
/// Test authentication handler that auto-authenticates all requests.
/// </summary>
public class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "TestScheme";

    public TestAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        System.Text.Encodings.Web.UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "test-user-id"),
            new Claim(ClaimTypes.Name, "testuser"),
        };
        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}

public class ImageEndpointsTests : IDisposable
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly string _tempDir;
    private readonly List<(LogLevel Level, string Message)> _capturedLogs = [];

    public ImageEndpointsTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"boxwise-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(_tempDir);
        var sqlitePath = Path.Combine(_tempDir, "test.db");

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("DataDirectory", _tempDir);
            builder.UseSetting("ConnectionStrings:DefaultConnection",
                $"Data Source={sqlitePath}");

            builder.ConfigureTestServices(services =>
            {
                services.PostConfigure<AuthorizationOptions>(options =>
                {
                    options.FallbackPolicy = new AuthorizationPolicyBuilder()
                        .RequireAssertion(_ => true)
                        .Build();
                });

                services.AddAuthentication(TestAuthHandler.SchemeName)
                    .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(
                        TestAuthHandler.SchemeName, options => { });

                services.AddLogging(b => b.AddProvider(new CaptureLoggerProvider(_capturedLogs)));
            });
        });
    }

    public void Dispose()
    {
        _factory?.Dispose();

        // Retry cleanup since SQLite may hold a lock briefly
        for (int i = 0; i < 3; i++)
        {
            try
            {
                if (Directory.Exists(_tempDir))
                    Directory.Delete(_tempDir, true);
                break;
            }
            catch (IOException)
            {
                Thread.Sleep(200);
            }
        }
    }

    private static byte[] CreateTestImageBytes(int width = 600, int height = 800)
    {
        using var bitmap = new SKBitmap(width, height);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(new SKColor(255, 0, 0));
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, 85);
        return data.ToArray();
    }

    /// <summary>
    /// Seed a test user and item into the app's database.
    /// </summary>
    private async Task<int> SeedTestItemAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        if (!db.Users.Any(u => u.Id == "test-user-id"))
        {
            db.Users.Add(new AppUser { Id = "test-user-id", UserName = "testuser" });
        }

        // Ensure the admin role exists (Program.cs tries to create it but may fail
        // if it was already created by a previous test in the same factory)
        var item = new Item
        {
            Name = "Test Item",
            CreatedByUserId = "test-user-id",
            CreatedAt = DateTime.UtcNow
        };
        db.Items.Add(item);
        await db.SaveChangesAsync();
        return item.Id;
    }

    private HttpClient CreateClient()
    {
        return _factory.CreateClient();
    }

    private MultipartFormDataContent BuildUploadRequest(int itemId, byte[] imageData)
    {
        var content = new MultipartFormDataContent();

        // Add itemId field
        content.Add(new StringContent(itemId.ToString()), "itemId");

        // Add file with explicit JPEG content type
        var fileContent = new ByteArrayContent(imageData);
        fileContent.Headers.ContentType =
            new System.Net.Http.Headers.MediaTypeHeaderValue("image/jpeg");
        content.Add(fileContent, "file", "test.jpg");

        return content;
    }

    [Fact]
    public async Task UploadAsync_ValidRequest_Returns202()
    {
        // Arrange
        var client = CreateClient();
        var itemId = await SeedTestItemAsync();
        var imageBytes = CreateTestImageBytes();
        var uploadContent = BuildUploadRequest(itemId, imageBytes);

        // Act
        var response = await client.PostAsync("/api/images/upload", uploadContent);

        // Assert
        Assert.Equal(202, (int)response.StatusCode);
    }

    [Fact]
    public async Task UploadAsync_WithoutFile_Returns400()
    {
        // Arrange
        var client = CreateClient();

        // Send POST without multipart content — endpoint checks HasFormContentType
        var content = new ByteArrayContent([]);
        content.Headers.ContentType =
            new System.Net.Http.Headers.MediaTypeHeaderValue("text/plain");

        // Act
        var response = await client.PostAsync("/api/images/upload", content);

        // Assert
        Assert.Equal(400, (int)response.StatusCode);
    }

    [Fact]
    public async Task UploadAsync_NonexistentItem_Returns400()
    {
        // Arrange
        var client = CreateClient();
        var imageBytes = CreateTestImageBytes();
        var uploadContent = BuildUploadRequest(999, imageBytes);

        // Act
        var response = await client.PostAsync("/api/images/upload", uploadContent);

        // Assert
        Assert.Equal(400, (int)response.StatusCode);
    }

    [Fact]
    public async Task UploadAsync_InvalidMagic_Returns400()
    {
        // Arrange: bytes that don't match JPEG/PNG/WebP magic, but valid Content-Type
        var client = CreateClient();
        var itemId = await SeedTestItemAsync();
        var nonImageBytes = new byte[100];
        Array.Fill(nonImageBytes, (byte)0xAB);

        var content = new MultipartFormDataContent();
        content.Add(new StringContent(itemId.ToString()), "itemId");
        var fileContent = new ByteArrayContent(nonImageBytes);
        fileContent.Headers.ContentType =
            new System.Net.Http.Headers.MediaTypeHeaderValue("image/jpeg");
        content.Add(fileContent, "file", "fake.jpg");

        // Act
        var response = await client.PostAsync("/api/images/upload", content);

        // Assert
        Assert.Equal(400, (int)response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("文件格式不支持", body);
    }

    [Fact]
    public async Task UploadAsync_WhenChannelFull_StillReturns202()
    {
        // Arrange
        var client = CreateClient();
        var itemId = await SeedTestItemAsync();
        var imageBytes = CreateTestImageBytes();

        // Fill the channel to capacity — DropWrite silently drops overflow
        var bgService = _factory.Services.GetRequiredService<ThumbnailBackgroundService>();
        for (int i = 0; i < 200; i++)
            bgService.TryEnqueue(10000 + i);

        // Act: endpoint always returns 202 regardless of queue state
        var uploadContent = BuildUploadRequest(itemId, imageBytes);
        var response = await client.PostAsync("/api/images/upload", uploadContent);

        // Assert: endpoint returns 202; Warning log captured (Reader.Count-based best-effort)
        Assert.Equal(202, (int)response.StatusCode);
        Assert.Contains(_capturedLogs, l =>
            l.Level == LogLevel.Warning && l.Message.Contains("Thumbnail queue full"));
    }

    [Fact]
    public async Task ServeAsync_ExistingFile_Returns200()
    {
        // Arrange: create a physical file that the server can serve
        var client = CreateClient();
        var itemId = await SeedTestItemAsync();

        using var scope = _factory.Services.CreateScope();
        var storage = scope.ServiceProvider.GetRequiredService<ImageStorageService>();

        // Create the original image file on disk
        var originalPath = storage.GetOriginalPath(itemId);
        var originalDir = Path.GetDirectoryName(originalPath);
        if (!string.IsNullOrEmpty(originalDir) && !Directory.Exists(originalDir))
            Directory.CreateDirectory(originalDir);

        var imageBytes = CreateTestImageBytes();
        await File.WriteAllBytesAsync(originalPath, imageBytes);

        // Act
        var response = await client.GetAsync($"/api/images/{itemId}?type=original");

        // Assert
        Assert.Equal(200, (int)response.StatusCode);
        Assert.Equal("image/jpeg", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task ServeAsync_NonexistentFile_Returns404()
    {
        // Arrange
        var client = CreateClient();

        // Act
        var response = await client.GetAsync("/api/images/99999?type=original");

        // Assert
        Assert.Equal(404, (int)response.StatusCode);
    }
}

internal sealed class CaptureLoggerProvider(List<(LogLevel Level, string Message)> capturedLogs) : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => new CaptureLogger(capturedLogs);

    public void Dispose() { }

    private sealed class CaptureLogger(List<(LogLevel, string)> capturedLogs) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            lock (capturedLogs)
            {
                capturedLogs.Add((logLevel, formatter(state, exception)));
            }
        }
    }
}
