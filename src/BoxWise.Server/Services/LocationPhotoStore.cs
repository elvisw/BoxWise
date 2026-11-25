using BoxWise.Shared.Validation;

namespace BoxWise.Server.Services;

/// <summary>
/// 位置照片独立存储。与物品图片目录完全隔离，
/// 每次写入使用服务器生成的文件名，并以“先发布文件、后提交数据库指针”的顺序更新。
/// 单实例部署下用一把进程内互斥锁串行化照片写入与位置删除。
/// </summary>
public class LocationPhotoStore
{
    private readonly string _root;
    private readonly string _stagingRoot;
    private readonly SemaphoreSlim _mutex = new(1, 1);

    public LocationPhotoStore(IConfiguration configuration)
    {
        var dataDirectory = configuration["DataDirectory"];
        if (string.IsNullOrWhiteSpace(dataDirectory)) dataDirectory = "data";

        _root = Path.GetFullPath(Path.Combine(dataDirectory, "location-photos"));
        _stagingRoot = Path.Combine(_root, "staging");

        Directory.CreateDirectory(_root);
        Directory.CreateDirectory(_stagingRoot);

        // 暂存区文件从不被数据库引用；首次创建 store 时清理上次进程异常退出留下的文件。
        // 这里只处理 staging，不主动扫描或删除已发布但未被引用的孤立文件。
        CleanStagingDirectory();
    }

    public async ValueTask<IAsyncDisposable> LockAsync(CancellationToken cancellationToken = default)
    {
        await _mutex.WaitAsync(cancellationToken);
        return new MutexReleaser(_mutex);
    }

    public static string CreateFileName(int locationId, string extension) =>
        $"{locationId}-{Guid.NewGuid():N}{extension}";

    /// <summary>
    /// 将验证过的照片写入暂存区（与最终目录同一卷），返回暂存文件名。
    /// </summary>
    public virtual async Task<string> StageAsync(
        Stream source, string extension, CancellationToken cancellationToken = default)
    {
        var stagedName = $"{Guid.NewGuid():N}{extension}";
        var path = Path.Combine(_stagingRoot, stagedName);

        try
        {
            var options = new FileStreamOptions
            {
                Mode = FileMode.CreateNew,
                Access = FileAccess.Write,
                Share = FileShare.None,
                Options = FileOptions.Asynchronous
            };
            await using var file = new FileStream(path, options);
            await source.CopyToAsync(file, cancellationToken);
            return stagedName;
        }
        catch
        {
            TryDeletePath(path);
            throw;
        }
    }

    /// <summary>
    /// 将暂存文件发布到最终名称。发布完成后数据库指针才会更新。
    /// </summary>
    public virtual void Publish(string stagedName, string finalName) =>
        File.Move(StagingPath(stagedName), RootPath(finalName));

    /// <summary>
    /// 删除已确认不再被数据库引用的文件。文件不存在视为成功。
    /// </summary>
    public virtual void Delete(string fileName)
    {
        var path = RootPath(fileName);
        if (File.Exists(path)) File.Delete(path);
    }

    public void TryDelete(string fileName)
    {
        try
        {
            Delete(fileName);
        }
        catch (IOException)
        {
            // 清理失败不影响已提交的数据库状态。
        }
        catch (UnauthorizedAccessException)
        {
            // 同上。
        }
        catch (ArgumentException)
        {
            // 文件名无效时没有可清理的文件。
        }
    }

    public void TryDeleteStaged(string stagedName) =>
        TryDeletePath(StagingPath(stagedName));

    /// <summary>
    /// 打开已提交指针指向的文件并验证 MIME。扩展名与文件头不一致时返回 null。
    /// </summary>
    public (string Path, string ContentType)? OpenVerified(string fileName)
    {
        if (!IsSafeFileName(fileName)) return null;

        var path = RootPath(fileName);
        if (!File.Exists(path)) return null;

        var extension = Path.GetExtension(fileName);
        var header = new byte[12];
        int read;
        try
        {
            using var stream = new FileStream(
                path, FileMode.Open, FileAccess.Read,
                FileShare.Read | FileShare.Delete, 12, FileOptions.SequentialScan);
            read = stream.Read(header, 0, header.Length);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }

        var contentType = LocationPhotoRules.DetectContentType(header.AsSpan(0, read));
        if (contentType is null) return null;

        var expectedExtension = LocationPhotoRules.GetExtension(contentType);
        if (!string.Equals(extension, expectedExtension, StringComparison.OrdinalIgnoreCase))
            return null;

        return (path, contentType);
    }

    private string RootPath(string fileName)
    {
        if (!IsSafeFileName(fileName))
            throw new ArgumentException("无效的位置照片文件名", nameof(fileName));

        return Path.Combine(_root, fileName);
    }

    private string StagingPath(string stagedName)
    {
        if (!IsSafeFileName(stagedName))
            throw new ArgumentException("无效的位置照片暂存文件名", nameof(stagedName));

        return Path.Combine(_stagingRoot, stagedName);
    }

    private static bool IsSafeFileName(string fileName) =>
        !string.IsNullOrWhiteSpace(fileName)
        && fileName == Path.GetFileName(fileName)
        && !Path.IsPathRooted(fileName)
        && fileName.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;

    private static void TryDeletePath(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (IOException)
        {
            // 暂存文件清理失败无需中断请求。
        }
        catch (UnauthorizedAccessException)
        {
            // 同上。
        }
    }

    private void CleanStagingDirectory()
    {
        try
        {
            foreach (var file in Directory.EnumerateFiles(_stagingRoot))
                TryDeletePath(file);
        }
        catch (IOException)
        {
            // 清理失败不影响服务启动。
        }
        catch (UnauthorizedAccessException)
        {
            // 同上。
        }
    }

    private sealed class MutexReleaser(SemaphoreSlim mutex) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            mutex.Release();
            return ValueTask.CompletedTask;
        }
    }
}
