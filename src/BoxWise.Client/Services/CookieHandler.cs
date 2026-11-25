using Microsoft.AspNetCore.Components.WebAssembly.Http;

namespace BoxWise.Client.Services;

public class CookieHandler : HttpClientHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.SetBrowserRequestCredentials(BrowserRequestCredentials.Include);

        // 调用方（例如位置照片写操作）可能已经显式设置；避免重复值让
        // CsrfValidationFilter 读到 "XMLHttpRequest, XMLHttpRequest"。
        if (!request.Headers.Contains("X-Requested-With"))
        {
            request.Headers.TryAddWithoutValidation("X-Requested-With", "XMLHttpRequest");
        }

        return base.SendAsync(request, cancellationToken);
    }
}
