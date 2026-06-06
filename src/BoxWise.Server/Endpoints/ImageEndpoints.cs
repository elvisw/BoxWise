using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using BoxWise.Server.Data;
using BoxWise.Server.Services;
using BoxWise.Shared.Dtos;

namespace BoxWise.Server.Endpoints;

public static class ImageEndpoints
{
    private const int MaxFileSize = 10 * 1024 * 1024; // 10MB
    private static readonly string[] AllowedTypes = ["image/jpeg", "image/jpg", "image/png", "image/webp"];

    private static readonly byte[] JpegMagic = [0xFF, 0xD8, 0xFF];
    private static readonly byte[] PngMagic = [0x89, 0x50, 0x4E, 0x47];
    private static readonly byte[] RiffMagic = [0x52, 0x49, 0x46, 0x46];
    private static readonly byte[] WebpMagic = [0x57, 0x45, 0x42, 0x50];

    public static RouteGroupBuilder MapImageEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/images");

        group.MapPost("/upload", UploadAsync)
            .Produces<UploadResultDto>(202)
            .ProducesProblem(400)
            .ProducesProblem(401)
            .WithTags("Images")
            .WithDescription("上传物品照片");

        group.MapGet("/{itemId:int}", ServeAsync)
            .Produces(200)
            .Produces(404)
            .ProducesProblem(401)
            .WithTags("Images")
            .WithDescription("获取图片文件（type=thumb|medium|original）");

        return group;
    }

    private static async Task<Results<Accepted<UploadResultDto>, ProblemHttpResult>>
        UploadAsync(HttpRequest request, ImageStorageService storage,
            ThumbnailBackgroundService thumbnailBg, AppDbContext db)
    {
        if (!request.HasFormContentType)
            return TypedResults.Problem("请求必须是 multipart/form-data", statusCode: 400);

        var form = await request.ReadFormAsync();
        var file = form.Files.GetFile("file");
        if (file is null || file.Length == 0)
            return TypedResults.Problem("未找到上传文件", statusCode: 400);

        if (!AllowedTypes.Contains(file.ContentType, StringComparer.OrdinalIgnoreCase))
            return TypedResults.Problem("仅支持 JPG、PNG、WebP 格式", statusCode: 400);

        if (file.Length > MaxFileSize)
            return TypedResults.Problem("文件大小不能超过 10MB", statusCode: 400);

        if (!int.TryParse(form["itemId"], out var itemId) || itemId <= 0)
            return TypedResults.Problem("无效的 itemId", statusCode: 400);

        var itemExists = await db.Items.AnyAsync(i => i.Id == itemId);
        if (!itemExists)
            return TypedResults.Problem("物品不存在", statusCode: 400);

        await using var stream = file.OpenReadStream();

        // 文件魔数验证（JPEG FFD8FF / PNG 89504E47 / WebP RIFF....WEBP）
        var header = new byte[12];
        var headerLen = await stream.ReadAsync(header.AsMemory(0, 12), CancellationToken.None);
        if (!IsValidMagic(header, headerLen))
            return TypedResults.Problem("文件格式不支持，请上传有效的图片", statusCode: 400);
        stream.Position = 0;  // 回退流位置，确保 SaveOriginalAsync 写入完整文件

        await storage.SaveOriginalAsync(itemId, stream);

        thumbnailBg.TryEnqueue(itemId);

        var dto = new UploadResultDto(itemId, $"/api/images/{itemId}?type=original");
        return TypedResults.Accepted($"/api/images/{itemId}", dto);
    }

    private static async Task<Results<PhysicalFileHttpResult, NotFound, ProblemHttpResult>>
        ServeAsync(int itemId, string? type, ImageStorageService storage)
    {
        var filePath = type switch
        {
            "thumb" => storage.GetThumbPath(itemId),
            "medium" => storage.GetMediumPath(itemId),
            _ => storage.GetOriginalPath(itemId)
        };

        if (!File.Exists(filePath))
            return TypedResults.NotFound();

        return TypedResults.PhysicalFile(filePath, "image/jpeg");
    }

    private static bool IsValidMagic(byte[] header, int length)
    {
        if (length < 3) return false;

        // JPEG: FF D8 FF
        if (length >= 3 && header.AsSpan(0, 3).SequenceEqual(JpegMagic))
            return true;

        // PNG: 89 50 4E 47
        if (length >= 4 && header.AsSpan(0, 4).SequenceEqual(PngMagic))
            return true;

        // WebP: RIFF....WEBP
        if (length >= 12
            && header.AsSpan(0, 4).SequenceEqual(RiffMagic)
            && header.AsSpan(8, 4).SequenceEqual(WebpMagic))
            return true;

        return false;
    }
}
