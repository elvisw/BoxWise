using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using BoxWise.Server.Models;
using BoxWise.Server.Repositories;
using BoxWise.Shared.Dtos;

namespace BoxWise.Server.Endpoints;

public static class ItemEndpoints
{
    public static RouteGroupBuilder MapItemEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/items");

        group.MapPost("/", CreateItemAsync)
            .Produces<ItemDto>(201)
            .ProducesProblem(400)
            .ProducesProblem(401)
            .WithTags("Items")
            .WithDescription("创建物品");

        group.MapGet("/{id:int}", GetItemByIdAsync)
            .Produces<ItemDto>(200)
            .Produces(404)
            .ProducesProblem(401)
            .WithTags("Items")
            .WithDescription("获取物品详情");

        group.MapGet("/", SearchItemsAsync)
            .Produces<ItemSummaryDto[]>(200)
            .ProducesProblem(401)
            .WithTags("Items")
            .WithDescription("搜索物品（关键词模糊匹配名称/备注/标签）");

        return group;
    }

    private static async Task<Results<Created<ItemDto>, ProblemHttpResult>>
        CreateItemAsync(CreateItemRequest request, ItemRepository repo,
            UserManager<AppUser> userManager, HttpContext httpContext)
    {
        try
        {
            var userId = userManager.GetUserId(httpContext.User);
            if (string.IsNullOrEmpty(userId))
                throw new InvalidOperationException("无法获取当前用户");

            var item = await repo.CreateAsync(request.Name, request.LocationId, request.TagIds, request.Note, userId);

            var dto = new ItemDto(
                item.Id, item.Name, item.Note,
                item.PhotoPath, item.ThumbPath, item.MediumPath,
                item.LocationId, null,
                item.CreatedByUser?.UserName ?? "",
                item.CreatedAt);

            return TypedResults.Created($"/api/items/{dto.Id}", dto);
        }
        catch (ArgumentException ex)
        {
            return TypedResults.Problem(ex.Message, statusCode: 400);
        }
        catch (InvalidOperationException ex)
        {
            return TypedResults.Problem(ex.Message, statusCode: 400);
        }
    }

    private static async Task<Results<Ok<ItemDto>, NotFound>>
        GetItemByIdAsync(int id, ItemRepository repo)
    {
        var item = await repo.GetByIdAsync(id);
        if (item is null) return TypedResults.NotFound();

        var dto = new ItemDto(
            item.Id, item.Name, item.Note,
            item.PhotoPath, item.ThumbPath, item.MediumPath,
            item.LocationId, item.Location?.Name,
            item.CreatedByUser?.UserName ?? "",
            item.CreatedAt);

        return TypedResults.Ok(dto);
    }

    private static async Task<Ok<ItemSummaryDto[]>>
        SearchItemsAsync(string? q, ItemRepository repo, HttpContext httpContext)
    {
        var items = string.IsNullOrWhiteSpace(q)
            ? []
            : await repo.SearchAsync(q);

        var dtos = items.Select(i => new ItemSummaryDto(
            i.Id, i.Name, i.ThumbPath,
            i.Location?.Path,
            i.Tags.Select(t => t.Name).ToList(),
            i.CreatedAt)).ToArray();

        httpContext.Response.Headers["X-Total-Count"] = dtos.Length.ToString();
        return TypedResults.Ok(dtos);
    }
}
