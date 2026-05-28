using Microsoft.AspNetCore.Http.HttpResults;

namespace BoxWise.Server.Services;

public class CsrfValidationFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var httpContext = context.HttpContext;
        var hasHeader = httpContext.Request.Headers.TryGetValue("X-Requested-With", out var header);

        if (!hasHeader || !string.Equals(header.ToString(), "XMLHttpRequest", StringComparison.OrdinalIgnoreCase))
        {
            return TypedResults.Problem("CSRF validation failed. Expected X-Requested-With: XMLHttpRequest header.", statusCode: 400);
        }

        return await next(context);
    }
}
