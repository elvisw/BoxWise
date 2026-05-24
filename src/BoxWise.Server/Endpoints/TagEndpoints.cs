using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using BoxWise.Server.Repositories;
using BoxWise.Shared.Dtos;

namespace BoxWise.Server.Endpoints;

public static class TagEndpoints
{
    public static RouteGroupBuilder MapTagEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/tags");

        group.MapGet("/", GetAllTagsAsync)
            .Produces<List<TagDto>>(200)
            .ProducesProblem(401)
            .WithTags("Tags")
            .WithDescription("获取所有标签");

        group.MapPost("/", CreateTagAsync)
            .Produces<TagDto>(201)
            .ProducesProblem(400)
            .ProducesProblem(401)
            .WithTags("Tags")
            .WithDescription("创建标签");

        return group;
    }

    private static async Task<Ok<List<TagDto>>>
        GetAllTagsAsync(TagRepository repo)
    {
        var tags = await repo.GetAllAsync();
        var dtos = tags.Select(t => new TagDto(t.Id, t.Name)).ToList();
        return TypedResults.Ok(dtos);
    }

    private static async Task<Results<Created<TagDto>, ProblemHttpResult>>
        CreateTagAsync(CreateTagRequest request, TagRepository repo)
    {
        try
        {
            var tag = await repo.CreateAsync(request.Name);
            var dto = new TagDto(tag.Id, tag.Name);
            return TypedResults.Created($"/api/tags/{dto.Id}", dto);
        }
        catch (ArgumentException ex)
        {
            return TypedResults.Problem(ex.Message, statusCode: 400);
        }
        catch (DbUpdateException)
        {
            return TypedResults.Problem("该标签名称已存在", statusCode: 400);
        }
    }
}
