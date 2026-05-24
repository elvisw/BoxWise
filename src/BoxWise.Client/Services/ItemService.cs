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

    public async Task<ItemDto?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        var response = await _http.GetAsync($"api/items/{id}", cancellationToken);
        if (!response.IsSuccessStatusCode) return null;
        return await response.Content.ReadFromJsonAsync<ItemDto>(cancellationToken);
    }

    public async Task<List<ItemSummaryDto>?> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var response = await _http.GetAsync("api/items", cancellationToken);
        if (!response.IsSuccessStatusCode) return null;
        return await response.Content.ReadFromJsonAsync<List<ItemSummaryDto>>(cancellationToken);
    }

    public async Task<List<ItemSummaryDto>?> SearchAsync(string query, CancellationToken cancellationToken = default)
    {
        var response = await _http.GetAsync($"api/items?q={Uri.EscapeDataString(query)}", cancellationToken);
        if (!response.IsSuccessStatusCode) return null;
        return await response.Content.ReadFromJsonAsync<List<ItemSummaryDto>>(cancellationToken);
    }
}
