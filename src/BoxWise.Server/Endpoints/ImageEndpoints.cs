using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using BoxWise.Server.Data;
using BoxWise.Server.Services;
using BoxWise.Shared.Dtos;

namespace BoxWise.Server.Endpoints;

public static class ImageEndpoints
{
    private const int MaxFileSize = 10 * 1024 * 1024; // 10MB
    private static readonly string[] AllowedTypes = ["image/jpeg", "image/png", "image/webp"];

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

        if (!AllowedTypes.Contains(file.ContentType))
            return TypedResults.Problem("仅支持 JPG、PNG、WebP 格式", statusCode: 400);

        if (file.Length > MaxFileSize)
            return TypedResults.Problem("文件大小不能超过 10MB", statusCode: 400);

        if (!int.TryParse(form["itemId"], out var itemId) || itemId <= 0)
            return TypedResults.Problem("无效的 itemId", statusCode: 400);

        var itemExists = await db.Items.AnyAsync(i => i.Id == itemId);
        if (!itemExists)
            return TypedResults.Problem("物品不存在", statusCode: 400);

        await using var stream = file.OpenReadStream();
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
}
