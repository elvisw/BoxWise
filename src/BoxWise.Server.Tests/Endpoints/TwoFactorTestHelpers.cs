using System.Reflection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using BoxWise.Server.Endpoints;

namespace BoxWise.Server.Tests.Endpoints;

public static class TwoFactorTestHelpers
{
    public static async Task<int> Invoke2FAAsync(string methodName, params object?[] args)
    {
        var method = typeof(TwoFactorEndpoints).GetMethod(
            methodName, BindingFlags.NonPublic | BindingFlags.Static)!;
        var task = (Task)method.Invoke(null, args)!;
        await task;
        var httpResult = task.GetType().GetProperty("Result")!.GetValue(task)!;
        var executeMethod = httpResult.GetType().GetMethod(
            "ExecuteAsync", [typeof(HttpContext)])!;
        var s = new ServiceCollection(); s.AddLogging();
        using var sp = s.BuildServiceProvider();
        var hc = new DefaultHttpContext { RequestServices = sp };
        hc.Response.Body = new MemoryStream();
        await (Task)executeMethod.Invoke(httpResult, [hc])!;
        return hc.Response.StatusCode;
    }

    public static async Task<(int StatusCode, string Body)> Invoke2FAWithBodyAsync(
        string methodName, params object?[] args)
    {
        var method = typeof(TwoFactorEndpoints).GetMethod(
            methodName, BindingFlags.NonPublic | BindingFlags.Static)!;
        var task = (Task)method.Invoke(null, args)!;
        await task;
        var httpResult = task.GetType().GetProperty("Result")!.GetValue(task)!;
        var executeMethod = httpResult.GetType().GetMethod(
            "ExecuteAsync", [typeof(HttpContext)])!;
        var s = new ServiceCollection(); s.AddLogging();
        using var sp = s.BuildServiceProvider();
        var hc = new DefaultHttpContext { RequestServices = sp };
        hc.Response.Body = new MemoryStream();
        await (Task)executeMethod.Invoke(httpResult, [hc])!;
        hc.Response.Body.Seek(0, SeekOrigin.Begin);
        var body = await new StreamReader(hc.Response.Body).ReadToEndAsync();
        return (hc.Response.StatusCode, body);
    }
}
