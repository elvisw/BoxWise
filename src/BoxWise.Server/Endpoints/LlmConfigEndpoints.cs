using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using BoxWise.Server.Data;
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
}
