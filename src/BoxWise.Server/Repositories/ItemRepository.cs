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
            Note = note?.Trim(),
            CreatedByUserId = userId,
            CreatedAt = DateTime.UtcNow,
            Tags = tags
        };

        _db.Items.Add(item);
        await _db.SaveChangesAsync();
        await _db.Entry(item).Reference(i => i.CreatedByUser).LoadAsync();

        return item;
    }
}
