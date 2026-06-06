using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using BoxWise.Server.Data;
using BoxWise.Server.Models;
using BoxWise.Shared.Dtos;

namespace BoxWise.Server.Endpoints;

public static class LlmConfigEndpoints
{
    private const int ConfigId = 1;

    public static RouteGroupBuilder MapLlmConfigEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/llm");

        group.MapGet("/config", GetLlmConfigAsync)
            .Produces<LlmConfigDto>(200)
            .ProducesProblem(401)
            .WithTags("LlmConfig")
            .WithDescription("获取 LLM API 配置");

        group.MapPut("/config", UpdateLlmConfigAsync)
            .Produces<LlmConfigDto>(200)
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(403)
            .WithTags("LlmConfig")
            .WithDescription("更新 LLM API 配置（管理员）");

        return group;
    }

    private static async Task<Results<Ok<LlmConfigDto>, UnauthorizedHttpResult>>
        GetLlmConfigAsync(HttpContext httpContext, AppDbContext db)
    {
        if (httpContext.User.Identity?.IsAuthenticated != true)
            return TypedResults.Unauthorized();

        var entity = await db.LlmConfigs.FindAsync(ConfigId);

        if (entity is not null)
            return TypedResults.Ok(new LlmConfigDto(
                entity.BaseUrl,
                entity.ApiKey,
                entity.Model ?? "doubao-seed-2-0-pro-260215",
                entity.TimeoutSeconds));

        return TypedResults.Ok(new LlmConfigDto(
            null,
            null,
            "doubao-seed-2-0-pro-260215",
            30));
    }

    private static async Task<Results<Ok<LlmConfigDto>, ProblemHttpResult, UnauthorizedHttpResult, ForbidHttpResult>>
        UpdateLlmConfigAsync(HttpContext httpContext, AppDbContext db,
            UserManager<AppUser> userManager, LlmConfigDto request)
    {
        var caller = await userManager.GetUserAsync(httpContext.User);
        if (caller is null)
            return TypedResults.Unauthorized();
        if (!await userManager.IsInRoleAsync(caller, "Admin"))
            return TypedResults.Forbid();

        if (string.IsNullOrWhiteSpace(request.BaseUrl))
            return TypedResults.Problem("BaseUrl 不能为空", statusCode: 400);

        var model = string.IsNullOrWhiteSpace(request.Model) ? "doubao-seed-2-0-pro-260215" : request.Model.Trim();
        var timeoutSeconds = Math.Clamp(request.TimeoutSeconds, 5, 120);

        try
        {
            var entity = await db.LlmConfigs.FindAsync(ConfigId);
            if (entity is null)
            {
                entity = new LlmConfig { Id = ConfigId };
                db.LlmConfigs.Add(entity);
            }

            entity.BaseUrl = request.BaseUrl.Trim();
            if (!string.IsNullOrWhiteSpace(request.ApiKey))
                entity.ApiKey = request.ApiKey.Trim();
            entity.Model = model;
            entity.TimeoutSeconds = timeoutSeconds;

            await db.SaveChangesAsync();

            return TypedResults.Ok(new LlmConfigDto(
                entity.BaseUrl,
                entity.ApiKey,
                entity.Model,
                entity.TimeoutSeconds));
        }
        catch (DbUpdateException)
        {
            // Retry: PK conflict means another request created the record
            var entity = await db.LlmConfigs.FindAsync(ConfigId);
            if (entity is null)
                return TypedResults.Problem("配置创建失败，请重试", statusCode: 400);

            entity.BaseUrl = request.BaseUrl.Trim();
            if (!string.IsNullOrWhiteSpace(request.ApiKey))
                entity.ApiKey = request.ApiKey.Trim();
            entity.Model = model;
            entity.TimeoutSeconds = timeoutSeconds;

            await db.SaveChangesAsync();
            return TypedResults.Ok(new LlmConfigDto(
                entity.BaseUrl,
                entity.ApiKey,
                entity.Model,
                entity.TimeoutSeconds));
        }
    }
}
