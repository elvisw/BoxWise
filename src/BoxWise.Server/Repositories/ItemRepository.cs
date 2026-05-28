using Microsoft.EntityFrameworkCore;
using BoxWise.Server.Data;
using BoxWise.Server.Models;

namespace BoxWise.Server.Repositories;

public class ItemRepository
{
    private readonly AppDbContext _db;

    public ItemRepository(AppDbContext db)
    {
        _db = db;
    }

    public async Task<Item> CreateAsync(string name, int locationId, List<int> tagIds, string? note, string userId)
    {
        tagIds ??= [];
        tagIds = tagIds.Distinct().ToList();

        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("物品名称不能为空");
        name = name.Trim();
        if (name.Length > 200)
            throw new ArgumentException("物品名称不能超过 200 个字符");

        if (note is not null && note.Length > 2000)
            throw new ArgumentException("备注不能超过 2000 个字符");

        var locationExists = await _db.Locations.AnyAsync(l => l.Id == locationId);
        if (!locationExists)
            throw new ArgumentException("位置不存在");

        var tags = await _db.Tags.Where(t => tagIds.Contains(t.Id)).ToListAsync();
        if (tags.Count != tagIds.Count)
            throw new ArgumentException("部分标签不存在");

        var item = new Item
        {
            Name = name,
            LocationId = locationId,
            Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim(),
            CreatedByUserId = userId,
            CreatedAt = DateTime.UtcNow,
            Version = Guid.NewGuid(),
            Tags = tags
        };

        _db.Items.Add(item);
        await _db.SaveChangesAsync();
        await _db.Entry(item).Reference(i => i.CreatedByUser).LoadAsync();
        await _db.Entry(item).Reference(i => i.Location).LoadAsync();

        return item;
    }

    public async Task<Item?> GetByIdAsync(int id)
    {
        return await _db.Items
            .Include(i => i.CreatedByUser)
            .Include(i => i.UpdatedByUser)
            .Include(i => i.Location)
            .Include(i => i.Tags)
            .FirstOrDefaultAsync(i => i.Id == id);
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken ct = default)
    {
        var item = await _db.Items.FindAsync([id], ct);
        if (item is null) return false;

        _db.Items.Remove(item);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<Item?> UpdateAsync(int id, string name, int locationId, List<int> tagIds, string? note, string userId, CancellationToken ct = default)
    {
        tagIds ??= [];
        tagIds = tagIds.Distinct().ToList();

        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("物品名称不能为空");
        name = name.Trim();
        if (name.Length > 200)
            throw new ArgumentException("物品名称不能超过 200 个字符");

        if (note is not null && note.Length > 2000)
            throw new ArgumentException("备注不能超过 2000 个字符");

        var locationExists = await _db.Locations.AnyAsync(l => l.Id == locationId, ct);
        if (!locationExists)
            throw new ArgumentException("位置不存在");

        var tags = await _db.Tags.Where(t => tagIds.Contains(t.Id)).ToListAsync(ct);
        if (tags.Count != tagIds.Count)
            throw new ArgumentException("部分标签不存在");

        var item = await _db.Items
            .Include(i => i.Location)
            .Include(i => i.CreatedByUser)
            .Include(i => i.UpdatedByUser)
            .Include(i => i.Tags)
            .FirstOrDefaultAsync(i => i.Id == id, ct);

        if (item is null) return null;

        item.Name = name;
        item.LocationId = locationId;
        item.Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        item.Tags.Clear();
        foreach (var tag in tags) item.Tags.Add(tag);
        item.UpdatedByUserId = userId;
        item.UpdatedByUser = await _db.Users.FindAsync([userId], ct);
        item.UpdatedAt = DateTime.UtcNow;
        item.Version = Guid.NewGuid();

        await _db.SaveChangesAsync(ct);
        return item;
    }

    public async Task<List<Item>> GetFilteredAsync(int? locationId, List<int>? tagIds, string? query)
    {
        IQueryable<Item> q = _db.Items
            .Include(i => i.Location)
            .Include(i => i.Tags)
            .AsNoTracking();

        if (locationId.HasValue)
        {
            var location = await _db.Locations.FindAsync(locationId.Value);
            if (location is not null)
            {
                q = q.Where(i => i.Location != null && i.Location.Path.StartsWith(location.Path));
            }
        }

        if (tagIds is { Count: > 0 })
        {
            foreach (var tagId in tagIds)
            {
                var id = tagId;
                q = q.Where(i => i.Tags.Any(t => t.Id == id));
            }
        }

        if (!string.IsNullOrWhiteSpace(query))
        {
            var keyword = query.Trim();
            q = q.Where(i => i.Name.Contains(keyword)
                          || (i.Note != null && i.Note.Contains(keyword))
                          || i.Tags.Any(t => t.Name.Contains(keyword)));
        }

        return await q
            .OrderByDescending(i => i.CreatedAt)
            .Take(100)
            .AsSplitQuery()
            .ToListAsync();
    }

}
