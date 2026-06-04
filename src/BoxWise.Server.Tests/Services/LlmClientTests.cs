using System.Net;
using System.Text.Json;
using BoxWise.Server.Configuration;
using BoxWise.Server.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Moq.Protected;

namespace BoxWise.Server.Tests.Services;

public class LlmClientTests
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

    private static (
        LlmClient client,
        string tempDir,
        string imagePath
    ) CreateClient(
        LlmOptions? opts = null,
        HttpStatusCode status = HttpStatusCode.OK,
        string responseContent = "",
        Mock<HttpMessageHandler>? customHandler = null)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"boxwise-llm-{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);
        var imagePath = Path.Combine(tempDir, "test.jpg");
        File.WriteAllText(imagePath, "dummy-image-content");

        var handler = customHandler ?? CreateHandler(status, responseContent);
        var httpClient = new HttpClient(handler.Object);
        var options = Options.Create(opts ?? new LlmOptions
        {
            BaseUrl = "https://api.test.com/v1",
            ApiKey = "sk-test-key",
            Model = "test-model"
        });
        var logger = new Mock<ILogger<LlmClient>>().Object;

        return (new LlmClient(httpClient, options, logger), tempDir, imagePath);
    }

    private static void Cleanup(string tempDir)
    {
        if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
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
    public async Task RecognizeAsync_ValidJson_ReturnsResult()
    {
        var content = MakeOpenAiResponse("""{"name":"螺丝刀","note":"蓝色手柄"}""");
        var (client, tempDir, imagePath) = CreateClient(responseContent: content);
        try
        {
            var result = await client.RecognizeAsync(imagePath);
            Assert.NotNull(result);
            Assert.Equal("螺丝刀", result!.Name);
            Assert.Equal("蓝色手柄", result.Note);
        }
        finally { Cleanup(tempDir); }
    }

    [Fact]
    public async Task RecognizeAsync_FallbackRegex_ReturnsResult()
    {
        // 代码块包裹的非标准 JSON → \n 换行确认正则提取
        var content = MakeOpenAiResponse("```json\\n{\\n  \"name\": \"锤子\",\\n  \"note\": \"木柄铁锤\"\\n}\\n```");
        var (client, tempDir, imagePath) = CreateClient(responseContent: content);
        try
        {
            var result = await client.RecognizeAsync(imagePath);
            Assert.NotNull(result);
            Assert.Equal("锤子", result!.Name);
            Assert.Equal("木柄铁锤", result.Note);
        }
        finally { Cleanup(tempDir); }
    }

    [Fact]
    public async Task RecognizeAsync_NoApiKey_ReturnsNull()
    {
        var opts = new LlmOptions { BaseUrl = "https://api.test.com/v1", Model = "test-model", ApiKey = "" };
        var (client, tempDir, imagePath) = CreateClient(opts);
        try
        {
            var result = await client.RecognizeAsync(imagePath);
            Assert.Null(result);
        }
        finally { Cleanup(tempDir); }
    }

    [Fact]
    public async Task RecognizeAsync_HttpError_ReturnsNull()
    {
        var (client, tempDir, imagePath) = CreateClient(status: HttpStatusCode.InternalServerError);
        try
        {
            var result = await client.RecognizeAsync(imagePath);
            Assert.Null(result);
        }
        finally { Cleanup(tempDir); }
    }

    [Fact]
    public async Task RecognizeAsync_InvalidResponse_ReturnsNull()
    {
        var content = MakeOpenAiResponse("完全无法解析的响应内容");
        var (client, tempDir, imagePath) = CreateClient(responseContent: content);
        try
        {
            var result = await client.RecognizeAsync(imagePath);
            Assert.Null(result);
        }
        finally { Cleanup(tempDir); }
    }

    [Fact]
    public async Task RecognizeAsync_ConfigurableTimeout_ReturnsNullOnTimeout()
    {
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Returns(async () =>
            {
                await Task.Delay(TimeSpan.FromSeconds(3));
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(MakeOpenAiResponse("""{"name":"too late","note":""}"""),
                        System.Text.Encoding.UTF8, "application/json")
                };
            });

        var opts = new LlmOptions
        {
            BaseUrl = "https://api.test.com/v1",
            ApiKey = "sk-test-key",
            Model = "test-model",
            TimeoutSeconds = 1
        };
        var (client, tempDir, imagePath) = CreateClient(opts, customHandler: handler);
        try
        {
            var result = await client.RecognizeAsync(imagePath);
            Assert.Null(result);
        }
        finally { Cleanup(tempDir); }
    }
}
