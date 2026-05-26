using Microsoft.AspNetCore.Http.HttpResults;
using BoxWise.Server.Services;
using BoxWise.Shared.Dtos;

namespace BoxWise.Server.Endpoints;

public static class AiEndpoints
{
    private const int MaxFileSize = 10 * 1024 * 1024; // 10MB
    private static readonly string[] AllowedTypes = ["image/jpeg", "image/jpg", "image/png", "image/webp"];
    private static readonly byte[] JpegMagic = [0xFF, 0xD8, 0xFF];
    private static readonly byte[] PngMagic = [0x89, 0x50, 0x4E, 0x47];
    private static readonly byte[] RiffMagic = [0x52, 0x49, 0x46, 0x46];  // RIFF container
    private static readonly byte[] WebpMagic = [0x57, 0x45, 0x42, 0x50];  // WEBP (offset 8)

    public static RouteGroupBuilder MapAiEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/ai");

        group.MapPost("/recognize", RecognizeAsync)
            .Produces<RecognitionResultDto>()
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(422)
            .WithTags("AI")
            .WithDescription("上传照片进行AI识别，返回物品名称和建议备注");

        return group;
    }

    private static async Task<Results<Ok<RecognitionResultDto>, ProblemHttpResult>>
        RecognizeAsync(HttpRequest request, LlmClient llmClient, CancellationToken cancellationToken)
    {
        if (!request.HasFormContentType)
            return TypedResults.Problem("请求必须是 multipart/form-data", statusCode: 400);

        var form = await request.ReadFormAsync(cancellationToken);
        var file = form.Files.GetFile("file");
        if (file is null || file.Length == 0)
            return TypedResults.Problem("未找到上传文件", statusCode: 400);

        if (!AllowedTypes.Contains(file.ContentType, StringComparer.OrdinalIgnoreCase))
            return TypedResults.Problem("仅支持 JPG、PNG、WebP 格式", statusCode: 400);

        if (file.Length > MaxFileSize)
            return TypedResults.Problem("文件大小不能超过 10MB", statusCode: 400);

        var tempPath = Path.Combine(Path.GetTempPath(), $"boxwise_ai_{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = file.OpenReadStream())
            {
                // 流式魔数验证：先读头部，验证通过后再写入文件
                var header = new byte[12];
                var headerLen = await stream.ReadAsync(header, 0, header.Length, cancellationToken);
                if (!IsValidMagic(header, headerLen))
                {
                    try { File.Delete(tempPath); } catch { }
                    return TypedResults.Problem("文件格式不支持，请上传有效的图片", statusCode: 400);
                }

                await using (var fs = File.Create(tempPath))
                {
                    await fs.WriteAsync(header.AsMemory(0, headerLen), cancellationToken);
                    await stream.CopyToAsync(fs, cancellationToken);
                }
            }

            var result = await llmClient.RecognizeAsync(tempPath, file.ContentType, cancellationToken);
            if (result is null)
                return TypedResults.Problem("AI 识别失败，请手动输入", statusCode: 422);

            return TypedResults.Ok(result);
        }
        catch (OperationCanceledException)
        {
            return TypedResults.Problem("请求已取消", statusCode: 400);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return TypedResults.Problem("AI 识别失败，请手动输入", statusCode: 422);
        }
        finally
        {
            try { File.Delete(tempPath); } catch { }
        }
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
