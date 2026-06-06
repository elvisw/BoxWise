namespace BoxWise.Server.Models;

public class LlmConfig
{
    public int Id { get; set; }
    public string? BaseUrl { get; set; }
    public string? ApiKey { get; set; }
    public string Model { get; set; } = "doubao-seed-2-0-pro-260215";
    public int TimeoutSeconds { get; set; } = 30;
}
