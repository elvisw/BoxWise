using System.Reflection;
using System.Text.Json;
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

public class ItemEndpointsTests : IAsyncLifetime
{
    private TestIdentityContext _identity = null!;
    private UserManager<AppUser> _userManager = null!;
    private HttpContext _httpContext = null!;

    public async Task InitializeAsync()
    {
        _identity = await TestIdentityFactory.CreateAsync();
        _userManager = _identity.UserManager;
        var user = new AppUser { UserName = "tester" };
        await _userManager.CreateAsync(user, "Test1234!");
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, user.Id)], "test"));
        _httpContext = new DefaultHttpContext { User = principal };
    }

    public async Task DisposeAsync() => await _identity.DisposeAsync();

    private static async Task<int> InvokeAsync(string methodName, params object?[] args)
    {
        var method = typeof(ItemEndpoints).GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Static)!;
        var task = (Task)method.Invoke(null, args)!;
        await task;
        var httpResult = task.GetType().GetProperty("Result")!.GetValue(task)!;
        var executeMethod = httpResult.GetType().GetMethod("ExecuteAsync", [typeof(HttpContext)])!;
        var services = new ServiceCollection();
        services.AddLogging();
        using var sp = services.BuildServiceProvider();
        var httpContext = new DefaultHttpContext { RequestServices = sp };
        httpContext.Response.Body = new MemoryStream();
        await (Task)executeMethod.Invoke(httpResult, [httpContext])!;
        return httpContext.Response.StatusCode;
    }

    private static async Task<(int StatusCode, string Body)> InvokeWithBodyAsync(string methodName, params object?[] args)
    {
        var method = typeof(ItemEndpoints).GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Static)!;
        var task = (Task)method.Invoke(null, args)!;
        await task;
        var httpResult = task.GetType().GetProperty("Result")!.GetValue(task)!;
        var executeMethod = httpResult.GetType().GetMethod("ExecuteAsync", [typeof(HttpContext)])!;
        var services = new ServiceCollection();
        services.AddLogging();
        using var sp = services.BuildServiceProvider();
        var httpContext = new DefaultHttpContext { RequestServices = sp };
        httpContext.Response.Body = new MemoryStream();
        await (Task)executeMethod.Invoke(httpResult, [httpContext])!;
        httpContext.Response.Body.Seek(0, SeekOrigin.Begin);
        var body = await new StreamReader(httpContext.Response.Body).ReadToEndAsync();
        return (httpContext.Response.StatusCode, body);
    }

    private static (Location Location, Tag Tag) SeedDb(AppDbContext db)
    {
        var location = new Location { Name = "客厅", Path = "/placeholder/" };
        db.Locations.Add(location);
        var tag = new Tag { Name = "工具" };
        db.Tags.Add(tag);
        db.SaveChanges();
        return (location, tag);
    }

    [Fact] public async Task CreateItemAsync_Valid_ReturnsCreated() { using var db = TestDbContextFactory.Create(); var (loc, tag) = SeedDb(db); var r = new ItemRepository(db); var lr = new LocationRepository(db); Assert.Equal(201, await InvokeAsync("CreateItemAsync", new CreateItemRequest("螺丝刀", loc.Id, [tag.Id], "蓝色"), r, _userManager, _httpContext, lr)); }
    [Fact] public async Task CreateItemAsync_EmptyName_ReturnsProblem() { using var db = TestDbContextFactory.Create(); var (loc, _) = SeedDb(db); var r = new ItemRepository(db); var lr = new LocationRepository(db); Assert.Equal(400, await InvokeAsync("CreateItemAsync", new CreateItemRequest("", loc.Id, [], null), r, _userManager, _httpContext, lr)); }
    [Fact] public async Task CreateItemAsync_BadLocation_ReturnsProblem() { using var db = TestDbContextFactory.Create(); SeedDb(db); var r = new ItemRepository(db); var lr = new LocationRepository(db); Assert.Equal(400, await InvokeAsync("CreateItemAsync", new CreateItemRequest("测试", 999, [], null), r, _userManager, _httpContext, lr)); }
    [Fact] public async Task SearchItemsAsync_NoParams_ReturnsOk() { using var db = TestDbContextFactory.Create(); var (loc, _) = SeedDb(db); var r = new ItemRepository(db); var lr = new LocationRepository(db); await r.CreateAsync("螺丝刀", loc.Id, [], null, "tester"); Assert.Equal(200, await InvokeAsync("SearchItemsAsync", null, null, null, r, lr, _httpContext)); }
    [Fact] public async Task SearchItemsAsync_ByKeyword_ReturnsMatching() { using var db = TestDbContextFactory.Create(); var (loc, _) = SeedDb(db); var r = new ItemRepository(db); var lr = new LocationRepository(db); await r.CreateAsync("螺丝刀", loc.Id, [], null, "tester"); await r.CreateAsync("锤子", loc.Id, [], null, "tester"); Assert.Equal(200, await InvokeAsync("SearchItemsAsync", "螺丝", null, null, r, lr, _httpContext)); }
    [Fact] public async Task SearchItemsAsync_ByLocation_ReturnsSubtree() { using var db = TestDbContextFactory.Create(); var (loc, _) = SeedDb(db); var r = new ItemRepository(db); var lr = new LocationRepository(db); var child = await lr.CreateAsync("电视机柜", loc.Id); await r.CreateAsync("螺丝刀", child.Id, [], null, "tester"); await r.CreateAsync("遥控器", loc.Id, [], null, "tester"); Assert.Equal(200, await InvokeAsync("SearchItemsAsync", null, loc.Id, null, r, lr, _httpContext)); }
    [Fact] public async Task GetItemByIdAsync_NonExistent_ReturnsNotFound() { using var db = TestDbContextFactory.Create(); var r = new ItemRepository(db); var lr = new LocationRepository(db); Assert.Equal(404, await InvokeAsync("GetItemByIdAsync", 999, r, lr)); }
    [Fact]
    public async Task GetItemByIdAsync_Exists_ReturnsOk()
    {
        using var db = TestDbContextFactory.Create();
        var location = new Location { Name = "客厅", Path = "/placeholder/" };
        db.Locations.Add(location);
        db.Users.Add(new AppUser { Id = "creator-1", UserName = "creator" });
        await db.SaveChangesAsync();

        var item = new Item
        {
            Name = "螺丝刀",
            LocationId = location.Id,
            CreatedByUserId = "creator-1",
            CreatedAt = DateTime.UtcNow
        };
        db.Items.Add(item);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var repo = new ItemRepository(db);
        var lr = new LocationRepository(db);

        var (status, body) = await InvokeWithBodyAsync("GetItemByIdAsync", item.Id, repo, lr);
        Assert.Equal(200, status);

        var dto = JsonSerializer.Deserialize<ItemDto>(body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        Assert.NotNull(dto);
        Assert.Equal("螺丝刀", dto.Name);
        Assert.Equal("creator", dto.CreatedByUserName);
        Assert.Equal("客厅", dto.LocationName);
    }
    [Fact] public async Task DeleteItemAsync_Exists_ReturnsNoContent() { using var db = TestDbContextFactory.Create(); var (loc, _) = SeedDb(db); var r = new ItemRepository(db); var c = await r.CreateAsync("待删除", loc.Id, [], null, "tester"); var s = new ImageStorageService(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["DataDirectory"] = Path.GetTempPath() }).Build()); Assert.Equal(204, await InvokeAsync("DeleteItemAsync", c.Id, r, s)); Assert.False(db.Items.Any(i => i.Id == c.Id)); }

    [Fact]
    public async Task SearchItemsAsync_ByTagId_ReturnsFiltered()
    {
        using var db = TestDbContextFactory.Create();
        var (loc, _) = SeedDb(db);
        var repo = new ItemRepository(db);
        var lr = new LocationRepository(db);

        var tag1 = await new TagRepository(db).CreateAsync("标签A");
        var tag2 = await new TagRepository(db).CreateAsync("标签B");

        await repo.CreateAsync("物品A", loc.Id, [tag1.Id], null, "tester");
        await repo.CreateAsync("物品B", loc.Id, [tag2.Id], null, "tester");

        var (status, body) = await InvokeWithBodyAsync("SearchItemsAsync",
            null, null, new string?[] { tag1.Id.ToString() }, repo, lr, _httpContext);
        Assert.Equal(200, status);

        var results = JsonSerializer.Deserialize<List<ItemSummaryDto>>(body,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        Assert.NotNull(results);
        Assert.Single(results);
        Assert.Equal("物品A", results[0].Name);
    }

    [Fact]
    public async Task SearchItemsAsync_ByMultipleTagIds_ReturnsIntersection()
    {
        using var db = TestDbContextFactory.Create();
        var (loc, _) = SeedDb(db);
        var repo = new ItemRepository(db);
        var lr = new LocationRepository(db);

        var tag1 = await new TagRepository(db).CreateAsync("标签A");
        var tag2 = await new TagRepository(db).CreateAsync("标签B");

        await repo.CreateAsync("双标签物品", loc.Id, [tag1.Id, tag2.Id], null, "tester");
        await repo.CreateAsync("单标签物品", loc.Id, [tag1.Id], null, "tester");

        var (status, body) = await InvokeWithBodyAsync("SearchItemsAsync",
            null, null, new string?[] { tag1.Id.ToString(), tag2.Id.ToString() }, repo, lr, _httpContext);
        Assert.Equal(200, status);

        var results = JsonSerializer.Deserialize<List<ItemSummaryDto>>(body,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        Assert.NotNull(results);
        Assert.Single(results);
        Assert.Equal("双标签物品", results[0].Name);
    }
}
