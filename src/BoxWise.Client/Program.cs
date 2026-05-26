using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using MudBlazor;
using MudBlazor.Services;
using BoxWise.Client;
using BoxWise.Client.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

var apiBaseUrl = builder.Configuration["ApiBaseUrl"] ?? "";
var hostBase = builder.HostEnvironment.BaseAddress;

var effectiveBase = TryCreateUri(apiBaseUrl);

// 确定有效的 API 基地址：
// - Client 开发服务器（5001→5000 跨端口 localhost）：使用配置的 ApiBaseUrl
// - 其他情况（Server 同源、手机通过 IP 访问、生产环境）：使用当前页面地址
if (!string.IsNullOrEmpty(hostBase) &&
    Uri.TryCreate(hostBase, UriKind.Absolute, out var hostUri))
{
    if (effectiveBase is null ||
        !hostUri.IsLoopback ||
        hostUri.Port == effectiveBase.Port)
    {
        effectiveBase = new Uri(hostUri, "/");
    }
}

builder.Services.AddScoped(sp => new HttpClient(new CookieHandler())
{
    BaseAddress = effectiveBase
});

static Uri? TryCreateUri(string url)
{
    if (string.IsNullOrEmpty(url)) return null;
    return Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri : null;
}

builder.Services.AddAuthorizationCore();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<CookieAuthenticationStateProvider>();
builder.Services.AddScoped<AuthenticationStateProvider>(sp => sp.GetRequiredService<CookieAuthenticationStateProvider>());
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<AppState>();
builder.Services.AddScoped<LocationService>();
builder.Services.AddScoped<TagService>();
builder.Services.AddScoped<ItemEntryService>();
builder.Services.AddScoped<ItemService>();
builder.Services.AddScoped<AiService>();
builder.Services.AddMudServices();

await builder.Build().RunAsync();
