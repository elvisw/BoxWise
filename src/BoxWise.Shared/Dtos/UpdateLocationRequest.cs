using System.Text.Json;
using System.Text.Json.Serialization;

namespace BoxWise.Shared.Dtos;

/// <summary>
/// 更新位置资料。
/// <para>
/// <c>description</c> 省略时保持原描述不变；显式传空字符串或 <c>null</c> 时清除描述。
/// 使用非可空 <see cref="JsonElement"/> 区分“属性缺失”（<see cref="JsonValueKind.Undefined"/>）
/// 与“显式 null”（<see cref="JsonValueKind.Null"/>），因为可空 <see cref="JsonElement"/> 会把
/// 两者都反序列化为 null。
/// </para>
/// </summary>
public record UpdateLocationRequest(
    string Name,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    JsonElement Description = default);
