namespace BoxWise.Shared.Dtos;

public record LocationDto(
    int Id,
    string Name,
    string Path,
    int? ParentId,
    int SortOrder,
    string? Description = null,
    bool HasPhoto = false,
    string? NamePath = null);
