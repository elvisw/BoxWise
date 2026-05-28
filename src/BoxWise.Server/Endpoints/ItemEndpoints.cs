using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using BoxWise.Server.Models;
using BoxWise.Server.Repositories;
using BoxWise.Server.Services;
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
            .WithDescription("搜索/筛选/浏览物品（可选参数 q/locationId/tagId）");

        group.MapDelete("/{id:int}", DeleteItemAsync)
            .Produces(204)
            .Produces(404)
            .ProducesProblem(401)
            .WithTags("Items")
            .WithDescription("删除物品（级联删除图片文件）");

        return group;
    }

    private static async Task<Results<Created<ItemDto>, ProblemHttpResult>>
        CreateItemAsync(CreateItemRequest request, ItemRepository repo,
            UserManager<AppUser> userManager, HttpContext httpContext,
            LocationRepository locationRepo)
    {
        try
        {
            var userId = userManager.GetUserId(httpContext.User);
            if (string.IsNullOrEmpty(userId))
                throw new InvalidOperationException("无法获取当前用户");

            var item = await repo.CreateAsync(request.Name, request.LocationId, request.TagIds, request.Note, userId);

            var locationPath = item.Location?.Path is not null
                ? await locationRepo.ResolvePathNamesAsync(item.Location.Path)
                : null;

            var dto = new ItemDto(
                item.Id, item.Name, item.Note,
                item.PhotoPath, item.ThumbPath, item.MediumPath,
                item.LocationId, item.Location?.Name, locationPath,
                item.Tags.Select(t => t.Name).ToList(),
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
        GetItemByIdAsync(int id, ItemRepository repo, LocationRepository locationRepo)
    {
        var item = await repo.GetByIdAsync(id);
        if (item is null) return TypedResults.NotFound();

        var locationPath = item.Location?.Path is not null
            ? await locationRepo.ResolvePathNamesAsync(item.Location.Path)
            : null;

        var dto = new ItemDto(
            item.Id, item.Name, item.Note,
            item.PhotoPath, item.ThumbPath, item.MediumPath,
            item.LocationId, item.Location?.Name, locationPath,
            item.Tags.Select(t => t.Name).ToList(),
            item.CreatedByUser?.UserName ?? "",
            item.CreatedAt);

        return TypedResults.Ok(dto);
    }

    private static async Task<Ok<ItemSummaryDto[]>>
        SearchItemsAsync(string? q, int? locationId, [FromQuery] string?[]? tagId,
            ItemRepository repo, LocationRepository locationRepo,
            HttpContext httpContext)
    {
        var tagIds = tagId is { Length: > 0 }
            ? tagId.Where(s => s is not null && int.TryParse(s, out _)).Select(s => int.Parse(s!)).ToList()
            : null;

        var items = await repo.GetFilteredAsync(locationId, tagIds, q);

        var paths = items.Select(i => i.Location?.Path).ToList();
        var pathDict = await locationRepo.ResolvePathNamesBatchAsync(paths);

        var dtos = items.Select(i =>
        {
            var namePath = i.Location?.Path is not null
                && pathDict.TryGetValue(i.Location.Path, out var n)
                ? n : null;
            return new ItemSummaryDto(
                i.Id, i.Name, i.ThumbPath,
                namePath,
                i.Tags.Select(t => t.Name).ToList(),
                i.CreatedAt);
        }).ToArray();

        httpContext.Response.Headers["X-Total-Count"] = dtos.Length.ToString();
        return TypedResults.Ok(dtos);
    }

    private static async Task<Results<NoContent, NotFound>>
        DeleteItemAsync(int id, ItemRepository repo, ImageStorageService imageStorage)
    {
        var deleted = await repo.DeleteAsync(id);
        if (!deleted) return TypedResults.NotFound();

        try { imageStorage.DeleteItemFiles(id); }
        catch { /* I/O 失败不阻止 DB 删除 */ }

        return TypedResults.NoContent();
    }
}
