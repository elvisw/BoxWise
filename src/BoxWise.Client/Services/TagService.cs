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

    public async Task<List<TagDto>> GetAllAsync()
    {
        var response = await _http.GetAsync("api/tags");
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<List<TagDto>>() ?? [];
    }
}
