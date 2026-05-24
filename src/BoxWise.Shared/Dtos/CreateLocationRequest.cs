namespace BoxWise.Shared.Dtos;

public record CreateLocationRequest(string Name, int? ParentId, int SortOrder = 0);
