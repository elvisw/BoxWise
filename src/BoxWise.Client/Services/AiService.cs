using System.Net.Http.Json;
using BoxWise.Shared.Dtos;

namespace BoxWise.Client.Services;

public class AiService
{
    private readonly HttpClient _http;
    private readonly int _timeoutSeconds;

    public AiService(HttpClient http, IConfiguration configuration)
    {
        _http = http;
        // 客户端超时需大于服务端 LlmOptions.TimeoutSeconds（默认 60s）+ 网络往返
        _timeoutSeconds = Math.Clamp(configuration.GetValue("AiSettings:TimeoutSeconds", 90), 1, 600);
    }

    public async Task<RecognitionResultDto?> RecognizeAsync(
        Stream imageStream, string fileName, string contentType,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var content = new MultipartFormDataContent();
            var streamContent = new StreamContent(imageStream);
            streamContent.Headers.ContentType = new(contentType);
            content.Add(streamContent, "file", fileName);

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(_timeoutSeconds));

            var response = await _http.PostAsync("api/ai/recognize", content, cts.Token);
            if (!response.IsSuccessStatusCode) return null;

            return await response.Content.ReadFromJsonAsync<RecognitionResultDto>(cts.Token);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }
}
