namespace BoxWise.Client.Components;

/// <summary>
/// 用户在本机选中、尚未保存的位置照片。
/// </summary>
public sealed record LocationPhotoSelection(byte[] Bytes, string ContentType, string FileName);
