namespace BoxWise.Server.Services;

public class ImageStorageService
{
    private readonly string _basePath;

    public ImageStorageService(IConfiguration configuration)
    {
        _basePath = configuration["DataDirectory"] ?? "../data/images";
        Directory.CreateDirectory(_basePath);
    }

    public string GetItemDirectory(int itemId)
    {
        var dir = Path.Combine(_basePath, itemId.ToString());
        Directory.CreateDirectory(dir);
        return dir;
    }

    public async Task<string> SaveOriginalAsync(int itemId, Stream stream)
    {
        var dir = GetItemDirectory(itemId);
        var filePath = Path.Combine(dir, "original.jpg");

        await using var fileStream = File.Create(filePath);
        await stream.CopyToAsync(fileStream);

        return Path.Combine(itemId.ToString(), "original.jpg");
    }

    public string GetOriginalPath(int itemId)
        => Path.Combine(_basePath, itemId.ToString(), "original.jpg");

    public string GetThumbPath(int itemId)
        => Path.Combine(_basePath, itemId.ToString(), "thumb.jpg");

    public string GetMediumPath(int itemId)
        => Path.Combine(_basePath, itemId.ToString(), "medium.jpg");
}
