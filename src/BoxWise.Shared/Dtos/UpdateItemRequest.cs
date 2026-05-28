namespace BoxWise.Shared.Dtos;

public record UpdateItemRequest(string Name, int LocationId, List<int> TagIds, string? Note);
