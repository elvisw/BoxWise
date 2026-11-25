using Microsoft.EntityFrameworkCore;
using BoxWise.Server.Data;
using BoxWise.Server.Models;
using BoxWise.Shared.Text;

namespace BoxWise.Server.Repositories;

public class LocationRepository
{
    private readonly AppDbContext _db;
    private const int MaxDepth = 10;

    public LocationRepository(AppDbContext db)
    {
        _db = db;
    }

    protected AppDbContext Db => _db;

    public async Task<Location> CreateAsync(
        string name, int? parentId, int sortOrder = 0, string? description = null)
    {
        name = NormalizeName(name);
        var normalizedDescription = NormalizeDescriptionOrThrow(description);

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
            Description = normalizedDescription,
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

    public async Task<Location> RenameAsync(int id, string name) =>
        await UpdateAsync(id, name, description: null, descriptionProvided: false);

    /// <summary>
    /// 更新位置名称，并按需更新描述。
    /// </summary>
    /// <param name="descriptionProvided">
    /// false 时保持原描述不变；true 时写入规范化后的描述（空值表示清除）。
    /// </param>
    public async Task<Location> UpdateAsync(
        int id, string name, string? description, bool descriptionProvided)
    {
        name = NormalizeName(name);
        var normalizedDescription = descriptionProvided
            ? NormalizeDescriptionOrThrow(description)
            : null;

        var location = await _db.Locations.FindAsync(id)
            ?? throw new KeyNotFoundException("位置不存在");

        location.Name = name;
        if (descriptionProvided)
            location.Description = normalizedDescription;

        await _db.SaveChangesAsync();
        return location;
    }

    /// <summary>
    /// 删除空位置。子位置/物品检查与删除由同一条条件 DELETE 原子完成，避免检查后被并发挂载。
    /// 返回被删除的实体，供调用方清理位置照片。
    /// </summary>
    public async Task<Location> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        if (_db.Database.IsRelational())
        {
            var location = await _db.Locations
                .AsNoTracking()
                .FirstOrDefaultAsync(l => l.Id == id, cancellationToken)
                ?? throw new KeyNotFoundException("位置不存在");

            // 单条条件 DELETE 在 SQLite 内原子执行：
            // 删除发生时只要子位置或物品已提交到该位置，语句就不会匹配到行。
            var deleted = await _db.Database.ExecuteSqlInterpolatedAsync($"""
                DELETE FROM "Locations"
                WHERE "Id" = {id}
                  AND NOT EXISTS (
                      SELECT 1 FROM "Locations" AS "child"
                      WHERE "child"."ParentId" = {id})
                  AND NOT EXISTS (
                      SELECT 1 FROM "Items" AS "item"
                      WHERE "item"."LocationId" = {id});
                """, cancellationToken);

            if (deleted == 1)
                return location;

            var hasChildren = await _db.Locations
                .AsNoTracking()
                .AnyAsync(l => l.ParentId == id, cancellationToken);
            if (hasChildren)
                throw new InvalidOperationException("无法删除：该位置下还有子位置");

            var hasItems = await _db.Items
                .AsNoTracking()
                .AnyAsync(i => i.LocationId == id, cancellationToken);
            if (hasItems)
                throw new InvalidOperationException("无法删除：该位置下还有物品");

            throw new KeyNotFoundException("位置不存在");
        }

        // 非关系型测试提供程序（InMemory）没有条件 SQL，保留内存检查路径。
        var memoryLocation = await _db.Locations
            .FirstOrDefaultAsync(l => l.Id == id, cancellationToken)
            ?? throw new KeyNotFoundException("位置不存在");

        var memoryHasChildren = await _db.Locations
            .AnyAsync(l => l.ParentId == id, cancellationToken);
        if (memoryHasChildren)
            throw new InvalidOperationException("无法删除：该位置下还有子位置");

        var memoryHasItems = await _db.Items
            .AnyAsync(i => i.LocationId == id, cancellationToken);
        if (memoryHasItems)
            throw new InvalidOperationException("无法删除：该位置下还有物品");

        _db.Locations.Remove(memoryLocation);
        await _db.SaveChangesAsync(cancellationToken);
        return memoryLocation;
    }

    public async Task<List<Location>> GetAllAsync()
    {
        return await _db.Locations
            .OrderBy(l => l.SortOrder)
            .ThenBy(l => l.Name)
            .ToListAsync();
    }

    public async Task<Location?> GetByIdAsync(int id)
    {
        return await _db.Locations.FirstOrDefaultAsync(l => l.Id == id);
    }

    public async Task<string?> ResolvePathNamesAsync(string idPath)
    {
        if (string.IsNullOrEmpty(idPath)) return null;

        var ids = ParseIds(idPath);
        if (ids.Count == 0) return null;

        var names = await _db.Locations
            .Where(l => ids.Contains(l.Id))
            .ToDictionaryAsync(l => l.Id, l => l.Name);

        return string.Join("/", ids.Select(id =>
            names.TryGetValue(id, out var n) ? n : "?"));
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
            foreach (var id in ParseIds(path))
                allIds.Add(id);
        }

        if (allIds.Count == 0)
            return new Dictionary<string, string?>();

        var nameDict = await _db.Locations
            .Where(l => allIds.Contains(l.Id))
            .ToDictionaryAsync(l => l.Id, l => l.Name);

        var result = new Dictionary<string, string?>();
        foreach (var path in paths)
        {
            result[path] = string.Join("/", ParseIds(path).Select(id =>
                nameDict.TryGetValue(id, out var n) ? n : "?"));
        }

        return result;
    }

    /// <summary>
    /// 将 ID 链解析为展示用完整路径，例如 <c>客厅 → 电视柜 → 抽屉</c>。
    /// </summary>
    public async Task<string?> ResolveDisplayPathAsync(string? idPath)
    {
        var ids = ParseIds(idPath);
        if (ids.Count == 0) return null;

        var names = await _db.Locations
            .Where(l => ids.Contains(l.Id))
            .ToDictionaryAsync(l => l.Id, l => l.Name);

        return BuildDisplayPath(ids, names);
    }

    public static string? BuildDisplayPath(
        string? idPath, IReadOnlyDictionary<int, string> names)
    {
        var ids = ParseIds(idPath);
        if (ids.Count == 0) return null;
        return BuildDisplayPath(ids, names);
    }

    private static string? BuildDisplayPath(
        IReadOnlyList<int> ids, IReadOnlyDictionary<int, string> names) =>
        string.Join(" → ", ids.Select(id =>
            names.TryGetValue(id, out var n) ? n : "?"));

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

    private static string NormalizeName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("位置名称不能为空");

        var trimmed = name.Trim();
        if (trimmed.Length > 100)
            throw new ArgumentException("位置名称不能超过 100 个字符");

        return trimmed;
    }

    private static string? NormalizeDescriptionOrThrow(string? description)
    {
        if (!GraphemeRules.TryNormalizeDescription(description, out var normalized, out var error))
            throw new ArgumentException(error);

        return normalized;
    }

    private static List<int> ParseIds(string? idPath)
    {
        if (string.IsNullOrEmpty(idPath)) return [];

        return idPath
            .Trim('/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(s => int.TryParse(s, out var id) ? id : (int?)null)
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .ToList();
    }
}
