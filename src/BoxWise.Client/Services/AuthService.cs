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

    public async Task<(bool Success, string? Error)> UpdateProfileAsync(string newUsername)
    {
        var response = await _http.PutAsJsonAsync("api/auth/me", new UpdateProfileRequest(newUsername));

        if (response.IsSuccessStatusCode)
        {
            var user = await response.Content.ReadFromJsonAsync<AuthUserDto>();
            if (user is not null)
            {
                _appState.SetUser(user.UserName, user.IsAdmin);
                _authStateProvider.NotifyAuthenticationStateChanged();
            }
            return (true, null);
        }

        var error = await TryGetErrorAsync(response);
        return (false, error ?? "修改失败");
    }

    public async Task<(bool Success, string? Error)> ChangePasswordAsync(string currentPassword, string newPassword)
    {
        var response = await _http.PutAsJsonAsync("api/auth/me/password",
            new ChangePasswordRequest(currentPassword, newPassword));

        if (response.IsSuccessStatusCode)
            return (true, null);

        var error = await TryGetErrorAsync(response);
        return (false, error ?? "密码修改失败");
    }

    private static async Task<string?> TryGetErrorAsync(HttpResponseMessage response)
    {
        try
        {
            var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
            if (problem?.Errors?.Count > 0)
            {
                return string.Join("; ", problem.Errors.Values.SelectMany(v => v));
            }
            return problem?.Detail;
        }
        catch
        {
            return null;
        }
    }

    private record ProblemDetails
    {
        public string? Detail { get; init; }
        public Dictionary<string, string[]>? Errors { get; init; }
    }
}

public enum LoginResult
{
    Success,
    Failure
}
