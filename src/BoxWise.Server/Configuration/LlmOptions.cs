using System.ComponentModel.DataAnnotations;

namespace BoxWise.Server.Configuration;

public class LlmOptions
{
    public const string SectionName = "Llm";

    public string BaseUrl { get; set; } = "https://api.openai.com/v1";
    public string Model { get; set; } = "gpt-4o";
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// AI 识别请求超时时间（秒）。默认 60 秒。
    /// 生产 VPS 带宽有限 + 视觉模型推理较慢，15 秒极易超时。
    /// 可根据实际 API 响应速度调整。
    /// </summary>
    [Range(1, 600)]
    public int TimeoutSeconds { get; set; } = 60;
}
