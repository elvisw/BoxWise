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

    public async Task<Location> CreateAsync(string name, int? parentId, int sortOrder = 0)
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
            SortOrder = sortOrder,
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

        var hasItems = await _db.Items.AnyAsync(i => i.LocationId == id);
        if (hasItems)
            throw new InvalidOperationException("无法删除：该位置下还有物品");

        _db.Locations.Remove(location);
        await _db.SaveChangesAsync();
    }

    public async Task<List<Location>> GetAllAsync()
    {
        return await _db.Locations
            .OrderBy(l => l.SortOrder)
            .ThenBy(l => l.Name)
            .ToListAsync();
    }

    public async Task<string?> ResolvePathNamesAsync(string idPath)
    {
        if (string.IsNullOrEmpty(idPath)) return null;

        var ids = idPath.Trim('/').Split('/')
            .Select(s => int.TryParse(s, out var id) ? id : (int?)null)
            .ToList();

        var validIds = ids.Where(id => id.HasValue).Select(id => id!.Value).ToList();
        if (validIds.Count == 0) return null;

        var names = await _db.Locations
            .Where(l => validIds.Contains(l.Id))
            .ToDictionaryAsync(l => l.Id, l => l.Name);

        return string.Join("/", ids.Select(id =>
            id.HasValue && names.TryGetValue(id.Value, out var n) ? n : "?"));
    }

    public async Task<Dictionary<string, string?>> ResolvePathNamesBatchAsync(IEnumerable<string?> idPaths)
    {
        var paths = idPaths
            .Where(p => !string.IsNullOrEmpty(p))
            .Select(p => p!)
            .Distinct()
            .ToList();

        if (paths.Count == 0)
            return new Dictionary<string, string?>();

        var allIds = new HashSet<int>();
        foreach (var path in paths)
        {
            foreach (var segment in path.Trim('/').Split('/'))
            {
                if (int.TryParse(segment, out var id))
                    allIds.Add(id);
            }
        }

        if (allIds.Count == 0)
            return new Dictionary<string, string?>();

        var nameDict = await _db.Locations
            .Where(l => allIds.Contains(l.Id))
            .ToDictionaryAsync(l => l.Id, l => l.Name);

        var result = new Dictionary<string, string?>();
        foreach (var path in paths)
        {
            var names = path.Trim('/').Split('/')
                .Select(s => int.TryParse(s, out var i) && nameDict.TryGetValue(i, out var n) ? n : "?");
            result[path] = string.Join("/", names);
        }

        return result;
    }

    public async Task<List<Location>> GetChildrenAsync(int id)
    {
        var location = await _db.Locations.FindAsync(id);
        if (location is null)
            throw new KeyNotFoundException("位置不存在");

        return await _db.Locations
            .Where(l => l.ParentId == id)
            .OrderBy(l => l.SortOrder)
            .ThenBy(l => l.Name)
            .ToListAsync();
    }
}
