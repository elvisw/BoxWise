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

    public static IEnumerable<object[]> InvalidLocationNames =>
        new List<object[]>
        {
            new object[] { "" },
            new object[] { new string('x', 101) }
        };

    [Theory]
    [MemberData(nameof(InvalidLocationNames))]
    public async Task CreateAsync_InvalidName_ThrowsArgumentException(string invalidName)
    {
        using var db = TestDbContextFactory.Create();
        var repo = new LocationRepository(db);

        await Assert.ThrowsAsync<ArgumentException>(() => repo.CreateAsync(invalidName, null));
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

    public static IEnumerable<object[]> InvalidLocationNamesForRename =>
        new List<object[]>
        {
            new object[] { "" },
            new object[] { new string('x', 101) }
        };

    [Theory]
    [MemberData(nameof(InvalidLocationNamesForRename))]
    public async Task RenameAsync_InvalidName_ThrowsArgumentException(string invalidName)
    {
        using var db = TestDbContextFactory.Create();
        var repo = new LocationRepository(db);
        var location = await repo.CreateAsync("客厅", null);

        await Assert.ThrowsAsync<ArgumentException>(() => repo.RenameAsync(location.Id, invalidName));
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

    [Fact]
    public async Task ResolvePathNamesAsync_ValidPath_ReturnsNamePath()
    {
        using var db = TestDbContextFactory.Create();
        var repo = new LocationRepository(db);
        var root = await repo.CreateAsync("客厅", null);
        var child = await repo.CreateAsync("电视机柜", root.Id);

        var path = await repo.ResolvePathNamesAsync($"/{root.Id}/{child.Id}/");

        Assert.Equal($"客厅/电视机柜", path);
    }

    [Fact]
    public async Task ResolvePathNamesAsync_NullOrEmpty_ReturnsNull()
    {
        using var db = TestDbContextFactory.Create();
        var repo = new LocationRepository(db);

        Assert.Null(await repo.ResolvePathNamesAsync(null!));
        Assert.Null(await repo.ResolvePathNamesAsync(""));
    }

    [Fact]
    public async Task ResolvePathNamesAsync_DeletedId_ShowsQuestionMark()
    {
        using var db = TestDbContextFactory.Create();
        var repo = new LocationRepository(db);
        var root = await repo.CreateAsync("客厅", null);
        var child = await repo.CreateAsync("电视机柜", root.Id);

        // 删除子位置，路径中的 ID 不再存在于数据库
        await repo.DeleteAsync(child.Id);

        var path = await repo.ResolvePathNamesAsync($"/{root.Id}/{child.Id}/");

        Assert.Equal("客厅/?", path);
    }

    [Fact]
    public async Task ResolvePathNamesAsync_OnlySeparators_ReturnsNull()
    {
        using var db = TestDbContextFactory.Create();
        var repo = new LocationRepository(db);

        var path = await repo.ResolvePathNamesAsync("///");

        Assert.Null(path);
    }

    [Fact]
    public async Task ResolvePathNamesBatchAsync_ResolvesMultiplePathsWithOneQuery()
    {
        using var db = TestDbContextFactory.Create();
        var repo = new LocationRepository(db);
        var root = await repo.CreateAsync("客厅", null);
        var child = await repo.CreateAsync("电视机柜", root.Id);

        var pathDict = await repo.ResolvePathNamesBatchAsync([
            $"/{root.Id}/",
            $"/{root.Id}/{child.Id}/"
        ]);

        Assert.Equal(2, pathDict.Count);
        Assert.Equal("客厅", pathDict[$"/{root.Id}/"]);
        Assert.Equal("客厅/电视机柜", pathDict[$"/{root.Id}/{child.Id}/"]);
    }

    [Fact]
    public async Task ResolvePathNamesBatchAsync_OverlappingIds_SingleQuery()
    {
        using var db = TestDbContextFactory.Create();
        var repo = new LocationRepository(db);
        var root = await repo.CreateAsync("客厅", null);

        var pathDict = await repo.ResolvePathNamesBatchAsync([
            $"/{root.Id}/",
            $"/{root.Id}/",
            $"/{root.Id}/"
        ]);

        Assert.Single(pathDict);
        Assert.Equal("客厅", pathDict[$"/{root.Id}/"]);
    }

    [Fact]
    public async Task ResolvePathNamesBatchAsync_EmptyInput_ReturnsEmptyDict()
    {
        using var db = TestDbContextFactory.Create();
        var repo = new LocationRepository(db);

        var pathDict = await repo.ResolvePathNamesBatchAsync([]);

        Assert.Empty(pathDict);
    }

    [Fact]
    public async Task ResolvePathNamesBatchAsync_AllNullPaths_ReturnsEmptyDict()
    {
        using var db = TestDbContextFactory.Create();
        var repo = new LocationRepository(db);

        var pathDict = await repo.ResolvePathNamesBatchAsync([null, ""]);

        Assert.Empty(pathDict);
    }

    [Fact]
    public async Task ResolvePathNamesBatchAsync_ConsistentWithSingleAsync()
    {
        using var db = TestDbContextFactory.Create();
        var repo = new LocationRepository(db);
        var root = await repo.CreateAsync("客厅", null);
        var child = await repo.CreateAsync("电视机柜", root.Id);

        var idPath = $"/{root.Id}/{child.Id}/";
        var single = await repo.ResolvePathNamesAsync(idPath);
        var batch = await repo.ResolvePathNamesBatchAsync([idPath]);

        Assert.Equal(single, batch[idPath]);
    }

    // ──────────── GetAllAsync ────────────

    [Fact]
    public async Task GetAllAsync_ReturnsAllOrderedBySortOrder()
    {
        using var db = TestDbContextFactory.Create();
        var repo = new LocationRepository(db);
        await repo.CreateAsync("第二", null, sortOrder: 2);
        await repo.CreateAsync("第一", null, sortOrder: 1);
        await repo.CreateAsync("第三", null, sortOrder: 3);

        var locations = await repo.GetAllAsync();

        Assert.Equal(3, locations.Count);
        Assert.Equal("第一", locations[0].Name);
        Assert.Equal("第二", locations[1].Name);
        Assert.Equal("第三", locations[2].Name);
    }

    [Fact]
    public async Task GetAllAsync_SameSortOrder_ThenByNames()
    {
        using var db = TestDbContextFactory.Create();
        var repo = new LocationRepository(db);
        await repo.CreateAsync("C", null, sortOrder: 1);
        await repo.CreateAsync("A", null, sortOrder: 1);
        await repo.CreateAsync("B", null, sortOrder: 1);

        var locations = await repo.GetAllAsync();

        // 相同 SortOrder 按 Name 排序
        Assert.Equal("A", locations[0].Name);
        Assert.Equal("B", locations[1].Name);
        Assert.Equal("C", locations[2].Name);
    }

    [Fact]
    public async Task GetAllAsync_Empty_ReturnsEmptyList()
    {
        using var db = TestDbContextFactory.Create();
        var repo = new LocationRepository(db);

        var locations = await repo.GetAllAsync();

        Assert.Empty(locations);
    }

    // ──────────── CreateAsync 边界 ────────────

    [Fact]
    public async Task CreateAsync_NonExistentParent_ThrowsArgumentException()
    {
        using var db = TestDbContextFactory.Create();
        var repo = new LocationRepository(db);

        await Assert.ThrowsAsync<ArgumentException>(() => repo.CreateAsync("子节点", parentId: 999));
    }

    [Fact]
    public async Task CreateAsync_ExceedsMaxDepth_ThrowsArgumentException()
    {
        using var db = TestDbContextFactory.Create();
        var repo = new LocationRepository(db);

        // 构建 10 层深链
        Location? parent = null;
        for (int i = 1; i <= 10; i++)
        {
            parent = await repo.CreateAsync($"第{i}层", parent?.Id);
        }

        // 第 11 层应抛出 ArgumentException
        await Assert.ThrowsAsync<ArgumentException>(() => repo.CreateAsync("第11层", parent!.Id));
    }

    [Fact]
    public async Task CreateAsync_AtMaxDepth_Succeeds()
    {
        using var db = TestDbContextFactory.Create();
        var repo = new LocationRepository(db);

        // 构建 9 层 → 第 10 层应创建成功
        Location? parent = null;
        for (int i = 1; i < 10; i++)
        {
            parent = await repo.CreateAsync($"第{i}层", parent?.Id);
        }

        var deepest = await repo.CreateAsync("第10层", parent?.Id);

        Assert.NotNull(deepest);
        Assert.True(deepest.Id > 0);
    }

    // ──────────── DeleteAsync 边界 ────────────

    [Fact]
    public async Task DeleteAsync_WithItems_ThrowsInvalidOperationException()
    {
        using var db = TestDbContextFactory.Create();
        var repo = new LocationRepository(db);
        var location = await repo.CreateAsync("客厅", null);

        // 创建关联 Item
        var item = new Item
        {
            Name = "电视",
            LocationId = location.Id,
            CreatedByUserId = "user-1",
            CreatedAt = DateTime.UtcNow
        };
        db.Items.Add(item);
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => repo.DeleteAsync(location.Id));
    }

}
