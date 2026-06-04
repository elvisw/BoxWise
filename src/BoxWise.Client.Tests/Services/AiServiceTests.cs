using System.Net;
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

    private static AiService CreateService(Mock<HttpMessageHandler> handler, int timeoutSeconds = 90)
    {
        var http = new HttpClient(handler.Object) { BaseAddress = new Uri("https://localhost:5000/") };
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AiSettings:TimeoutSeconds"] = timeoutSeconds.ToString()
            })
            .Build();
        return new AiService(http, config);
    }

    [Fact]
    public async Task RecognizeAsync_Success_ReturnsResult()
    {
        var dto = new RecognitionResultDto("螺丝刀", "蓝色手柄");
        var json = JsonSerializer.Serialize(dto);
        var handler = CreateHandler(HttpStatusCode.OK, json);
        var service = CreateService(handler);
        using var stream = new MemoryStream(new byte[] { 1, 2, 3 });

        var result = await service.RecognizeAsync(stream, "test.jpg", "image/jpeg");

        Assert.NotNull(result);
        Assert.Equal("螺丝刀", result!.Name);
        Assert.Equal("蓝色手柄", result.Note);
    }

    [Fact]
    public async Task RecognizeAsync_HttpError_ReturnsNull()
    {
        var handler = CreateHandler(HttpStatusCode.InternalServerError, "");
        var service = CreateService(handler);
        using var stream = new MemoryStream(new byte[] { 1, 2, 3 });

        var result = await service.RecognizeAsync(stream, "test.jpg", "image/jpeg");

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
        var service = CreateService(handler);
        using var stream = new MemoryStream(new byte[] { 1, 2, 3 });

        var result = await service.RecognizeAsync(stream, "test.jpg", "image/jpeg");

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
        var service = CreateService(handler);
        using var stream = new MemoryStream(new byte[] { 1, 2, 3 });

        var result = await service.RecognizeAsync(stream, "test.jpg", "image/jpeg");

        Assert.Null(result);
    }

    [Fact]
    public async Task RecognizeAsync_SendsMultipartContent()
    {
        string? capturedFileName = null;
        string? capturedMediaType = null;
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((req, _) =>
            {
                if (req.Content is MultipartFormDataContent multipart)
                {
                    var part = multipart.FirstOrDefault();
                    capturedFileName = part?.Headers.ContentDisposition?.FileName;
                    capturedMediaType = part?.Headers.ContentType?.MediaType;
                }
            })
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(JsonSerializer.Serialize(new RecognitionResultDto("A", "B")),
                    System.Text.Encoding.UTF8, "application/json")
            });
        var service = CreateService(handler);
        using var stream = new MemoryStream(new byte[] { 1, 2, 3 });

        await service.RecognizeAsync(stream, "photo.png", "image/png");

        Assert.Equal("photo.png", capturedFileName);
        Assert.Equal("image/png", capturedMediaType);
    }
}
