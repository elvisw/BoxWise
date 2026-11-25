namespace BoxWise.Shared.Validation;

/// <summary>
/// 位置照片的格式与大小规则，客户端与服务端共用。
/// 单张照片严格限制为 10 MiB；multipart 请求体的额外开销单独计算。
/// </summary>
public static class LocationPhotoRules
{
    public const int MaxBytes = 10 * 1024 * 1024;

    /// <summary>
    /// multipart 边界、文件名和字段头的预留空间，不属于照片文件本身。
    /// </summary>
    public const int MultipartOverheadBytes = 1024 * 1024;

    public const string TooLargeMessage = "照片超过 10 MB，请换一张更小的图片。";

    public const string UnsupportedFormatMessage = "仅支持 JPG、PNG、WebP 格式的照片。";

    public const string CameraUnavailableMessage = "无法使用相机，请从设备选择照片。";

    private static readonly byte[] JpegMagic = [0xFF, 0xD8, 0xFF];
    private static readonly byte[] PngMagic = [0x89, 0x50, 0x4E, 0x47];
    private static readonly byte[] RiffMagic = [0x52, 0x49, 0x46, 0x46];
    private static readonly byte[] WebpMagic = [0x57, 0x45, 0x42, 0x50];

    public static bool IsAllowedContentType(string? contentType) => contentType?.ToLowerInvariant() switch
    {
        "image/jpeg" or "image/jpg" or "image/png" or "image/webp" => true,
        _ => false
    };

    /// <summary>
    /// 根据文件头判断实际图片类型；无法识别时返回 null。
    /// </summary>
    public static string? DetectContentType(ReadOnlySpan<byte> header)
    {
        if (header.Length >= 3 && header[..3].SequenceEqual(JpegMagic))
            return "image/jpeg";

        if (header.Length >= 4 && header[..4].SequenceEqual(PngMagic))
            return "image/png";

        if (header.Length >= 12
            && header[..4].SequenceEqual(RiffMagic)
            && header.Slice(8, 4).SequenceEqual(WebpMagic))
            return "image/webp";

        return null;
    }

    public static string? GetExtension(string? contentType) => contentType?.ToLowerInvariant() switch
    {
        "image/jpeg" or "image/jpg" => ".jpg",
        "image/png" => ".png",
        "image/webp" => ".webp",
        _ => null
    };
}
