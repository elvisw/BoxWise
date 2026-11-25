using System.Runtime.InteropServices;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Net.Http.Headers;
using BoxWise.Server.Data;
using BoxWise.Server.Models;
using BoxWise.Server.Repositories;
using BoxWise.Server.Services;
using BoxWise.Shared.Dtos;
using BoxWise.Shared.Validation;
using SkiaSharp;

namespace BoxWise.Server.Endpoints;

public static class LocationPhotoEndpoints
{
    public static RouteGroupBuilder MapLocationPhotoEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/locations/{id:int}/photo")
            .RequireAuthorization();

        group.MapGet("", GetPhotoAsync)
            .Produces(200)
            .Produces(404)
            .ProducesProblem(401)
            .WithTags("Locations")
            .WithDescription("按需获取位置照片；仅在数据库指针已提交时读取文件");

        group.MapPut("", UploadPhotoAsync)
            .Produces<LocationDto>(200)
            .ProducesProblem(400)
            .ProducesProblem(401)
            .Produces(404)
            .ProducesProblem(413)
            .WithTags("Locations")
            .WithDescription("上传或替换位置照片（multipart/form-data，字段名 file）")
            .AddEndpointFilter<CsrfValidationFilter>();

        group.MapDelete("", RemovePhotoAsync)
            .Produces(204)
            .Produces<LocationCleanupWarning>(200)
            .ProducesProblem(400)
            .ProducesProblem(401)
            .Produces(404)
            .WithTags("Locations")
            .WithDescription("移除位置照片；文件清理失败时返回清理警告")
            .AddEndpointFilter<CsrfValidationFilter>();

        return group;
    }

    private static async Task<Results<PhysicalFileHttpResult, NotFound>>
        GetPhotoAsync(int id, HttpContext httpContext, AppDbContext db, LocationPhotoStore store)
    {
        var fileName = await db.Locations
            .AsNoTracking()
            .Where(l => l.Id == id)
            .Select(l => l.PhotoFileName)
            .FirstOrDefaultAsync();

        if (string.IsNullOrEmpty(fileName))
            return TypedResults.NotFound();

        var file = store.OpenVerified(fileName);
        if (file is null)
            return TypedResults.NotFound();

        httpContext.Response.Headers.CacheControl = "no-store";
        return TypedResults.PhysicalFile(file.Value.Path, file.Value.ContentType);
    }

    private static async Task<Results<Ok<LocationDto>, NotFound, ProblemHttpResult>>
        UploadPhotoAsync(
            int id,
            HttpContext httpContext,
            AppDbContext db,
            LocationRepository repo,
            LocationPhotoStore store,
            IServiceScopeFactory scopeFactory,
            CancellationToken cancellationToken)
    {
        // 位置照片端点自己的请求边界：10 MiB 文件 + multipart 开销。
        // 不修改全局 FormOptions，物品上传仍保持原有 10 MiB 表单限制。
        var sizeFeature = httpContext.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (sizeFeature is { IsReadOnly: false })
            sizeFeature.MaxRequestBodySize =
                LocationPhotoRules.MaxBytes + LocationPhotoRules.MultipartOverheadBytes;

        var read = await ReadPhotoPartAsync(httpContext.Request, cancellationToken);
        if (read.Error is not null)
            return read.Error;

        var bytes = read.Bytes!;
        var detectedType = read.ContentType!;
        var extension = LocationPhotoRules.GetExtension(detectedType)!;

        // 照片写入与位置删除共用同一把锁，避免删除后又被并发写回。
        await using var lease = await store.LockAsync(cancellationToken);

        var location = await db.Locations.FirstOrDefaultAsync(l => l.Id == id, cancellationToken);
        if (location is null)
            return TypedResults.NotFound();

        await using var stream = new MemoryStream(bytes, writable: false);
        var stagedName = await store.StageAsync(stream, extension, cancellationToken);
        var finalName = LocationPhotoStore.CreateFileName(id, extension);

        try
        {
            // 先发布文件，再提交数据库指针。
            store.Publish(stagedName, finalName);
        }
        catch
        {
            store.TryDeleteStaged(stagedName);
            throw;
        }

        var previousFileName = location.PhotoFileName;
        location.PhotoFileName = finalName;

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            // 无法从失败响应本身判断事务是否已提交。用新的 scope/连接、且不依赖
            // 已取消的请求 token 读取已提交指针；只有确认当前不引用新文件时才清理。
            var reference = await CheckPhotoReferenceAsync(
                scopeFactory, id, finalName, CancellationToken.None);

            switch (reference)
            {
                case PhotoReference.Referenced:
                    // 已确认新指针提交；旧文件才可视为未引用。
                    if (!string.IsNullOrEmpty(previousFileName) && previousFileName != finalName)
                        store.TryDelete(previousFileName);
                    break;

                case PhotoReference.NotReferenced:
                    // 已确认新指针未提交；只清理新文件，旧指针/旧文件保持。
                    store.TryDelete(finalName);
                    break;

                default:
                    // 无法确认时同时保留新旧文件，避免误删可能已提交的照片。
                    break;
            }

            // 不把未知结果伪装成回滚；由客户端按 Unknown 处理并重新核对。
            throw;
        }

        if (!string.IsNullOrEmpty(previousFileName) && previousFileName != finalName)
            store.TryDelete(previousFileName);

        var namePath = await repo.ResolveDisplayPathAsync(location.Path);
        return TypedResults.Ok(ToDto(location, namePath));
    }

    private static async Task<Results<NoContent, Ok<LocationCleanupWarning>, NotFound>>
        RemovePhotoAsync(
            int id,
            AppDbContext db,
            LocationPhotoStore store,
            IServiceScopeFactory scopeFactory,
            CancellationToken cancellationToken)
    {
        await using var lease = await store.LockAsync(cancellationToken);

        var location = await db.Locations.FirstOrDefaultAsync(l => l.Id == id, cancellationToken);
        if (location is null)
            return TypedResults.NotFound();

        var fileName = location.PhotoFileName;
        if (string.IsNullOrEmpty(fileName))
            return TypedResults.NoContent();

        location.PhotoFileName = null;

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            // 与上传对称：只有确认真实未引用时才清理文件，否则保留并让客户端重新核对。
            var reference = await CheckPhotoReferenceAsync(
                scopeFactory, id, fileName, CancellationToken.None);
            if (reference == PhotoReference.NotReferenced)
                store.TryDelete(fileName);

            throw;
        }

        try
        {
            store.Delete(fileName);
        }
        catch (IOException)
        {
            return TypedResults.Ok(new LocationCleanupWarning(true));
        }
        catch (UnauthorizedAccessException)
        {
            return TypedResults.Ok(new LocationCleanupWarning(true));
        }
        catch (ArgumentException)
        {
            return TypedResults.Ok(new LocationCleanupWarning(true));
        }

        return TypedResults.NoContent();
    }

    private sealed record PhotoReadResult(
        byte[]? Bytes, string? ContentType, ProblemHttpResult? Error);

    private static async Task<PhotoReadResult> ReadPhotoPartAsync(
        HttpRequest request, CancellationToken cancellationToken)
    {
        if (!MediaTypeHeaderValue.TryParse(request.ContentType, out var mediaType)
            || !string.Equals(mediaType.MediaType.Value, "multipart/form-data",
                StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrEmpty(mediaType.Boundary.Value))
        {
            return Error("请求必须是 multipart/form-data", 400);
        }

        var reader = new MultipartReader(mediaType.Boundary.Value, request.Body)
        {
            HeadersLengthLimit = 16 * 1024,
            HeadersCountLimit = 16
        };

        try
        {
            var section = await reader.ReadNextSectionAsync(cancellationToken);
            if (section is null)
                return Error("未找到上传文件", 400);

            if (!ContentDispositionHeaderValue.TryParse(
                    section.ContentDisposition, out var disposition)
                || !string.Equals(disposition.DispositionType.Value, "form-data",
                    StringComparison.OrdinalIgnoreCase)
                || !string.Equals(disposition.Name.Value, "file",
                    StringComparison.OrdinalIgnoreCase)
                || !disposition.FileName.HasValue)
            {
                return Error("请求必须只包含一个名为 file 的照片文件", 400);
            }

            if (MediaTypeHeaderValue.TryParse(section.ContentType, out var declaredType)
                && !LocationPhotoRules.IsAllowedContentType(declaredType.MediaType.Value))
            {
                return Error(LocationPhotoRules.UnsupportedFormatMessage, 400);
            }

            using var buffer = new MemoryStream();
            var chunk = new byte[81920];
            var total = 0;

            while (true)
            {
                var read = await section.Body.ReadAsync(chunk, cancellationToken);
                if (read == 0) break;

                total += read;
                if (total > LocationPhotoRules.MaxBytes)
                    return Error(LocationPhotoRules.TooLargeMessage, 400);

                await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken);
            }

            if (total == 0)
                return Error("未找到上传文件", 400);

            // 只接受一份 file；额外的表单字段或第二个文件都拒绝。
            var extra = await reader.ReadNextSectionAsync(cancellationToken);
            if (extra is not null)
                return Error("请求必须只包含一个名为 file 的照片文件", 400);

            var bytes = buffer.ToArray();
            var validation = await DetectDecodableImageAsync(bytes, cancellationToken);
            if (validation.ContentType is null)
                return Error(
                    validation.Error ?? LocationPhotoRules.UnsupportedFormatMessage, 400);

            return new PhotoReadResult(bytes, validation.ContentType, null);
        }
        catch (BadHttpRequestException ex) when (ex.StatusCode == 413)
        {
            return Error(LocationPhotoRules.TooLargeMessage, 413);
        }
        catch (BadHttpRequestException)
        {
            return Error("无效的 multipart 请求", 400);
        }
        catch (InvalidDataException)
        {
            return Error("无效的 multipart 请求", 400);
        }
        catch (IOException)
        {
            return Error("读取上传文件失败", 400);
        }
    }

    private const int MaxDecodeDimension = 512;

    // 完整像素解码的安全边界。常见 12MP/27MP 照片和 48MP JPEG 可覆盖；
    // 超过边界的 PNG 按“尺寸过大”拒绝，避免压缩图片触发放大后的内存峰值。
    private const long MaxScanlineOrScaledPixels = 64_000_000;
    private const long MaxFullDecodePixels = 32_000_000;
    private const int MaxFullDecodeBytes = (int)(MaxFullDecodePixels * 4);
    private const int MaxScanlineRowBytes = 256 * 1024;
    private const int MaxScanlineRows = 65_536;
    private const int ScanlineRowsPerChunk = 64;
    private const string ImageDimensionsTooLargeMessage =
        "图片尺寸过大，无法安全处理，请换一张照片。";

    // PNG 等不支持缩放/子区域/scanline 的 codec 必须完整解码；全进程串行，
    // 保证同一时刻最多持有一块 <=128 MiB 的完整像素缓冲。
    private static readonly SemaphoreSlim FullDecodeGate = new(1, 1);

    private sealed record ImageValidationResult(string? ContentType, string? Error)
    {
        public static ImageValidationResult Invalid { get; } = new(null, null);

        public static ImageValidationResult TooLarge { get; } =
            new(null, ImageDimensionsTooLargeMessage);
    }

    /// <summary>
    /// 实际解码像素数据，确保不仅是文件头合法。仅接受 JPG/PNG/WebP。
    /// JPEG 用逐行全图扫描；WebP 用 512px 缩放解码；
    /// PNG 等其余格式使用受像素数与并发限制的完整解码。
    /// </summary>
    private static async Task<ImageValidationResult> DetectDecodableImageAsync(
        byte[] bytes, CancellationToken cancellationToken)
    {
        try
        {
            using var data = SKData.CreateCopy(bytes);
            using var codec = SKCodec.Create(data);
            if (codec is null || codec.Info.Width <= 0 || codec.Info.Height <= 0)
                return ImageValidationResult.Invalid;

            var contentType = codec.EncodedFormat switch
            {
                SKEncodedImageFormat.Jpeg => "image/jpeg",
                SKEncodedImageFormat.Png => "image/png",
                SKEncodedImageFormat.Webp => "image/webp",
                _ => null
            };

            if (contentType is null)
                return ImageValidationResult.Invalid;

            var pixelCount = (long)codec.Info.Width * codec.Info.Height;
            if (pixelCount > MaxScanlineOrScaledPixels)
                return ImageValidationResult.TooLarge;

            // JPEG 的 ScanlineDecode 只保留一小组行的缓冲，可完整校验像素数据。
            if (codec.EncodedFormat == SKEncodedImageFormat.Jpeg
                && TryDecodeJpegScanlines(bytes))
            {
                return new ImageValidationResult(contentType, null);
            }

            // WebP 支持 SKBitmap 的缩放解码；缩放仍会走完整像素数据。
            if (codec.EncodedFormat == SKEncodedImageFormat.Webp
                && TryDecodeScaledBitmap(bytes))
            {
                return new ImageValidationResult(contentType, null);
            }

            // 不支持缩放/scanline 的 codec（典型为 PNG）只能完整解码；
            // 此处限制峰值内存并串行化。
            if (pixelCount > MaxFullDecodePixels)
                return ImageValidationResult.TooLarge;

            await FullDecodeGate.WaitAsync(cancellationToken);
            try
            {
                return TryDecodeFull(bytes)
                    ? new ImageValidationResult(contentType, null)
                    : ImageValidationResult.Invalid;
            }
            finally
            {
                FullDecodeGate.Release();
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return ImageValidationResult.Invalid;
        }
    }

    private static bool TryDecodeJpegScanlines(byte[] bytes)
    {
        using var data = SKData.CreateCopy(bytes);
        using var codec = SKCodec.Create(data);
        if (codec is null)
            return false;

        var info = new SKImageInfo(
            codec.Info.Width, codec.Info.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
        var rowBytes = info.Width * 4;
        if (rowBytes <= 0
            || rowBytes > MaxScanlineRowBytes
            || info.Height > MaxScanlineRows)
        {
            return false;
        }

        if (codec.StartScanlineDecode(info) != SKCodecResult.Success)
            return false;

        var rowsPerChunk = Math.Min(ScanlineRowsPerChunk, info.Height);
        var buffer = Marshal.AllocHGlobal(rowBytes * rowsPerChunk);
        try
        {
            var decodedRows = 0;
            while (decodedRows < info.Height)
            {
                var requested = Math.Min(rowsPerChunk, info.Height - decodedRows);
                var decoded = codec.GetScanlines(buffer, requested, rowBytes);

                // 少于请求行数意味着输入不完整；默认填充不能当作成功。
                if (decoded != requested)
                    return false;

                decodedRows += decoded;
            }

            return decodedRows == info.Height;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static bool TryDecodeScaledBitmap(byte[] bytes)
    {
        using var data = SKData.CreateCopy(bytes);
        using var codec = SKCodec.Create(data);
        if (codec is null)
            return false;

        var source = codec.Info;
        var scale = Math.Min(
            1f,
            (float)MaxDecodeDimension / Math.Max(source.Width, source.Height));
        if (scale >= 1f)
            return false;

        var target = new SKImageInfo(
            Math.Max(1, (int)Math.Round(source.Width * scale)),
            Math.Max(1, (int)Math.Round(source.Height * scale)),
            SKColorType.Bgra8888,
            SKAlphaType.Premul);

        using var bitmap = SKBitmap.Decode(codec, target);
        return bitmap is not null && bitmap.Width > 0 && bitmap.Height > 0;
    }

    private static bool TryDecodeFull(byte[] bytes)
    {
        using var data = SKData.CreateCopy(bytes);
        using var codec = SKCodec.Create(data);
        if (codec is null)
            return false;

        var info = new SKImageInfo(
            codec.Info.Width, codec.Info.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
        if (info.BytesSize <= 0 || info.BytesSize > MaxFullDecodeBytes)
            return false;

        var pixels = GC.AllocateUninitializedArray<byte>(info.BytesSize);
        return codec.GetPixels(info, pixels) == SKCodecResult.Success;
    }

    private enum PhotoReference
    {
        Unknown,
        Referenced,
        NotReferenced
    }

    private static async Task<PhotoReference> CheckPhotoReferenceAsync(
        IServiceScopeFactory scopeFactory,
        int locationId,
        string fileName,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var checkDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var current = await checkDb.Locations
                .AsNoTracking()
                .Where(l => l.Id == locationId)
                .Select(l => l.PhotoFileName)
                .FirstOrDefaultAsync(cancellationToken);

            return string.Equals(current, fileName, StringComparison.Ordinal)
                ? PhotoReference.Referenced
                : PhotoReference.NotReferenced;
        }
        catch (Exception)
        {
            // 无法确认是否被引用时保留新旧文件，避免误删可能已提交的数据。
            return PhotoReference.Unknown;
        }
    }

    private static PhotoReadResult Error(string message, int statusCode) =>
        new(null, null, TypedResults.Problem(message, statusCode: statusCode));

    private static LocationDto ToDto(Location location, string? namePath) =>
        new(location.Id, location.Name, location.Path, location.ParentId, location.SortOrder,
            location.Description, !string.IsNullOrEmpty(location.PhotoFileName), namePath);
}
