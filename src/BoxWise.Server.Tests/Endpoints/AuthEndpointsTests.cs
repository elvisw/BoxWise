using System.Reflection;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using BoxWise.Server.Endpoints;
using BoxWise.Server.Models;
using BoxWise.Shared.Dtos;

namespace BoxWise.Server.Tests.Endpoints;

public class AuthEndpointsTests : IAsyncDisposable
{
    private readonly TestIdentityContext _ctx;
    private readonly UserManager<AppUser> _userManager;
    private readonly IConfiguration _config;

    public AuthEndpointsTests()
    {
        _ctx = TestIdentityFactory.CreateAsync().GetAwaiter().GetResult();
        _userManager = _ctx.UserManager;
        _config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>()).Build();
    }

    public async ValueTask DisposeAsync() => await _ctx.DisposeAsync();

    private static async Task<int> InvokeAsync(string methodName, params object?[] args)
    {
        var method = typeof(AuthEndpoints).GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Static)!;
        var task = (Task)method.Invoke(null, args)!;
        await task;
        var httpResult = task.GetType().GetProperty("Result")!.GetValue(task)!;
        var executeMethod = httpResult.GetType().GetMethod("ExecuteAsync", [typeof(HttpContext)])!;
        var s = new ServiceCollection(); s.AddLogging();
        var hc = new DefaultHttpContext { RequestServices = s.BuildServiceProvider() };
        hc.Response.Body = new MemoryStream();
        await (Task)executeMethod.Invoke(httpResult, [hc])!;
        return hc.Response.StatusCode;
    }

    [Fact] public async Task GetCurrentUserAsync_Unauthenticated_ReturnsOk() { var hc = new DefaultHttpContext(); Assert.Equal(200, await InvokeAsync("GetCurrentUserAsync", _userManager, hc, _config)); }
    [Fact] public async Task GetCurrentUserAsync_Authenticated_ReturnsUser() { var u = new AppUser { UserName = "cu" }; await _userManager.CreateAsync(u, "Test1234!"); var hc = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, u.Id)], "test")) }; Assert.Equal(200, await InvokeAsync("GetCurrentUserAsync", _userManager, hc, _config)); }
    [Fact] public async Task UpdateProfileAsync_ValidName_Succeeds() { var u = new AppUser { UserName = "on" }; await _userManager.CreateAsync(u, "Test1234!"); var hc = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, u.Id)], "test")) }; Assert.Equal(200, await InvokeAsync("UpdateProfileAsync", new UpdateProfileRequest("nn"), _userManager, hc, _config)); }
    [Fact] public async Task UpdateProfileAsync_DuplicateName_Fails() { var u1 = new AppUser { UserName = "un1" }; var u2 = new AppUser { UserName = "un2" }; await _userManager.CreateAsync(u1, "Test1234!"); await _userManager.CreateAsync(u2, "Test1234!"); var hc = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, u1.Id)], "test")) }; Assert.Equal(400, await InvokeAsync("UpdateProfileAsync", new UpdateProfileRequest("un2"), _userManager, hc, _config)); }
    [Fact] public async Task ChangePasswordAsync_CorrectPassword_Succeeds() { var u = new AppUser { UserName = "pu" }; await _userManager.CreateAsync(u, "OldPass1!"); var hc = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, u.Id)], "test")) }; Assert.Equal(200, await InvokeAsync("ChangePasswordAsync", new ChangePasswordRequest("OldPass1!", "NewPass2!"), _userManager, hc)); }
    [Fact] public async Task ChangePasswordAsync_WrongCurrent_Fails() { var u = new AppUser { UserName = "pu2" }; await _userManager.CreateAsync(u, "OldPass1!"); var hc = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, u.Id)], "test")) }; Assert.Equal(400, await InvokeAsync("ChangePasswordAsync", new ChangePasswordRequest("WrongOld!", "NewPass2!"), _userManager, hc)); }
}
