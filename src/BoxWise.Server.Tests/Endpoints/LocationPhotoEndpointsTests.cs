using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Data.Common;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using BoxWise.Server.Data;
using BoxWise.Server.Endpoints;
using BoxWise.Server.Models;
using BoxWise.Server.Repositories;
using BoxWise.Server.Services;
using BoxWise.Shared.Dtos;
using BoxWise.Shared.Validation;
using SkiaSharp;

namespace BoxWise.Server.Tests.Endpoints;

public sealed class HeaderTestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "HeaderTestScheme";
    public const string HeaderName = "X-Test-User";

    public HeaderTestAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        System.Text.Encodings.Web.UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(HeaderName, out var value)
            || string.IsNullOrWhiteSpace(value))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var user = value.ToString();
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user),
            new Claim(ClaimTypes.Name, user)
        };
        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}

public sealed class LocationPhotoTestFactory : WebApplicationFactory<Program>
{
    private readonly LocationPhotoStore? _photoStoreOverride;
    private readonly ISaveChangesInterceptor? _saveChangesInterceptor;
    private readonly DbCommandInterceptor? _commandInterceptor;
    private readonly bool _deleteOnDispose;

    public string TempDirectory { get; } =
        Path.Combine(Path.GetTempPath(), $"boxwise-location-photo-{Guid.NewGuid():N}");

    public string SqliteConnectionString =>
        $"Data Source={Path.Combine(TempDirectory, "test.db")};Foreign Keys=True";

    public LocationPhotoTestFactory(
        LocationPhotoStore? photoStoreOverride = null,
        ISaveChangesInterceptor? saveChangesInterceptor = null,
        DbCommandInterceptor? commandInterceptor = null,
        string? tempDirectory = null,
        bool deleteOnDispose = true)
    {
        _photoStoreOverride = photoStoreOverride;
        _saveChangesInterceptor = saveChangesInterceptor;
        _commandInterceptor = commandInterceptor;
        _deleteOnDispose = deleteOnDispose;
        if (!string.IsNullOrWhiteSpace(tempDirectory))
            TempDirectory = tempDirectory;

        Directory.CreateDirectory(TempDirectory);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("DataDirectory", TempDirectory);
        builder.UseSetting("ConnectionStrings:DefaultConnection", SqliteConnectionString);

        builder.ConfigureTestServices(services =>
        {
            // 保留 Program.cs 的全局 FallbackPolicy = RequireAuthenticatedUser，
            // 只替换认证方式，让未带测试头的请求真实返回 401。
            services.AddAuthentication(options =>
                {
                    options.DefaultScheme = HeaderTestAuthHandler.SchemeName;
                    options.DefaultAuthenticateScheme = HeaderTestAuthHandler.SchemeName;
                    options.DefaultChallengeScheme = HeaderTestAuthHandler.SchemeName;
                })
                .AddScheme<AuthenticationSchemeOptions, HeaderTestAuthHandler>(
                    HeaderTestAuthHandler.SchemeName, _ => { });

            if (_photoStoreOverride is not null)
                services.AddSingleton(_photoStoreOverride);

            if (_saveChangesInterceptor is not null || _commandInterceptor is not null)
            {
                var interceptors = new List<IInterceptor>();
                if (_saveChangesInterceptor is not null)
                    interceptors.Add(_saveChangesInterceptor);
                if (_commandInterceptor is not null)
                    interceptors.Add(_commandInterceptor);

                services.RemoveAll<AppDbContext>();
                services.RemoveAll<DbContextOptions<AppDbContext>>();
                services.AddDbContext<AppDbContext>(options =>
                    options.UseSqlite(SqliteConnectionString)
                        .AddInterceptors(interceptors));
            }
        });
    }

    public HttpClient CreateAuthenticatedClient(bool includeCsrf = true)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add(HeaderTestAuthHandler.HeaderName, "test-user");
        if (includeCsrf)
            client.DefaultRequestHeaders.Add("X-Requested-With", "XMLHttpRequest");
        return client;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (!disposing || !_deleteOnDispose) return;

        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                if (Directory.Exists(TempDirectory))
                    Directory.Delete(TempDirectory, true);
                break;
            }
            catch (IOException)
            {
                Thread.Sleep(200);
            }
            catch (UnauthorizedAccessException)
            {
                Thread.Sleep(200);
            }
        }
    }
}

internal sealed class FailingBeforeCommitInterceptor : SaveChangesInterceptor
{
    public bool Armed { get; set; }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (Armed)
        {
            Armed = false;
            throw new DbUpdateException("injected failure before commit");
        }

        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }
}

internal sealed class FailingAfterCommitInterceptor : SaveChangesInterceptor
{
    public bool Armed { get; set; }

    public override ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData,
        int result,
        CancellationToken cancellationToken = default)
    {
        if (Armed)
        {
            Armed = false;
            throw new DbUpdateException("injected failure after commit");
        }

        return base.SavedChangesAsync(eventData, result, cancellationToken);
    }
}

internal sealed class DeleteBarrierInterceptor : DbCommandInterceptor
{
    private readonly TaskCompletionSource<bool> _reached =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<bool> _release =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task DeleteReached => _reached.Task;

    public void Release() => _release.TrySetResult(true);

    public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (command.CommandText.Contains("DELETE FROM \"Locations\"", StringComparison.Ordinal)
            && command.CommandText.Contains("NOT EXISTS", StringComparison.Ordinal))
        {
            _reached.TrySetResult(true);
            await _release.Task.WaitAsync(cancellationToken);
        }

        return await base.NonQueryExecutingAsync(
            command, eventData, result, cancellationToken);
    }
}

internal sealed class FailingPublishLocationPhotoStore : LocationPhotoStore
{
    private bool _allowFirstPublish = true;

    public FailingPublishLocationPhotoStore(IConfiguration configuration)
        : base(configuration)
    {
    }

    public override void Publish(string stagedName, string finalName)
    {
        if (_allowFirstPublish)
        {
            _allowFirstPublish = false;
            base.Publish(stagedName, finalName);
            return;
        }

        // 已进入暂存区，但在数据库指针提交前确定性失败。
        throw new IOException("injected publish failure");
    }
}

internal sealed class FailingDeleteLocationPhotoStore : LocationPhotoStore
{
    public FailingDeleteLocationPhotoStore(IConfiguration configuration)
        : base(configuration)
    {
    }

    public override void Delete(string fileName) =>
        throw new IOException("injected delete failure");
}

public class LocationPhotoEndpointsTests
{
    private static async Task EnsureUserAsync(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if (!db.Users.Any(u => u.Id == "test-user"))
        {
            db.Users.Add(new AppUser { Id = "test-user", UserName = "test-user" });
            await db.SaveChangesAsync();
        }
    }

    private static async Task<Location> SeedLocationAsync(
        WebApplicationFactory<Program> factory,
        string name,
        string? description = null,
        int? parentId = null)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var repo = new LocationRepository(db);
        return await repo.CreateAsync(name, parentId, 0, description);
    }

    private static AppDbContext CreateStandaloneDbContext(LocationPhotoTestFactory factory) =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(factory.SqliteConnectionString)
            .Options);

    private static async Task<Item> SeedItemAsync(
        WebApplicationFactory<Program> factory, int? locationId = null)
    {
        await EnsureUserAsync(factory);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var item = new Item
        {
            Name = "测试物品",
            LocationId = locationId,
            CreatedByUserId = "test-user",
            CreatedAt = DateTime.UtcNow
        };
        db.Items.Add(item);
        await db.SaveChangesAsync();
        return item;
    }

    private static byte[] CreateImageBytes(
        SKEncodedImageFormat format, int width = 16, int height = 16)
    {
        using var bitmap = new SKBitmap(width, height);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(new SKColor(30, 120, 200));
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(format, 90);
        return data?.ToArray() ?? throw new InvalidOperationException("无法生成测试图片");
    }

    private static byte[] TruncatedJpegWithParseableHeader()
    {
        var valid = CreateImageBytes(SKEncodedImageFormat.Jpeg, 128, 128);

        for (var length = 64; length < valid.Length - 1; length += 16)
        {
            var truncated = valid.Take(length).ToArray();
            using var data = SKData.CreateCopy(truncated);
            using var codec = SKCodec.Create(data);
            if (codec is null || codec.Info.Width <= 0 || codec.Info.Height <= 0)
                continue;

            using var bitmap = new SKBitmap(new SKImageInfo(
                codec.Info.Width, codec.Info.Height,
                SKColorType.Bgra8888, SKAlphaType.Premul));
            if (codec.GetPixels(bitmap.Info, bitmap.GetPixels()) != SKCodecResult.Success)
                return truncated;
        }

        throw new InvalidOperationException("无法构造可解析头部但缺少像素数据的 JPEG");
    }

    private static byte[] TruncatedWebpWithParseableHeader()
    {
        var valid = CreateImageBytes(SKEncodedImageFormat.Webp, 1200, 900);

        for (var length = 128; length < valid.Length - 1; length += 64)
        {
            var truncated = valid.Take(length).ToArray();
            using var data = SKData.CreateCopy(truncated);
            using var codec = SKCodec.Create(data);
            if (codec is null || codec.Info.Width <= 0 || codec.Info.Height <= 0)
                continue;

            var scale = Math.Min(1f, 512f / Math.Max(codec.Info.Width, codec.Info.Height));
            var target = new SKImageInfo(
                Math.Max(1, (int)Math.Round(codec.Info.Width * scale)),
                Math.Max(1, (int)Math.Round(codec.Info.Height * scale)),
                SKColorType.Bgra8888, SKAlphaType.Premul);

            using var bitmap = SKBitmap.Decode(codec, target);
            if (bitmap is null)
                return truncated;
        }

        throw new InvalidOperationException("无法构造可解析头部但缺少像素数据的 WebP");
    }

    private static byte[] TruncatedPngWithParseableHeader()
    {
        var valid = CreateImageBytes(SKEncodedImageFormat.Png, 1200, 900);

        for (var length = 128; length < valid.Length - 1; length += 64)
        {
            var truncated = valid.Take(length).ToArray();
            using var data = SKData.CreateCopy(truncated);
            using var codec = SKCodec.Create(data);
            if (codec is null || codec.Info.Width <= 0 || codec.Info.Height <= 0)
                continue;

            var info = new SKImageInfo(
                codec.Info.Width, codec.Info.Height,
                SKColorType.Bgra8888, SKAlphaType.Premul);
            var pixels = new byte[info.BytesSize];
            if (codec.GetPixels(info, pixels) != SKCodecResult.Success)
                return truncated;
        }

        throw new InvalidOperationException("无法构造可解析头部但缺少像素数据的 PNG");
    }

    private static byte[] PadToSize(byte[] image, int size)
    {
        if (image.Length >= size) return image;

        var padded = new byte[size];
        Buffer.BlockCopy(image, 0, padded, 0, image.Length);
        return padded;
    }

    private static byte[] JpegBytes(int length = 0)
    {
        var bytes = CreateImageBytes(SKEncodedImageFormat.Jpeg);
        return length > bytes.Length ? PadToSize(bytes, length) : bytes;
    }

    private static byte[] PngBytes(int length = 0)
    {
        var bytes = CreateImageBytes(SKEncodedImageFormat.Png);
        return length > bytes.Length ? PadToSize(bytes, length) : bytes;
    }

    private static byte[] WebpBytes() => CreateImageBytes(SKEncodedImageFormat.Webp);

    private static MultipartFormDataContent PhotoContent(
        byte[] bytes, string contentType = "image/jpeg", string fileName = "photo.jpg")
    {
        var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        content.Add(file, "file", fileName);
        return content;
    }

    private static async Task<HttpResponseMessage> PutDescriptionAsync(
        HttpClient client, int id, string name, string? description,
        bool includeDescription = true)
    {
        var payload = includeDescription
            ? JsonSerializer.Serialize(new { name, description })
            : JsonSerializer.Serialize(new { name });

        return await client.PutAsync(
            $"/api/locations/{id}",
            new StringContent(payload, Encoding.UTF8, "application/json"));
    }

    private static async Task<LocationDto?> GetMetadataAsync(HttpClient client, int id)
    {
        var response = await client.GetAsync($"/api/locations/{id}");
        if (response.StatusCode != HttpStatusCode.OK) return null;
        return await response.Content.ReadFromJsonAsync<LocationDto>();
    }

    private static async Task<string> ReadBodyAsync(HttpResponseMessage response) =>
        await response.Content.ReadAsStringAsync();

    [Fact]
    public async Task Endpoints_WithoutAuthentication_Return401()
    {
        using var factory = new LocationPhotoTestFactory();
        var client = factory.CreateClient();
        var location = await SeedLocationAsync(factory, "抽屉");

        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.GetAsync("/api/locations")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.GetAsync($"/api/locations/{location.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.GetAsync($"/api/locations/{location.Id}/photo")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.PutAsync($"/api/locations/{location.Id}/photo",
                PhotoContent(JpegBytes(32)))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.DeleteAsync($"/api/locations/{location.Id}/photo")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.DeleteAsync($"/api/locations/{location.Id}")).StatusCode);
    }

    [Fact]
    public async Task Metadata_ReturnsDescriptionPhotoPresenceAndNamePath()
    {
        using var factory = new LocationPhotoTestFactory();
        var client = factory.CreateAuthenticatedClient();
        var parent = await SeedLocationAsync(factory, "书房");
        var child = await SeedLocationAsync(factory, "抽屉", "充电线与说明书\n左侧为空", parent.Id);

        var upload = await client.PutAsync(
            $"/api/locations/{child.Id}/photo", PhotoContent(JpegBytes(64)));
        Assert.Equal(HttpStatusCode.OK, upload.StatusCode);

        var all = await client.GetFromJsonAsync<List<LocationDto>>("/api/locations");
        Assert.NotNull(all);

        var dto = Assert.Single(all!, l => l.Id == child.Id);
        Assert.Equal("充电线与说明书\n左侧为空", dto.Description);
        Assert.True(dto.HasPhoto);
        Assert.Equal("书房 → 抽屉", dto.NamePath);

        var byId = await GetMetadataAsync(client, child.Id);
        Assert.NotNull(byId);
        Assert.Equal(dto.Description, byId!.Description);
        Assert.Equal(dto.HasPhoto, byId.HasPhoto);
        Assert.Equal("书房 → 抽屉", byId.NamePath);
    }

    [Fact]
    public async Task Description_300Graphemes_Accepted_301Rejected_NoTruncation()
    {
        using var factory = new LocationPhotoTestFactory();
        var client = factory.CreateAuthenticatedClient();
        var location = await SeedLocationAsync(factory, "抽屉");

        var family = "👨‍👩‍👧‍👦";
        var combining = "e\u0301";
        var accepted = string.Concat(Enumerable.Repeat(family, 300));
        var acceptedCombining = string.Concat(Enumerable.Repeat(combining, 300));

        var first = await PutDescriptionAsync(client, location.Id, "抽屉", accepted);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        var saved = await readDto(first);
        Assert.Equal(accepted, saved.Description);

        var second = await PutDescriptionAsync(client, location.Id, "抽屉", acceptedCombining);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);

        var savedCombining = await readDto(second);
        Assert.Equal(acceptedCombining, savedCombining.Description);

        var tooLong = string.Concat(Enumerable.Repeat(family, 301));
        var rejected = await PutDescriptionAsync(client, location.Id, "抽屉", tooLong);
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        Assert.Contains("300", await ReadBodyAsync(rejected));

        var unchanged = await GetMetadataAsync(client, location.Id);
        Assert.Equal(acceptedCombining, unchanged!.Description);

        static async Task<LocationDto> readDto(HttpResponseMessage response)
        {
            var dto = await response.Content.ReadFromJsonAsync<LocationDto>();
            Assert.NotNull(dto);
            return dto!;
        }
    }

    [Fact]
    public async Task Description_TrimsEdges_PreservesInternalLineBreaks_WhitespaceIsNone()
    {
        using var factory = new LocationPhotoTestFactory();
        var client = factory.CreateAuthenticatedClient();
        var location = await SeedLocationAsync(factory, "抽屉");

        var response = await PutDescriptionAsync(
            client, location.Id, "抽屉", "\r\n  第一行\r\n第二行  \r\n");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var dto = await response.Content.ReadFromJsonAsync<LocationDto>();
        Assert.Equal("第一行\r\n第二行", dto!.Description);

        var cleared = await PutDescriptionAsync(client, location.Id, "抽屉", " \r\n\t ");
        Assert.Equal(HttpStatusCode.OK, cleared.StatusCode);

        var clearedDto = await cleared.Content.ReadFromJsonAsync<LocationDto>();
        Assert.Null(clearedDto!.Description);
    }

    [Fact]
    public async Task Description_Omitted_RemainsUnchanged_ExplicitEmpty_Clears()
    {
        using var factory = new LocationPhotoTestFactory();
        var client = factory.CreateAuthenticatedClient();
        var location = await SeedLocationAsync(factory, "抽屉", "旧描述");

        var renamed = await PutDescriptionAsync(
            client, location.Id, "新名称", null, includeDescription: false);
        Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);

        var afterRename = await GetMetadataAsync(client, location.Id);
        Assert.Equal("新名称", afterRename!.Name);
        Assert.Equal("旧描述", afterRename.Description);

        var cleared = await PutDescriptionAsync(client, location.Id, "新名称", "");
        Assert.Equal(HttpStatusCode.OK, cleared.StatusCode);

        var afterClear = await GetMetadataAsync(client, location.Id);
        Assert.Null(afterClear!.Description);
    }

    [Fact]
    public async Task Description_ExplicitJsonNull_Clears()
    {
        using var factory = new LocationPhotoTestFactory();
        var client = factory.CreateAuthenticatedClient();
        var location = await SeedLocationAsync(factory, "抽屉", "旧描述");

        var response = await client.PutAsync(
            $"/api/locations/{location.Id}",
            new StringContent(
                "{\"name\":\"抽屉\",\"description\":null}",
                Encoding.UTF8,
                "application/json"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var dto = await response.Content.ReadFromJsonAsync<LocationDto>();
        Assert.Null(dto!.Description);
    }

    [Fact]
    public async Task Upload_Exactly10MiB_Succeeds_AndServesNoStore()
    {
        using var factory = new LocationPhotoTestFactory();
        var client = factory.CreateAuthenticatedClient();
        var location = await SeedLocationAsync(factory, "抽屉");
        var bytes = JpegBytes(10 * 1024 * 1024);

        var upload = await client.PutAsync(
            $"/api/locations/{location.Id}/photo", PhotoContent(bytes));
        Assert.Equal(HttpStatusCode.OK, upload.StatusCode);

        var dto = await upload.Content.ReadFromJsonAsync<LocationDto>();
        Assert.True(dto!.HasPhoto);

        var response = await client.GetAsync($"/api/locations/{location.Id}/photo");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("image/jpeg", response.Content.Headers.ContentType?.MediaType);
        Assert.True(response.Headers.CacheControl?.NoStore);

        var served = await response.Content.ReadAsByteArrayAsync();
        Assert.Equal(bytes.Length, served.Length);
        Assert.Equal(bytes[..3], served[..3]);
    }

    [Fact]
    public async Task Upload_OverSizeLimit_Rejected_SavedPhotoUnchanged()
    {
        using var factory = new LocationPhotoTestFactory();
        var client = factory.CreateAuthenticatedClient();
        var location = await SeedLocationAsync(factory, "抽屉");
        var original = JpegBytes(128);

        var first = await client.PutAsync(
            $"/api/locations/{location.Id}/photo", PhotoContent(original));
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        var oversized = JpegBytes(10 * 1024 * 1024 + 1);
        var rejected = await client.PutAsync(
            $"/api/locations/{location.Id}/photo", PhotoContent(oversized));
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        Assert.Contains("10 MB", await ReadBodyAsync(rejected));

        var served = await client.GetAsync($"/api/locations/{location.Id}/photo");
        Assert.Equal(HttpStatusCode.OK, served.StatusCode);
        Assert.Equal(original, await served.Content.ReadAsByteArrayAsync());

        var meta = await GetMetadataAsync(client, location.Id);
        Assert.True(meta!.HasPhoto);
    }

    [Fact]
    public async Task Upload_UnsupportedFormatOrMagic_Rejected_WithoutPhoto()
    {
        using var factory = new LocationPhotoTestFactory();
        var client = factory.CreateAuthenticatedClient();
        var location = await SeedLocationAsync(factory, "抽屉");

        var gif = new byte[64];
        gif[0] = 0x47;
        gif[1] = 0x49;
        gif[2] = 0x46;

        var wrongType = await client.PutAsync(
            $"/api/locations/{location.Id}/photo",
            PhotoContent(gif, "image/gif", "photo.gif"));
        Assert.Equal(HttpStatusCode.BadRequest, wrongType.StatusCode);
        Assert.Contains("JPG", await ReadBodyAsync(wrongType));

        var fakeJpeg = new byte[64];
        Array.Fill(fakeJpeg, (byte)0xAB);
        var wrongMagic = await client.PutAsync(
            $"/api/locations/{location.Id}/photo",
            PhotoContent(fakeJpeg, "image/jpeg", "photo.jpg"));
        Assert.Equal(HttpStatusCode.BadRequest, wrongMagic.StatusCode);
        Assert.Contains("格式", await ReadBodyAsync(wrongMagic));

        var meta = await GetMetadataAsync(client, location.Id);
        Assert.False(meta!.HasPhoto);
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.GetAsync($"/api/locations/{location.Id}/photo")).StatusCode);
    }

    [Fact]
    public async Task Upload_PngAndWebp_AreServedWithVerifiedMime()
    {
        using var factory = new LocationPhotoTestFactory();
        var client = factory.CreateAuthenticatedClient();
        var location = await SeedLocationAsync(factory, "抽屉");

        var png = await client.PutAsync(
            $"/api/locations/{location.Id}/photo",
            PhotoContent(PngBytes(64), "image/png", "photo.png"));
        Assert.Equal(HttpStatusCode.OK, png.StatusCode);

        var pngResponse = await client.GetAsync($"/api/locations/{location.Id}/photo");
        Assert.Equal("image/png", pngResponse.Content.Headers.ContentType?.MediaType);

        var webp = WebpBytes();

        var uploadWebp = await client.PutAsync(
            $"/api/locations/{location.Id}/photo",
            PhotoContent(webp, "image/webp", "photo.webp"));
        Assert.Equal(HttpStatusCode.OK, uploadWebp.StatusCode);

        var webpResponse = await client.GetAsync($"/api/locations/{location.Id}/photo");
        Assert.Equal("image/webp", webpResponse.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Upload_Replace_RemovesOldFileAndServesNewBytes()
    {
        using var factory = new LocationPhotoTestFactory();
        var client = factory.CreateAuthenticatedClient();
        var location = await SeedLocationAsync(factory, "抽屉");

        var firstBytes = JpegBytes(128);
        var first = await client.PutAsync(
            $"/api/locations/{location.Id}/photo", PhotoContent(firstBytes));
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        var secondBytes = PngBytes(96);
        var second = await client.PutAsync(
            $"/api/locations/{location.Id}/photo",
            PhotoContent(secondBytes, "image/png", "photo.png"));
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);

        var served = await client.GetAsync($"/api/locations/{location.Id}/photo");
        Assert.Equal("image/png", served.Content.Headers.ContentType?.MediaType);
        Assert.Equal(secondBytes, await served.Content.ReadAsByteArrayAsync());

        var files = Directory.GetFiles(
            Path.Combine(factory.TempDirectory, "location-photos"),
            $"{location.Id}-*");
        Assert.Single(files);
    }

    [Fact]
    public async Task RemovePhoto_ClearsPointer_AndIsIdempotent()
    {
        using var factory = new LocationPhotoTestFactory();
        var client = factory.CreateAuthenticatedClient();
        var location = await SeedLocationAsync(factory, "抽屉");

        await client.PutAsync(
            $"/api/locations/{location.Id}/photo", PhotoContent(JpegBytes(64)));

        var removed = await client.DeleteAsync($"/api/locations/{location.Id}/photo");
        Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.GetAsync($"/api/locations/{location.Id}/photo")).StatusCode);

        var meta = await GetMetadataAsync(client, location.Id);
        Assert.False(meta!.HasPhoto);

        var again = await client.DeleteAsync($"/api/locations/{location.Id}/photo");
        Assert.Equal(HttpStatusCode.NoContent, again.StatusCode);
    }

    [Fact]
    public async Task Delete_NonEmptyLocation_Rejected_RecordAndPhotoRemain()
    {
        using var factory = new LocationPhotoTestFactory();
        var client = factory.CreateAuthenticatedClient();
        var parent = await SeedLocationAsync(factory, "柜子", "上层放工具");
        await SeedLocationAsync(factory, "抽屉", null, parent.Id);
        await client.PutAsync(
            $"/api/locations/{parent.Id}/photo", PhotoContent(JpegBytes(64)));

        var rejected = await client.DeleteAsync($"/api/locations/{parent.Id}");
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);

        var meta = await GetMetadataAsync(client, parent.Id);
        Assert.NotNull(meta);
        Assert.Equal("上层放工具", meta!.Description);
        Assert.True(meta.HasPhoto);
        Assert.Equal(HttpStatusCode.OK,
            (await client.GetAsync($"/api/locations/{parent.Id}/photo")).StatusCode);
    }

    [Fact]
    public async Task Delete_LocationWithItem_Rejected_PhotoRemains()
    {
        using var factory = new LocationPhotoTestFactory();
        var client = factory.CreateAuthenticatedClient();
        var location = await SeedLocationAsync(factory, "柜子", "有物品");
        await SeedItemAsync(factory, location.Id);
        await client.PutAsync(
            $"/api/locations/{location.Id}/photo", PhotoContent(JpegBytes(64)));

        var rejected = await client.DeleteAsync($"/api/locations/{location.Id}");
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);

        var meta = await GetMetadataAsync(client, location.Id);
        Assert.Equal("有物品", meta!.Description);
        Assert.True(meta.HasPhoto);
    }

    [Fact]
    public async Task Delete_EmptyLocation_RemovesDescriptionAndPhoto()
    {
        using var factory = new LocationPhotoTestFactory();
        var client = factory.CreateAuthenticatedClient();
        var location = await SeedLocationAsync(factory, "空箱子", "旧说明");
        await client.PutAsync(
            $"/api/locations/{location.Id}/photo", PhotoContent(JpegBytes(64)));

        var deleted = await client.DeleteAsync($"/api/locations/{location.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

        Assert.Null(await GetMetadataAsync(client, location.Id));
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.GetAsync($"/api/locations/{location.Id}/photo")).StatusCode);
    }

    [Fact]
    public async Task Delete_PhotoCleanupFails_CommittedDeleteWithWarning_Photo404()
    {
        var tempDirectory = Path.Combine(
            Path.GetTempPath(), $"boxwise-location-photo-{Guid.NewGuid():N}");
        var failingStore = new FailingDeleteLocationPhotoStore(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DataDirectory"] = tempDirectory
            })
            .Build());

        using var factory = new LocationPhotoTestFactory(
            failingStore, tempDirectory: tempDirectory);
        var client = factory.CreateAuthenticatedClient();
        var location = await SeedLocationAsync(factory, "空箱子", "旧说明");
        await client.PutAsync(
            $"/api/locations/{location.Id}/photo", PhotoContent(JpegBytes(64)));

        var deleted = await client.DeleteAsync($"/api/locations/{location.Id}");
        Assert.Equal(HttpStatusCode.OK, deleted.StatusCode);

        var warning = await deleted.Content.ReadFromJsonAsync<LocationCleanupWarning>();
        Assert.True(warning!.CleanupWarning);

        // 数据库删除已提交：元数据和照片都返回 404，不重试删除。
        Assert.Null(await GetMetadataAsync(client, location.Id));
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.GetAsync($"/api/locations/{location.Id}/photo")).StatusCode);
    }

    [Fact]
    public async Task Upload_PublishFailsAfterStaging_OldPhotoAndMetadataRemain()
    {
        var tempDirectory = Path.Combine(
            Path.GetTempPath(), $"boxwise-location-photo-{Guid.NewGuid():N}");
        var failingStore = new FailingPublishLocationPhotoStore(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DataDirectory"] = tempDirectory
            })
            .Build());

        using var factory = new LocationPhotoTestFactory(
            failingStore, tempDirectory: tempDirectory);
        var client = factory.CreateAuthenticatedClient();
        var location = await SeedLocationAsync(factory, "抽屉", "旧描述");

        var original = JpegBytes(128);
        var first = await client.PutAsync(
            $"/api/locations/{location.Id}/photo", PhotoContent(original));
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        var replacement = PngBytes(256);
        var failed = await client.PutAsync(
            $"/api/locations/{location.Id}/photo",
            PhotoContent(replacement, "image/png", "new.png"));
        Assert.Equal(HttpStatusCode.InternalServerError, failed.StatusCode);

        var served = await client.GetAsync($"/api/locations/{location.Id}/photo");
        Assert.Equal(HttpStatusCode.OK, served.StatusCode);
        Assert.Equal("image/jpeg", served.Content.Headers.ContentType?.MediaType);
        Assert.Equal(original, await served.Content.ReadAsByteArrayAsync());

        var meta = await GetMetadataAsync(client, location.Id);
        Assert.Equal("旧描述", meta!.Description);
        Assert.True(meta.HasPhoto);

        var published = Directory.GetFiles(
            Path.Combine(factory.TempDirectory, "location-photos"),
            $"{location.Id}-*");
        Assert.Single(published);

        var staging = Directory.GetFiles(
            Path.Combine(factory.TempDirectory, "location-photos", "staging"));
        Assert.Empty(staging);
    }

    [Fact]
    public async Task Delete_ItemMovedInByAnotherConnection_Rejected_ItemLocationAndPhotoRemain()
    {
        using var factory = new LocationPhotoTestFactory();
        var client = factory.CreateAuthenticatedClient();
        var target = await SeedLocationAsync(factory, "目标抽屉", "目标描述");
        var source = await SeedLocationAsync(factory, "原位置");
        var item = await SeedItemAsync(factory, source.Id);

        await client.PutAsync(
            $"/api/locations/{target.Id}/photo", PhotoContent(JpegBytes(64)));

        // 另一条 SQLite 连接（独立 DbContext）先提交物品移入，再尝试删除目标位置。
        await using (var other = CreateStandaloneDbContext(factory))
        {
            var tracked = await other.Items.FirstAsync(i => i.Id == item.Id);
            tracked.LocationId = target.Id;
            await other.SaveChangesAsync();
        }

        var rejected = await client.DeleteAsync($"/api/locations/{target.Id}");
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);

        await using (var verify = CreateStandaloneDbContext(factory))
        {
            var after = await verify.Items
                .AsNoTracking()
                .FirstAsync(i => i.Id == item.Id);
            Assert.Equal(target.Id, after.LocationId);

            var location = await verify.Locations
                .AsNoTracking()
                .FirstAsync(l => l.Id == target.Id);
            Assert.Equal("目标描述", location.Description);
            Assert.False(string.IsNullOrEmpty(location.PhotoFileName));
        }

        Assert.Equal(HttpStatusCode.OK,
            (await client.GetAsync($"/api/locations/{target.Id}/photo")).StatusCode);
    }

    [Fact]
    public async Task Delete_ItemMovedInWhileBarrier_Rejected_ItemAssociationPreserved()
    {
        var barrier = new DeleteBarrierInterceptor();
        using var factory = new LocationPhotoTestFactory(commandInterceptor: barrier);
        var client = factory.CreateAuthenticatedClient();
        var target = await SeedLocationAsync(factory, "目标抽屉", "目标描述");
        var source = await SeedLocationAsync(factory, "原位置");
        var item = await SeedItemAsync(factory, source.Id);

        await client.PutAsync(
            $"/api/locations/{target.Id}/photo", PhotoContent(JpegBytes(64)));

        var deleteTask = client.DeleteAsync($"/api/locations/{target.Id}");

        // 暂停在条件 DELETE 真正执行之前；另一条连接在此时提交物品移入。
        await barrier.DeleteReached.WaitAsync(TimeSpan.FromSeconds(10));

        await using (var other = CreateStandaloneDbContext(factory))
        {
            var tracked = await other.Items.FirstAsync(i => i.Id == item.Id);
            tracked.LocationId = target.Id;
            await other.SaveChangesAsync();
        }

        barrier.Release();
        var response = await deleteTask;

        // 若实现是无条件 DELETE（或先检查后删除而未与删除原子化），
        // 这里会 204、物品 LocationId 被 SetNull，位置/照片消失；断言会失败。
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        await using (var verify = CreateStandaloneDbContext(factory))
        {
            var after = await verify.Items
                .AsNoTracking()
                .FirstAsync(i => i.Id == item.Id);
            Assert.Equal(target.Id, after.LocationId);

            var location = await verify.Locations
                .AsNoTracking()
                .FirstAsync(l => l.Id == target.Id);
            Assert.Equal("目标描述", location.Description);
            Assert.False(string.IsNullOrEmpty(location.PhotoFileName));
        }

        Assert.Equal(HttpStatusCode.OK,
            (await client.GetAsync($"/api/locations/{target.Id}/photo")).StatusCode);
    }

    [Fact]
    public async Task Delete_EmptyLocationThenMoveIn_RejectedByForeignKey_NotSilentlyOrphaned()
    {
        using var factory = new LocationPhotoTestFactory();
        var client = factory.CreateAuthenticatedClient();
        var target = await SeedLocationAsync(factory, "将被删除");
        var source = await SeedLocationAsync(factory, "原位置");
        var item = await SeedItemAsync(factory, source.Id);

        Assert.Equal(HttpStatusCode.NoContent,
            (await client.DeleteAsync($"/api/locations/{target.Id}")).StatusCode);

        await using (var other = CreateStandaloneDbContext(factory))
        {
            var tracked = await other.Items.FirstAsync(i => i.Id == item.Id);
            tracked.LocationId = target.Id;

            var error = await Record.ExceptionAsync(() => other.SaveChangesAsync());
            Assert.NotNull(error);
        }

        await using (var verify = CreateStandaloneDbContext(factory))
        {
            var after = await verify.Items
                .AsNoTracking()
                .FirstAsync(i => i.Id == item.Id);
            Assert.Equal(source.Id, after.LocationId);
        }
    }

    [Fact]
    public async Task Upload_BeforeCommitFailure_KeepsOldBytesAndRemovesOnlyUnreferencedFile()
    {
        var interceptor = new FailingBeforeCommitInterceptor();
        using var factory = new LocationPhotoTestFactory(saveChangesInterceptor: interceptor);
        var client = factory.CreateAuthenticatedClient();
        var location = await SeedLocationAsync(factory, "抽屉", "旧描述");

        var original = JpegBytes();
        var first = await client.PutAsync(
            $"/api/locations/{location.Id}/photo", PhotoContent(original));
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        interceptor.Armed = true;
        var replacement = PngBytes();
        var failed = await client.PutAsync(
            $"/api/locations/{location.Id}/photo",
            PhotoContent(replacement, "image/png", "new.png"));
        Assert.Equal(HttpStatusCode.InternalServerError, failed.StatusCode);

        var served = await client.GetAsync($"/api/locations/{location.Id}/photo");
        Assert.Equal(HttpStatusCode.OK, served.StatusCode);
        Assert.Equal("image/jpeg", served.Content.Headers.ContentType?.MediaType);
        Assert.Equal(original, await served.Content.ReadAsByteArrayAsync());

        var meta = await GetMetadataAsync(client, location.Id);
        Assert.Equal("旧描述", meta!.Description);
        Assert.True(meta.HasPhoto);

        Assert.Single(Directory.GetFiles(
            Path.Combine(factory.TempDirectory, "location-photos"),
            $"{location.Id}-*"));
        Assert.Empty(Directory.GetFiles(
            Path.Combine(factory.TempDirectory, "location-photos", "staging")));
    }

    [Fact]
    public async Task Upload_AfterCommitFailure_RecheckSeesCommittedNewBytes()
    {
        var interceptor = new FailingAfterCommitInterceptor();
        using var factory = new LocationPhotoTestFactory(saveChangesInterceptor: interceptor);
        var client = factory.CreateAuthenticatedClient();
        var location = await SeedLocationAsync(factory, "抽屉");

        var original = JpegBytes();
        var first = await client.PutAsync(
            $"/api/locations/{location.Id}/photo", PhotoContent(original));
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        interceptor.Armed = true;
        var replacement = PngBytes();
        var failed = await client.PutAsync(
            $"/api/locations/{location.Id}/photo",
            PhotoContent(replacement, "image/png", "new.png"));
        Assert.Equal(HttpStatusCode.InternalServerError, failed.StatusCode);

        // 保存实际上已提交：不因错误响应断言回滚，重新核对后应读到新照片。
        var meta = await GetMetadataAsync(client, location.Id);
        Assert.True(meta!.HasPhoto);

        var served = await client.GetAsync($"/api/locations/{location.Id}/photo");
        Assert.Equal(HttpStatusCode.OK, served.StatusCode);
        Assert.Equal("image/png", served.Content.Headers.ContentType?.MediaType);
        Assert.Equal(replacement, await served.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task Remove_BeforeCommitFailure_KeepsPointerAndFile()
    {
        var interceptor = new FailingBeforeCommitInterceptor();
        using var factory = new LocationPhotoTestFactory(saveChangesInterceptor: interceptor);
        var client = factory.CreateAuthenticatedClient();
        var location = await SeedLocationAsync(factory, "抽屉");

        var original = JpegBytes();
        Assert.Equal(HttpStatusCode.OK,
            (await client.PutAsync(
                $"/api/locations/{location.Id}/photo", PhotoContent(original))).StatusCode);

        interceptor.Armed = true;
        var failed = await client.DeleteAsync($"/api/locations/{location.Id}/photo");
        Assert.Equal(HttpStatusCode.InternalServerError, failed.StatusCode);

        var meta = await GetMetadataAsync(client, location.Id);
        Assert.True(meta!.HasPhoto);

        var served = await client.GetAsync($"/api/locations/{location.Id}/photo");
        Assert.Equal(HttpStatusCode.OK, served.StatusCode);
        Assert.Equal(original, await served.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task Remove_AfterCommitFailure_RecheckClearsFile()
    {
        var interceptor = new FailingAfterCommitInterceptor();
        using var factory = new LocationPhotoTestFactory(saveChangesInterceptor: interceptor);
        var client = factory.CreateAuthenticatedClient();
        var location = await SeedLocationAsync(factory, "抽屉");

        Assert.Equal(HttpStatusCode.OK,
            (await client.PutAsync(
                $"/api/locations/{location.Id}/photo", PhotoContent(JpegBytes()))).StatusCode);

        interceptor.Armed = true;
        var failed = await client.DeleteAsync($"/api/locations/{location.Id}/photo");
        Assert.Equal(HttpStatusCode.InternalServerError, failed.StatusCode);

        // 指针清空已提交；重试前重新核对即可，文件已按确认未引用清理。
        var meta = await GetMetadataAsync(client, location.Id);
        Assert.False(meta!.HasPhoto);
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.GetAsync($"/api/locations/{location.Id}/photo")).StatusCode);
        Assert.Empty(Directory.GetFiles(
            Path.Combine(factory.TempDirectory, "location-photos"),
            $"{location.Id}-*"));
    }

    [Fact]
    public async Task PhotoWrites_WithoutCsrfHeader_Rejected()
    {
        using var factory = new LocationPhotoTestFactory();
        var client = factory.CreateAuthenticatedClient(includeCsrf: false);
        var location = await SeedLocationAsync(factory, "抽屉");

        var put = await client.PutAsync(
            $"/api/locations/{location.Id}/photo", PhotoContent(JpegBytes()));
        Assert.Equal(HttpStatusCode.BadRequest, put.StatusCode);
        Assert.Contains("CSRF", await ReadBodyAsync(put));

        var delete = await client.DeleteAsync($"/api/locations/{location.Id}/photo");
        Assert.Equal(HttpStatusCode.BadRequest, delete.StatusCode);

        Assert.Equal(HttpStatusCode.NotFound,
            (await client.GetAsync($"/api/locations/{location.Id}/photo")).StatusCode);
    }

    [Fact]
    public async Task Upload_NonMultipart_Rejected()
    {
        using var factory = new LocationPhotoTestFactory();
        var client = factory.CreateAuthenticatedClient();
        var location = await SeedLocationAsync(factory, "抽屉");

        var content = new ByteArrayContent(JpegBytes());
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

        var response = await client.PutAsync(
            $"/api/locations/{location.Id}/photo", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("multipart", await ReadBodyAsync(response));
    }

    [Fact]
    public async Task Upload_MultipleFileParts_Rejected()
    {
        using var factory = new LocationPhotoTestFactory();
        var client = factory.CreateAuthenticatedClient();
        var location = await SeedLocationAsync(factory, "抽屉");

        using var content = new MultipartFormDataContent();
        var first = new ByteArrayContent(JpegBytes());
        first.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        content.Add(first, "file", "first.jpg");
        var second = new ByteArrayContent(JpegBytes());
        second.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        content.Add(second, "file", "second.jpg");

        var response = await client.PutAsync(
            $"/api/locations/{location.Id}/photo", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("只包含一个", await ReadBodyAsync(response));
        Assert.False((await GetMetadataAsync(client, location.Id))!.HasPhoto);
    }

    [Fact]
    public async Task Upload_FieldNotNamedFile_Rejected()
    {
        using var factory = new LocationPhotoTestFactory();
        var client = factory.CreateAuthenticatedClient();
        var location = await SeedLocationAsync(factory, "抽屉");

        using var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(JpegBytes());
        file.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        content.Add(file, "photo", "photo.jpg");

        var response = await client.PutAsync(
            $"/api/locations/{location.Id}/photo", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("file", await ReadBodyAsync(response));
    }

    [Theory]
    [InlineData("png", 768, 576)]
    [InlineData("png", 1200, 900)]
    [InlineData("png", 4000, 3000)]
    [InlineData("png", 6000, 4500)]
    [InlineData("jpeg", 6000, 4500)]
    public async Task Upload_CommonPngAndLargeJpegDimensions_Accepted(
        string format, int width, int height)
    {
        using var factory = new LocationPhotoTestFactory();
        var client = factory.CreateAuthenticatedClient();
        var location = await SeedLocationAsync(factory, "抽屉");

        var bytes = format == "png"
            ? CreateImageBytes(SKEncodedImageFormat.Png, width, height)
            : CreateImageBytes(SKEncodedImageFormat.Jpeg, width, height);
        Assert.True(bytes.Length <= LocationPhotoRules.MaxBytes);

        var contentType = format == "png" ? "image/png" : "image/jpeg";
        var fileName = format == "png" ? "photo.png" : "photo.jpg";
        var upload = await client.PutAsync(
            $"/api/locations/{location.Id}/photo",
            PhotoContent(bytes, contentType, fileName));

        Assert.Equal(HttpStatusCode.OK, upload.StatusCode);

        var meta = await GetMetadataAsync(client, location.Id);
        Assert.True(meta!.HasPhoto);

        var served = await client.GetAsync($"/api/locations/{location.Id}/photo");
        Assert.Equal(HttpStatusCode.OK, served.StatusCode);
        Assert.Equal(bytes, await served.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task Upload_TruncatedWebpWithParseableHeader_Rejected_PreservesSavedPhoto()
    {
        using var factory = new LocationPhotoTestFactory();
        var client = factory.CreateAuthenticatedClient();
        var location = await SeedLocationAsync(factory, "抽屉");

        var original = WebpBytes();
        Assert.Equal(HttpStatusCode.OK,
            (await client.PutAsync(
                $"/api/locations/{location.Id}/photo",
                PhotoContent(original, "image/webp", "original.webp"))).StatusCode);

        var truncated = TruncatedWebpWithParseableHeader();
        var rejected = await client.PutAsync(
            $"/api/locations/{location.Id}/photo",
            PhotoContent(truncated, "image/webp", "truncated.webp"));

        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        Assert.Contains("格式", await ReadBodyAsync(rejected));

        var served = await client.GetAsync($"/api/locations/{location.Id}/photo");
        Assert.Equal(HttpStatusCode.OK, served.StatusCode);
        Assert.Equal(original, await served.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task Upload_TruncatedPngWithParseableHeader_Rejected_PreservesSavedPhoto()
    {
        using var factory = new LocationPhotoTestFactory();
        var client = factory.CreateAuthenticatedClient();
        var location = await SeedLocationAsync(factory, "抽屉");

        var original = PngBytes();
        Assert.Equal(HttpStatusCode.OK,
            (await client.PutAsync(
                $"/api/locations/{location.Id}/photo",
                PhotoContent(original, "image/png", "original.png"))).StatusCode);

        var truncated = TruncatedPngWithParseableHeader();
        var rejected = await client.PutAsync(
            $"/api/locations/{location.Id}/photo",
            PhotoContent(truncated, "image/png", "truncated.png"));

        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        Assert.Contains("格式", await ReadBodyAsync(rejected));

        var served = await client.GetAsync($"/api/locations/{location.Id}/photo");
        Assert.Equal(HttpStatusCode.OK, served.StatusCode);
        Assert.Equal(original, await served.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task Upload_ParseableHeaderWithoutPixels_Rejected_PreservesSavedPhoto()
    {
        using var factory = new LocationPhotoTestFactory();
        var client = factory.CreateAuthenticatedClient();
        var location = await SeedLocationAsync(factory, "抽屉");

        var original = JpegBytes();
        Assert.Equal(HttpStatusCode.OK,
            (await client.PutAsync(
                $"/api/locations/{location.Id}/photo", PhotoContent(original))).StatusCode);

        var truncated = TruncatedJpegWithParseableHeader();
        var rejected = await client.PutAsync(
            $"/api/locations/{location.Id}/photo",
            PhotoContent(truncated, "image/jpeg", "truncated.jpg"));

        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        Assert.Contains("格式", await ReadBodyAsync(rejected));

        var served = await client.GetAsync($"/api/locations/{location.Id}/photo");
        Assert.Equal(HttpStatusCode.OK, served.StatusCode);
        Assert.Equal(original, await served.Content.ReadAsByteArrayAsync());

        var meta = await GetMetadataAsync(client, location.Id);
        Assert.True(meta!.HasPhoto);
    }

    [Fact]
    public async Task Upload_TruncatedImage_RejectedEvenWithJpegMagic()
    {
        using var factory = new LocationPhotoTestFactory();
        var client = factory.CreateAuthenticatedClient();
        var location = await SeedLocationAsync(factory, "抽屉");

        var truncated = JpegBytes().Take(10).ToArray();
        var response = await client.PutAsync(
            $"/api/locations/{location.Id}/photo",
            PhotoContent(truncated, "image/jpeg", "broken.jpg"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("格式", await ReadBodyAsync(response));
        Assert.False((await GetMetadataAsync(client, location.Id))!.HasPhoto);
    }

    [Fact]
    public async Task SameId_LocationAndItem_PhotosAreIsolated()
    {
        // 场景 1：删除位置不影响同 ID 物品照片。
        using (var factory = new LocationPhotoTestFactory())
        {
            var client = factory.CreateAuthenticatedClient();
            var location = await SeedLocationAsync(factory, "抽屉");
            var otherLocation = await SeedLocationAsync(factory, "另一个位置");
            var item = await SeedItemAsync(factory, otherLocation.Id);
            Assert.Equal(1, location.Id);
            Assert.Equal(1, item.Id);

            await client.PutAsync(
                $"/api/locations/{location.Id}/photo", PhotoContent(JpegBytes(64)));

            var itemImage = JpegBytes(48);
            var itemUpload = new MultipartFormDataContent();
            itemUpload.Add(new StringContent(item.Id.ToString()), "itemId");
            var itemFile = new ByteArrayContent(itemImage);
            itemFile.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
            itemUpload.Add(itemFile, "file", "item.jpg");
            Assert.Equal(HttpStatusCode.Accepted,
                (await client.PostAsync("/api/images/upload", itemUpload)).StatusCode);

            Assert.Equal(HttpStatusCode.NoContent,
                (await client.DeleteAsync($"/api/locations/{location.Id}")).StatusCode);

            var itemPhoto = await client.GetAsync($"/api/images/{item.Id}?type=original");
            Assert.Equal(HttpStatusCode.OK, itemPhoto.StatusCode);
            Assert.Equal(itemImage, await itemPhoto.Content.ReadAsByteArrayAsync());
        }

        // 场景 2：删除物品不影响同 ID 位置照片。
        using (var factory = new LocationPhotoTestFactory())
        {
            var client = factory.CreateAuthenticatedClient();
            var location = await SeedLocationAsync(factory, "抽屉");
            var otherLocation = await SeedLocationAsync(factory, "另一个位置");
            var item = await SeedItemAsync(factory, otherLocation.Id);
            Assert.Equal(1, location.Id);
            Assert.Equal(1, item.Id);

            var locationImage = JpegBytes(80);
            await client.PutAsync(
                $"/api/locations/{location.Id}/photo", PhotoContent(locationImage));

            Assert.Equal(HttpStatusCode.NoContent,
                (await client.DeleteAsync($"/api/items/{item.Id}")).StatusCode);

            var locationPhoto = await client.GetAsync($"/api/locations/{location.Id}/photo");
            Assert.Equal(HttpStatusCode.OK, locationPhoto.StatusCode);
            Assert.Equal(locationImage, await locationPhoto.Content.ReadAsByteArrayAsync());
        }
    }

    [Fact]
    public async Task GetPhoto_PointerButMissingFile_Returns404()
    {
        using var factory = new LocationPhotoTestFactory();
        var client = factory.CreateAuthenticatedClient();
        var location = await SeedLocationAsync(factory, "抽屉");

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var entity = await db.Locations.FindAsync(location.Id);
            entity!.PhotoFileName = "1-missing.jpg";
            await db.SaveChangesAsync();
        }

        var meta = await GetMetadataAsync(client, location.Id);
        Assert.True(meta!.HasPhoto);
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.GetAsync($"/api/locations/{location.Id}/photo")).StatusCode);
    }

    [Fact]
    public async Task RemovePhoto_CleanupFails_ReturnsWarning_PointerCleared()
    {
        var tempDirectory = Path.Combine(
            Path.GetTempPath(), $"boxwise-location-photo-{Guid.NewGuid():N}");
        var failingStore = new FailingDeleteLocationPhotoStore(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DataDirectory"] = tempDirectory
            })
            .Build());

        using var factory = new LocationPhotoTestFactory(
            failingStore, tempDirectory: tempDirectory);
        var client = factory.CreateAuthenticatedClient();
        var location = await SeedLocationAsync(factory, "抽屉");

        await client.PutAsync(
            $"/api/locations/{location.Id}/photo", PhotoContent(JpegBytes(64)));

        var removed = await client.DeleteAsync($"/api/locations/{location.Id}/photo");
        Assert.Equal(HttpStatusCode.OK, removed.StatusCode);

        var warning = await removed.Content.ReadFromJsonAsync<LocationCleanupWarning>();
        Assert.True(warning!.CleanupWarning);

        var meta = await GetMetadataAsync(client, location.Id);
        Assert.False(meta!.HasPhoto);
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.GetAsync($"/api/locations/{location.Id}/photo")).StatusCode);
    }

    [Fact]
    public async Task Create_WithDescription_ReturnsPersistedMetadata()
    {
        using var factory = new LocationPhotoTestFactory();
        var client = factory.CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync("/api/locations", new
        {
            name = "新抽屉",
            parentId = (int?)null,
            sortOrder = 0,
            description = "  创建时的描述\r\n第二行  "
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var dto = await response.Content.ReadFromJsonAsync<LocationDto>();
        Assert.NotNull(dto);
        Assert.Equal("创建时的描述\r\n第二行", dto!.Description);
        Assert.False(dto.HasPhoto);
        Assert.Equal("新抽屉", dto.NamePath);

        var tooLong = await client.PostAsJsonAsync("/api/locations", new
        {
            name = "超限位置",
            parentId = (int?)null,
            sortOrder = 1,
            description = string.Concat(Enumerable.Repeat("👨‍👩‍👧‍👦", 301))
        });
        Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);
    }

    [Fact]
    public async Task DescriptionAndPhoto_PersistAcrossHostRestart()
    {
        var tempDirectory = Path.Combine(
            Path.GetTempPath(), $"boxwise-location-photo-{Guid.NewGuid():N}");
        var bytes = JpegBytes(256);
        int locationId;

        using (var first = new LocationPhotoTestFactory(
            tempDirectory: tempDirectory, deleteOnDispose: false))
        {
            var client = first.CreateAuthenticatedClient();
            var location = await SeedLocationAsync(first, "抽屉", "重启前描述");
            locationId = location.Id;

            var upload = await client.PutAsync(
                $"/api/locations/{location.Id}/photo", PhotoContent(bytes));
            Assert.Equal(HttpStatusCode.OK, upload.StatusCode);
        }

        // 同一 SQLite 文件与照片目录，重新启动应用。
        using (var second = new LocationPhotoTestFactory(tempDirectory: tempDirectory))
        {
            var client = second.CreateAuthenticatedClient();

            var meta = await GetMetadataAsync(client, locationId);
            Assert.NotNull(meta);
            Assert.Equal("重启前描述", meta!.Description);
            Assert.True(meta.HasPhoto);

            var photo = await client.GetAsync($"/api/locations/{locationId}/photo");
            Assert.Equal(HttpStatusCode.OK, photo.StatusCode);
            Assert.Equal(bytes, await photo.Content.ReadAsByteArrayAsync());
        }
    }
}
