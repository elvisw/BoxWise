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
            _appState.SetUser(loginResponse.Username ?? username, loginResponse.IsAdmin ?? false, loginResponse.PasswordManagedByEnv, loginResponse.Email);
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

    public async Task<LoginResult> VerifyTwoFactorAsync(string code, string? token = null, string? method = null)
    {
        var response = await _http.PostAsJsonAsync("api/auth/2fa/verify", new VerifyTwoFactorRequest(code, token, method));

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

    /// <summary>
    /// 重新发送邮箱验证码，返回新 emailToken。
    /// </summary>
    public async Task<string?> ResendTwoFactorChallengeCodeAsync()
    {
        var response = await _http.PostAsync("api/auth/2fa/send-challenge-code", null);
        if (response.IsSuccessStatusCode)
        {
            var result = await response.Content.ReadFromJsonAsync<SendChallengeCodeResponse>();
            return result?.Token;
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

    public async Task<string?> SetupEmailTwoFactorAsync(string sessionToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "api/auth/2fa/setup-email");
        request.Headers.Add("X-Session-Token", sessionToken);

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

    // ===== 2FA Modify Methods (Story 9.1) =====

    /// <summary>
    /// 通过 2FA 验证获取 modify session token（TOTP / Email / RecoveryCode）。
    /// </summary>
    public async Task<string?> AuthenticateForModifyAsync(string code, string? method, string? token)
    {
        var response = await _http.PostAsJsonAsync("api/auth/2fa/modify/authenticate",
            new VerifyTwoFactorRequest(code, token, method));

        if (response.IsSuccessStatusCode)
        {
            var result = await response.Content.ReadFromJsonAsync<ReAuthenticateResponse>();
            return result?.SessionToken;
        }

        var error = await TryGetErrorAsync(response);
        throw new InvalidOperationException(error ?? "2FA 验证失败");
    }

    // ===== Email Verification Methods (Consolidated Settings) =====

    /// <summary>
    /// 发送邮箱验证码到新邮箱。需要 X-Session-Token（密码重新认证后）。
    /// </summary>
    public async Task<string?> SendEmailVerificationCodeAsync(string sessionToken, string newEmail)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "api/auth/email/send-code");
        request.Headers.Add("X-Session-Token", sessionToken);
        request.Content = JsonContent.Create(new { email = newEmail });

        var response = await _http.SendAsync(request);

        if (response.IsSuccessStatusCode)
        {
            var result = await response.Content.ReadFromJsonAsync<EmailVerificationSendResponse>();
            return result?.Token;
        }

        var error = await TryGetErrorAsync(response);
        throw new InvalidOperationException(error ?? "发送验证码失败");
    }

    /// <summary>
    /// 验证邮箱验证码，成功后返回 operation token。
    /// </summary>
    public async Task<(string? OperationToken, string? VerifiedEmail)> VerifyEmailCodeAsync(string code, string token)
    {
        var response = await _http.PostAsJsonAsync("api/auth/email/verify-code", new { code, token });

        if (response.IsSuccessStatusCode)
        {
            var result = await response.Content.ReadFromJsonAsync<EmailVerifyCodeResponse>();
            return (result?.OperationToken, result?.VerifiedEmail);
        }

        var error = await TryGetErrorAsync(response);
        throw new InvalidOperationException(error ?? "验证码验证失败");
    }

    /// <summary>
    /// 使用 operation token 更新邮箱（原子更新 user.Email + EmailForTwoFactor）。
    /// </summary>
    public async Task<(bool Success, string? Error)> UpdateEmailAsync(string newEmail, string operationToken)
    {
        var response = await _http.PutAsJsonAsync("api/auth/me",
            new UpdateProfileRequest(_appState.CurrentUserName ?? "", newEmail, operationToken));

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
        return (false, error ?? "邮箱修改失败");
    }

    /// <summary>
    /// 重置 TOTP：生成新密钥和 QR 码。
    /// </summary>
    public async Task<(string SecretKey, string QrCodeUri)?> ModifyTotpAsync(string sessionToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "api/auth/2fa/modify/totp");
        request.Headers.Add("X-Session-Token", sessionToken);

        var response = await _http.SendAsync(request);

        if (response.IsSuccessStatusCode)
        {
            var result = await response.Content.ReadFromJsonAsync<TwoFactorSetupResponse>();
            if (result is not null)
                return (result.SecretKey, result.QrCodeUri);
        }

        var error = await TryGetErrorAsync(response);
        throw new InvalidOperationException(error ?? "TOTP 重置失败");
    }

    /// <summary>
    /// 验证新 TOTP 密钥的验证码。
    /// </summary>
    public async Task<bool> VerifyModifyTotpAsync(string sessionToken, string code)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "api/auth/2fa/modify/totp/verify");
        request.Headers.Add("X-Session-Token", sessionToken);
        request.Content = JsonContent.Create(new VerifyTwoFactorRequest(code));

        var response = await _http.SendAsync(request);

        if (response.IsSuccessStatusCode)
            return true;

        var error = await TryGetErrorAsync(response);
        if (error is not null)
            throw new InvalidOperationException(error);

        return false;
    }

    /// <summary>
    /// 重新生成恢复码（旧码全部失效），需要 modify session token。
    /// </summary>
    public async Task<List<string>?> ModifyRegenerateRecoveryCodesAsync(string sessionToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "api/auth/2fa/modify/recovery/regenerate");
        request.Headers.Add("X-Session-Token", sessionToken);

        var response = await _http.SendAsync(request);

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

    // ===== WebAuthn Methods =====

    /// <summary>
    /// 检查 WebAuthn 在当前 origin 下是否可用（返回完整响应，含 Origin 和 UserHandle）。
    /// </summary>
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

    /// <summary>
    /// 获取已注册的 WebAuthn 凭据列表。
    /// </summary>
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

    /// <summary>
    /// 删除指定的 WebAuthn 凭据。
    /// </summary>
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

    /// <summary>
    /// 开始 WebAuthn 注册，获取 CredentialCreateOptions。
    /// POST（非 GET——端点修改服务器端 Session 状态）。
    /// </summary>
    public async Task<string?> StartWebAuthnRegistrationAsync()
    {
        var response = await _http.PostAsync("api/auth/webauthn/register-begin", null);
        if (!response.IsSuccessStatusCode) return null;
        return await response.Content.ReadAsStringAsync();
    }

    /// <summary>
    /// 完成 WebAuthn 注册，提交 attestation 并获取恢复码。
    /// 需要 using System.Text; 和 using System.Net.Http;
    /// </summary>
    public async Task<List<string>?> CompleteWebAuthnRegistrationAsync(string attestationJson, string deviceName)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "api/auth/webauthn/register-complete");
        request.Content = new StringContent(attestationJson, System.Text.Encoding.UTF8, "application/json");
        request.Headers.Add("X-Device-Name", deviceName.Trim());
        var response = await _http.SendAsync(request);
        if (!response.IsSuccessStatusCode) return null;

        var result = await response.Content.ReadFromJsonAsync<RecoveryCodesResponse>();
        _lastRecoveryCodes = result?.Codes;
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

    public async Task LogoutAsync()
    {
        await _http.PostAsync("api/auth/logout", null);
        _appState.Clear();
        _authStateProvider.NotifyAuthenticationStateChanged();
    }

    public async Task<(bool Success, string? Error)> UpdateProfileAsync(string newUsername, string? newEmail = null, string? operationToken = null)
    {
        var response = await _http.PutAsJsonAsync("api/auth/me", new UpdateProfileRequest(newUsername, newEmail, operationToken));

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
    PasswordRequiresChange,
    CredentialNotFound
}
