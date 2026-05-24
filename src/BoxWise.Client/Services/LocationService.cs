using System.Net.Http.Json;
using BoxWise.Shared.Dtos;

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
}
