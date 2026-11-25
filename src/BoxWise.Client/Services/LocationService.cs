using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BoxWise.Shared.Dtos;
using BoxWise.Shared.Validation;

namespace BoxWise.Client.Services;

public class LocationService
{
    private readonly HttpClient _http;

    public LocationService(HttpClient http)
    {
        _http = http;
    }

    public async Task<List<LocationDto>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var response = await _http.GetAsync("api/locations", cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<List<LocationDto>>(cancellationToken) ?? [];
    }

    /// <summary>
    /// 读取单个位置元数据（描述、照片状态、完整名称路径）。失败时返回 null，不探测照片 URL。
    /// </summary>
    public async Task<LocationDto?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _http.GetAsync($"api/locations/{id}", cancellationToken);
            if (!response.IsSuccessStatusCode) return null;
            return await response.Content.ReadFromJsonAsync<LocationDto>(cancellationToken);
        }
        catch (HttpRequestException)
        {
            return null;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public async Task<LocationMutationResult<LocationDto>> CreateAsync(
        CreateLocationRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _http.PostAsJsonAsync(
                "api/locations", request, cancellationToken);
            return await ReadLocationResultAsync(response, cancellationToken);
        }
        catch (HttpRequestException)
        {
            return LocationMutationResult<LocationDto>.Unknown();
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return LocationMutationResult<LocationDto>.Unknown();
        }
    }

    public async Task<LocationMutationResult<LocationDto>> UpdateAsync(
        int id, UpdateLocationRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _http.PutAsJsonAsync(
                $"api/locations/{id}", request, cancellationToken);
            return await ReadLocationResultAsync(response, cancellationToken);
        }
        catch (HttpRequestException)
        {
            return LocationMutationResult<LocationDto>.Unknown();
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return LocationMutationResult<LocationDto>.Unknown();
        }
    }

    public async Task<LocationDeleteOutcome> DeleteAsync(
        int id, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _http.DeleteAsync($"api/locations/{id}", cancellationToken);

            if (response.StatusCode == HttpStatusCode.NoContent)
                return new LocationDeleteOutcome(LocationDeleteStatus.Deleted);

            if (response.IsSuccessStatusCode)
            {
                var warning = await TryReadCleanupWarningAsync(response, cancellationToken);
                return new LocationDeleteOutcome(
                    LocationDeleteStatus.Deleted, warning);
            }

            if (response.StatusCode == HttpStatusCode.NotFound)
                return new LocationDeleteOutcome(LocationDeleteStatus.NotFound);

            if ((int)response.StatusCode >= 500)
                return new LocationDeleteOutcome(LocationDeleteStatus.Unknown);

            return new LocationDeleteOutcome(
                LocationDeleteStatus.Rejected,
                Error: await ReadProblemAsync(response, cancellationToken));
        }
        catch (HttpRequestException)
        {
            return new LocationDeleteOutcome(LocationDeleteStatus.Unknown);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new LocationDeleteOutcome(LocationDeleteStatus.Unknown);
        }
    }

    public async Task<LocationMutationResult<LocationDto>> UploadPhotoAsync(
        int id,
        byte[] bytes,
        string contentType,
        string fileName,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var content = new MultipartFormDataContent();
            var file = new ByteArrayContent(bytes);
            file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
            content.Add(file, "file", fileName);

            using var request = new HttpRequestMessage(
                HttpMethod.Put, $"api/locations/{id}/photo")
            {
                Content = content
            };
            request.Headers.TryAddWithoutValidation("X-Requested-With", "XMLHttpRequest");

            var response = await _http.SendAsync(request, cancellationToken);

            if (response.StatusCode == HttpStatusCode.RequestEntityTooLarge)
                return LocationMutationResult<LocationDto>.Rejected(
                    LocationPhotoRules.TooLargeMessage);

            if (response.StatusCode == HttpStatusCode.UnsupportedMediaType)
                return LocationMutationResult<LocationDto>.Rejected(
                    LocationPhotoRules.UnsupportedFormatMessage);

            return await ReadLocationResultAsync(response, cancellationToken);
        }
        catch (HttpRequestException)
        {
            return LocationMutationResult<LocationDto>.Unknown();
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return LocationMutationResult<LocationDto>.Unknown();
        }
    }

    public async Task<LocationPhotoRemoveOutcome> RemovePhotoAsync(
        int id, CancellationToken cancellationToken = default)
    {
        try
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Delete, $"api/locations/{id}/photo");
            request.Headers.TryAddWithoutValidation("X-Requested-With", "XMLHttpRequest");

            var response = await _http.SendAsync(request, cancellationToken);

            if (response.StatusCode == HttpStatusCode.NoContent)
                return new LocationPhotoRemoveOutcome(LocationMutationStatus.Success);

            if (response.IsSuccessStatusCode)
            {
                var warning = await TryReadCleanupWarningAsync(response, cancellationToken);
                return new LocationPhotoRemoveOutcome(
                    LocationMutationStatus.Success, warning);
            }

            if (response.StatusCode == HttpStatusCode.NotFound)
                return new LocationPhotoRemoveOutcome(LocationMutationStatus.NotFound);

            if ((int)response.StatusCode >= 500)
                return new LocationPhotoRemoveOutcome(LocationMutationStatus.Unknown);

            return new LocationPhotoRemoveOutcome(
                LocationMutationStatus.Rejected,
                Error: await ReadProblemAsync(response, cancellationToken));
        }
        catch (HttpRequestException)
        {
            return new LocationPhotoRemoveOutcome(LocationMutationStatus.Unknown);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new LocationPhotoRemoveOutcome(LocationMutationStatus.Unknown);
        }
    }

    /// <summary>
    /// 按需读取已保存的位置照片。仅在用户主动展开/打开弹窗时调用。
    /// </summary>
    public async Task<LocationPhotoContent?> GetPhotoAsync(
        int id, long cacheKey, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _http.GetAsync(
                $"api/locations/{id}/photo?v={cacheKey}", cancellationToken);
            if (!response.IsSuccessStatusCode) return null;

            var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            if (bytes.Length == 0) return null;

            var contentType = response.Content.Headers.ContentType?.MediaType
                ?? "application/octet-stream";
            return new LocationPhotoContent(bytes, contentType);
        }
        catch (HttpRequestException)
        {
            return null;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static async Task<LocationMutationResult<LocationDto>> ReadLocationResultAsync(
        HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            try
            {
                var dto = await response.Content.ReadFromJsonAsync<LocationDto>(cancellationToken);
                return dto is null
                    ? LocationMutationResult<LocationDto>.Unknown()
                    : LocationMutationResult<LocationDto>.Success(dto);
            }
            catch (JsonException)
            {
                return LocationMutationResult<LocationDto>.Unknown();
            }
            catch (NotSupportedException)
            {
                return LocationMutationResult<LocationDto>.Unknown();
            }
        }

        if (response.StatusCode == HttpStatusCode.NotFound)
            return LocationMutationResult<LocationDto>.NotFound();

        if ((int)response.StatusCode >= 500)
            return LocationMutationResult<LocationDto>.Unknown();

        return LocationMutationResult<LocationDto>.Rejected(
            await ReadProblemAsync(response, cancellationToken));
    }

    private static async Task<bool> TryReadCleanupWarningAsync(
        HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(body)) return true;

            using var document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind != JsonValueKind.Object) return true;

            if (!document.RootElement.TryGetProperty("cleanupWarning", out var property)
                || property.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            {
                // 200 + 非预期正文：按“可能有清理警告”保守处理，不静默丢弃。
                return true;
            }

            return property.GetBoolean();
        }
        catch (JsonException)
        {
            // 200 + 非 JSON 正文：按可能有清理警告处理。
            return true;
        }
    }

    private static async Task<string?> ReadProblemAsync(
        HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(body)) return null;

            using var document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind != JsonValueKind.Object) return null;

            foreach (var propertyName in new[] { "detail", "title" })
            {
                if (document.RootElement.TryGetProperty(propertyName, out var property)
                    && property.ValueKind == JsonValueKind.String)
                {
                    return property.GetString();
                }
            }
        }
        catch (JsonException)
        {
            // 非 ProblemDetails 响应，交给上层显示通用失败提示。
        }

        return null;
    }
}
