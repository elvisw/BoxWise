namespace BoxWise.Shared.Dtos;

/// <summary>
/// 位置或位置照片已从数据库移除，但磁盘文件清理失败。
/// </summary>
public record LocationCleanupWarning(bool CleanupWarning);
