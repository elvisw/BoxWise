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
                item.LocationId,
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
}
