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

}
