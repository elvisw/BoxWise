using BoxWise.Server.Data;
using BoxWise.Server.Models;
using BoxWise.Server.Repositories;

namespace BoxWise.Server.Tests.Repositories;

public class ItemRepositoryTests
{
    private static async Task SeedLocationAndTags(AppDbContext db)
    {
        db.Locations.Add(new Location { Name = "客厅", Path = "/1/" });
        db.Tags.Add(new Tag { Name = "工具" });
        db.Tags.Add(new Tag { Name = "电子" });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task CreateAsync_ValidInput_Succeeds()
    {
        using var db = TestDbContextFactory.Create();
        await SeedLocationAndTags(db);
        var repo = new ItemRepository(db);

        var item = await repo.CreateAsync("螺丝刀", 1, [1, 2], "蓝色", "user-1");

        Assert.Equal("螺丝刀", item.Name);
        Assert.Equal(1, item.LocationId);
        Assert.Equal("user-1", item.CreatedByUserId);
        Assert.NotEqual(default, item.CreatedAt);
        Assert.Equal(2, item.Tags.Count);
    }

    [Fact]
    public async Task CreateAsync_EmptyName_ThrowsArgumentException()
    {
        using var db = TestDbContextFactory.Create();
        var repo = new ItemRepository(db);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            repo.CreateAsync("", 1, [], null, "user-1"));
    }

    [Fact]
    public async Task CreateAsync_NameExceedsMaxLength_ThrowsArgumentException()
    {
        using var db = TestDbContextFactory.Create();
        var repo = new ItemRepository(db);
        var longName = new string('x', 201);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            repo.CreateAsync(longName, 1, [], null, "user-1"));
    }

    [Fact]
    public async Task CreateAsync_InvalidLocationId_ThrowsArgumentException()
    {
        using var db = TestDbContextFactory.Create();
        var repo = new ItemRepository(db);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            repo.CreateAsync("物品", 999, [], null, "user-1"));
    }

    [Fact]
    public async Task CreateAsync_NonExistentTagId_ThrowsArgumentException()
    {
        using var db = TestDbContextFactory.Create();
        await SeedLocationAndTags(db);
        var repo = new ItemRepository(db);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            repo.CreateAsync("物品", 1, [999], null, "user-1"));
    }

    [Fact]
    public async Task CreateAsync_EmptyTagIds_Succeeds()
    {
        using var db = TestDbContextFactory.Create();
        await SeedLocationAndTags(db);
        var repo = new ItemRepository(db);

        var item = await repo.CreateAsync("无标签物品", 1, [], null, "user-1");

        Assert.NotNull(item);
        Assert.Empty(item.Tags);
    }

    [Fact]
    public async Task GetFilteredAsync_NoParams_ReturnsAllItems()
    {
        using var db = TestDbContextFactory.Create();
        await SeedLocationAndTags(db);
        var repo = new ItemRepository(db);
        await repo.CreateAsync("物品A", 1, [1], null, "user-1");
        await repo.CreateAsync("物品B", 1, [2], null, "user-1");

        var items = await repo.GetFilteredAsync(null, null, null);

        Assert.Equal(2, items.Count);
    }

    [Fact]
    public async Task GetFilteredAsync_ByLocation_ReturnsSubtreeItems()
    {
        using var db = TestDbContextFactory.Create();
        db.Locations.Add(new Location { Name = "客厅", Path = "/1/" });
        db.Locations.Add(new Location { Name = "柜子", Path = "/1/2/", ParentId = 1 });
        await db.SaveChangesAsync();
        var tag = new Tag { Name = "工具" };
        db.Tags.Add(tag);
        await db.SaveChangesAsync();

        var repo = new ItemRepository(db);
        await repo.CreateAsync("物品A", 1, [], null, "user-1");
        await repo.CreateAsync("物品B", 2, [], null, "user-1");

        var items = await repo.GetFilteredAsync(1, null, null);

        Assert.Equal(2, items.Count);
    }

    [Fact]
    public async Task GetFilteredAsync_ByTags_ReturnsAndMatch()
    {
        using var db = TestDbContextFactory.Create();
        await SeedLocationAndTags(db);
        var repo = new ItemRepository(db);
        await repo.CreateAsync("电子工具", 1, [1, 2], null, "user-1");
        await repo.CreateAsync("纯工具", 1, [1], null, "user-1");

        var items = await repo.GetFilteredAsync(null, [1, 2], null);

        Assert.Single(items);
        Assert.Equal("电子工具", items[0].Name);
    }

    [Fact]
    public async Task GetFilteredAsync_Combined_ReturnsIntersection()
    {
        using var db = TestDbContextFactory.Create();
        db.Locations.Add(new Location { Name = "客厅", Path = "/1/" });
        db.Locations.Add(new Location { Name = "卧室", Path = "/2/" });
        db.Tags.Add(new Tag { Name = "工具" });
        db.Tags.Add(new Tag { Name = "电子" });
        await db.SaveChangesAsync();

        var repo = new ItemRepository(db);
        await repo.CreateAsync("客厅电子工具", 1, [1, 2], null, "user-1");
        await repo.CreateAsync("卧室工具", 2, [1], null, "user-1");
        await repo.CreateAsync("客厅杂物", 1, [], null, "user-1");

        var items = await repo.GetFilteredAsync(1, [1], null);

        Assert.Single(items);
        Assert.Equal("客厅电子工具", items[0].Name);
    }

    [Fact]
    public async Task GetFilteredAsync_ByKeyword_SearchesNameNoteTags()
    {
        using var db = TestDbContextFactory.Create();
        await SeedLocationAndTags(db);
        var repo = new ItemRepository(db);
        await repo.CreateAsync("数据线", 1, [2], "充电用", "user-1");
        await repo.CreateAsync("螺丝刀", 1, [1], null, "user-1");

        var items = await repo.GetFilteredAsync(null, null, "充电");

        Assert.Single(items);
        Assert.Equal("数据线", items[0].Name);
    }

    [Fact]
    public async Task GetFilteredAsync_InvalidLocationId_ReturnsAll()
    {
        using var db = TestDbContextFactory.Create();
        await SeedLocationAndTags(db);
        var repo = new ItemRepository(db);
        await repo.CreateAsync("物品", 1, [], null, "user-1");

        var items = await repo.GetFilteredAsync(999, null, null);

        Assert.Single(items);
    }

}
