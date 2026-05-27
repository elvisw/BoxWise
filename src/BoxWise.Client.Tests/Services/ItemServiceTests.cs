using System.Net;
using System.Text.Json;
using BoxWise.Client.Services;
using BoxWise.Shared.Dtos;
using Moq;
using Moq.Protected;

namespace BoxWise.Client.Tests.Services;

public class ItemServiceTests
{
    private sealed class UriCapture
    {
        public string Value { get; set; } = string.Empty;
    }

    private static (ItemService Service, UriCapture Capture) CreateServiceWithUriCapture(
        HttpStatusCode status = HttpStatusCode.OK,
        string responseContent = "[]")
    {
        var capture = new UriCapture();
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((req, _) => capture.Value = req.RequestUri!.AbsoluteUri)
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = status,
                Content = new StringContent(responseContent, System.Text.Encoding.UTF8, "application/json")
            });

        var http = new HttpClient(handler.Object) { BaseAddress = new Uri("https://localhost:5000/") };
        return (new ItemService(http), capture);
    }

    private static (ItemService Service, Mock<HttpMessageHandler> Handler) CreateService(
        HttpStatusCode status = HttpStatusCode.OK,
        string responseContent = "[]")
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

        var http = new HttpClient(handler.Object) { BaseAddress = new Uri("https://localhost:5000/") };
        return (new ItemService(http), handler);
    }

    [Fact]
    public async Task GetAllAsync_Success_ReturnsList()
    {
        var dto = new ItemSummaryDto(1, "扳手", null, null, Array.Empty<string>(), DateTime.UtcNow);
        var json = JsonSerializer.Serialize(new[] { dto });
        var (service, _) = CreateService(responseContent: json);

        var result = await service.GetAllAsync();

        Assert.NotNull(result);
        Assert.Single(result);
        Assert.Equal("扳手", result![0].Name);
    }

    [Fact]
    public async Task SearchAsync_DelegatesToGetFiltered()
    {
        var (service, _) = CreateService();

        var result = await service.SearchAsync("螺丝");

        Assert.NotNull(result);
    }

    [Fact]
    public async Task GetFilteredAsync_NoParams_ReturnsBaseUrl()
    {
        var (service, capture) = CreateServiceWithUriCapture();

        await service.GetFilteredAsync(null, null, null);

        Assert.Equal("https://localhost:5000/api/items", capture.Value);
    }

    [Fact]
    public async Task GetFilteredAsync_WithLocationId_AppendsParam()
    {
        var (service, capture) = CreateServiceWithUriCapture();

        await service.GetFilteredAsync(3, null, null);

        Assert.Equal("https://localhost:5000/api/items?locationId=3", capture.Value);
    }

    [Fact]
    public async Task GetFilteredAsync_WithMultipleTagIds_EachWithAmpersand()
    {
        var (service, capture) = CreateServiceWithUriCapture();

        await service.GetFilteredAsync(null, new[] { 1, 2 }, null);

        Assert.Equal("https://localhost:5000/api/items?tagId=1&tagId=2", capture.Value);
    }

    [Fact]
    public async Task GetFilteredAsync_WithEmptyTagIds_NoTagParams()
    {
        var (service, capture) = CreateServiceWithUriCapture();

        await service.GetFilteredAsync(null, Array.Empty<int>(), null);

        Assert.Equal("https://localhost:5000/api/items", capture.Value);
    }

    [Fact]
    public async Task GetFilteredAsync_WithQuery_EscapesValue()
    {
        var (service, capture) = CreateServiceWithUriCapture();

        await service.GetFilteredAsync(null, null, "螺丝刀 蓝色");

        Assert.Contains("q=%E8%9E%BA%E4%B8%9D%E5%88%80%20%E8%93%9D%E8%89%B2", capture.Value);
    }

    [Fact]
    public async Task GetFilteredAsync_AllParamsCombined_CorrectOrder()
    {
        var (service, capture) = CreateServiceWithUriCapture();

        await service.GetFilteredAsync(5, new[] { 1, 2 }, "test");

        Assert.Equal("https://localhost:5000/api/items?locationId=5&tagId=1&tagId=2&q=test", capture.Value);
    }

    [Fact]
    public async Task GetFilteredAsync_HttpError_ReturnsNull()
    {
        var (service, _) = CreateService(status: HttpStatusCode.InternalServerError);

        var result = await service.GetFilteredAsync(null, null, null);

        Assert.Null(result);
    }

    [Fact]
    public async Task DeleteAsync_Success_ReturnsTrue()
    {
        var (service, _) = CreateService(status: HttpStatusCode.NoContent);

        var result = await service.DeleteAsync(1);

        Assert.True(result);
    }

    [Fact]
    public async Task DeleteAsync_Failure_ReturnsFalse()
    {
        var (service, _) = CreateService(status: HttpStatusCode.NotFound);

        var result = await service.DeleteAsync(1);

        Assert.False(result);
    }

    [Fact]
    public async Task GetByIdAsync_Success_ReturnsDto()
    {
        var dto = new ItemDto(1, "扳手", null, null, null, null, null, null, null, Array.Empty<string>(), "elvis", DateTime.UtcNow);
        var json = JsonSerializer.Serialize(dto);
        var (service, _) = CreateService(responseContent: json);

        var result = await service.GetByIdAsync(1);

        Assert.NotNull(result);
        Assert.Equal("扳手", result!.Name);
    }

    [Fact]
    public async Task GetByIdAsync_Failure_ReturnsNull()
    {
        var (service, _) = CreateService(status: HttpStatusCode.NotFound);

        var result = await service.GetByIdAsync(999);

        Assert.Null(result);
    }
}
