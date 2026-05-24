using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using BoxWise.Server.Repositories;
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

        group.MapPost("/", CreateLocationAsync)
            .Produces<LocationDto>(201)
            .ProducesProblem(400)
            .ProducesProblem(401)
            .WithTags("Locations")
            .WithDescription("创建位置节点");

        group.MapPut("/{id:int}", RenameLocationAsync)
            .Produces<LocationDto>(200)
            .ProducesProblem(400)
            .ProducesProblem(401)
            .Produces(404)
            .WithTags("Locations")
            .WithDescription("重命名位置");

        group.MapDelete("/{id:int}", DeleteLocationAsync)
            .Produces(204)
            .ProducesProblem(400)
            .ProducesProblem(401)
            .Produces(404)
            .WithTags("Locations")
            .WithDescription("删除空位置");

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
            var location = await repo.CreateAsync(request.Name, request.ParentId);
            var dto = new LocationDto(location.Id, location.Name, location.Path, location.ParentId, location.SortOrder);
            return TypedResults.Created($"/api/locations/{dto.Id}", dto);
        }
        catch (ArgumentException ex)
        {
            return TypedResults.Problem(ex.Message, statusCode: 400);
        }
    }

    private static async Task<Results<Ok<LocationDto>, NotFound, ProblemHttpResult>>
        RenameLocationAsync(int id, RenameLocationRequest request, LocationRepository repo)
    {
        try
        {
            var location = await repo.RenameAsync(id, request.Name);
            var dto = new LocationDto(location.Id, location.Name, location.Path, location.ParentId, location.SortOrder);
            return TypedResults.Ok(dto);
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

    private static async Task<Results<NoContent, NotFound, ProblemHttpResult>>
        DeleteLocationAsync(int id, LocationRepository repo)
    {
        try
        {
            await repo.DeleteAsync(id);
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
        var dtos = locations.Select(l => new LocationDto(
            l.Id, l.Name, l.Path, l.ParentId, l.SortOrder
        )).ToList();
        return TypedResults.Ok(dtos);
    }

    private static async Task<Results<Ok<List<LocationDto>>, NotFound>>
        GetChildrenAsync(int id, LocationRepository repo)
    {
        try
        {
            var children = await repo.GetChildrenAsync(id);
            var dtos = children.Select(l => new LocationDto(
                l.Id, l.Name, l.Path, l.ParentId, l.SortOrder
            )).ToList();
            return TypedResults.Ok(dtos);
        }
        catch (KeyNotFoundException)
        {
            return TypedResults.NotFound();
        }
    }
}
