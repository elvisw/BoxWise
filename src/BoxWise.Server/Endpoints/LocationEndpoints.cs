using System.Text.Json;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using BoxWise.Server.Models;
using BoxWise.Server.Repositories;
using BoxWise.Server.Services;
using BoxWise.Shared.Dtos;

namespace BoxWise.Server.Endpoints;

public static class LocationEndpoints
{
    public static RouteGroupBuilder MapLocationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/locations");

        group.MapGet("/", GetAllLocationsAsync)
            .Produces<List<LocationDto>>(200)
            .ProducesProblem(401)
            .WithTags("Locations")
            .WithDescription("获取所有位置列表");

        group.MapGet("/{id:int}", GetLocationByIdAsync)
            .Produces<LocationDto>(200)
            .Produces(404)
            .ProducesProblem(401)
            .WithTags("Locations")
            .WithDescription("获取单个位置元数据（含描述、照片状态和完整名称路径）");

        group.MapPost("/", CreateLocationAsync)
            .Produces<LocationDto>(201)
            .ProducesProblem(400)
            .ProducesProblem(401)
            .WithTags("Locations")
            .WithDescription("创建位置节点");

        group.MapPut("/{id:int}", UpdateLocationAsync)
            .Produces<LocationDto>(200)
            .ProducesProblem(400)
            .ProducesProblem(401)
            .Produces(404)
            .WithTags("Locations")
            .WithDescription("更新位置名称和描述");

        group.MapDelete("/{id:int}", DeleteLocationAsync)
            .Produces(204)
            .Produces<LocationCleanupWarning>(200)
            .ProducesProblem(400)
            .ProducesProblem(401)
            .Produces(404)
            .WithTags("Locations")
            .WithDescription("删除空位置；位置照片清理失败时返回清理警告");

        group.MapGet("/{id:int}/children", GetChildrenAsync)
            .Produces<List<LocationDto>>(200)
            .ProducesProblem(401)
            .Produces(404)
            .WithTags("Locations")
            .WithDescription("获取直接子节点");

        return group;
    }

    private static async Task<Results<Created<LocationDto>, ProblemHttpResult>>
        CreateLocationAsync(CreateLocationRequest request, LocationRepository repo)
    {
        try
        {
            var location = await repo.CreateAsync(
                request.Name, request.ParentId, request.SortOrder, request.Description);
            var namePath = await repo.ResolveDisplayPathAsync(location.Path);
            var dto = ToDto(location, namePath);
            return TypedResults.Created($"/api/locations/{dto.Id}", dto);
        }
        catch (ArgumentException ex)
        {
            return TypedResults.Problem(ex.Message, statusCode: 400);
        }
    }

    private static async Task<Results<Ok<LocationDto>, NotFound, ProblemHttpResult>>
        UpdateLocationAsync(int id, UpdateLocationRequest request, LocationRepository repo)
    {
        try
        {
            var descriptionProvided = request.Description.ValueKind != JsonValueKind.Undefined;
            var description = request.Description.ValueKind switch
            {
                JsonValueKind.Undefined or JsonValueKind.Null => (string?)null,
                JsonValueKind.String => request.Description.GetString(),
                _ => throw new ArgumentException("描述格式无效")
            };

            var location = await repo.UpdateAsync(id, request.Name, description, descriptionProvided);
            var namePath = await repo.ResolveDisplayPathAsync(location.Path);
            return TypedResults.Ok(ToDto(location, namePath));
        }
        catch (KeyNotFoundException)
        {
            return TypedResults.NotFound();
        }
        catch (ArgumentException ex)
        {
            return TypedResults.Problem(ex.Message, statusCode: 400);
        }
    }

    private static async Task<Results<NoContent, Ok<LocationCleanupWarning>, NotFound, ProblemHttpResult>>
        DeleteLocationAsync(int id, LocationRepository repo, LocationPhotoStore photoStore)
    {
        await using var lease = await photoStore.LockAsync();
        try
        {
            var location = await repo.DeleteAsync(id);

            if (!string.IsNullOrEmpty(location.PhotoFileName))
            {
                try
                {
                    photoStore.Delete(location.PhotoFileName);
                }
                catch (IOException)
                {
                    return TypedResults.Ok(new LocationCleanupWarning(true));
                }
                catch (UnauthorizedAccessException)
                {
                    return TypedResults.Ok(new LocationCleanupWarning(true));
                }
                catch (ArgumentException)
                {
                    return TypedResults.Ok(new LocationCleanupWarning(true));
                }
            }

            return TypedResults.NoContent();
        }
        catch (KeyNotFoundException)
        {
            return TypedResults.NotFound();
        }
        catch (InvalidOperationException ex)
        {
            return TypedResults.Problem(ex.Message, statusCode: 400);
        }
        catch (DbUpdateException)
        {
            return TypedResults.Problem("无法删除：该位置下还有关联数据", statusCode: 400);
        }
    }

    private static async Task<Ok<List<LocationDto>>>
        GetAllLocationsAsync(LocationRepository repo)
    {
        var locations = await repo.GetAllAsync();
        var idToName = locations.ToDictionary(l => l.Id, l => l.Name);
        var dtos = locations.Select(l => ToDto(
            l, LocationRepository.BuildDisplayPath(l.Path, idToName))).ToList();
        return TypedResults.Ok(dtos);
    }

    private static async Task<Results<Ok<LocationDto>, NotFound>>
        GetLocationByIdAsync(int id, LocationRepository repo)
    {
        var location = await repo.GetByIdAsync(id);
        if (location is null) return TypedResults.NotFound();

        var namePath = await repo.ResolveDisplayPathAsync(location.Path);
        return TypedResults.Ok(ToDto(location, namePath));
    }

    private static async Task<Results<Ok<List<LocationDto>>, NotFound>>
        GetChildrenAsync(int id, LocationRepository repo)
    {
        try
        {
            var children = await repo.GetChildrenAsync(id);
            var dtos = children.Select(l => ToDto(l, null)).ToList();
            return TypedResults.Ok(dtos);
        }
        catch (KeyNotFoundException)
        {
            return TypedResults.NotFound();
        }
    }

    private static LocationDto ToDto(Location location, string? namePath) =>
        new(location.Id, location.Name, location.Path, location.ParentId, location.SortOrder,
            location.Description, !string.IsNullOrEmpty(location.PhotoFileName), namePath);
}
