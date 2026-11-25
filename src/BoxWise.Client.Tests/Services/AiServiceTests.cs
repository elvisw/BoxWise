using System.Net;
using System.Text;
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

    private static (Mock<IHttpClientFactory> Factory, List<CapturedRequest> Requests) CreateLlmFactory(
        HttpStatusCode status = HttpStatusCode.OK,
        string? responseContent = null)
    {
        var requests = new List<CapturedRequest>();
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((req, _) =>
                requests.Add(new CapturedRequest(
                    req.RequestUri,
                    req.Headers.Authorization?.ToString(),
                    req.Content?.ReadAsStringAsync().GetAwaiter().GetResult())))
            .ReturnsAsync(() => new HttpResponseMessage
            {
                StatusCode = status,
                Content = new StringContent(
                    responseContent ?? MakeOpenAiResponse("""{"name":"螺丝刀","note":"蓝色手柄"}"""),
                    Encoding.UTF8,
                    "application/json")
            });

        var http = new HttpClient(handler.Object);
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(http);
        return (factory, requests);
    }

    private static HttpClient CreateServerWithConfig(string configJson)
    {
        var serverHandler = new Mock<HttpMessageHandler>();
        serverHandler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(configJson, Encoding.UTF8, "application/json")
            });
        return new HttpClient(serverHandler.Object)
        {
            BaseAddress = new Uri("https://test-server")
        };
    }

    private static HttpClient CreateServerHttp(Mock<HttpMessageHandler> handler)
        => new(handler.Object) { BaseAddress = new Uri("https://test-server") };

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

    private static string ConfigJson(string? baseUrl = "https://api.test.com/v1", string? apiKey = "sk-test-key",
        string model = "test-model", int timeoutSeconds = 30)
        => JsonSerializer.Serialize(new LlmConfigDto(baseUrl, apiKey, model, timeoutSeconds));

    private static (AiService Service, List<CapturedRequest> Requests) CreateService(
        string? baseUrl = "https://api.test.com/v1",
        string? apiKey = "sk-test-key",
        string model = "test-model",
        int timeoutSeconds = 30,
        HttpStatusCode status = HttpStatusCode.OK,
        string? responseContent = null)
    {
        var serverHttp = CreateServerWithConfig(ConfigJson(baseUrl, apiKey, model, timeoutSeconds));
        var (factory, requests) = CreateLlmFactory(status, responseContent);
        return (new AiService(factory.Object, serverHttp), requests);
    }

    [Fact]
    public async Task RecognizeAsync_Success_ReturnsResult()
    {
        var (service, _) = CreateService();

        var result = await service.RecognizeAsync(new byte[] { 1, 2, 3 }, "image/jpeg");

        Assert.NotNull(result);
        Assert.Equal("螺丝刀", result!.Name);
        Assert.Equal("蓝色手柄", result.Note);
    }

    [Fact]
    public async Task RecognizeAsync_ConfigApiUnavailable_ReturnsNull()
    {
        var serverHttp = CreateServerHttp(CreateErrorServerHandler(HttpStatusCode.InternalServerError));
        var service = new AiService(Mock.Of<IHttpClientFactory>(), serverHttp);

        var result = await service.RecognizeAsync(new byte[] { 1, 2, 3 }, "image/jpeg");

        Assert.Null(result);
    }

    [Fact]
    public async Task RecognizeAsync_ConfigApiReturnsNull_ReturnsNull()
    {
        var serverHttp = CreateServerWithConfig(ConfigJson(apiKey: null));
        var service = new AiService(Mock.Of<IHttpClientFactory>(), serverHttp);

        var result = await service.RecognizeAsync(new byte[] { 1, 2, 3 }, "image/jpeg");

        Assert.Null(result);
    }

    [Fact]
    public async Task RecognizeAsync_HttpError_ReturnsNull()
    {
        var (service, _) = CreateService(status: HttpStatusCode.InternalServerError);

        var result = await service.RecognizeAsync(new byte[] { 1, 2, 3 }, "image/jpeg");

        Assert.Null(result);
    }

    [Fact]
    public async Task RecognizeAsync_Timeout_ReturnsNull()
    {
        var serverHttp = CreateServerWithConfig(ConfigJson());
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
        var serverHttp = CreateServerWithConfig(ConfigJson());
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
        var (service, requests) = CreateService(responseContent: MakeOpenAiResponse("""{"name":"A","note":"B"}"""));

        await service.RecognizeAsync(new byte[] { 1, 2, 3 }, "image/png");

        var request = Assert.Single(requests);
        Assert.NotNull(request.Body);
        Assert.Contains("\"model\":\"test-model\"", request.Body);
        Assert.DoesNotContain("\"max_tokens\"", request.Body);
        Assert.DoesNotContain("\"max_completion_tokens\"", request.Body);
        Assert.DoesNotContain("\"thinking\"", request.Body);
        Assert.Contains("\"type\":\"image_url\"", request.Body);
        Assert.Contains("data:image/png;base64,AQID", request.Body);
        Assert.Contains("\"type\":\"text\"", request.Body);
        Assert.Equal("Bearer sk-test-key", request.Authorization);
        Assert.Equal(new Uri("https://api.test.com/v1/chat/completions"), request.Endpoint);
    }

    [Theory]
    [InlineData("https://ark.cn-beijing.volces.com/api/v3", "doubao-seed-2-0-pro-260215",
        "https://ark.cn-beijing.volces.com/api/v3/chat/completions")]
    [InlineData("https://api.deepseek.com", "deepseek-flash",
        "https://api.deepseek.com/chat/completions")]
    public async Task RecognizeAsync_DocumentedConfigurations_ReturnResultFromSinglePost(
        string baseUrl, string model, string expectedUri)
    {
        var (service, requests) = CreateService(baseUrl: baseUrl, apiKey: "test-key", model: model);

        var result = await service.RecognizeAsync(new byte[] { 1, 2, 3 }, "image/jpeg");

        Assert.NotNull(result);
        Assert.Equal("螺丝刀", result!.Name);
        Assert.Equal("蓝色手柄", result.Note);
        var request = Assert.Single(requests);
        Assert.Equal(new Uri(expectedUri), request.Endpoint);
        Assert.Contains($"\"model\":\"{model}\"", request.Body);
        Assert.Equal("Bearer test-key", request.Authorization);
    }

    [Theory]
    [InlineData("https://api.deepseek.com", "https://api.deepseek.com/chat/completions")]
    [InlineData("https://api.deepseek.com/", "https://api.deepseek.com/chat/completions")]
    [InlineData("https://api.deepseek.com/chat/completions", "https://api.deepseek.com/chat/completions")]
    [InlineData("https://api.deepseek.com/chat/completions/", "https://api.deepseek.com/chat/completions")]
    [InlineData("https://gateway.test/openai/v1", "https://gateway.test/openai/v1/chat/completions")]
    [InlineData("https://gateway.test/openai/v1/", "https://gateway.test/openai/v1/chat/completions")]
    [InlineData("https://gateway.test/openai/v1/chat/completions/", "https://gateway.test/openai/v1/chat/completions")]
    [InlineData("https://gateway.test/openai/v1/?profile=vision", "https://gateway.test/openai/v1/chat/completions?profile=vision")]
    [InlineData("https://gateway.test/openai/v1/chat/completions/?profile=vision", "https://gateway.test/openai/v1/chat/completions?profile=vision")]
    [InlineData("https://ark.cn-beijing.volces.com/api/v3", "https://ark.cn-beijing.volces.com/api/v3/chat/completions")]
    [InlineData("https://ark.cn-beijing.volces.com/api/v3/", "https://ark.cn-beijing.volces.com/api/v3/chat/completions")]
    [InlineData("https://ark.cn-beijing.volces.com", "https://ark.cn-beijing.volces.com/api/v3/chat/completions")]
    [InlineData("https://ark.cn-beijing.volces.com/", "https://ark.cn-beijing.volces.com/api/v3/chat/completions")]
    [InlineData("https://ark.cn-beijing.volces.com:443", "https://ark.cn-beijing.volces.com/api/v3/chat/completions")]
    public async Task RecognizeAsync_ResolvesConfiguredEndpoint(string baseUrl, string expectedUri)
    {
        var (service, requests) = CreateService(baseUrl: baseUrl);

        await service.RecognizeAsync(new byte[] { 1, 2, 3 }, "image/jpeg");

        var request = Assert.Single(requests);
        Assert.Equal(new Uri(expectedUri), request.Endpoint);
    }

    [Theory]
    [InlineData("https://ark.cn-beijing.volces.com.evil.test",
        "https://ark.cn-beijing.volces.com.evil.test/chat/completions")]
    [InlineData("https://foo.ark.cn-beijing.volces.com",
        "https://foo.ark.cn-beijing.volces.com/chat/completions")]
    [InlineData("https://ark.cn-beijing.volces.com:8443",
        "https://ark.cn-beijing.volces.com:8443/chat/completions")]
    [InlineData("http://ark.cn-beijing.volces.com",
        "http://ark.cn-beijing.volces.com/chat/completions")]
    [InlineData("https://ark.cn-beijing.volces.com/custom",
        "https://ark.cn-beijing.volces.com/custom/chat/completions")]
    [InlineData("https://ark.cn-beijing.volces.com?profile=vision",
        "https://ark.cn-beijing.volces.com/chat/completions?profile=vision")]
    public async Task RecognizeAsync_ArkLookalikes_DoNotUseBareOriginException(string baseUrl, string expectedUri)
    {
        var (service, requests) = CreateService(baseUrl: baseUrl);

        await service.RecognizeAsync(new byte[] { 1, 2, 3 }, "image/jpeg");

        var request = Assert.Single(requests);
        Assert.Equal(new Uri(expectedUri), request.Endpoint);
    }

    [Theory]
    [InlineData("/api/v3")]
    [InlineData("api.test.com/v1")]
    [InlineData("ftp://api.test.com/v1")]
    [InlineData("https://user:pass@api.test.com/v1")]
    [InlineData("https://api.test.com/v1#fragment")]
    public async Task RecognizeAsync_InvalidBaseUrl_ReturnsNullWithoutOutboundRequest(string baseUrl)
    {
        var (service, requests) = CreateService(baseUrl: baseUrl);

        var result = await service.RecognizeAsync(new byte[] { 1, 2, 3 }, "image/jpeg");

        Assert.Null(result);
        Assert.Empty(requests);
    }

    [Fact]
    public async Task RecognizeAsync_AcceptsExactNameAndNoteLengths()
    {
        var name = new string('名', 200);
        var note = new string('注', 2000);
        var content = JsonSerializer.Serialize(new { name, note });
        var (service, _) = CreateService(responseContent: MakeOpenAiResponse(content));

        var result = await service.RecognizeAsync(new byte[] { 1, 2, 3 }, "image/jpeg");

        Assert.NotNull(result);
        Assert.Equal(200, result!.Name.Length);
        Assert.Equal(2000, result.Note.Length);
    }

    [Fact]
    public async Task RecognizeAsync_TrimsBeforeLengthValidation()
    {
        var content = JsonSerializer.Serialize(new
        {
            name = "  " + new string('名', 200) + "  ",
            note = "  " + new string('注', 2000) + "  "
        });
        var (service, _) = CreateService(responseContent: MakeOpenAiResponse(content));

        var result = await service.RecognizeAsync(new byte[] { 1, 2, 3 }, "image/jpeg");

        Assert.NotNull(result);
        Assert.Equal(200, result!.Name.Length);
        Assert.Equal(2000, result.Note.Length);
    }

    [Theory]
    [InlineData(201, 10)]
    [InlineData(10, 2001)]
    public async Task RecognizeAsync_RejectsOverlongJsonOutput(int nameLength, int noteLength)
    {
        var content = JsonSerializer.Serialize(new
        {
            name = new string('名', nameLength),
            note = new string('注', noteLength)
        });
        var (service, _) = CreateService(responseContent: MakeOpenAiResponse(content));

        var result = await service.RecognizeAsync(new byte[] { 1, 2, 3 }, "image/jpeg");

        Assert.Null(result);
    }

    [Fact]
    public async Task RecognizeAsync_RegexFallback_AcceptsExactLengths()
    {
        var content = "prefix " + JsonSerializer.Serialize(new
        {
            name = new string('a', 200),
            note = new string('b', 2000)
        });
        var (service, _) = CreateService(responseContent: MakeOpenAiResponse(content));

        var result = await service.RecognizeAsync(new byte[] { 1, 2, 3 }, "image/jpeg");

        Assert.NotNull(result);
        Assert.Equal(200, result!.Name.Length);
        Assert.Equal(2000, result.Note.Length);
    }

    [Theory]
    [InlineData(201, 10)]
    [InlineData(10, 2001)]
    public async Task RecognizeAsync_RegexFallback_RejectsOverlongOutput(int nameLength, int noteLength)
    {
        var content = "prefix " + JsonSerializer.Serialize(new
        {
            name = new string('a', nameLength),
            note = new string('b', noteLength)
        });
        var (service, _) = CreateService(responseContent: MakeOpenAiResponse(content));

        var result = await service.RecognizeAsync(new byte[] { 1, 2, 3 }, "image/jpeg");

        Assert.Null(result);
    }

    [Fact]
    public async Task RecognizeAsync_TrimsParsedValues()
    {
        var content = JsonSerializer.Serialize(new { name = "  螺丝刀  ", note = "  蓝色手柄  " });
        var (service, _) = CreateService(responseContent: MakeOpenAiResponse(content));

        var result = await service.RecognizeAsync(new byte[] { 1, 2, 3 }, "image/jpeg");

        Assert.NotNull(result);
        Assert.Equal("螺丝刀", result!.Name);
        Assert.Equal("蓝色手柄", result.Note);
    }

    [Fact]
    public async Task RecognizeAsync_EmptyResponse_ReturnsNull()
    {
        var (service, _) = CreateService(responseContent: MakeOpenAiResponse(""));

        var result = await service.RecognizeAsync(new byte[] { 1, 2, 3 }, "image/jpeg");

        Assert.Null(result);
    }

    [Fact]
    public async Task RecognizeAsync_NonJsonContent_ReturnsNull()
    {
        var (service, _) = CreateService(responseContent: MakeOpenAiResponse("完全无法解析的纯文本响应"));

        var result = await service.RecognizeAsync(new byte[] { 1, 2, 3 }, "image/jpeg");

        Assert.Null(result);
    }

    [Fact]
    public async Task RecognizeAsync_HandlesMissingBaseUrl()
    {
        var serverHttp = CreateServerWithConfig(ConfigJson(baseUrl: null));
        var service = new AiService(Mock.Of<IHttpClientFactory>(), serverHttp);

        var result = await service.RecognizeAsync(new byte[] { 1, 2, 3 }, "image/jpeg");

        Assert.Null(result);
    }

    private sealed record CapturedRequest(Uri? Endpoint, string? Authorization, string? Body);
}
