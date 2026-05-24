using Microsoft.EntityFrameworkCore;
using BoxWise.Server.Data;
using BoxWise.Server.Models;

namespace BoxWise.Server.Repositories;

public class LocationRepository
{
    private readonly AppDbContext _db;
    private const int MaxDepth = 10;

    public LocationRepository(AppDbContext db)
    {
        _db = db;
    }

    public async Task<Location> CreateAsync(string name, int? parentId)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("位置名称不能为空");

        name = name.Trim();
        if (name.Length > 100)
            throw new ArgumentException("位置名称不能超过 100 个字符");

        Location? parent = null;
        if (parentId is not null)
        {
            parent = await _db.Locations.FindAsync(parentId.Value)
                ?? throw new ArgumentException("父节点不存在");

            if (parent.Path.Split('/', StringSplitOptions.RemoveEmptyEntries).Length >= MaxDepth)
                throw new ArgumentException($"位置层级不能超过 {MaxDepth} 层");
        }

        var location = new Location
        {
            Name = name,
            ParentId = parentId,
            Path = "/"
        };

        _db.Locations.Add(location);
        await _db.SaveChangesAsync();

        location.Path = parent is not null
            ? $"{parent.Path}{location.Id}/"
            : $"/{location.Id}/";

        await _db.SaveChangesAsync();
        return location;
    }

    public async Task<Location> RenameAsync(int id, string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("位置名称不能为空");

        name = name.Trim();
        if (name.Length > 100)
            throw new ArgumentException("位置名称不能超过 100 个字符");

        var location = await _db.Locations.FindAsync(id)
            ?? throw new KeyNotFoundException("位置不存在");

        location.Name = name;
        await _db.SaveChangesAsync();
        return location;
    }

    public async Task DeleteAsync(int id)
    {
        var location = await _db.Locations.FindAsync(id)
            ?? throw new KeyNotFoundException("位置不存在");

        var hasChildren = await _db.Locations.AnyAsync(l => l.ParentId == id);
        if (hasChildren)
            throw new InvalidOperationException("无法删除：该位置下还有子位置");

        // TODO: 物品关联检查 (Story 3.1)
        // var hasItems = await _db.Items.AnyAsync(i => i.LocationId == id);
        // if (hasItems) throw new InvalidOperationException("无法删除：该位置下还有物品");

        _db.Locations.Remove(location);
        await _db.SaveChangesAsync();
    }
}
