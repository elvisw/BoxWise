using System.Net.Http.Json;
using BoxWise.Shared.Dtos;

namespace BoxWise.Client.Services;

public class AiService
{
    private readonly HttpClient _http;

    public AiService(HttpClient http)
    {
        _http = http;
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
            cts.CancelAfter(TimeSpan.FromSeconds(20));

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
