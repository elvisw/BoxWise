using System.Net.Http.Json;
using BoxWise.Shared.Dtos;

namespace BoxWise.Client.Services;

public class ItemEntryService
{
    private readonly HttpClient _http;

    public ItemEntryService(HttpClient http)
    {
        _http = http;
    }

    public async Task<int?> CreateItemAsync(CreateItemRequest request, CancellationToken cancellationToken = default)
    {
        var response = await _http.PostAsJsonAsync("api/items", request, cancellationToken);
        if (!response.IsSuccessStatusCode) return null;
        var dto = await response.Content.ReadFromJsonAsync<ItemDto>(cancellationToken);
        return dto?.Id;
    }
}
