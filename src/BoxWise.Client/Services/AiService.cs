using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BoxWise.Shared.Dtos;

namespace BoxWise.Client.Services;

public class AiService
{
    private const int MaxImageBytes = 10 * 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly IHttpClientFactory _httpFactory;
    private readonly HttpClient _serverHttp;
    private readonly Lazy<Task<LlmConfigDto?>> _configCache;

    public AiService(IHttpClientFactory httpFactory, HttpClient serverHttp)
    {
        _httpFactory = httpFactory;
        _serverHttp = serverHttp;
        _configCache = new Lazy<Task<LlmConfigDto?>>(FetchConfigAsync);
    }

    private async Task<LlmConfigDto?> FetchConfigAsync()
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using var response = await _serverHttp.GetAsync("/api/llm/config", cts.Token);
            if (response.IsSuccessStatusCode)
                return await response.Content.ReadFromJsonAsync<LlmConfigDto>(cts.Token);
        }
        catch (OperationCanceledException) { }
        catch (HttpRequestException) { }
        catch (JsonException) { }
        return null;
    }

    public async Task<RecognitionResultDto?> RecognizeAsync(
        byte[] imageBytes, string contentType, CancellationToken cancellationToken = default)
    {
        if (imageBytes is null || imageBytes.Length == 0)
            return null;

        if (imageBytes.Length > MaxImageBytes)
            return null;

        var config = await _configCache.Value;
        if (config is null || string.IsNullOrWhiteSpace(config.ApiKey) || string.IsNullOrWhiteSpace(config.BaseUrl))
            return null;

        if (!Uri.TryCreate(config.BaseUrl, UriKind.Absolute, out var baseUri))
            return null;

        var model = string.IsNullOrWhiteSpace(config.Model) ? "doubao-seed-2-0-pro-260215" : config.Model.Trim();
        var timeoutSeconds = Math.Clamp(config.TimeoutSeconds, 5, 120);

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

            var base64 = Convert.ToBase64String(imageBytes);
            var mime = GetMimeType(contentType);
            var requestBody = new
            {
                model,
                messages = new[]
                {
                    new
                    {
                        role = "user",
                        content = new object[]
                        {
                            new { type = "text", text = "识别这张照片中的物品，返回物品名称和简短描述。请以JSON格式返回：{\"name\":\"物品名称\",\"note\":\"简要描述\"}" },
                            new { type = "image_url", image_url = new { url = $"data:{mime};base64,{base64}" } }
                        }
                    }
                },
                max_tokens = 200
            };

            using var httpClient = _httpFactory.CreateClient();
            httpClient.BaseAddress = baseUri;
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", config.ApiKey);

            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v3/chat/completions")
            {
                Content = JsonContent.Create(requestBody)
            };
            var response = await httpClient.SendAsync(request, cts.Token);

            if (!response.IsSuccessStatusCode)
                return null;

            var responseBody = await response.Content.ReadAsStringAsync(cts.Token);
            var result = JsonSerializer.Deserialize<OpenAiResponse>(responseBody, JsonOptions);
            var content = result?.Choices?.FirstOrDefault()?.Message?.Content;
            if (content is null) return null;

            return TryParse(content);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (HttpRequestException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static RecognitionResultDto? TryParse(string content)
    {
        try
        {
            var dto = JsonSerializer.Deserialize<RecognitionResultDto>(content, JsonOptions);
            if (dto is not null && !string.IsNullOrWhiteSpace(dto.Name))
                return dto;
            return null;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            var nameMatch = System.Text.RegularExpressions.Regex.Match(content, "\"name\"\\s*:\\s*\"([^\"]+)\"");
            var noteMatch = System.Text.RegularExpressions.Regex.Match(content, "\"note\"\\s*:\\s*\"([^\"]+)\"");
            if (nameMatch.Success)
                return new RecognitionResultDto(nameMatch.Groups[1].Value, noteMatch.Success ? noteMatch.Groups[1].Value : "");

            return null;
        }
    }

    private static string GetMimeType(string contentType) => contentType switch
    {
        "image/png" => "image/png",
        "image/webp" => "image/webp",
        _ => "image/jpeg"
    };

    internal class OpenAiResponse
    {
        public List<Choice>? Choices { get; set; }
    }

    internal class Choice
    {
        public Message? Message { get; set; }
    }

    internal class Message
    {
        public string? Content { get; set; }
    }
}
