using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using BoxWise.Server.Configuration;
using BoxWise.Shared.Dtos;

namespace BoxWise.Server.Services;

public class LlmClient
{
    private const int MaxImageBytes = 10 * 1024 * 1024; // 10MB
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly HttpClient _http;
    private readonly LlmOptions _options;
    private readonly ILogger<LlmClient> _logger;

    public LlmClient(HttpClient http, IOptions<LlmOptions> options, ILogger<LlmClient> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<RecognitionResultDto?> RecognizeAsync(string imagePath)
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

            var fileInfo = new FileInfo(imagePath);
            if (!fileInfo.Exists || fileInfo.Length > MaxImageBytes)
            {
                _logger.LogWarning("Image file too large or missing: {Path}, {Size}", imagePath, fileInfo.Exists ? fileInfo.Length : -1);
                return null;
            }

            var imageBase64 = Convert.ToBase64String(await File.ReadAllBytesAsync(imagePath, cts.Token));
            var requestBody = new
            {
                model = _options.Model,
                messages = new[]
                {
                    new
                    {
                        role = "user",
                        content = new object[]
                        {
                            new { type = "text", text = "识别这张照片中的物品，返回物品名称和简短描述。请以JSON格式返回：{\"name\":\"物品名称\",\"note\":\"简要描述\"}" },
                            new { type = "image_url", image_url = new { url = $"data:image/jpeg;base64,{imageBase64}" } }
                        }
                    }
                },
                max_tokens = 200
            };

            var url = $"{_options.BaseUrl.TrimEnd('/')}/chat/completions";
            var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = JsonContent.Create(requestBody)
            };
            request.Headers.Add("Authorization", $"Bearer {_options.ApiKey}");

            var response = await _http.SendAsync(request, cts.Token);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("AI API returned {StatusCode}", (int)response.StatusCode);
                return null;
            }

            var result = await response.Content.ReadFromJsonAsync<OpenAiResponse>(cancellationToken: cts.Token);
            var content = result?.Choices?.FirstOrDefault()?.Message?.Content;
            if (content is null) return null;

            return TryParse(content);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("AI API 超时，降级为手动输入");
            return null;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _logger.LogWarning(ex, "AI 识别失败，降级为手动输入");
            return null;
        }
    }

    private static RecognitionResultDto? TryParse(string content)
    {
        try
        {
            return JsonSerializer.Deserialize<RecognitionResultDto>(content, JsonOptions);
        }
        catch
        {
            var nameMatch = System.Text.RegularExpressions.Regex.Match(content, "\"name\"\\s*:\\s*\"([^\"]+)\"");
            var noteMatch = System.Text.RegularExpressions.Regex.Match(content, "\"note\"\\s*:\\s*\"([^\"]+)\"");
            if (nameMatch.Success)
                return new RecognitionResultDto(nameMatch.Groups[1].Value, noteMatch.Success ? noteMatch.Groups[1].Value : "");

            return null;
        }
    }

    private class OpenAiResponse
    {
        public List<Choice>? Choices { get; set; }
    }

    private class Choice
    {
        public Message? Message { get; set; }
    }

    private class Message
    {
        public string? Content { get; set; }
    }
}
