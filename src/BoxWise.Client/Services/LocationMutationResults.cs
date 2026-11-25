namespace BoxWise.Client.Services;

/// <summary>
/// 位置写操作的客户端结果分类。Unknown 只用于超时或响应丢失，不能断言服务端未写入。
/// </summary>
public enum LocationMutationStatus
{
    Success,
    Rejected,
    NotFound,
    Unknown
}

public sealed record LocationMutationResult<T>(
    LocationMutationStatus Status,
    T? Value = default,
    string? Error = null)
{
    public bool IsSuccess => Status == LocationMutationStatus.Success;

    public static LocationMutationResult<T> Success(T value) =>
        new(LocationMutationStatus.Success, value);

    public static LocationMutationResult<T> Rejected(string? error = null) =>
        new(LocationMutationStatus.Rejected, default, error);

    public static LocationMutationResult<T> NotFound() =>
        new(LocationMutationStatus.NotFound);

    public static LocationMutationResult<T> Unknown() =>
        new(LocationMutationStatus.Unknown);
}

public enum LocationDeleteStatus
{
    Deleted,
    Rejected,
    NotFound,
    Unknown
}

public sealed record LocationDeleteOutcome(
    LocationDeleteStatus Status,
    bool CleanupWarning = false,
    string? Error = null);

public sealed record LocationPhotoRemoveOutcome(
    LocationMutationStatus Status,
    bool CleanupWarning = false,
    string? Error = null);

public sealed record LocationPhotoContent(byte[] Bytes, string ContentType);
