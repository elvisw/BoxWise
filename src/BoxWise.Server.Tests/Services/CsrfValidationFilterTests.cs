using BoxWise.Server.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace BoxWise.Server.Tests.Services;

public class CsrfValidationFilterTests
{
    private static DefaultHttpContext CreateContext(string? headerValue)
    {
        var context = new DefaultHttpContext();
        if (headerValue is not null)
        {
            context.Request.Headers["X-Requested-With"] = headerValue;
        }
        return context;
    }

    [Fact]
    public async Task ValidHeader_Passes()
    {
        var filter = new CsrfValidationFilter();
        var httpContext = CreateContext("XMLHttpRequest");
        var invocationContext = EndpointFilterInvocationContext.Create(httpContext, Array.Empty<object>());
        var nextCalled = false;

        var result = await filter.InvokeAsync(invocationContext, _ =>
        {
            nextCalled = true;
            return ValueTask.FromResult<object?>("ok");
        });

        Assert.True(nextCalled);
        Assert.Equal("ok", result);
    }

    [Fact]
    public async Task MissingHeader_Fails()
    {
        var filter = new CsrfValidationFilter();
        var httpContext = CreateContext(null);
        var invocationContext = EndpointFilterInvocationContext.Create(httpContext, Array.Empty<object>());
        var nextCalled = false;

        var result = await filter.InvokeAsync(invocationContext, _ =>
        {
            nextCalled = true;
            return ValueTask.FromResult<object?>("ok");
        });

        Assert.False(nextCalled);
        var problemResult = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(400, problemResult.StatusCode);
    }

    [Fact]
    public async Task WrongValue_Fails()
    {
        var filter = new CsrfValidationFilter();
        var httpContext = CreateContext("something-else");
        var invocationContext = EndpointFilterInvocationContext.Create(httpContext, Array.Empty<object>());
        var nextCalled = false;

        var result = await filter.InvokeAsync(invocationContext, _ =>
        {
            nextCalled = true;
            return ValueTask.FromResult<object?>("ok");
        });

        Assert.False(nextCalled);
        var problemResult = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(400, problemResult.StatusCode);
    }

    [Fact]
    public async Task CaseInsensitive()
    {
        var filter = new CsrfValidationFilter();
        var httpContext = CreateContext("xmlhttprequest");
        var invocationContext = EndpointFilterInvocationContext.Create(httpContext, Array.Empty<object>());
        var nextCalled = false;

        var result = await filter.InvokeAsync(invocationContext, _ =>
        {
            nextCalled = true;
            return ValueTask.FromResult<object?>("ok");
        });

        Assert.True(nextCalled);
        Assert.Equal("ok", result);
    }
}
