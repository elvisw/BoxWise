using System.Net.Http.Json;
using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;

namespace BoxWise.Client.Services;

public class CookieAuthenticationStateProvider : AuthenticationStateProvider
{
    private readonly HttpClient _http;
    private readonly AppState _appState;

    public CookieAuthenticationStateProvider(HttpClient http, AppState appState)
    {
        _http = http;
        _appState = appState;
    }

    public override async Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        try
        {
            var response = await _http.GetAsync("api/auth/me");
            if (response.IsSuccessStatusCode)
            {
                var user = await response.Content.ReadFromJsonAsync<AuthUser>();
                if (user is { UserName: not null and not "" })
                {
                    if (_appState.CurrentUserName != user.UserName)
                        _appState.SetUser(user.UserName, user.IsAdmin, user.PasswordManagedByEnv);

                    var identity = new ClaimsIdentity(
                        claims: new[]
                        {
                            new Claim(ClaimTypes.Name, user.UserName),
                            new Claim("IsAdmin", user.IsAdmin.ToString())
                        },
                        authenticationType: "Identity.Application");

                    return new AuthenticationState(new ClaimsPrincipal(identity));
                }
            }
        }
        catch
        {
            // 未连接服务器或未登录
        }

        return new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity()));
    }

    public void NotifyAuthenticationStateChanged()
    {
        NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
    }

    private record AuthUser(string UserName, bool IsAdmin, bool PasswordManagedByEnv = false);
}
