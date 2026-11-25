using System.Text.Json;
using BoxWise.Shared.Dtos;
using BoxWise.Shared.Text;

namespace BoxWise.Client.Services;

/// <summary>
/// 位置保存的两个独立步骤：先资料，后照片。照片步骤只会在资料明确成功（或无需修改）后执行。
/// </summary>
public sealed record LocationSaveRequest(
    bool IsCreate,
    LocationDto? ExistingLocation,
    string Name,
    string? Description,
    int? ParentId,
    int SortOrder,
    bool HasSavedPhoto,
    byte[]? NewPhotoBytes,
    string? NewPhotoContentType,
    string? NewPhotoFileName,
    bool RemovePhoto);

public sealed record LocationSaveOperations(
    Func<CreateLocationRequest, CancellationToken, Task<LocationMutationResult<LocationDto>>> Create,
    Func<int, UpdateLocationRequest, CancellationToken, Task<LocationMutationResult<LocationDto>>> Update,
    Func<int, byte[], string, string, CancellationToken, Task<LocationMutationResult<LocationDto>>> UploadPhoto,
    Func<int, CancellationToken, Task<LocationPhotoRemoveOutcome>> RemovePhoto);

public enum LocationSaveStatus
{
    Completed,
    MetadataRejected,
    MetadataNotFound,
    MetadataUnknown,
    PhotoRejected,
    PhotoNotFound,
    PhotoUnknown,
    RemoveRejected,
    RemoveNotFound,
    RemoveUnknown
}

public sealed record LocationSaveResult(
    LocationSaveStatus Status,
    LocationDto? Location = null,
    bool Created = false,
    bool MetadataSaved = false,
    bool CleanupWarning = false,
    string? Error = null);

/// <summary>
/// 纯编排逻辑，便于离线验证“资料失败/未知不触发照片操作”和“重试沿用已创建 ID”。
/// 不合并资料与照片事务：两步仍分别由端点和客户端提交。
/// </summary>
public static class LocationSaveCoordinator
{
    public static async Task<LocationSaveResult> SaveAsync(
        LocationSaveRequest request,
        LocationSaveOperations operations,
        CancellationToken cancellationToken = default)
    {
        LocationDto? saved;
        var created = false;
        var metadataSaved = false;
        var name = request.Name.Trim();
        var normalizedDescription = GraphemeRules.NormalizeDescription(request.Description);

        if (request.IsCreate)
        {
            LocationMutationResult<LocationDto> createResult;
            try
            {
                createResult = await operations.Create(
                    new CreateLocationRequest(name, request.ParentId, request.SortOrder, normalizedDescription),
                    cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                return new LocationSaveResult(LocationSaveStatus.MetadataUnknown);
            }

            if (!createResult.IsSuccess || createResult.Value is null)
                return MapMetadataFailure(createResult);

            saved = createResult.Value;
            created = true;
            metadataSaved = true;
        }
        else
        {
            var existing = request.ExistingLocation;
            if (existing is null)
                return new LocationSaveResult(LocationSaveStatus.MetadataNotFound);

            var nameChanged = name != existing.Name;
            var descriptionChanged = normalizedDescription != existing.Description;

            if (nameChanged || descriptionChanged)
            {
                var update = descriptionChanged
                    ? new UpdateLocationRequest(name, JsonSerializer.SerializeToElement(normalizedDescription))
                    : new UpdateLocationRequest(name);

                LocationMutationResult<LocationDto> updateResult;
                try
                {
                    updateResult = await operations.Update(existing.Id, update, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception)
                {
                    return new LocationSaveResult(LocationSaveStatus.MetadataUnknown);
                }

                if (!updateResult.IsSuccess || updateResult.Value is null)
                    return MapMetadataFailure(updateResult);

                saved = updateResult.Value;
                metadataSaved = true;
            }
            else
            {
                saved = existing;
            }
        }

        if (request.RemovePhoto && request.HasSavedPhoto)
        {
            LocationPhotoRemoveOutcome removeResult;
            try
            {
                removeResult = await operations.RemovePhoto(saved.Id, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                return new LocationSaveResult(
                    LocationSaveStatus.RemoveUnknown, saved, created, metadataSaved);
            }

            return removeResult.Status switch
            {
                LocationMutationStatus.Success => new LocationSaveResult(
                    LocationSaveStatus.Completed,
                    saved with { HasPhoto = false },
                    created,
                    metadataSaved,
                    removeResult.CleanupWarning),
                LocationMutationStatus.NotFound => new LocationSaveResult(
                    LocationSaveStatus.RemoveNotFound, saved, created, metadataSaved,
                    Error: removeResult.Error),
                LocationMutationStatus.Unknown => new LocationSaveResult(
                    LocationSaveStatus.RemoveUnknown, saved, created, metadataSaved,
                    Error: removeResult.Error),
                _ => new LocationSaveResult(
                    LocationSaveStatus.RemoveRejected, saved, created, metadataSaved,
                    Error: removeResult.Error)
            };
        }

        if (request.NewPhotoBytes is not null)
        {
            LocationMutationResult<LocationDto> uploadResult;
            try
            {
                uploadResult = await operations.UploadPhoto(
                    saved.Id,
                    request.NewPhotoBytes,
                    request.NewPhotoContentType ?? "application/octet-stream",
                    request.NewPhotoFileName ?? "photo",
                    cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                return new LocationSaveResult(
                    LocationSaveStatus.PhotoUnknown, saved, created, metadataSaved);
            }

            return uploadResult.Status switch
            {
                LocationMutationStatus.Success => new LocationSaveResult(
                    LocationSaveStatus.Completed,
                    uploadResult.Value ?? (saved with { HasPhoto = true }),
                    created,
                    metadataSaved),
                LocationMutationStatus.NotFound => new LocationSaveResult(
                    LocationSaveStatus.PhotoNotFound, saved, created, metadataSaved,
                    Error: uploadResult.Error),
                LocationMutationStatus.Unknown => new LocationSaveResult(
                    LocationSaveStatus.PhotoUnknown, saved, created, metadataSaved,
                    Error: uploadResult.Error),
                _ => new LocationSaveResult(
                    LocationSaveStatus.PhotoRejected, saved, created, metadataSaved,
                    Error: uploadResult.Error)
            };
        }

        return new LocationSaveResult(
            LocationSaveStatus.Completed, saved, created, metadataSaved);
    }

    private static LocationSaveResult MapMetadataFailure(
        LocationMutationResult<LocationDto> result) =>
        result.Status switch
        {
            LocationMutationStatus.NotFound => new LocationSaveResult(
                LocationSaveStatus.MetadataNotFound, Error: result.Error),
            LocationMutationStatus.Unknown => new LocationSaveResult(
                LocationSaveStatus.MetadataUnknown, Error: result.Error),
            _ => new LocationSaveResult(
                LocationSaveStatus.MetadataRejected, Error: result.Error)
        };
}
