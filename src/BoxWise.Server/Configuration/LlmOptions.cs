namespace BoxWise.Server.Configuration;

public class LlmOptions
{
    public const string SectionName = "Llm";

    public string BaseUrl { get; set; } = "https://api.openai.com/v1";
    public string Model { get; set; } = "gpt-4o";
    public string ApiKey { get; set; } = string.Empty;
}
