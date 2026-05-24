namespace BoxWise.Shared.Dtos;

public record ItemSummaryDto(
    int Id,
    string Name,
    string? ThumbPath,
    string? LocationPath,
    IReadOnlyList<string> TagNames,
    DateTime CreatedAt);
