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

    public async Task<LocationDto?> CreateAsync(CreateLocationRequest request)
    {
        var response = await _http.PostAsJsonAsync("api/locations", request);
        if (!response.IsSuccessStatusCode) return null;
        return await response.Content.ReadFromJsonAsync<LocationDto>();
    }

    public async Task<LocationDto?> RenameAsync(int id, RenameLocationRequest request)
    {
        var response = await _http.PutAsJsonAsync($"api/locations/{id}", request);
        if (!response.IsSuccessStatusCode) return null;
        return await response.Content.ReadFromJsonAsync<LocationDto>();
    }

    public async Task<bool> DeleteAsync(int id)
    {
        var response = await _http.DeleteAsync($"api/locations/{id}");
        return response.IsSuccessStatusCode;
    }
}
