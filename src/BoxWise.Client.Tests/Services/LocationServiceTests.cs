using System.Net;
using System.Text;
using System.Text.Json;
using BoxWise.Client.Services;
using BoxWise.Shared.Dtos;
using BoxWise.Shared.Validation;

namespace BoxWise.Client.Tests.Services;

public class LocationServiceTests
{
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _handler;

        public StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            _handler(request, cancellationToken);
    }

    private static LocationService CreateService(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder,
        out StubHandler handler)
    {
        handler = new StubHandler(responder);
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost:5000/") };
        return new LocationService(http);
    }

    private static HttpResponseMessage Json<T>(HttpStatusCode status, T value) =>
        new(status)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(value), Encoding.UTF8, "application/json")
        };

    private static HttpResponseMessage Problem(HttpStatusCode status, string detail) =>
        Json(status, new
        {
            type = "https://tools.ietf.org/html/rfc9110",
            title = "Error",
            status = (int)status,
            detail
        });

    private static LocationDto SampleDto(string name = "抽屉") =>
        new(1, name, "/1/", null, 0, "描述", true, name);

    [Fact]
    public async Task CreateAsync_Success_ReturnsDto()
    {
        var service = CreateService(
            (_, _) => Task.FromResult(Json(HttpStatusCode.Created, SampleDto())),
            out _);

        var result = await service.CreateAsync(
            new CreateLocationRequest("抽屉", null, 0, "描述"));

        Assert.Equal(LocationMutationStatus.Success, result.Status);
        Assert.Equal("抽屉", result.Value!.Name);
    }

    [Fact]
    public async Task CreateAsync_Rejected_MapsProblemDetail()
    {
        var service = CreateService(
            (_, _) => Task.FromResult(
                Problem(HttpStatusCode.BadRequest, "描述不能超过 300 字。")),
            out _);

        var result = await service.CreateAsync(new CreateLocationRequest("抽屉", null));

        Assert.Equal(LocationMutationStatus.Rejected, result.Status);
        Assert.Equal("描述不能超过 300 字。", result.Error);
    }

    [Fact]
    public async Task CreateAsync_ServerError_ReturnsUnknown()
    {
        var service = CreateService(
            (_, _) => Task.FromResult(
                Problem(HttpStatusCode.InternalServerError, "路径查询失败")),
            out _);

        var result = await service.CreateAsync(new CreateLocationRequest("抽屉", null));

        Assert.Equal(LocationMutationStatus.Unknown, result.Status);
    }

    [Fact]
    public async Task CreateAsync_Timeout_ReturnsUnknown()
    {
        var service = CreateService(
            (_, _) => throw new TaskCanceledException("timeout"),
            out _);

        var result = await service.CreateAsync(new CreateLocationRequest("抽屉", null));

        Assert.Equal(LocationMutationStatus.Unknown, result.Status);
    }

    [Fact]
    public async Task CreateAsync_MalformedSuccessBody_ReturnsUnknown()
    {
        var service = CreateService(
            (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Created)
            {
                Content = new StringContent("not-json", Encoding.UTF8, "application/json")
            }),
            out _);

        var result = await service.CreateAsync(new CreateLocationRequest("抽屉", null));

        Assert.Equal(LocationMutationStatus.Unknown, result.Status);
    }

    [Fact]
    public async Task UpdateAsync_OmittedDescription_DoesNotSerializeProperty()
    {
        string? body = null;
        var service = CreateService(
            async (request, ct) =>
            {
                body = await request.Content!.ReadAsStringAsync(ct);
                return Json(HttpStatusCode.OK, SampleDto("新名称"));
            },
            out _);

        await service.UpdateAsync(1, new UpdateLocationRequest("新名称"));

        Assert.NotNull(body);
        Assert.DoesNotContain("description", body);
    }

    [Fact]
    public async Task UpdateAsync_EmptyDescription_SerializesExplicitEmptyString()
    {
        string? body = null;
        var service = CreateService(
            async (request, ct) =>
            {
                body = await request.Content!.ReadAsStringAsync(ct);
                return Json(HttpStatusCode.OK, SampleDto("新名称"));
            },
            out _);

        await service.UpdateAsync(
            1,
            new UpdateLocationRequest("新名称", JsonSerializer.SerializeToElement("")));

        Assert.NotNull(body);
        Assert.Contains("\"description\":\"\"", body);
    }

    [Fact]
    public async Task UpdateAsync_NotFound_ReturnsNotFound()
    {
        var service = CreateService(
            (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)),
            out _);

        var result = await service.UpdateAsync(1, new UpdateLocationRequest("新名称"));

        Assert.Equal(LocationMutationStatus.NotFound, result.Status);
    }

    [Fact]
    public async Task DeleteAsync_NoContent_ReturnsDeleted()
    {
        var service = CreateService(
            (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent)),
            out _);

        var result = await service.DeleteAsync(1);

        Assert.Equal(LocationDeleteStatus.Deleted, result.Status);
        Assert.False(result.CleanupWarning);
    }

    [Fact]
    public async Task DeleteAsync_CleanupWarning_ReturnsDeletedWithWarning()
    {
        var service = CreateService(
            (_, _) => Task.FromResult(
                Json(HttpStatusCode.OK, new LocationCleanupWarning(true))),
            out _);

        var result = await service.DeleteAsync(1);

        Assert.Equal(LocationDeleteStatus.Deleted, result.Status);
        Assert.True(result.CleanupWarning);
    }

    [Fact]
    public async Task DeleteAsync_Rejected_KeepsError()
    {
        var service = CreateService(
            (_, _) => Task.FromResult(
                Problem(HttpStatusCode.BadRequest, "无法删除：该位置下还有物品")),
            out _);

        var result = await service.DeleteAsync(1);

        Assert.Equal(LocationDeleteStatus.Rejected, result.Status);
        Assert.Equal("无法删除：该位置下还有物品", result.Error);
    }

    [Fact]
    public async Task DeleteAsync_MalformedCleanupWarningBody_ReturnsDeletedWithWarning()
    {
        var service = CreateService(
            (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("not-json", Encoding.UTF8, "application/json")
            }),
            out _);

        var result = await service.DeleteAsync(1);

        Assert.Equal(LocationDeleteStatus.Deleted, result.Status);
        Assert.True(result.CleanupWarning);
    }

    [Fact]
    public async Task DeleteAsync_UnexpectedCleanupWarningShape_ReturnsDeletedWithWarning()
    {
        var service = CreateService(
            (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "{\"other\":true}", Encoding.UTF8, "application/json")
            }),
            out _);

        var result = await service.DeleteAsync(1);

        Assert.Equal(LocationDeleteStatus.Deleted, result.Status);
        Assert.True(result.CleanupWarning);
    }

    [Fact]
    public async Task DeleteAsync_NotFound_ReturnsNotFound()
    {
        var service = CreateService(
            (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)),
            out _);

        var result = await service.DeleteAsync(1);

        Assert.Equal(LocationDeleteStatus.NotFound, result.Status);
    }

    [Fact]
    public async Task DeleteAsync_LostResponse_ReturnsUnknown()
    {
        var service = CreateService(
            (_, _) => throw new HttpRequestException("connection reset"),
            out _);

        var result = await service.DeleteAsync(1);

        Assert.Equal(LocationDeleteStatus.Unknown, result.Status);
    }

    [Fact]
    public async Task DeleteAsync_ServerError_ReturnsUnknown()
    {
        var service = CreateService(
            (_, _) => Task.FromResult(
                Problem(HttpStatusCode.InternalServerError, "服务器错误")),
            out _);

        var result = await service.DeleteAsync(1);

        // 端点可能在提交后因响应失败返回 5xx，不能断言未删除。
        Assert.Equal(LocationDeleteStatus.Unknown, result.Status);
    }

    [Fact]
    public async Task UploadPhotoAsync_Success_SendsMultipartFile()
    {
        string? body = null;
        string? contentType = null;
        string? csrf = null;
        var service = CreateService(
            async (request, ct) =>
            {
                body = await request.Content!.ReadAsStringAsync(ct);
                contentType = request.Content.Headers.ContentType?.MediaType;
                csrf = request.Headers.TryGetValues("X-Requested-With", out var values)
                    ? values.Single()
                    : null;
                return Json(HttpStatusCode.OK, SampleDto());
            },
            out _);

        var bytes = new byte[] { 0xFF, 0xD8, 0xFF, 0x01 };
        var result = await service.UploadPhotoAsync(1, bytes, "image/jpeg", "photo.jpg");

        Assert.Equal(LocationMutationStatus.Success, result.Status);
        Assert.Equal("multipart/form-data", contentType);
        Assert.Contains("name=file", body);
        Assert.Contains("filename=photo.jpg", body);
        Assert.Equal("XMLHttpRequest", csrf);
    }

    [Fact]
    public async Task UploadPhotoAsync_BadRequest_ReturnsRejected()
    {
        var service = CreateService(
            (_, _) => Task.FromResult(
                Problem(HttpStatusCode.BadRequest, "照片超过 10 MB，请换一张更小的图片。")),
            out _);

        var result = await service.UploadPhotoAsync(
            1, new byte[] { 1 }, "image/jpeg", "photo.jpg");

        Assert.Equal(LocationMutationStatus.Rejected, result.Status);
        Assert.Contains("10 MB", result.Error);
    }

    [Fact]
    public async Task UploadPhotoAsync_TooLargeResponse_MapsSpecificMessage()
    {
        var service = CreateService(
            (_, _) => Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.RequestEntityTooLarge)),
            out _);

        var result = await service.UploadPhotoAsync(
            1, new byte[] { 1 }, "image/jpeg", "photo.jpg");

        Assert.Equal(LocationMutationStatus.Rejected, result.Status);
        Assert.Equal(LocationPhotoRules.TooLargeMessage, result.Error);
    }

    [Fact]
    public async Task UploadPhotoAsync_ServerError_ReturnsUnknown()
    {
        var service = CreateService(
            (_, _) => Task.FromResult(
                Problem(HttpStatusCode.InternalServerError, "路径查询失败")),
            out _);

        var result = await service.UploadPhotoAsync(
            1, new byte[] { 1 }, "image/jpeg", "photo.jpg");

        Assert.Equal(LocationMutationStatus.Unknown, result.Status);
    }

    [Fact]
    public async Task UploadPhotoAsync_CsrfRejected_ReturnsRejected()
    {
        var service = CreateService(
            (_, _) => Task.FromResult(
                Problem(HttpStatusCode.BadRequest, "CSRF validation failed.")),
            out _);

        var result = await service.UploadPhotoAsync(
            1, new byte[] { 1 }, "image/jpeg", "photo.jpg");

        Assert.Equal(LocationMutationStatus.Rejected, result.Status);
    }

    [Fact]
    public async Task UploadPhotoAsync_LostResponse_ReturnsUnknown()
    {
        var service = CreateService(
            (_, _) => throw new TaskCanceledException("timeout"),
            out _);

        var result = await service.UploadPhotoAsync(
            1, new byte[] { 1 }, "image/jpeg", "photo.jpg");

        Assert.Equal(LocationMutationStatus.Unknown, result.Status);
    }

    [Fact]
    public async Task RemovePhotoAsync_NoContent_ReturnsSuccess()
    {
        string? csrf = null;
        string? method = null;
        var service = CreateService(
            (request, _) =>
            {
                method = request.Method.Method;
                csrf = request.Headers.TryGetValues("X-Requested-With", out var values)
                    ? values.Single()
                    : null;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
            },
            out _);

        var result = await service.RemovePhotoAsync(1);

        Assert.Equal(LocationMutationStatus.Success, result.Status);
        Assert.False(result.CleanupWarning);
        Assert.Equal("DELETE", method);
        Assert.Equal("XMLHttpRequest", csrf);
    }

    [Fact]
    public async Task RemovePhotoAsync_CleanupWarning_ReturnsSuccessWithWarning()
    {
        var service = CreateService(
            (_, _) => Task.FromResult(
                Json(HttpStatusCode.OK, new LocationCleanupWarning(true))),
            out _);

        var result = await service.RemovePhotoAsync(1);

        Assert.Equal(LocationMutationStatus.Success, result.Status);
        Assert.True(result.CleanupWarning);
    }

    [Fact]
    public async Task RemovePhotoAsync_MalformedCleanupWarningBody_ReturnsSuccessWithWarning()
    {
        var service = CreateService(
            (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("not-json", Encoding.UTF8, "application/json")
            }),
            out _);

        var result = await service.RemovePhotoAsync(1);

        Assert.Equal(LocationMutationStatus.Success, result.Status);
        Assert.True(result.CleanupWarning);
    }

    [Fact]
    public async Task RemovePhotoAsync_UnexpectedCleanupWarningShape_ReturnsSuccessWithWarning()
    {
        var service = CreateService(
            (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "{\"cleanupWarning\":\"yes\"}", Encoding.UTF8, "application/json")
            }),
            out _);

        var result = await service.RemovePhotoAsync(1);

        Assert.Equal(LocationMutationStatus.Success, result.Status);
        Assert.True(result.CleanupWarning);
    }

    [Fact]
    public async Task RemovePhotoAsync_ServerError_ReturnsUnknown()
    {
        var service = CreateService(
            (_, _) => Task.FromResult(
                Problem(HttpStatusCode.InternalServerError, "服务器错误")),
            out _);

        var result = await service.RemovePhotoAsync(1);

        Assert.Equal(LocationMutationStatus.Unknown, result.Status);
    }

    [Fact]
    public async Task RemovePhotoAsync_LostResponse_ReturnsUnknown()
    {
        var service = CreateService(
            (_, _) => throw new TaskCanceledException("timeout"),
            out _);

        var result = await service.RemovePhotoAsync(1);

        Assert.Equal(LocationMutationStatus.Unknown, result.Status);
    }

    [Fact]
    public async Task GetPhotoAsync_Success_ReturnsBytesAndContentType()
    {
        var bytes = new byte[] { 0x89, 0x50, 0x4E, 0x47 };
        var service = CreateService(
            (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(bytes)
                {
                    Headers = { ContentType = new("image/png") }
                }
            }),
            out _);

        var result = await service.GetPhotoAsync(1, 123);

        Assert.NotNull(result);
        Assert.Equal(bytes, result!.Bytes);
        Assert.Equal("image/png", result.ContentType);
    }

    [Fact]
    public async Task GetPhotoAsync_NotFound_ReturnsNull()
    {
        var service = CreateService(
            (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)),
            out _);

        Assert.Null(await service.GetPhotoAsync(1, 123));
    }

    [Fact]
    public async Task GetPhotoAsync_LostResponse_ReturnsNull()
    {
        var service = CreateService(
            (_, _) => throw new HttpRequestException("offline"),
            out _);

        Assert.Null(await service.GetPhotoAsync(1, 123));
    }

    [Fact]
    public async Task GetByIdAsync_Success_ReturnsMetadata()
    {
        var service = CreateService(
            (_, _) => Task.FromResult(Json(HttpStatusCode.OK, SampleDto())),
            out _);

        var result = await service.GetByIdAsync(1);

        Assert.NotNull(result);
        Assert.True(result!.HasPhoto);
        Assert.Equal("描述", result.Description);
    }

    [Fact]
    public async Task GetByIdAsync_Failure_ReturnsNull()
    {
        var service = CreateService(
            (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)),
            out _);

        Assert.Null(await service.GetByIdAsync(1));
    }
}
