using System.Text.Json;
using BoxWise.Client.Services;
using BoxWise.Shared.Dtos;

namespace BoxWise.Client.Tests.Services;

public class LocationSaveCoordinatorTests
{
    private static LocationDto Location(
        int id = 1, string name = "抽屉", string? description = null, bool hasPhoto = false) =>
        new(id, name, $"/{id}/", null, 0, description, hasPhoto, name);

    private sealed class RecordingOperations
    {
        public LocationMutationResult<LocationDto> CreateResult { get; set; } =
            LocationMutationResult<LocationDto>.Success(Location());

        public LocationMutationResult<LocationDto> UpdateResult { get; set; } =
            LocationMutationResult<LocationDto>.Success(Location());

        public LocationMutationResult<LocationDto> UploadResult { get; set; } =
            LocationMutationResult<LocationDto>.Success(Location(hasPhoto: true));

        public LocationPhotoRemoveOutcome RemoveResult { get; set; } =
            new(LocationMutationStatus.Success);

        public Exception? CreateException { get; set; }
        public Exception? UpdateException { get; set; }
        public Exception? UploadException { get; set; }
        public Exception? RemoveException { get; set; }

        public int CreateCalls { get; private set; }
        public int UpdateCalls { get; private set; }
        public int UploadCalls { get; private set; }
        public int RemoveCalls { get; private set; }

        public CreateLocationRequest? LastCreate { get; private set; }
        public UpdateLocationRequest? LastUpdate { get; private set; }
        public int? LastUpdateId { get; private set; }
        public int? LastUploadId { get; private set; }
        public int? LastRemoveId { get; private set; }

        public LocationSaveOperations ToOperations() => new(
            (request, _) =>
            {
                CreateCalls++;
                LastCreate = request;
                return CreateException is null
                    ? Task.FromResult(CreateResult)
                    : Task.FromException<LocationMutationResult<LocationDto>>(CreateException);
            },
            (id, request, _) =>
            {
                UpdateCalls++;
                LastUpdateId = id;
                LastUpdate = request;
                return UpdateException is null
                    ? Task.FromResult(UpdateResult)
                    : Task.FromException<LocationMutationResult<LocationDto>>(UpdateException);
            },
            (id, _, _, _, _) =>
            {
                UploadCalls++;
                LastUploadId = id;
                return UploadException is null
                    ? Task.FromResult(UploadResult)
                    : Task.FromException<LocationMutationResult<LocationDto>>(UploadException);
            },
            (id, _) =>
            {
                RemoveCalls++;
                LastRemoveId = id;
                return RemoveException is null
                    ? Task.FromResult(RemoveResult)
                    : Task.FromException<LocationPhotoRemoveOutcome>(RemoveException);
            });
    }

    private static LocationSaveRequest CreateRequest(byte[]? photo = null) => new(
        IsCreate: true,
        ExistingLocation: null,
        Name: "新抽屉",
        Description: "新描述",
        ParentId: null,
        SortOrder: 0,
        HasSavedPhoto: false,
        NewPhotoBytes: photo,
        NewPhotoContentType: photo is null ? null : "image/jpeg",
        NewPhotoFileName: photo is null ? null : "photo.jpg",
        RemovePhoto: false);

    private static LocationSaveRequest EditRequest(
        LocationDto existing,
        string name,
        string? description,
        byte[]? photo = null,
        bool removePhoto = false) => new(
        IsCreate: false,
        ExistingLocation: existing,
        Name: name,
        Description: description,
        ParentId: existing.ParentId,
        SortOrder: existing.SortOrder,
        HasSavedPhoto: existing.HasPhoto,
        NewPhotoBytes: photo,
        NewPhotoContentType: photo is null ? null : "image/jpeg",
        NewPhotoFileName: photo is null ? null : "photo.jpg",
        RemovePhoto: removePhoto);

    [Fact]
    public async Task Create_MetadataRejected_DoesNotTouchPhoto()
    {
        var operations = new RecordingOperations
        {
            CreateResult = LocationMutationResult<LocationDto>.Rejected("名称无效")
        };

        var result = await LocationSaveCoordinator.SaveAsync(
            CreateRequest([1, 2, 3]), operations.ToOperations());

        Assert.Equal(LocationSaveStatus.MetadataRejected, result.Status);
        Assert.False(result.MetadataSaved);
        Assert.False(result.Created);
        Assert.Equal(1, operations.CreateCalls);
        Assert.Equal(0, operations.UploadCalls);
        Assert.Equal(0, operations.RemoveCalls);
    }

    [Fact]
    public async Task Create_MetadataUnknown_DoesNotTouchPhoto()
    {
        var operations = new RecordingOperations
        {
            CreateResult = LocationMutationResult<LocationDto>.Unknown()
        };

        var result = await LocationSaveCoordinator.SaveAsync(
            CreateRequest([1, 2, 3]), operations.ToOperations());

        Assert.Equal(LocationSaveStatus.MetadataUnknown, result.Status);
        Assert.Equal(0, operations.UploadCalls);
        Assert.Equal(0, operations.RemoveCalls);
    }

    [Fact]
    public async Task Create_MetadataThrows_ReturnsUnknownWithoutPhoto()
    {
        var operations = new RecordingOperations
        {
            CreateException = new HttpRequestException("connection reset")
        };

        var result = await LocationSaveCoordinator.SaveAsync(
            CreateRequest([1, 2, 3]), operations.ToOperations());

        Assert.Equal(LocationSaveStatus.MetadataUnknown, result.Status);
        Assert.Equal(0, operations.UploadCalls);
        Assert.Equal(0, operations.RemoveCalls);
    }

    [Fact]
    public async Task Create_PhotoRejected_RetryUsesSameIdWithoutRecreating()
    {
        var created = Location(42, "新抽屉", "新描述");
        var operations = new RecordingOperations
        {
            CreateResult = LocationMutationResult<LocationDto>.Success(created),
            UploadResult = LocationMutationResult<LocationDto>.Rejected("照片超过 10 MB")
        };

        var result = await LocationSaveCoordinator.SaveAsync(
            CreateRequest([1, 2, 3]), operations.ToOperations());

        Assert.Equal(LocationSaveStatus.PhotoRejected, result.Status);
        Assert.True(result.Created);
        Assert.True(result.MetadataSaved);
        Assert.Equal(42, result.Location!.Id);
        Assert.Equal(1, operations.CreateCalls);
        Assert.Equal(1, operations.UploadCalls);
        Assert.Equal(42, operations.LastUploadId);

        // 重试：表单已切换为编辑该新位置，只补传照片。
        var retryOperations = new RecordingOperations
        {
            UploadResult = LocationMutationResult<LocationDto>.Success(
                Location(42, "新抽屉", "新描述", hasPhoto: true))
        };
        var retry = await LocationSaveCoordinator.SaveAsync(
            EditRequest(result.Location!, "新抽屉", "新描述", [1, 2, 3]),
            retryOperations.ToOperations());

        Assert.Equal(LocationSaveStatus.Completed, retry.Status);
        Assert.Equal(0, retryOperations.CreateCalls);
        Assert.Equal(0, retryOperations.UpdateCalls);
        Assert.Equal(42, retryOperations.LastUploadId);
        Assert.Equal(1, retryOperations.UploadCalls);
    }

    [Fact]
    public async Task Create_PhotoUnknown_KeepsCreatedIdForRecheck()
    {
        var operations = new RecordingOperations
        {
            CreateResult = LocationMutationResult<LocationDto>.Success(Location(7)),
            UploadResult = LocationMutationResult<LocationDto>.Unknown()
        };

        var result = await LocationSaveCoordinator.SaveAsync(
            CreateRequest([1, 2, 3]), operations.ToOperations());

        Assert.Equal(LocationSaveStatus.PhotoUnknown, result.Status);
        Assert.True(result.Created);
        Assert.True(result.MetadataSaved);
        Assert.Equal(7, result.Location!.Id);
    }

    [Fact]
    public async Task Edit_NameOnly_OmitsUnchangedDescription()
    {
        var existing = Location(5, "旧名", "旧描述");
        var operations = new RecordingOperations
        {
            UpdateResult = LocationMutationResult<LocationDto>.Success(
                Location(5, "新名", "旧描述"))
        };

        var result = await LocationSaveCoordinator.SaveAsync(
            EditRequest(existing, "新名", "旧描述"), operations.ToOperations());

        Assert.Equal(LocationSaveStatus.Completed, result.Status);
        Assert.Equal(1, operations.UpdateCalls);
        Assert.NotNull(operations.LastUpdate);
        Assert.Equal("新名", operations.LastUpdate!.Name);
        Assert.Equal(JsonValueKind.Undefined, operations.LastUpdate.Description.ValueKind);
        Assert.Equal(0, operations.UploadCalls);
    }

    [Fact]
    public async Task Edit_DescriptionOnly_SendsExplicitDescription()
    {
        var existing = Location(5, "名称", "旧描述");
        var operations = new RecordingOperations
        {
            UpdateResult = LocationMutationResult<LocationDto>.Success(
                Location(5, "名称", "新描述"))
        };

        var result = await LocationSaveCoordinator.SaveAsync(
            EditRequest(existing, "名称", "新描述"), operations.ToOperations());

        Assert.Equal(LocationSaveStatus.Completed, result.Status);
        Assert.Equal(1, operations.UpdateCalls);
        Assert.Equal(JsonValueKind.String, operations.LastUpdate!.Description.ValueKind);
        Assert.Equal("新描述", operations.LastUpdate.Description.GetString());
    }

    [Fact]
    public async Task Edit_ClearingDescription_SendsExplicitNull()
    {
        var existing = Location(5, "名称", "旧描述");
        var operations = new RecordingOperations
        {
            UpdateResult = LocationMutationResult<LocationDto>.Success(Location(5))
        };

        var result = await LocationSaveCoordinator.SaveAsync(
            EditRequest(existing, "名称", "   "), operations.ToOperations());

        Assert.Equal(LocationSaveStatus.Completed, result.Status);
        Assert.Equal(JsonValueKind.Null, operations.LastUpdate!.Description.ValueKind);
    }

    [Fact]
    public async Task Edit_NoMetadataChange_SkipsUpdateAndUploadsWithExistingId()
    {
        var existing = Location(9, "名称", "描述", hasPhoto: true);
        var operations = new RecordingOperations
        {
            UploadResult = LocationMutationResult<LocationDto>.Success(
                Location(9, "名称", "描述", hasPhoto: true))
        };

        var result = await LocationSaveCoordinator.SaveAsync(
            EditRequest(existing, "名称", "描述", [4, 5, 6]),
            operations.ToOperations());

        Assert.Equal(LocationSaveStatus.Completed, result.Status);
        Assert.Equal(0, operations.UpdateCalls);
        Assert.Equal(1, operations.UploadCalls);
        Assert.Equal(9, operations.LastUploadId);
    }

    [Fact]
    public async Task Edit_MetadataRejected_DoesNotTouchPhoto()
    {
        var existing = Location(5, "旧名", "旧描述");
        var operations = new RecordingOperations
        {
            UpdateResult = LocationMutationResult<LocationDto>.Rejected("描述不能超过 300 字。")
        };

        var result = await LocationSaveCoordinator.SaveAsync(
            EditRequest(existing, "新名", "新描述", [1]), operations.ToOperations());

        Assert.Equal(LocationSaveStatus.MetadataRejected, result.Status);
        Assert.Equal(0, operations.UploadCalls);
    }

    [Fact]
    public async Task Edit_MetadataUnknown_DoesNotTouchPhoto()
    {
        var existing = Location(5, "旧名", "旧描述");
        var operations = new RecordingOperations
        {
            UpdateResult = LocationMutationResult<LocationDto>.Unknown()
        };

        var result = await LocationSaveCoordinator.SaveAsync(
            EditRequest(existing, "新名", "新描述", [1]), operations.ToOperations());

        Assert.Equal(LocationSaveStatus.MetadataUnknown, result.Status);
        Assert.Equal(0, operations.UploadCalls);
    }

    [Fact]
    public async Task Edit_UpdateThrows_ReturnsMetadataUnknownWithoutPhoto()
    {
        var existing = Location(5, "旧名", "旧描述");
        var operations = new RecordingOperations
        {
            UpdateException = new HttpRequestException("timeout")
        };

        var result = await LocationSaveCoordinator.SaveAsync(
            EditRequest(existing, "新名", "新描述", [1]), operations.ToOperations());

        Assert.Equal(LocationSaveStatus.MetadataUnknown, result.Status);
        Assert.False(result.MetadataSaved);
        Assert.Equal(0, operations.UploadCalls);
    }

    [Fact]
    public async Task Upload_Success_ReturnsServerUpdatedHasPhoto()
    {
        var existing = Location(5, "名称", null, hasPhoto: false);
        var updated = Location(5, "名称", null, hasPhoto: true);
        var operations = new RecordingOperations
        {
            UploadResult = LocationMutationResult<LocationDto>.Success(updated)
        };

        var result = await LocationSaveCoordinator.SaveAsync(
            EditRequest(existing, "名称", null, [1]),
            operations.ToOperations());

        Assert.Equal(LocationSaveStatus.Completed, result.Status);
        Assert.Equal(updated, result.Location);
        Assert.True(result.Location!.HasPhoto);
    }

    [Fact]
    public async Task Remove_Success_ReturnsHasPhotoFalse()
    {
        var existing = Location(5, "名称", null, hasPhoto: true);
        var operations = new RecordingOperations
        {
            RemoveResult = new LocationPhotoRemoveOutcome(LocationMutationStatus.Success)
        };

        var result = await LocationSaveCoordinator.SaveAsync(
            EditRequest(existing, "名称", null, removePhoto: true),
            operations.ToOperations());

        Assert.Equal(LocationSaveStatus.Completed, result.Status);
        Assert.False(result.Location!.HasPhoto);
        Assert.Equal(5, result.Location.Id);
    }

    [Fact]
    public async Task Upload_Throws_ReturnsPhotoUnknownWithMetadataSaved()
    {
        var operations = new RecordingOperations
        {
            CreateResult = LocationMutationResult<LocationDto>.Success(Location(11)),
            UploadException = new HttpRequestException("reset")
        };

        var result = await LocationSaveCoordinator.SaveAsync(
            CreateRequest([1]), operations.ToOperations());

        Assert.Equal(LocationSaveStatus.PhotoUnknown, result.Status);
        Assert.True(result.MetadataSaved);
        Assert.True(result.Created);
        Assert.Equal(11, result.Location!.Id);
    }

    [Fact]
    public async Task Remove_NotFound_ReturnsRemoveNotFoundWithoutUpload()
    {
        var existing = Location(3, "名称", null, hasPhoto: true);
        var operations = new RecordingOperations
        {
            RemoveResult = new LocationPhotoRemoveOutcome(LocationMutationStatus.NotFound)
        };

        var result = await LocationSaveCoordinator.SaveAsync(
            EditRequest(existing, "名称", null, removePhoto: true),
            operations.ToOperations());

        Assert.Equal(LocationSaveStatus.RemoveNotFound, result.Status);
        Assert.Equal(1, operations.RemoveCalls);
        Assert.Equal(0, operations.UploadCalls);
    }

    [Fact]
    public async Task Remove_CleanupWarning_ReturnsCompletedWithWarning()
    {
        var existing = Location(3, "名称", null, hasPhoto: true);
        var operations = new RecordingOperations
        {
            RemoveResult = new LocationPhotoRemoveOutcome(
                LocationMutationStatus.Success, true)
        };

        var result = await LocationSaveCoordinator.SaveAsync(
            EditRequest(existing, "名称", null, removePhoto: true),
            operations.ToOperations());

        Assert.Equal(LocationSaveStatus.Completed, result.Status);
        Assert.True(result.CleanupWarning);
        Assert.Equal(0, operations.UploadCalls);
    }

    [Fact]
    public async Task Remove_Throws_ReturnsRemoveUnknownWithMetadataSaved()
    {
        var existing = Location(3, "旧名称", null, hasPhoto: true);
        var operations = new RecordingOperations
        {
            UpdateResult = LocationMutationResult<LocationDto>.Success(
                Location(3, "新名称", null, hasPhoto: true)),
            RemoveException = new HttpRequestException("reset")
        };

        var result = await LocationSaveCoordinator.SaveAsync(
            EditRequest(existing, "新名称", null, removePhoto: true),
            operations.ToOperations());

        Assert.Equal(LocationSaveStatus.RemoveUnknown, result.Status);
        Assert.True(result.MetadataSaved);
    }
}
