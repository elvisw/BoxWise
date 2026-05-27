using System.Reflection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using BoxWise.Server.Endpoints;
using BoxWise.Server.Repositories;
using BoxWise.Shared.Dtos;

namespace BoxWise.Server.Tests.Endpoints;

public class TagEndpointsTests
{
    private static async Task<int> InvokeAsync(string methodName, params object?[] args)
    {
        var method = typeof(TagEndpoints).GetMethod(methodName,
            BindingFlags.NonPublic | BindingFlags.Static)!;
        var task = (Task)method.Invoke(null, args)!;
        await task;
        var httpResult = task.GetType().GetProperty("Result")!.GetValue(task)!;
        var executeMethod = httpResult.GetType().GetMethod("ExecuteAsync",
            [typeof(HttpContext)])!;
        var services = new ServiceCollection();
        services.AddLogging();
        var httpContext = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        httpContext.Response.Body = new MemoryStream();
        await (Task)executeMethod.Invoke(httpResult, [httpContext])!;
        return httpContext.Response.StatusCode;
    }

    [Fact]
    public async Task GetAllTagsAsync_ReturnsOk()
    {
        using var db = TestDbContextFactory.Create();
        var repo = new TagRepository(db);
        await repo.CreateAsync("工具");
        await repo.CreateAsync("证件");
        Assert.Equal(200, await InvokeAsync("GetAllTagsAsync", repo));
    }

    [Fact]
    public async Task CreateTagAsync_ValidName_ReturnsCreated()
    {
        using var db = TestDbContextFactory.Create();
        var repo = new TagRepository(db);
        Assert.Equal(201, await InvokeAsync("CreateTagAsync", new CreateTagRequest("电子配件"), repo));
    }

    [Fact]
    public async Task CreateTagAsync_EmptyName_ReturnsProblem()
    {
        using var db = TestDbContextFactory.Create();
        var repo = new TagRepository(db);
        Assert.Equal(400, await InvokeAsync("CreateTagAsync", new CreateTagRequest(""), repo));
    }

    [Fact]
    public async Task RenameTagAsync_Success()
    {
        using var db = TestDbContextFactory.Create();
        var repo = new TagRepository(db);
        var tag = await repo.CreateAsync("旧名称");
        Assert.Equal(200, await InvokeAsync("RenameTagAsync", tag.Id, new RenameTagRequest("新名称"), repo));
    }

    [Fact]
    public async Task RenameTagAsync_NotFound()
    {
        using var db = TestDbContextFactory.Create();
        var repo = new TagRepository(db);
        Assert.Equal(404, await InvokeAsync("RenameTagAsync", 999, new RenameTagRequest("x"), repo));
    }

    [Fact]
    public async Task DeleteTagAsync_NotFound() { using var db = TestDbContextFactory.Create(); var r = new TagRepository(db); Assert.Equal(404, await InvokeAsync("DeleteTagAsync", 999, r)); }
    [Fact] public async Task DeleteTagAsync_Success()
    {
        using var db = TestDbContextFactory.Create();
        var repo = new TagRepository(db);
        var tag = await repo.CreateAsync("待删除");
        Assert.Equal(204, await InvokeAsync("DeleteTagAsync", tag.Id, repo));
        Assert.False(db.Tags.Any(t => t.Id == tag.Id));
    }
}
