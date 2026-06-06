using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using BoxWise.Client.Services;
using BoxWise.Shared.Dtos;
using Microsoft.Extensions.Configuration;
using Moq;
using Moq.Protected;

namespace BoxWise.Client.Tests.Services;

public class AiServiceTests
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

    private static (AiService service, Mock<HttpMessageHandler> handler) CreateService(
        int timeoutSeconds = 30,
        string? apiKey = null,
        string? baseUrl = null,
        string? model = null)
    {
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(
                    MakeOpenAiResponse("""{"name":"螺丝刀","note":"蓝色手柄"}"""),
                    System.Text.Encoding.UTF8, "application/json")
            });

        var httpClient = new HttpClient(handler.Object)
        {
            BaseAddress = new Uri(baseUrl ?? "https://ark.cn-beijing.volces.com/api/v3")
        };
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient("VolcEngine")).Returns(httpClient);

        var configValues = new Dictionary<string, string?>
        {
            ["VolcEngine:BaseUrl"] = baseUrl ?? "https://ark.cn-beijing.volces.com/api/v3",
            ["VolcEngine:ApiKey"] = apiKey ?? "sk-test-key",
            ["VolcEngine:Model"] = model ?? "test-model",
            ["VolcEngine:TimeoutSeconds"] = timeoutSeconds.ToString()
        };
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(configValues)
            .Build();

        return (new AiService(factory.Object, config), handler);
    }

    private static string MakeOpenAiResponse(string contentField)
    {
        var response = new
        {
            choices = new[]
            {
                new { message = new { content = contentField } }
            }
        };
        return JsonSerializer.Serialize(response);
    }

    [Fact]
    public async Task RecognizeAsync_Success_ReturnsResult()
    {
        var callCount = 0;
        var json = MakeOpenAiResponse("""{"name":"螺丝刀","note":"蓝色手柄"}""");
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Callback(() => callCount++)
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
            });

        var httpClient = new HttpClient(handler.Object) { BaseAddress = new Uri("https://api.test.com/v1") };
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient("VolcEngine")).Returns(httpClient);
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["VolcEngine:BaseUrl"] = "https://api.test.com/v1",
                ["VolcEngine:ApiKey"] = "sk-test-key",
                ["VolcEngine:Model"] = "test-model"
            })
            .Build();
        var service = new AiService(factory.Object, config);

        var result = await service.RecognizeAsync(new byte[] { 1, 2, 3 }, "image/jpeg");

        Assert.True(callCount > 0, $"SendAsync was called {callCount} times");
        Assert.NotNull(result);
        Assert.Equal("螺丝刀", result!.Name);
        Assert.Equal("蓝色手柄", result.Note);
    }

    [Fact]
    public async Task RecognizeAsync_HttpError_ReturnsNull()
    {
        var handler = CreateHandler(HttpStatusCode.InternalServerError, "");
        var httpClient = new HttpClient(handler.Object) { BaseAddress = new Uri("https://api.test.com/v1") };
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient("VolcEngine")).Returns(httpClient);
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["VolcEngine:BaseUrl"] = "https://api.test.com/v1",
                ["VolcEngine:ApiKey"] = "sk-test-key"
            })
            .Build();
        var service = new AiService(factory.Object, config);

        var result = await service.RecognizeAsync(new byte[] { 1, 2, 3 }, "image/jpeg");

        Assert.Null(result);
    }

    [Fact]
    public async Task RecognizeAsync_Timeout_ReturnsNull()
    {
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new OperationCanceledException());
        var httpClient = new HttpClient(handler.Object) { BaseAddress = new Uri("https://api.test.com/v1") };
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient("VolcEngine")).Returns(httpClient);
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["VolcEngine:BaseUrl"] = "https://api.test.com/v1",
                ["VolcEngine:ApiKey"] = "sk-test-key"
            })
            .Build();
        var service = new AiService(factory.Object, config);

        var result = await service.RecognizeAsync(new byte[] { 1, 2, 3 }, "image/jpeg");

        Assert.Null(result);
    }

    [Fact]
    public async Task RecognizeAsync_NetworkError_ReturnsNull()
    {
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new HttpRequestException());
        var httpClient = new HttpClient(handler.Object) { BaseAddress = new Uri("https://api.test.com/v1") };
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient("VolcEngine")).Returns(httpClient);
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["VolcEngine:BaseUrl"] = "https://api.test.com/v1",
                ["VolcEngine:ApiKey"] = "sk-test-key"
            })
            .Build();
        var service = new AiService(factory.Object, config);

        var result = await service.RecognizeAsync(new byte[] { 1, 2, 3 }, "image/jpeg");

        Assert.Null(result);
    }

    [Fact]
    public async Task RecognizeAsync_SendsCorrectPayload()
    {
        string? capturedBody = null;
        string? capturedAuth = null;
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((req, _) =>
            {
                capturedBody = req.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
                capturedAuth = req.Headers.Authorization?.ToString();
            })
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(MakeOpenAiResponse("""{"name":"A","note":"B"}"""),
                    System.Text.Encoding.UTF8, "application/json")
            });

        var httpClient = new HttpClient(handler.Object) { BaseAddress = new Uri("https://api.test.com/v1") };
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient("VolcEngine")).Returns(httpClient);
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["VolcEngine:BaseUrl"] = "https://api.test.com/v1",
                ["VolcEngine:ApiKey"] = "sk-test-key",
                ["VolcEngine:Model"] = "test-model"
            })
            .Build();
        var service = new AiService(factory.Object, config);

        await service.RecognizeAsync(new byte[] { 1, 2, 3 }, "image/png");

        Assert.NotNull(capturedBody);
        Assert.Contains("\"model\":\"test-model\"", capturedBody);
        Assert.Contains("\"max_tokens\":200", capturedBody);
        Assert.Contains("\"type\":\"image_url\"", capturedBody);
        Assert.Contains("data:image/png;base64,AQID", capturedBody);
        Assert.Contains("\"type\":\"text\"", capturedBody);
        Assert.Contains("Bearer sk-test-key", capturedAuth);
    }

    [Fact]
    public async Task RecognizeAsync_MissingConfig_ReturnsNull()
    {
        var handler = new Mock<HttpMessageHandler>();
        var httpClient = new HttpClient(handler.Object) { BaseAddress = new Uri("https://api.test.com/v1") };
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient("VolcEngine")).Returns(httpClient);
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["VolcEngine:BaseUrl"] = "https://api.test.com/v1",
                ["VolcEngine:ApiKey"] = null
            })
            .Build();
        var service = new AiService(factory.Object, config);

        var result = await service.RecognizeAsync(new byte[] { 1, 2, 3 }, "image/jpeg");

        Assert.Null(result);
    }

    [Fact]
    public async Task RecognizeAsync_EmptyResponse_ReturnsNull()
    {
        var json = MakeOpenAiResponse("");
        var handler = CreateHandler(HttpStatusCode.OK, json);
        var httpClient = new HttpClient(handler.Object) { BaseAddress = new Uri("https://api.test.com/v1") };
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient("VolcEngine")).Returns(httpClient);
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["VolcEngine:BaseUrl"] = "https://api.test.com/v1",
                ["VolcEngine:ApiKey"] = "sk-test-key"
            })
            .Build();
        var service = new AiService(factory.Object, config);

        var result = await service.RecognizeAsync(new byte[] { 1, 2, 3 }, "image/jpeg");

        Assert.Null(result);
    }

    [Fact]
    public async Task RecognizeAsync_NonJsonContent_ReturnsNull()
    {
        var json = MakeOpenAiResponse("完全无法解析的纯文本响应");
        var handler = CreateHandler(HttpStatusCode.OK, json);
        var httpClient = new HttpClient(handler.Object) { BaseAddress = new Uri("https://api.test.com/v1") };
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient("VolcEngine")).Returns(httpClient);
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["VolcEngine:BaseUrl"] = "https://api.test.com/v1",
                ["VolcEngine:ApiKey"] = "sk-test-key"
            })
            .Build();
        var service = new AiService(factory.Object, config);

        var result = await service.RecognizeAsync(new byte[] { 1, 2, 3 }, "image/jpeg");

        Assert.Null(result);
    }
}
