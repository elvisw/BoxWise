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

    // ===== WebAuthn Methods =====

    public async Task<WebAuthnAvailableResponse?> GetWebAuthnAvailableInfoAsync()
    {
        try
        {
            var response = await _http.GetAsync("api/auth/webauthn/available");
            if (response.IsSuccessStatusCode)
                return await response.Content.ReadFromJsonAsync<WebAuthnAvailableResponse>();
        }
        catch { }
        return null;
    }

    public async Task<bool> GetWebAuthnAvailableAsync()
    {
        try
        {
            var response = await _http.GetAsync("api/auth/webauthn/available");
            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<WebAuthnAvailableResponse>();
                return result?.Available ?? false;
            }
        }
        catch { }
        return false;
    }

    public async Task<List<WebAuthnCredentialDto>> GetWebAuthnCredentialsAsync()
    {
        try
        {
            var response = await _http.GetAsync("api/auth/webauthn/credentials");
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<List<WebAuthnCredentialDto>>() ?? new();
        }
        catch (HttpRequestException)
        {
            throw new InvalidOperationException("登录已过期，请重新登录");
        }
    }

    public async Task<bool> DeleteWebAuthnCredentialAsync(int id)
    {
        try
        {
            var response = await _http.DeleteAsync($"api/auth/webauthn/credentials/{id}");
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return false;
            response.EnsureSuccessStatusCode();
            return true;
        }
        catch (HttpRequestException)
        {
            throw new InvalidOperationException("登录已过期，请重新登录");
        }
    }

    public async Task<string?> StartWebAuthnRegistrationAsync()
    {
        var response = await _http.PostAsync("api/auth/webauthn/register-begin", null);
        if (!response.IsSuccessStatusCode) return null;
        return await response.Content.ReadAsStringAsync();
    }

    public async Task<List<string>?> CompleteWebAuthnRegistrationAsync(string attestationJson, string deviceName)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "api/auth/webauthn/register-complete");
        request.Content = new StringContent(attestationJson, System.Text.Encoding.UTF8, "application/json");
        request.Headers.Add("X-Device-Name", deviceName.Trim());
        var response = await _http.SendAsync(request);
        if (!response.IsSuccessStatusCode) return null;

        var result = await response.Content.ReadFromJsonAsync<RecoveryCodesResponse>();
        return result?.Codes;
    }

    // ===== Passkey 无密码登录 =====

    public async Task<string?> StartWebAuthnLoginAsync()
    {
        var response = await _http.PostAsync("api/auth/webauthn/login-begin", null);
        if (!response.IsSuccessStatusCode) return null;
        return await response.Content.ReadAsStringAsync();
    }

    public async Task<LoginResult> CompleteWebAuthnLoginAsync(string assertionJson)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "api/auth/webauthn/login-complete");
        request.Content = new StringContent(assertionJson, System.Text.Encoding.UTF8, "application/json");
        var response = await _http.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                return LoginResult.CredentialNotFound;
            return LoginResult.Failure;
        }

        var user = await response.Content.ReadFromJsonAsync<AuthUserDto>();
        if (user is null)
            return LoginResult.Failure;
        _appState.SetUser(user.UserName, user.IsAdmin, user.PasswordManagedByEnv, user.Email);
        _authStateProvider.NotifyAuthenticationStateChanged();
        return LoginResult.Success;
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
    Failure,
    CredentialNotFound
}
