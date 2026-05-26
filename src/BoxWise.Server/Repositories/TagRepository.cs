using Microsoft.EntityFrameworkCore;
using BoxWise.Server.Data;
using BoxWise.Server.Models;

namespace BoxWise.Server.Repositories;

public class TagRepository
{
    private readonly AppDbContext _db;

    public TagRepository(AppDbContext db)
    {
        _db = db;
    }

    public async Task<List<Tag>> GetAllAsync()
    {
        return await _db.Tags
            .Include(t => t.Items)
            .OrderBy(t => t.Name)
            .ToListAsync();
    }

    public async Task<Tag> CreateAsync(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("标签名称不能为空");

        name = name.Trim();
        if (name.Length > 50)
            throw new ArgumentException("标签名称不能超过 50 个字符");

        var exists = await _db.Tags.AnyAsync(t => t.Name == name);
        if (exists)
            throw new ArgumentException($"标签 '{name}' 已存在");

        var tag = new Tag { Name = name };
        _db.Tags.Add(tag);
        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            throw new ArgumentException($"标签 '{name}' 已存在");
        }

        return tag;
    }

    public async Task<Tag> RenameAsync(int id, string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("标签名称不能为空");

        name = name.Trim();
        if (name.Length > 50)
            throw new ArgumentException("标签名称不能超过 50 个字符");

        var tag = await _db.Tags.FindAsync(id)
            ?? throw new KeyNotFoundException("标签不存在");

        var exists = await _db.Tags.AnyAsync(t => t.Name == name && t.Id != id);
        if (exists)
            throw new ArgumentException($"标签 '{name}' 已存在");

        tag.Name = name;
        await _db.SaveChangesAsync();
        return tag;
    }

    public async Task DeleteAsync(int id)
    {
        var tag = await _db.Tags.FindAsync(id)
            ?? throw new KeyNotFoundException("标签不存在");

        _db.Tags.Remove(tag);
        await _db.SaveChangesAsync();
    }

    public async Task<Tag> GetOrCreateAsync(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("标签名称不能为空");

        name = name.Trim();
        if (name.Length > 50)
            throw new ArgumentException("标签名称不能超过 50 个字符");

        var existing = await _db.Tags.FirstOrDefaultAsync(t => t.Name == name);
        if (existing is not null)
            return existing;

        var tag = new Tag { Name = name };
        _db.Tags.Add(tag);
        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            return await _db.Tags.FirstOrDefaultAsync(t => t.Name == name)
                ?? throw new InvalidOperationException($"并发创建标签 '{name}' 失败");
        }

        return tag;
    }
}
