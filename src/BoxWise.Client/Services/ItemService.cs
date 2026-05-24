using System.Net.Http.Json;
using BoxWise.Shared.Dtos;

namespace BoxWise.Client.Services;

public class ItemService
{
    private readonly HttpClient _http;

    public ItemService(HttpClient http)
    {
        _http = http;
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        var response = await _http.DeleteAsync($"api/items/{id}", cancellationToken);
        return response.IsSuccessStatusCode;
    }

    public async Task<ItemDto?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        var response = await _http.GetAsync($"api/items/{id}", cancellationToken);
        if (!response.IsSuccessStatusCode) return null;
        return await response.Content.ReadFromJsonAsync<ItemDto>(cancellationToken);
    }

    public async Task<List<ItemSummaryDto>?> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await GetFilteredAsync(null, null, null, cancellationToken);
    }

    public async Task<List<ItemSummaryDto>?> SearchAsync(string query, CancellationToken cancellationToken = default)
    {
        return await GetFilteredAsync(null, null, query, cancellationToken);
    }

    public async Task<List<ItemSummaryDto>?> GetFilteredAsync(
        int? locationId, IReadOnlyCollection<int>? tagIds, string? query,
        CancellationToken cancellationToken = default)
    {
        var url = new System.Text.StringBuilder("api/items");
        var sep = '?';

        if (locationId.HasValue)
        {
            url.Append($"{sep}locationId={locationId.Value}"); sep = '&';
        }
        if (tagIds is { Count: > 0 })
        {
            foreach (var tagId in tagIds)
            {
                url.Append($"{sep}tagId={tagId}"); sep = '&';
            }
        }
        if (!string.IsNullOrWhiteSpace(query))
        {
            url.Append($"{sep}q={Uri.EscapeDataString(query.Trim())}"); sep = '&';
        }

        var response = await _http.GetAsync(url.ToString(), cancellationToken);
        if (!response.IsSuccessStatusCode) return null;
        return await response.Content.ReadFromJsonAsync<List<ItemSummaryDto>>(cancellationToken);
    }
}
