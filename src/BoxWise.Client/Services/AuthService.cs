using System.Net.Http.Json;
using BoxWise.Shared.Dtos;

namespace BoxWise.Client.Services;

public class AuthService
{
    private readonly HttpClient _http;
    private readonly AppState _appState;
    private readonly CookieAuthenticationStateProvider _authStateProvider;

    public AuthService(HttpClient http, AppState appState, CookieAuthenticationStateProvider authStateProvider)
    {
        _http = http;
        _appState = appState;
        _authStateProvider = authStateProvider;
    }

    public async Task<LoginResult> LoginAsync(string username, string password)
    {
        var response = await _http.PostAsJsonAsync("api/auth/login", new LoginRequest(username, password));

        if (response.IsSuccessStatusCode)
        {
            var user = await response.Content.ReadFromJsonAsync<AuthUserDto>();
            _appState.SetUser(user?.UserName ?? username, user?.IsAdmin ?? false);
            _authStateProvider.NotifyAuthenticationStateChanged();
            return LoginResult.Success;
        }

        return LoginResult.Failure;
    }

    public async Task LogoutAsync()
    {
        await _http.PostAsync("api/auth/logout", null);
        _appState.Clear();
        _authStateProvider.NotifyAuthenticationStateChanged();
    }

    private record AuthUserDto(string UserName, bool IsAdmin);
}

public enum LoginResult
{
    Success,
    Failure
}
