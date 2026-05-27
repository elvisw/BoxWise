using System.Net;
using System.Text.Json;
using BoxWise.Client.Services;
using BoxWise.Shared.Dtos;
using Moq;
using Moq.Protected;

namespace BoxWise.Client.Tests.Services;

public class AuthServiceTests
{
    private static Mock<HttpMessageHandler> CreateHandler(HttpStatusCode status, string responseContent)
    {
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = status,
                Content = new StringContent(responseContent, System.Text.Encoding.UTF8, "application/json")
            });
        return handler;
    }

    private static (AuthService Service, AppState AppState, Mock<CookieAuthenticationStateProvider> AuthProviderMock)
        CreateService(
            HttpStatusCode status = HttpStatusCode.OK,
            string responseContent = "",
            Mock<HttpMessageHandler>? existingHandler = null)
    {
        var handler = existingHandler ?? CreateHandler(status, responseContent);
        var http = new HttpClient(handler.Object) { BaseAddress = new Uri("https://localhost:5000/") };
        var appState = new AppState();
        var authProviderMock = new Mock<CookieAuthenticationStateProvider>(http, appState);
        var service = new AuthService(http, appState, authProviderMock.Object);
        return (service, appState, authProviderMock);
    }

    [Fact]
    public async Task LoginAsync_Success_SetsAppState()
    {
        var dto = new AuthUserDto("elvis", true, false);
        var json = JsonSerializer.Serialize(dto);
        var (service, appState, _) = CreateService(responseContent: json);

        var result = await service.LoginAsync("elvis", "pass");

        Assert.Equal(LoginResult.Success, result);
        Assert.Equal("elvis", appState.CurrentUserName);
        Assert.True(appState.IsAdmin);
    }

    [Fact]
    public async Task LoginAsync_Success_FiresAppStateEvent()
    {
        var dto = new AuthUserDto("elvis", false, false);
        var json = JsonSerializer.Serialize(dto);
        var (service, appState, _) = CreateService(responseContent: json);
        var fired = false;
        appState.StateChanged += () => fired = true;

        await service.LoginAsync("elvis", "pass");

        Assert.True(fired);
    }

    [Fact]
    public async Task LoginAsync_Failure_DoesNotFireAppStateEvent()
    {
        var (service, appState, _) = CreateService(status: HttpStatusCode.Unauthorized);
        var fired = false;
        appState.StateChanged += () => fired = true;

        await service.LoginAsync("bad", "wrong");

        Assert.False(fired);
    }

    [Fact]
    public async Task LoginAsync_Failure_ReturnsFailure()
    {
        var (service, appState, _) = CreateService(status: HttpStatusCode.Unauthorized);

        var result = await service.LoginAsync("bad", "wrong");

        Assert.Equal(LoginResult.Failure, result);
        Assert.False(appState.IsLoggedIn);
    }

    [Fact]
    public async Task LoginAsync_NullResponse_UsesUsername()
    {
        // 返回 null JSON → ReadFromJsonAsync 返回 null → 使用 username 参数
        var (service, appState, _) = CreateService(responseContent: "null");

        await service.LoginAsync("elvis", "pass");

        Assert.Equal("elvis", appState.CurrentUserName);
    }

    [Fact]
    public async Task LogoutAsync_ClearsAppState()
    {
        var dto = new AuthUserDto("elvis", false, false);
        var json = JsonSerializer.Serialize(dto);
        var (service, appState, _) = CreateService(responseContent: json);
        await service.LoginAsync("elvis", "pass");

        // 重新 mock handler 为 logout 返回 200
        var handler2 = CreateHandler(HttpStatusCode.OK, "");
        var http2 = new HttpClient(handler2.Object) { BaseAddress = new Uri("https://localhost:5000/") };
        var authMock2 = new Mock<CookieAuthenticationStateProvider>(http2, appState);
        var service2 = new AuthService(http2, appState, authMock2.Object);

        await service2.LogoutAsync();

        Assert.False(appState.IsLoggedIn);
        Assert.Null(appState.CurrentUserName);
    }

    [Fact]
    public async Task LogoutAsync_FiresAppStateEvent()
    {
        var (service, appState, _) = CreateService();
        var fired = false;
        appState.StateChanged += () => fired = true;

        await service.LogoutAsync();

        Assert.True(fired);
    }

    [Fact]
    public async Task UpdateProfileAsync_Success_UpdatesUser()
    {
        var dto = new AuthUserDto("newname", false, false);
        var json = JsonSerializer.Serialize(dto);
        var (service, appState, authMock) = CreateService(responseContent: json);

        var (success, error) = await service.UpdateProfileAsync("newname");

        Assert.True(success);
        Assert.Null(error);
        Assert.Equal("newname", appState.CurrentUserName);
    }

    [Fact]
    public async Task UpdateProfileAsync_Success_FiresAppStateEvent()
    {
        var dto = new AuthUserDto("newname", false, false);
        var json = JsonSerializer.Serialize(dto);
        var (service, appState, _) = CreateService(responseContent: json);
        var fired = false;
        appState.StateChanged += () => fired = true;

        await service.UpdateProfileAsync("newname");

        Assert.True(fired);
    }

    [Fact]
    public async Task UpdateProfileAsync_Failure_ReturnsError()
    {
        var (service, _, _) = CreateService(status: HttpStatusCode.BadRequest, responseContent: "{}");

        var (success, error) = await service.UpdateProfileAsync("newname");

        Assert.False(success);
        Assert.NotNull(error);
    }

    [Fact]
    public async Task UpdateProfileAsync_Failure_ParsesDetail()
    {
        var problem = new { detail = "用户名已存在" };
        var json = JsonSerializer.Serialize(problem);
        var (service, _, _) = CreateService(status: HttpStatusCode.BadRequest, responseContent: json);

        var (success, error) = await service.UpdateProfileAsync("newname");

        Assert.False(success);
        Assert.Equal("用户名已存在", error);
    }

    [Fact]
    public async Task UpdateProfileAsync_Failure_ParsesValidationErrors()
    {
        var problem = new
        {
            detail = "验证失败",
            errors = new Dictionary<string, string[]>
            {
                ["UserName"] = new[] { "用户名不能为空", "用户名过长" }
            }
        };
        var json = JsonSerializer.Serialize(problem);
        var (service, _, _) = CreateService(status: HttpStatusCode.BadRequest, responseContent: json);

        var (success, error) = await service.UpdateProfileAsync("");

        Assert.False(success);
        Assert.Contains("用户名不能为空", error);
        Assert.Contains("用户名过长", error);
    }

    [Fact]
    public async Task ChangePasswordAsync_Success_ReturnsTrue()
    {
        var (service, _, _) = CreateService();

        var (success, error) = await service.ChangePasswordAsync("old", "new");

        Assert.True(success);
        Assert.Null(error);
    }

    [Fact]
    public async Task ChangePasswordAsync_Failure_ReturnsError()
    {
        var problem = new { detail = "当前密码错误" };
        var json = JsonSerializer.Serialize(problem);
        var (service, _, _) = CreateService(status: HttpStatusCode.BadRequest, responseContent: json);

        var (success, error) = await service.ChangePasswordAsync("wrong", "new");

        Assert.False(success);
        Assert.Equal("当前密码错误", error);
    }

    [Fact]
    public async Task ChangePasswordAsync_Failure_NoDetail_UsesDefault()
    {
        var (service, _, _) = CreateService(status: HttpStatusCode.BadRequest, responseContent: "{}");

        var (success, error) = await service.ChangePasswordAsync("old", "new");

        Assert.False(success);
        Assert.Equal("密码修改失败", error);
    }
}
