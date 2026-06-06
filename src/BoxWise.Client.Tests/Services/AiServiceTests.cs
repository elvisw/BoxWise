using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using BoxWise.Client.Services;
using BoxWise.Shared.Dtos;
using Moq;
using Moq.Protected;

namespace BoxWise.Client.Tests.Services;

public class AiServiceTests
{
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

    private static (Mock<HttpMessageHandler> serverHandler, HttpClient serverHttp, Mock<IHttpClientFactory> llmFactory)
        CreateServerWithConfig(string configJson)
    {
        var serverHandler = new Mock<HttpMessageHandler>();
        serverHandler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(configJson, System.Text.Encoding.UTF8, "application/json")
            });
        var serverHttp = new HttpClient(serverHandler.Object)
        {
            BaseAddress = new Uri("https://test-server")
        };

        var llmHandler = new Mock<HttpMessageHandler>();
        llmHandler.Protected()
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
        var llmHttp = new HttpClient(llmHandler.Object);
        var llmFactory = new Mock<IHttpClientFactory>();
        llmFactory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(llmHttp);

        return (serverHandler, serverHttp, llmFactory);
    }

    private static Mock<HttpMessageHandler> CreateErrorServerHandler(HttpStatusCode status)
    {
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage { StatusCode = status });
        return handler;
    }

    private static HttpClient CreateServerHttp(Mock<HttpMessageHandler> handler)
        => new(handler.Object) { BaseAddress = new Uri("https://test-server") };

    private static string ConfigJson(string? baseUrl = "https://api.test.com/v1", string? apiKey = "sk-test-key",
        string model = "test-model", int timeoutSeconds = 30)
        => JsonSerializer.Serialize(new LlmConfigDto(baseUrl, apiKey, model, timeoutSeconds));

    [Fact]
    public async Task RecognizeAsync_Success_ReturnsResult()
    {
        var (_, serverHttp, llmFactory) = CreateServerWithConfig(ConfigJson());
        var service = new AiService(llmFactory.Object, serverHttp);

        var result = await service.RecognizeAsync(new byte[] { 1, 2, 3 }, "image/jpeg");

        Assert.NotNull(result);
        Assert.Equal("螺丝刀", result!.Name);
        Assert.Equal("蓝色手柄", result.Note);
    }

    [Fact]
    public async Task RecognizeAsync_ConfigApiUnavailable_ReturnsNull()
    {
        var serverHandler = CreateErrorServerHandler(HttpStatusCode.InternalServerError);
        var serverHttp = CreateServerHttp(serverHandler);
        var service = new AiService(Mock.Of<IHttpClientFactory>(), serverHttp);

        var result = await service.RecognizeAsync(new byte[] { 1, 2, 3 }, "image/jpeg");

        Assert.Null(result);
    }

    [Fact]
    public async Task RecognizeAsync_ConfigApiReturnsNull_ReturnsNull()
    {
        var (_, serverHttp, _) = CreateServerWithConfig(ConfigJson(apiKey: null));
        var service = new AiService(Mock.Of<IHttpClientFactory>(), serverHttp);

        var result = await service.RecognizeAsync(new byte[] { 1, 2, 3 }, "image/jpeg");

        Assert.Null(result);
    }

    [Fact]
    public async Task RecognizeAsync_HttpError_ReturnsNull()
    {
        var (_, serverHttp, _) = CreateServerWithConfig(ConfigJson());
        var llmHandler = CreateHandler(HttpStatusCode.InternalServerError, "");
        var llmHttp = new HttpClient(llmHandler.Object);
        var llmFactory = new Mock<IHttpClientFactory>();
        llmFactory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(llmHttp);
        var service = new AiService(llmFactory.Object, serverHttp);

        var result = await service.RecognizeAsync(new byte[] { 1, 2, 3 }, "image/jpeg");

        Assert.Null(result);
    }

    [Fact]
    public async Task RecognizeAsync_Timeout_ReturnsNull()
    {
        var (_, serverHttp, _) = CreateServerWithConfig(ConfigJson());
        var llmHandler = new Mock<HttpMessageHandler>();
        llmHandler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new OperationCanceledException());
        var llmHttp = new HttpClient(llmHandler.Object);
        var llmFactory = new Mock<IHttpClientFactory>();
        llmFactory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(llmHttp);
        var service = new AiService(llmFactory.Object, serverHttp);

        var result = await service.RecognizeAsync(new byte[] { 1, 2, 3 }, "image/jpeg");

        Assert.Null(result);
    }

    [Fact]
    public async Task RecognizeAsync_NetworkError_ReturnsNull()
    {
        var (_, serverHttp, _) = CreateServerWithConfig(ConfigJson());
        var llmHandler = new Mock<HttpMessageHandler>();
        llmHandler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new HttpRequestException());
        var llmHttp = new HttpClient(llmHandler.Object);
        var llmFactory = new Mock<IHttpClientFactory>();
        llmFactory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(llmHttp);
        var service = new AiService(llmFactory.Object, serverHttp);

        var result = await service.RecognizeAsync(new byte[] { 1, 2, 3 }, "image/jpeg");

        Assert.Null(result);
    }

    [Fact]
    public async Task RecognizeAsync_SendsCorrectPayload()
    {
        string? capturedBody = null;
        string? capturedAuth = null;
        var (_, serverHttp, _) = CreateServerWithConfig(ConfigJson());

        var llmHandler = new Mock<HttpMessageHandler>();
        llmHandler.Protected()
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
        var llmHttp = new HttpClient(llmHandler.Object);
        var llmFactory = new Mock<IHttpClientFactory>();
        llmFactory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(llmHttp);
        var service = new AiService(llmFactory.Object, serverHttp);

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
    public async Task RecognizeAsync_EmptyResponse_ReturnsNull()
    {
        var (_, serverHttp, _) = CreateServerWithConfig(ConfigJson());
        var llmHandler = CreateHandler(HttpStatusCode.OK, MakeOpenAiResponse(""));
        var llmHttp = new HttpClient(llmHandler.Object);
        var llmFactory = new Mock<IHttpClientFactory>();
        llmFactory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(llmHttp);
        var service = new AiService(llmFactory.Object, serverHttp);

        var result = await service.RecognizeAsync(new byte[] { 1, 2, 3 }, "image/jpeg");

        Assert.Null(result);
    }

    [Fact]
    public async Task RecognizeAsync_NonJsonContent_ReturnsNull()
    {
        var (_, serverHttp, _) = CreateServerWithConfig(ConfigJson());
        var llmHandler = CreateHandler(HttpStatusCode.OK, MakeOpenAiResponse("完全无法解析的纯文本响应"));
        var llmHttp = new HttpClient(llmHandler.Object);
        var llmFactory = new Mock<IHttpClientFactory>();
        llmFactory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(llmHttp);
        var service = new AiService(llmFactory.Object, serverHttp);

        var result = await service.RecognizeAsync(new byte[] { 1, 2, 3 }, "image/jpeg");

        Assert.Null(result);
    }

    [Fact]
    public async Task RecognizeAsync_HandlesMissingBaseUrl()
    {
        var (_, serverHttp, _) = CreateServerWithConfig(ConfigJson(baseUrl: null));
        var service = new AiService(Mock.Of<IHttpClientFactory>(), serverHttp);

        var result = await service.RecognizeAsync(new byte[] { 1, 2, 3 }, "image/jpeg");

        Assert.Null(result);
    }
}
