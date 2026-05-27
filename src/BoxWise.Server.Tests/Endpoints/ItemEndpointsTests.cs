using System.Reflection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Security.Claims;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using BoxWise.Server.Data;
using BoxWise.Server.Endpoints;
using BoxWise.Server.Models;
using BoxWise.Server.Repositories;
using BoxWise.Server.Services;
using BoxWise.Shared.Dtos;

namespace BoxWise.Server.Tests.Endpoints;

public class ItemEndpointsTests : IAsyncDisposable
{
    private readonly TestIdentityContext _identity;
    private readonly UserManager<AppUser> _userManager;
    private readonly HttpContext _httpContext;

    public ItemEndpointsTests()
    {
        _identity = TestIdentityFactory.CreateAsync().GetAwaiter().GetResult();
        _userManager = _identity.UserManager;
        var user = new AppUser { UserName = "tester" };
        _userManager.CreateAsync(user, "Test1234!").GetAwaiter().GetResult();
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, user.Id)], "test"));
        _httpContext = new DefaultHttpContext { User = principal };
    }

    public async ValueTask DisposeAsync() => await _identity.DisposeAsync();

    private static async Task<int> InvokeAsync(string methodName, params object?[] args)
    {
        var method = typeof(ItemEndpoints).GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Static)!;
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

    private static void SeedDb(AppDbContext db)
    {
        db.Locations.Add(new Location { Name = "客厅", Path = "/1/" });
        db.Tags.Add(new Tag { Name = "工具" });
        db.SaveChanges();
    }

    [Fact] public async Task CreateItemAsync_Valid_ReturnsCreated() { using var db = TestDbContextFactory.Create(); SeedDb(db); var r = new ItemRepository(db); var lr = new LocationRepository(db); Assert.Equal(201, await InvokeAsync("CreateItemAsync", new CreateItemRequest("螺丝刀", 1, [1], "蓝色"), r, _userManager, _httpContext, lr)); }
    [Fact] public async Task CreateItemAsync_EmptyName_ReturnsProblem() { using var db = TestDbContextFactory.Create(); SeedDb(db); var r = new ItemRepository(db); var lr = new LocationRepository(db); Assert.Equal(400, await InvokeAsync("CreateItemAsync", new CreateItemRequest("", 1, [], null), r, _userManager, _httpContext, lr)); }
    [Fact] public async Task CreateItemAsync_BadLocation_ReturnsProblem() { using var db = TestDbContextFactory.Create(); SeedDb(db); var r = new ItemRepository(db); var lr = new LocationRepository(db); Assert.Equal(400, await InvokeAsync("CreateItemAsync", new CreateItemRequest("测试", 999, [], null), r, _userManager, _httpContext, lr)); }
    [Fact] public async Task SearchItemsAsync_NoParams_ReturnsOk() { using var db = TestDbContextFactory.Create(); SeedDb(db); var r = new ItemRepository(db); var lr = new LocationRepository(db); await r.CreateAsync("螺丝刀", 1, [], null, "tester"); Assert.Equal(200, await InvokeAsync("SearchItemsAsync", null, null, null, r, lr, _httpContext)); }
    [Fact] public async Task SearchItemsAsync_ByKeyword_ReturnsMatching() { using var db = TestDbContextFactory.Create(); SeedDb(db); var r = new ItemRepository(db); var lr = new LocationRepository(db); await r.CreateAsync("螺丝刀", 1, [], null, "tester"); await r.CreateAsync("锤子", 1, [], null, "tester"); Assert.Equal(200, await InvokeAsync("SearchItemsAsync", "螺丝", null, null, r, lr, _httpContext)); }
    [Fact] public async Task SearchItemsAsync_ByLocation_ReturnsSubtree() { using var db = TestDbContextFactory.Create(); SeedDb(db); var r = new ItemRepository(db); var lr = new LocationRepository(db); var child = await lr.CreateAsync("电视机柜", 1); await r.CreateAsync("螺丝刀", child.Id, [], null, "tester"); await r.CreateAsync("遥控器", 1, [], null, "tester"); Assert.Equal(200, await InvokeAsync("SearchItemsAsync", null, 1, null, r, lr, _httpContext)); }
    [Fact] public async Task GetItemByIdAsync_NonExistent_ReturnsNotFound() { using var db = TestDbContextFactory.Create(); var r = new ItemRepository(db); var lr = new LocationRepository(db); Assert.Equal(404, await InvokeAsync("GetItemByIdAsync", 999, r, lr)); }
    [Fact] public async Task DeleteItemAsync_Exists_ReturnsNoContent() { using var db = TestDbContextFactory.Create(); SeedDb(db); var r = new ItemRepository(db); var c = await r.CreateAsync("待删除", 1, [], null, "tester"); var s = new ImageStorageService(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["DataDirectory"] = Path.GetTempPath() }).Build()); Assert.Equal(204, await InvokeAsync("DeleteItemAsync", c.Id, r, s)); Assert.False(db.Items.Any(i => i.Id == c.Id)); }
}
