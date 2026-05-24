namespace BoxWise.Shared.Dtos;

public record CreateItemRequest(string Name, int LocationId, List<int> TagIds, string? Note);
