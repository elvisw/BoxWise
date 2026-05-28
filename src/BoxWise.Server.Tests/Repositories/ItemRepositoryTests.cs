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

    private static async Task SeedUser(AppDbContext db, string userId = "user-1", string userName = "test")
    {
        db.Users.Add(new AppUser { Id = userId, UserName = userName });
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

    // ──────────── GetByIdAsync ────────────

    // 注意：EF Core InMemory provider 有一个已知限制 —
    // .Include(i => i.CreatedByUser) 当 AppUser 实体不存在于数据库时，
    // FirstOrDefaultAsync 可能返回 null。GetFilteredAsync (无此 Include) 正常。
    // 生产 SQLite 中此行为正确（CreatedByUser 返回 null），通过集成测试验证。
    [Fact]
    public async Task GetByIdAsync_Exists_ReturnsItem()
    {
        using var db = TestDbContextFactory.Create();
        await SeedLocationAndTags(db);

        // 植入 AppUser 实体以绕过 EF Core InMemory Include 限制
        db.Users.Add(new AppUser { Id = "user-1", UserName = "test" });

        var item = new Item
        {
            Name = "螺丝刀", LocationId = 1, Note = "测试备注",
            CreatedByUserId = "user-1", CreatedAt = DateTime.UtcNow
        };
        db.Items.Add(item);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var repo = new ItemRepository(db);
        var result = await repo.GetByIdAsync(item.Id);

        Assert.NotNull(result);
        Assert.Equal("螺丝刀", result.Name);
        Assert.Equal("测试备注", result.Note);
        Assert.NotNull(result.CreatedByUser);
        Assert.Equal("test", result.CreatedByUser.UserName);
    }

    [Fact]
    public async Task GetByIdAsync_NonExistent_ReturnsNull()
    {
        using var db = TestDbContextFactory.Create();
        var repo = new ItemRepository(db);

        var item = await repo.GetByIdAsync(999);

        Assert.Null(item);
    }

    // ──────────── DeleteAsync ────────────

    [Fact]
    public async Task DeleteAsync_Exists_ReturnsTrueAndDeletes()
    {
        using var db = TestDbContextFactory.Create();
        await SeedLocationAndTags(db);
        var repo = new ItemRepository(db);
        var created = await repo.CreateAsync("待删除物品", 1, [], null, "user-1");

        var result = await repo.DeleteAsync(created.Id);

        Assert.True(result);
        var deleted = await db.Items.FindAsync(created.Id);
        Assert.Null(deleted);
    }

    [Fact]
    public async Task DeleteAsync_NonExistent_ReturnsFalse()
    {
        using var db = TestDbContextFactory.Create();
        var repo = new ItemRepository(db);

        var result = await repo.DeleteAsync(999);

        Assert.False(result);
    }

    [Fact]
    public async Task GetByIdAsync_WithMultipleTags_IncludesAllTags()
    {
        using var db = TestDbContextFactory.Create();
        await SeedLocationAndTags(db);

        // 植入 AppUser 实体以绕过 EF Core InMemory Include 限制
        db.Users.Add(new AppUser { Id = "user-1", UserName = "test" });

        var item = new Item
        {
            Name = "多标签物品", LocationId = 1,
            CreatedByUserId = "user-1", CreatedAt = DateTime.UtcNow
        };
        db.Items.Add(item);
        await db.SaveChangesAsync();

        var tag1 = await db.Tags.FindAsync(1);
        var tag2 = await db.Tags.FindAsync(2);
        item.Tags.Add(tag1!);
        item.Tags.Add(tag2!);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var repo = new ItemRepository(db);
        var result = await repo.GetByIdAsync(item.Id);

        Assert.NotNull(result);
        Assert.Equal(2, result.Tags.Count);
        Assert.NotNull(result.CreatedByUser);
        Assert.Equal("test", result.CreatedByUser.UserName);
    }

    [Fact]
    public async Task DeleteAsync_WithTags_CascadeDeletesItemTag()
    {
        using var db = TestDbContextFactory.Create();
        await SeedLocationAndTags(db);
        var repo = new ItemRepository(db);
        var created = await repo.CreateAsync("带标签物品", 1, [1, 2], null, "user-1");

        await repo.DeleteAsync(created.Id);

        // 物品已删除
        Assert.False(db.Items.Any(i => i.Id == created.Id));
        // Tag 本身保留
        Assert.True(db.Tags.Any(t => t.Id == 1));
        Assert.True(db.Tags.Any(t => t.Id == 2));
    }

    // ──────────── UpdateAsync ────────────

    [Fact]
    public async Task UpdateAsync_ValidInput_Succeeds()
    {
        using var db = TestDbContextFactory.Create();
        await SeedLocationAndTags(db);
        await SeedUser(db, "user-1", "tester");
        var repo = new ItemRepository(db);
        var created = await repo.CreateAsync("旧名称", 1, [1], "旧备注", "user-1");

        var updated = await repo.UpdateAsync(created.Id, "新名称", 1, [2], "新备注");

        Assert.NotNull(updated);
        Assert.Equal("新名称", updated.Name);
        Assert.Equal("新备注", updated.Note);
        Assert.Single(updated.Tags);
        Assert.Equal(2, updated.Tags.First().Id);
    }

    [Fact]
    public async Task UpdateAsync_NonExistent_ReturnsNull()
    {
        using var db = TestDbContextFactory.Create();
        await SeedLocationAndTags(db);
        await SeedUser(db, "user-1", "tester");
        var repo = new ItemRepository(db);

        var result = await repo.UpdateAsync(999, "名称", 1, [], null);

        Assert.Null(result);
    }

    [Fact]
    public async Task UpdateAsync_EmptyName_ThrowsArgumentException()
    {
        using var db = TestDbContextFactory.Create();
        await SeedLocationAndTags(db);
        var repo = new ItemRepository(db);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            repo.UpdateAsync(1, "", 1, [], null));
    }

    [Fact]
    public async Task UpdateAsync_NameExceedsMaxLength_ThrowsArgumentException()
    {
        using var db = TestDbContextFactory.Create();
        await SeedLocationAndTags(db);
        var repo = new ItemRepository(db);
        var longName = new string('x', 201);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            repo.UpdateAsync(1, longName, 1, [], null));
    }

    [Fact]
    public async Task UpdateAsync_InvalidLocationId_ThrowsArgumentException()
    {
        using var db = TestDbContextFactory.Create();
        var repo = new ItemRepository(db);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            repo.UpdateAsync(1, "物品", 999, [], null));
    }

    [Fact]
    public async Task UpdateAsync_NonExistentTag_ThrowsArgumentException()
    {
        using var db = TestDbContextFactory.Create();
        await SeedLocationAndTags(db);
        var repo = new ItemRepository(db);
        var created = await repo.CreateAsync("物品", 1, [], null, "user-1");

        await Assert.ThrowsAsync<ArgumentException>(() =>
            repo.UpdateAsync(created.Id, "物品", 1, [999], null));
    }

    [Fact]
    public async Task UpdateAsync_UpdateTags_ReplacesTagsCorrectly()
    {
        using var db = TestDbContextFactory.Create();
        await SeedLocationAndTags(db);
        await SeedUser(db, "user-1", "tester");
        var repo = new ItemRepository(db);
        var created = await repo.CreateAsync("物品", 1, [1, 2], null, "user-1");

        var updated = await repo.UpdateAsync(created.Id, "物品", 1, [1], null);

        Assert.NotNull(updated);
        Assert.Single(updated.Tags);
        Assert.Equal(1, updated.Tags.First().Id);
    }

    [Fact]
    public async Task UpdateAsync_NoteEmpty_StoredAsNull()
    {
        using var db = TestDbContextFactory.Create();
        await SeedLocationAndTags(db);
        await SeedUser(db, "user-1", "tester");
        var repo = new ItemRepository(db);
        var created = await repo.CreateAsync("物品", 1, [], "旧备注", "user-1");

        var updated = await repo.UpdateAsync(created.Id, "物品", 1, [], "");

        Assert.NotNull(updated);
        Assert.Null(updated.Note);
    }

    [Fact]
    public async Task UpdateAsync_NoteWhiteSpace_StoredAsNull()
    {
        using var db = TestDbContextFactory.Create();
        await SeedLocationAndTags(db);
        await SeedUser(db, "user-1", "tester");
        var repo = new ItemRepository(db);
        var created = await repo.CreateAsync("物品", 1, [], null, "user-1");

        var updated = await repo.UpdateAsync(created.Id, "物品", 1, [], "   ");

        Assert.NotNull(updated);
        Assert.Null(updated.Note);
    }

    [Fact]
    public async Task UpdateAsync_PreservesPhotoPaths()
    {
        using var db = TestDbContextFactory.Create();
        await SeedLocationAndTags(db);
        await SeedUser(db, "user-1", "tester");
        var repo = new ItemRepository(db);
        var created = await repo.CreateAsync("物品", 1, [], null, "user-1");

        // 模拟已有照片路径（直接设 DB）
        db.Attach(created);
        created.PhotoPath = "images/1/original.jpg";
        created.ThumbPath = "images/1/thumb.jpg";
        created.MediumPath = "images/1/medium.jpg";
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var updated = await repo.UpdateAsync(created.Id, "新名称", 1, [], null);

        Assert.NotNull(updated);
        Assert.Equal("images/1/original.jpg", updated.PhotoPath);
        Assert.Equal("images/1/thumb.jpg", updated.ThumbPath);
        Assert.Equal("images/1/medium.jpg", updated.MediumPath);
    }
}
