using Microsoft.EntityFrameworkCore;
using BoxWise.Server.Models;
using BoxWise.Server.Repositories;

namespace BoxWise.Server.Tests.Repositories;

public class LocationRepositoryTests
{
    [Fact]
    public async Task CreateAsync_RootNode_GeneratesCorrectPath()
    {
        using var db = TestDbContextFactory.Create();
        var repo = new LocationRepository(db);

        var result = await repo.CreateAsync("客厅", null);

        Assert.Equal("客厅", result.Name);
        Assert.Equal($"/{result.Id}/", result.Path);
        Assert.Null(result.ParentId);
    }

    [Fact]
    public async Task CreateAsync_ChildNode_InheritsParentPath()
    {
        using var db = TestDbContextFactory.Create();
        var repo = new LocationRepository(db);
        var parent = await repo.CreateAsync("客厅", null);

        var child = await repo.CreateAsync("电视机柜", parent.Id);

        Assert.Equal($"/{parent.Id}/{child.Id}/", child.Path);
        Assert.Equal(parent.Id, child.ParentId);
    }

    [Fact]
    public async Task CreateAsync_EmptyName_ThrowsArgumentException()
    {
        using var db = TestDbContextFactory.Create();
        var repo = new LocationRepository(db);

        await Assert.ThrowsAsync<ArgumentException>(() => repo.CreateAsync("", null));
    }

    [Fact]
    public async Task CreateAsync_NameExceedsMaxLength_ThrowsArgumentException()
    {
        using var db = TestDbContextFactory.Create();
        var repo = new LocationRepository(db);
        var longName = new string('x', 101);

        await Assert.ThrowsAsync<ArgumentException>(() => repo.CreateAsync(longName, null));
    }

    [Fact]
    public async Task RenameAsync_UpdatesName()
    {
        using var db = TestDbContextFactory.Create();
        var repo = new LocationRepository(db);
        var location = await repo.CreateAsync("客厅", null);

        var renamed = await repo.RenameAsync(location.Id, "卧室");

        Assert.Equal("卧室", renamed.Name);
    }

    [Fact]
    public async Task RenameAsync_NonExistentId_ThrowsKeyNotFoundException()
    {
        using var db = TestDbContextFactory.Create();
        var repo = new LocationRepository(db);

        await Assert.ThrowsAsync<KeyNotFoundException>(() => repo.RenameAsync(999, "不存在"));
    }

    [Fact]
    public async Task DeleteAsync_LeafNode_Succeeds()
    {
        using var db = TestDbContextFactory.Create();
        var repo = new LocationRepository(db);
        var location = await repo.CreateAsync("抽屉", null);

        await repo.DeleteAsync(location.Id);

        var exists = db.Locations.Any();
        Assert.False(exists);
    }

    [Fact]
    public async Task DeleteAsync_WithChildren_ThrowsInvalidOperationException()
    {
        using var db = TestDbContextFactory.Create();
        var repo = new LocationRepository(db);
        var parent = await repo.CreateAsync("客厅", null);
        await repo.CreateAsync("电视机柜", parent.Id);

        await Assert.ThrowsAsync<InvalidOperationException>(() => repo.DeleteAsync(parent.Id));
    }

    [Fact]
    public async Task GetChildrenAsync_ReturnsDirectChildren()
    {
        using var db = TestDbContextFactory.Create();
        var repo = new LocationRepository(db);
        var parent = await repo.CreateAsync("客厅", null);
        var child1 = await repo.CreateAsync("电视机柜", parent.Id);
        var child2 = await repo.CreateAsync("书架", parent.Id);

        var children = await repo.GetChildrenAsync(parent.Id);

        Assert.Equal(2, children.Count);
        Assert.Contains(children, c => c.Id == child1.Id);
        Assert.Contains(children, c => c.Id == child2.Id);
    }

    [Fact]
    public async Task GetChildrenAsync_NonExistentId_ThrowsKeyNotFoundException()
    {
        using var db = TestDbContextFactory.Create();
        var repo = new LocationRepository(db);

        await Assert.ThrowsAsync<KeyNotFoundException>(() => repo.GetChildrenAsync(999));
    }
}
