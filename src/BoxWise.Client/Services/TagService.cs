using System.Net.Http.Json;
using BoxWise.Shared.Dtos;

namespace BoxWise.Client.Services;

public class TagService
{
    private readonly HttpClient _http;

    public TagService(HttpClient http)
    {
        _http = http;
    }

    public async Task<List<TagDto>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var response = await _http.GetAsync("api/tags", cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<List<TagDto>>(cancellationToken) ?? [];
    }

    public async Task<TagDto?> CreateAsync(CreateTagRequest request)
    {
        var response = await _http.PostAsJsonAsync("api/tags", request);
        if (!response.IsSuccessStatusCode) return null;
        return await response.Content.ReadFromJsonAsync<TagDto>();
    }

    public async Task<TagDto?> RenameAsync(int id, RenameTagRequest request)
    {
        var response = await _http.PutAsJsonAsync($"api/tags/{id}", request);
        if (!response.IsSuccessStatusCode) return null;
        return await response.Content.ReadFromJsonAsync<TagDto>();
    }

    public async Task<bool> DeleteAsync(int id)
    {
        var response = await _http.DeleteAsync($"api/tags/{id}");
        return response.IsSuccessStatusCode;
    }
}
