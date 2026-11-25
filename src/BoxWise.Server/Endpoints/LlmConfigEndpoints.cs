using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using BoxWise.Server.Data;
using BoxWise.Server.Models;
using BoxWise.Server.Services;
using BoxWise.Shared.Dtos;

namespace BoxWise.Server.Endpoints;

public static class LlmConfigEndpoints
{
    private const int ConfigId = LlmConfigWriter.ConfigId;

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
            .ProducesProblem(409)
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

        var result = await LlmConfigWriter.SaveAsync(
            db,
            new LlmConfigUpdate(request.BaseUrl, request.ApiKey, request.Model, request.TimeoutSeconds));

        if (result.Status == LlmConfigWriteStatus.Saved)
        {
            var saved = result.Config!;
            return TypedResults.Ok(new LlmConfigDto(
                saved.BaseUrl,
                saved.ApiKey,
                saved.Model,
                saved.TimeoutSeconds));
        }

        if (result.Status == LlmConfigWriteStatus.KeyRequired)
            return TypedResults.Problem(LlmConfigWriter.KeyRequiredMessage, statusCode: 400);

        return TypedResults.Problem(LlmConfigWriter.ConflictMessage, statusCode: 409);
    }
}
