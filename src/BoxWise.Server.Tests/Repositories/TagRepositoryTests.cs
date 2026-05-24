using BoxWise.Server.Repositories;

namespace BoxWise.Server.Tests.Repositories;

public class TagRepositoryTests
{
    [Fact]
    public async Task CreateAsync_ValidName_Succeeds()
    {
        using var db = TestDbContextFactory.Create();
        var repo = new TagRepository(db);

        var result = await repo.CreateAsync("电子配件");

        Assert.Equal("电子配件", result.Name);
        Assert.True(result.Id > 0);
    }

    [Fact]
    public async Task CreateAsync_DuplicateName_ThrowsArgumentException()
    {
        using var db = TestDbContextFactory.Create();
        var repo = new TagRepository(db);
        await repo.CreateAsync("工具");

        await Assert.ThrowsAsync<ArgumentException>(() => repo.CreateAsync("工具"));
    }

    [Fact]
    public async Task GetOrCreateAsync_ExistingName_ReturnsExisting()
    {
        using var db = TestDbContextFactory.Create();
        var repo = new TagRepository(db);
        var created = await repo.CreateAsync("证件");

        var result = await repo.GetOrCreateAsync("证件");

        Assert.Equal(created.Id, result.Id);
    }

    [Fact]
    public async Task GetOrCreateAsync_NewName_CreatesAndReturns()
    {
        using var db = TestDbContextFactory.Create();
        var repo = new TagRepository(db);

        var result = await repo.GetOrCreateAsync("新标签");

        Assert.Equal("新标签", result.Name);
        var exists = db.Tags.Any(t => t.Name == "新标签");
        Assert.True(exists);
    }

    [Fact]
    public async Task GetAllAsync_ReturnsAllTags()
    {
        using var db = TestDbContextFactory.Create();
        var repo = new TagRepository(db);
        await repo.CreateAsync("证件");
        await repo.CreateAsync("工具");
        await repo.CreateAsync("电子配件");

        var tags = await repo.GetAllAsync();

        Assert.Equal(3, tags.Count);
        Assert.Contains(tags, t => t.Name == "工具");
        Assert.Contains(tags, t => t.Name == "电子配件");
        Assert.Contains(tags, t => t.Name == "证件");
        // Note: SQLite ORDER BY Name produces correct alphabetical order;
        // InMemory provider may differ in ordering behavior.
    }
}
