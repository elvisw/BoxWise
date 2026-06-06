namespace BoxWise.Shared.Dtos;

public sealed record LlmConfigDto(
    string? BaseUrl,
    string? ApiKey,
    string Model,
    int TimeoutSeconds)
{
    public override string ToString()
        => $"LlmConfigDto {{ BaseUrl = {BaseUrl}, ApiKey = ***, Model = {Model}, TimeoutSeconds = {TimeoutSeconds} }}";
}
