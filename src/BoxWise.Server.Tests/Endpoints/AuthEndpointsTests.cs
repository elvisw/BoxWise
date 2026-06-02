using System.Reflection;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using BoxWise.Server.Data;
using BoxWise.Server.Endpoints;
using BoxWise.Server.Models;

namespace BoxWise.Server.Tests.Endpoints;

public class AuthEndpointsTests : IAsyncLifetime
{
    private TestIdentityContext _ctx = null!;
    private UserManager<AppUser> _userManager = null!;
    private IConfiguration _config = null!;

    public async Task InitializeAsync()
    {
        _ctx = await TestIdentityFactory.CreateAsync();
        _userManager = _ctx.UserManager;
        _config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>()).Build();
    }

    public async Task DisposeAsync() => await _ctx.DisposeAsync();

    private static async Task<int> InvokeAsync(string methodName, params object?[] args)
    {
        var method = typeof(AuthEndpoints).GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Static)!;
        var task = (Task)method.Invoke(null, args)!;
        await task;
        var httpResult = task.GetType().GetProperty("Result")!.GetValue(task)!;
        var executeMethod = httpResult.GetType().GetMethod("ExecuteAsync", [typeof(HttpContext)])!;
        var s = new ServiceCollection(); s.AddLogging(); s.AddDataProtection();
        using var sp = s.BuildServiceProvider();
        var hc = new DefaultHttpContext { RequestServices = sp };
        hc.Response.Body = new MemoryStream();
        await (Task)executeMethod.Invoke(httpResult, [hc])!;
        return hc.Response.StatusCode;
    }

    [Fact] public async Task GetCurrentUserAsync_Unauthenticated_ReturnsOk() { var hc = new DefaultHttpContext(); Assert.Equal(200, await InvokeAsync("GetCurrentUserAsync", _userManager, hc, _config)); }
    [Fact] public async Task GetCurrentUserAsync_Authenticated_ReturnsUser() { var u = new AppUser { UserName = "cu" }; await _userManager.CreateAsync(u, "Test1234!"); var hc = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, u.Id)], "test")) }; Assert.Equal(200, await InvokeAsync("GetCurrentUserAsync", _userManager, hc, _config)); }
}
