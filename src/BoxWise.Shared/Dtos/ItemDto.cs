namespace BoxWise.Shared.Dtos;

public record ItemDto(
    int Id,
    string Name,
    string? Note,
    string? PhotoPath,
    string? ThumbPath,
    string? MediumPath,
    int? LocationId,
    string? LocationName,
    string? LocationPath,
    IReadOnlyList<string> TagNames,
    string CreatedByUserName,
    DateTime CreatedAt);
