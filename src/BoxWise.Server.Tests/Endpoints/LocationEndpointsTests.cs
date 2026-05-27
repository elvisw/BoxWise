using System.Reflection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using BoxWise.Server.Endpoints;
using BoxWise.Server.Repositories;
using BoxWise.Shared.Dtos;

namespace BoxWise.Server.Tests.Endpoints;

public class LocationEndpointsTests
{
    private static async Task<int> InvokeAsync(string methodName, params object?[] args)
    {
        var method = typeof(LocationEndpoints).GetMethod(methodName,
            BindingFlags.NonPublic | BindingFlags.Static)!;
        var task = (Task)method.Invoke(null, args)!;
        await task;
        var httpResult = task.GetType().GetProperty("Result")!.GetValue(task)!;
        var executeMethod = httpResult.GetType().GetMethod("ExecuteAsync", [typeof(HttpContext)])!;
        var services = new ServiceCollection();
        services.AddLogging();
        var httpContext = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        httpContext.Response.Body = new MemoryStream();
        await (Task)executeMethod.Invoke(httpResult, [httpContext])!;
        return httpContext.Response.StatusCode;
    }

    [Fact] public async Task GetAllLocationsAsync_ReturnsOk() { using var db = TestDbContextFactory.Create(); var r = new LocationRepository(db); await r.CreateAsync("客厅", null); await r.CreateAsync("卧室", null); Assert.Equal(200, await InvokeAsync("GetAllLocationsAsync", r)); }
    [Fact] public async Task GetChildrenAsync_ReturnsOk() { using var db = TestDbContextFactory.Create(); var r = new LocationRepository(db); var p = await r.CreateAsync("客厅", null); await r.CreateAsync("电视机柜", p.Id); Assert.Equal(200, await InvokeAsync("GetChildrenAsync", p.Id, r)); }
    [Fact] public async Task CreateLocationAsync_Root_ReturnsCreated() { using var db = TestDbContextFactory.Create(); var r = new LocationRepository(db); Assert.Equal(201, await InvokeAsync("CreateLocationAsync", new CreateLocationRequest("客厅", null, 0), r)); }
    [Fact] public async Task CreateLocationAsync_Child_ReturnsCreated() { using var db = TestDbContextFactory.Create(); var r = new LocationRepository(db); var p = await r.CreateAsync("客厅", null); Assert.Equal(201, await InvokeAsync("CreateLocationAsync", new CreateLocationRequest("电视机柜", p.Id, 0), r)); }
    [Fact] public async Task CreateLocationAsync_EmptyName_ReturnsProblem() { using var db = TestDbContextFactory.Create(); var r = new LocationRepository(db); Assert.Equal(400, await InvokeAsync("CreateLocationAsync", new CreateLocationRequest("", null, 0), r)); }
    [Fact] public async Task RenameLocationAsync_Success() { using var db = TestDbContextFactory.Create(); var r = new LocationRepository(db); var l = await r.CreateAsync("客厅", null); Assert.Equal(200, await InvokeAsync("RenameLocationAsync", l.Id, new RenameLocationRequest("卧室"), r)); }
    [Fact] public async Task RenameLocationAsync_NotFound() { using var db = TestDbContextFactory.Create(); var r = new LocationRepository(db); Assert.Equal(404, await InvokeAsync("RenameLocationAsync", 999, new RenameLocationRequest("x"), r)); }
    [Fact] public async Task GetChildrenAsync_NotFound() { using var db = TestDbContextFactory.Create(); var r = new LocationRepository(db); Assert.Equal(404, await InvokeAsync("GetChildrenAsync", 999, r)); }
    [Fact] public async Task DeleteLocationAsync_Leaf_ReturnsNoContent() { using var db = TestDbContextFactory.Create(); var r = new LocationRepository(db); var l = await r.CreateAsync("抽屉", null); Assert.Equal(204, await InvokeAsync("DeleteLocationAsync", l.Id, r)); Assert.False(db.Locations.Any(x => x.Id == l.Id)); }
}
