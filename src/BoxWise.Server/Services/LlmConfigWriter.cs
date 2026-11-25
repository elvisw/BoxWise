using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using BoxWise.Server.Data;
using BoxWise.Server.Models;

namespace BoxWise.Server.Services;

public enum LlmConfigWriteStatus
{
    Saved,
    KeyRequired,
    Conflict
}

public sealed record LlmConfigUpdate(
    string BaseUrl,
    string? ApiKey,
    string Model,
    int TimeoutSeconds);

public sealed record LlmConfigWriteResult(
    LlmConfigWriteStatus Status,
    LlmConfig? Config = null);

/// <summary>
/// The single writer for the singleton LLM configuration. Existing rows are changed with
/// one conditional UPDATE whose predicate pins the exact URL and Key snapshot used for the
/// origin check, so a concurrent write cannot leave a new URL paired with another writer's Key.
/// </summary>
public static class LlmConfigWriter
{
    public const int ConfigId = 1;
    public const string KeyRequiredMessage =
        "仅当 API 地址不变（可只改模型或超时）或协议、主机和有效端口可证明相同时，才能留空 ApiKey；更改来源或地址无法证明同源（包括无效旧地址）需填写新 Key";
    public const string ConflictMessage = "配置已被其他管理员修改，请刷新后重试";

    private const string DefaultModel = "doubao-seed-2-0-pro-260215";
    private const int MaxAttempts = 4;

    public static async Task<LlmConfigWriteResult> SaveAsync(
        AppDbContext db, LlmConfigUpdate update, CancellationToken cancellationToken = default)
    {
        var targetBaseUrl = update.BaseUrl.Trim();
        var targetKey = string.IsNullOrWhiteSpace(update.ApiKey) ? null : update.ApiKey.Trim();
        var targetModel = string.IsNullOrWhiteSpace(update.Model) ? DefaultModel : update.Model.Trim();
        var targetTimeout = Math.Clamp(update.TimeoutSeconds, 5, 120);

        LlmConfig? lastSnapshot = null;
        for (var attempt = 0; attempt < MaxAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var snapshot = await db.LlmConfigs
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == ConfigId, cancellationToken);

            lastSnapshot = snapshot;

            if (snapshot is null)
            {
                var created = new LlmConfig
                {
                    Id = ConfigId,
                    BaseUrl = targetBaseUrl,
                    ApiKey = targetKey,
                    Model = targetModel,
                    TimeoutSeconds = targetTimeout
                };
                db.LlmConfigs.Add(created);

                try
                {
                    await db.SaveChangesAsync(cancellationToken);
                    return new LlmConfigWriteResult(LlmConfigWriteStatus.Saved, created);
                }
                catch (Exception ex) when (ex is DbUpdateException or DbException)
                {
                    // Another request inserted the singleton first; re-read and apply the
                    // origin rule to that row instead of guessing.
                    db.Entry(created).State = EntityState.Detached;
                    continue;
                }
            }

            if (!LlmConfigKeyPolicy.CanRetainKey(
                    snapshot.BaseUrl, snapshot.ApiKey, targetBaseUrl, targetKey))
            {
                return new LlmConfigWriteResult(LlmConfigWriteStatus.KeyRequired, snapshot);
            }

            var resolvedKey = targetKey ?? snapshot.ApiKey;
            if (Matches(snapshot, targetBaseUrl, resolvedKey, targetModel, targetTimeout))
                return new LlmConfigWriteResult(LlmConfigWriteStatus.Saved, snapshot);

            int affected;
            try
            {
                // URL and Key are written in the same statement, and both old values are
                // part of the predicate. If either changed after the snapshot, this is a
                // no-op and the loop re-reads before re-checking the origin rule.
                affected = await db.LlmConfigs
                    .Where(c => c.Id == ConfigId
                        && c.BaseUrl == snapshot.BaseUrl
                        && c.ApiKey == snapshot.ApiKey)
                    .ExecuteUpdateAsync(setters => setters
                            .SetProperty(c => c.BaseUrl, targetBaseUrl)
                            .SetProperty(c => c.ApiKey, resolvedKey)
                            .SetProperty(c => c.Model, targetModel)
                            .SetProperty(c => c.TimeoutSeconds, targetTimeout),
                        cancellationToken);
            }
            catch (Exception ex) when (ex is DbUpdateException or DbException)
            {
                continue;
            }

            if (affected == 1)
            {
                return new LlmConfigWriteResult(LlmConfigWriteStatus.Saved, new LlmConfig
                {
                    Id = ConfigId,
                    BaseUrl = targetBaseUrl,
                    ApiKey = resolvedKey,
                    Model = targetModel,
                    TimeoutSeconds = targetTimeout
                });
            }
        }

        return new LlmConfigWriteResult(LlmConfigWriteStatus.Conflict, lastSnapshot);
    }

    private static bool Matches(
        LlmConfig snapshot, string baseUrl, string? apiKey, string model, int timeoutSeconds)
        => string.Equals(snapshot.BaseUrl, baseUrl, StringComparison.Ordinal)
           && string.Equals(snapshot.ApiKey, apiKey, StringComparison.Ordinal)
           && string.Equals(snapshot.Model, model, StringComparison.Ordinal)
           && snapshot.TimeoutSeconds == timeoutSeconds;
}
