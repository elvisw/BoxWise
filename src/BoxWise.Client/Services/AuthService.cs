using System.Net.Http.Json;
using BoxWise.Shared.Dtos;

namespace BoxWise.Client.Services;

public class AuthService
{
    private readonly HttpClient _http;
    private readonly AppState _appState;
    private readonly CookieAuthenticationStateProvider _authStateProvider;
    private List<string>? _lastRecoveryCodes;

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
            var loginResponse = await response.Content.ReadFromJsonAsync<LoginResponse>();
            if (loginResponse is null)
                return LoginResult.Failure;

            if (loginResponse.RequiresTwoFactor)
            {
                return LoginResult.RequiresTwoFactor;
            }

            // 所有非 TwoFactor 路径均需设置用户状态
            _appState.SetUser(loginResponse.Username ?? username, loginResponse.IsAdmin ?? false, false, loginResponse.Email);
            _authStateProvider.NotifyAuthenticationStateChanged();

            if (loginResponse.RequiresTwoFactorSetup)
            {
                return LoginResult.RequiresTwoFactorSetup;
            }

            if (loginResponse.PasswordRequiresChange)
            {
                return LoginResult.PasswordRequiresChange;
            }

            return LoginResult.Success;
        }

        return LoginResult.Failure;
    }

    public async Task<LoginResult> VerifyTwoFactorAsync(string code, string? token = null)
    {
        var response = await _http.PostAsJsonAsync("api/auth/2fa/verify", new VerifyTwoFactorRequest(code, token));

        if (response.IsSuccessStatusCode)
        {
            var user = await response.Content.ReadFromJsonAsync<AuthUserDto>();
            _appState.SetUser(user?.UserName ?? "", user?.IsAdmin ?? false, user?.PasswordManagedByEnv ?? false, user?.Email);
            _authStateProvider.NotifyAuthenticationStateChanged();
            return LoginResult.Success;
        }

        var error = await TryGetErrorAsync(response);
        if (error is not null)
            throw new InvalidOperationException(error);

        return LoginResult.Failure;
    }

    public async Task<TwoFactorStatusDto?> GetTwoFactorStatusAsync()
    {
        var response = await _http.GetAsync("api/auth/2fa/status");

        if (response.IsSuccessStatusCode)
        {
            return await response.Content.ReadFromJsonAsync<TwoFactorStatusDto>();
        }

        return null;
    }

    public async Task<TwoFactorChallengeResponse?> GetTwoFactorChallengeAsync()
    {
        var response = await _http.PostAsync("api/auth/2fa/challenge", null);

        if (response.IsSuccessStatusCode)
        {
            return await response.Content.ReadFromJsonAsync<TwoFactorChallengeResponse>();
        }

        return null;
    }

    public async Task<string?> ReAuthenticateAsync(string password)
    {
        var response = await _http.PostAsJsonAsync("api/auth/2fa/re-authenticate", new ReAuthenticateRequest(password));

        if (response.IsSuccessStatusCode)
        {
            var result = await response.Content.ReadFromJsonAsync<ReAuthenticateResponse>();
            return result?.SessionToken;
        }

        var error = await TryGetErrorAsync(response);
        throw new InvalidOperationException(error ?? "密码验证失败");
    }

    public async Task<(string SecretKey, string QrCodeUri)?> SetupTotpAsync(string sessionToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "api/auth/2fa/setup-totp");
        request.Headers.Add("X-Session-Token", sessionToken);

        var response = await _http.SendAsync(request);

        if (response.IsSuccessStatusCode)
        {
            var result = await response.Content.ReadFromJsonAsync<TwoFactorSetupResponse>();
            if (result is not null)
                return (result.SecretKey, result.QrCodeUri);
        }

        var error = await TryGetErrorAsync(response);
        throw new InvalidOperationException(error ?? "TOTP 设置失败");
    }

    public async Task<bool> VerifyTotpSetupAsync(string sessionToken, string code)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "api/auth/2fa/verify-totp");
        request.Headers.Add("X-Session-Token", sessionToken);
        request.Content = JsonContent.Create(new VerifyTwoFactorRequest(code));

        var response = await _http.SendAsync(request);

        if (response.IsSuccessStatusCode)
        {
            var result = await response.Content.ReadFromJsonAsync<RecoveryCodesResponse>();
            _lastRecoveryCodes = result?.Codes;
            return true;
        }

        var error = await TryGetErrorAsync(response);
        if (error is not null)
            throw new InvalidOperationException(error);

        return false;
    }

    public async Task<string?> SetupEmailTwoFactorAsync(string sessionToken, string email)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "api/auth/2fa/setup-email");
        request.Headers.Add("X-Session-Token", sessionToken);
        request.Content = JsonContent.Create(new SetupEmailTwoFactorRequest(email));

        var response = await _http.SendAsync(request);

        if (response.IsSuccessStatusCode)
        {
            var result = await response.Content.ReadFromJsonAsync<EmailTwoFactorSetupResponse>();
            return result?.Token;
        }

        var error = await TryGetErrorAsync(response);
        throw new InvalidOperationException(error ?? "发送验证码失败");
    }

    public async Task<(bool Success, List<string>? RecoveryCodes)> VerifyEmailTwoFactorAsync(string sessionToken, string code, string token)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "api/auth/2fa/verify-email");
        request.Headers.Add("X-Session-Token", sessionToken);
        request.Content = JsonContent.Create(new VerifyTwoFactorRequest(code, token));

        var response = await _http.SendAsync(request);

        if (response.IsSuccessStatusCode)
        {
            var result = await response.Content.ReadFromJsonAsync<RecoveryCodesResponse>();
            _lastRecoveryCodes = result?.Codes;
            return (true, result?.Codes);
        }

        var error = await TryGetErrorAsync(response);
        if (error is not null)
            throw new InvalidOperationException(error);

        return (false, null);
    }

    public async Task<LoginResult> VerifyRecoveryCodeDuringLoginAsync(string code)
    {
        var response = await _http.PostAsJsonAsync("api/auth/2fa/recovery/verify", new VerifyTwoFactorRequest(code));

        if (response.IsSuccessStatusCode)
        {
            var user = await response.Content.ReadFromJsonAsync<AuthUserDto>();
            _appState.SetUser(user?.UserName ?? "", user?.IsAdmin ?? false, user?.PasswordManagedByEnv ?? false, user?.Email);
            _authStateProvider.NotifyAuthenticationStateChanged();
            return LoginResult.Success;
        }

        var error = await TryGetErrorAsync(response);
        if (error is not null)
            throw new InvalidOperationException(error);

        return LoginResult.Failure;
    }

    public async Task<List<string>?> RegenerateRecoveryCodesAsync()
    {
        var response = await _http.PostAsync("api/auth/2fa/recovery/regenerate", null);

        if (response.IsSuccessStatusCode)
        {
            var result = await response.Content.ReadFromJsonAsync<RecoveryCodesResponse>();
            _lastRecoveryCodes = result?.Codes;
            return result?.Codes;
        }

        var error = await TryGetErrorAsync(response);
        if (error is not null)
            throw new InvalidOperationException(error);

        return null;
    }

    /// <summary>
    /// 获取最近一次设置的恢复码（组件间传递用）。
    /// </summary>
    public List<string>? GetLastRecoveryCodes() => _lastRecoveryCodes;

    /// <summary>
    /// 清除最近一次设置的恢复码。
    /// </summary>
    public void ClearLastRecoveryCodes() => _lastRecoveryCodes = null;

    // ===== WebAuthn Methods (stubs — full implementation in Story 8-5) =====

    /// <summary>
    /// 获取 WebAuthn 验证挑战。
    /// </summary>
    public async Task<string?> GetWebAuthnVerificationChallengeAsync()
    {
        var response = await _http.GetAsync("api/auth/webauthn/verify-begin");
        if (response.IsSuccessStatusCode)
        {
            return await response.Content.ReadAsStringAsync();
        }
        return null;
    }

    /// <summary>
    /// 验证 WebAuthn 断言。
    /// </summary>
    public async Task<LoginResult> VerifyWebAuthnAsync(string credentialJson, string challengeJson)
    {
        var response = await _http.PostAsJsonAsync("api/auth/webauthn/verify-complete",
            new { Credential = credentialJson, Challenge = challengeJson });

        if (response.IsSuccessStatusCode)
            return LoginResult.Success;

        return LoginResult.Failure;
    }

    /// <summary>
    /// 检查 WebAuthn 在当前 origin 下是否可用。
    /// </summary>
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

    public async Task LogoutAsync()
    {
        await _http.PostAsync("api/auth/logout", null);
        _appState.Clear();
        _authStateProvider.NotifyAuthenticationStateChanged();
    }

    public async Task<(bool Success, string? Error)> UpdateProfileAsync(string newUsername, string? newEmail = null)
    {
        var response = await _http.PutAsJsonAsync("api/auth/me", new UpdateProfileRequest(newUsername, newEmail));

        if (response.IsSuccessStatusCode)
        {
            var user = await response.Content.ReadFromJsonAsync<AuthUserDto>();
            if (user is not null)
            {
                _appState.SetUser(user.UserName, user.IsAdmin, user.PasswordManagedByEnv, user.Email);
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
    Failure,
    RequiresTwoFactor,
    RequiresTwoFactorSetup,
    PasswordRequiresChange
}
