using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using BoxWise.Server.Data;
using BoxWise.Server.Models;
using OtpNet;

namespace BoxWise.Server.Services;

/// <summary>
/// 2FA 清理结果状态。
/// </summary>
public enum TwoFactorCleanupStatus
{
    /// <summary>清理已提交。</summary>
    Succeeded,

    /// <summary>恢复码不在自定义表或 Identity token store 中；未写入任何状态。</summary>
    InvalidCode,

    /// <summary>并发修改或 SQLite 写锁冲突；事务已回滚。</summary>
    Conflict,

    /// <summary>存储失败；事务已回滚。</summary>
    StorageFailure
}

/// <summary>
/// 2FA 清理结果。<see cref="OutcomeConfirmed"/> 为 false 表示无法确认提交是否生效；
/// 调用方不得向用户断言最终状态，也不得据此宣称“恢复码未被使用”。
/// </summary>
public sealed record TwoFactorCleanupResult(
    TwoFactorCleanupStatus Status,
    bool OutcomeConfirmed = true)
{
    public bool Succeeded => Status == TwoFactorCleanupStatus.Succeeded;
}

public class RecoveryCodeService
{
    private readonly AppDbContext _db;
    private readonly ILogger<RecoveryCodeService>? _logger;

    public RecoveryCodeService(AppDbContext db, ILogger<RecoveryCodeService>? logger = null)
    {
        _db = db;
        _logger = logger;
    }

    /// <summary>
    /// 生成 8 个 10 位恢复码（base32 编码）。
    /// </summary>
    public static List<string> GenerateRecoveryCodes()
    {
        var codes = new List<string>(8);
        for (int i = 0; i < 8; i++)
        {
            var bytes = new byte[6]; // 6 bytes = 48 bits → 10 base32 chars
            RandomNumberGenerator.Fill(bytes);
            var code = Base32Encoding.ToString(bytes);
            // 确保恰好 10 字符（补齐或截断）
            codes.Add(code.Length >= 10 ? code[..10] : code.PadRight(10, 'A'));
        }
        return codes;
    }

    /// <summary>
    /// 存储恢复码哈希（SHA-256）到数据库。先清除该用户的旧恢复码。
    /// </summary>
    public async Task StoreRecoveryCodesAsync(AppUser user, List<string> codes)
    {
        var oldCodes = await _db.RecoveryCodes
            .Where(rc => rc.UserId == user.Id)
            .ToListAsync();
        _db.RecoveryCodes.RemoveRange(oldCodes);

        var recoveryCodes = codes.Select(c => new RecoveryCode
        {
            UserId = user.Id,
            CodeHash = HashCode(c)
        }).ToList();

        _db.RecoveryCodes.AddRange(recoveryCodes);
        await _db.SaveChangesAsync();
    }

    /// <summary>
    /// 用户自助关闭双重验证（A2）：清除两套恢复码与全部 2FA 字段并轮换 SecurityStamp；
    /// 保留 Identity authenticator key、通行密钥和现有宽限期。
    /// 调用方应在提交成功后刷新当前登录。
    /// </summary>
    public Task<TwoFactorCleanupResult> DisableTwoFactorAsync(
        AppUser user, UserManager<AppUser> userManager) =>
        ExecuteCleanupAsync(user, userManager, CleanupMode.SelfDisable, recoveryCode: null);

    /// <summary>
    /// 使用恢复码关闭双重验证（A3/A4）：先检查自定义恢复码，未命中时才检查并消耗 Identity 恢复码。
    /// 成功后保留 Identity authenticator key 与通行密钥，设置 24 小时宽限期并轮换 SecurityStamp。
    /// </summary>
    public Task<TwoFactorCleanupResult> DisableWithRecoveryCodeAsync(
        AppUser user, string? recoveryCode, UserManager<AppUser> userManager) =>
        ExecuteCleanupAsync(user, userManager, CleanupMode.RecoveryCode, recoveryCode);

    /// <summary>
    /// 管理员重置目标用户的双重验证（A1）：删除通行密钥，清除两套恢复码与全部 2FA 字段，
    /// 清除宽限期；仅当 Identity authenticator key 存在时轮换它，并轮换 SecurityStamp。
    /// </summary>
    public Task<TwoFactorCleanupResult> ResetForAdminAsync(
        AppUser user, UserManager<AppUser> userManager) =>
        ExecuteCleanupAsync(user, userManager, CleanupMode.AdminReset, recoveryCode: null);

    /// <summary>
    /// 重新生成恢复码（旧码全部失效）。
    /// </summary>
    public async Task<List<string>> RegenerateRecoveryCodesAsync(AppUser user)
    {
        var codes = GenerateRecoveryCodes();
        await StoreRecoveryCodesAsync(user, codes);
        return codes;
    }

    /// <summary>
    /// 删除该用户全部恢复码（不更改 2FA 状态）。用于删除最后一个通行密钥后关闭 2FA 的场景。
    /// </summary>
    public async Task RemoveAllRecoveryCodesAsync(AppUser user)
    {
        var codes = await _db.RecoveryCodes
            .Where(rc => rc.UserId == user.Id)
            .ToListAsync();
        if (codes.Count == 0)
            return;

        _db.RecoveryCodes.RemoveRange(codes);
        await _db.SaveChangesAsync();
    }

    /// <summary>
    /// 检查用户是否存在恢复码。
    /// </summary>
    public async Task<bool> HasRecoveryCodesAsync(AppUser user)
    {
        return await _db.RecoveryCodes.AnyAsync(rc => rc.UserId == user.Id);
    }

    /// <summary>
    /// 非消耗性验证恢复码（仅校验哈希，不销毁码，不更改用户状态）。
    /// 用于 modify 流程中的身份验证（区别于登录时的消耗式验证）。
    /// </summary>
    public async Task<bool> ValidateRecoveryCodeAsync(AppUser user, string code)
    {
        if (string.IsNullOrWhiteSpace(code))
            return false;
        var codeHash = HashCode(code);
        return await _db.RecoveryCodes.AnyAsync(rc => rc.UserId == user.Id && rc.CodeHash == codeHash);
    }

    private enum CleanupMode
    {
        SelfDisable,
        RecoveryCode,
        AdminReset
    }

    /// <summary>
    /// 在单个 SQLite 事务中完成全部清理。UserManager 与共享的 scoped
    /// <see cref="AppDbContext"/> 是同一个上下文，因此 Identity 内部的多次 SaveChanges 都在该事务内。
    /// </summary>
    private async Task<TwoFactorCleanupResult> ExecuteCleanupAsync(
        AppUser user,
        UserManager<AppUser> userManager,
        CleanupMode mode,
        string? recoveryCode)
    {
        IDbContextTransaction? transaction = null;

        try
        {
            // BeginTransactionAsync 也可能因 SQLite 写锁失败，因此与事务体共用同一套分类。
            try
            {
                transaction = await _db.Database.BeginTransactionAsync();
                var failure = await CleanupInTransactionAsync(user, userManager, mode, recoveryCode);
                if (failure is not null)
                    return await RollBackAsync(transaction, failure.Value);
            }
            catch (Exception ex) when (IsConflict(ex))
            {
                _logger?.LogWarning(ex, "Concurrent change during 2FA cleanup for user {UserId}.", user.Id);
                return await RollBackAsync(transaction, TwoFactorCleanupStatus.Conflict);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "2FA cleanup failed for user {UserId}.", user.Id);
                return await RollBackAsync(transaction, TwoFactorCleanupStatus.StorageFailure);
            }

            try
            {
                await transaction.CommitAsync();
                return new TwoFactorCleanupResult(TwoFactorCleanupStatus.Succeeded);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to commit the 2FA cleanup transaction for user {UserId}.", user.Id);
                await TryRollBackAsync(transaction);
                _db.ChangeTracker.Clear();
                // 提交状态未知：不得报告成功，也不得断言清理未生效。
                return new TwoFactorCleanupResult(
                    TwoFactorCleanupStatus.StorageFailure, OutcomeConfirmed: false);
            }
        }
        finally
        {
            if (transaction is not null)
            {
                try
                {
                    await transaction.DisposeAsync();
                }
                catch (Exception ex)
                {
                    _logger?.LogDebug(ex, "Failed to dispose the 2FA cleanup transaction.");
                }
            }
        }
    }

    /// <summary>
    /// 事务体：重载用户、按需校验并消耗恢复码、应用清理。
    /// 返回 null 表示可以提交；否则为需要回滚的状态。
    /// </summary>
    private async Task<TwoFactorCleanupStatus?> CleanupInTransactionAsync(
        AppUser user, UserManager<AppUser> userManager, CleanupMode mode, string? recoveryCode)
    {
        if (!await TryReloadAsync(user))
        {
            _logger?.LogWarning("2FA cleanup target user {UserId} is no longer available.", user.Id);
            return TwoFactorCleanupStatus.StorageFailure;
        }

        if (mode == CleanupMode.RecoveryCode)
        {
            var codeFailure = await RedeemRecoveryCodeAsync(user, userManager, recoveryCode);
            if (codeFailure is not null)
                return codeFailure;
        }

        return await ApplyCleanupAsync(user, userManager, mode);
    }

    /// <summary>
    /// 用 <see cref="EntityEntry.ReloadAsync"/> 把同一 scoped UserManager 取出的 tracked 用户
    /// 刷新为数据库最新值；它同时更新 OriginalValues，因此并发令牌仍能被真实检测。
    /// Detached 表示调用方传入的不是该上下文的用户；重载抛并发异常表示该行已不存在。
    /// </summary>
    private async Task<bool> TryReloadAsync(AppUser user)
    {
        var entry = _db.Entry(user);
        if (entry.State == EntityState.Detached)
            return false;

        try
        {
            await entry.ReloadAsync();
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            return false;
        }
    }

    /// <summary>
    /// 校验并在事务内消耗恢复码。自定义码命中时由清理阶段统一删除全部自定义码；
    /// Identity 码命中时先按原生语义消耗本次使用的码（保存一次），再清空剩余码。
    /// </summary>
    private async Task<TwoFactorCleanupStatus?> RedeemRecoveryCodeAsync(
        AppUser user, UserManager<AppUser> userManager, string? recoveryCode)
    {
        // 自定义码为 base32 大写；容忍用户输入空格、前后空白和小写。
        var normalized = NormalizeRecoveryCode(recoveryCode);
        if (normalized.Length == 0)
            return TwoFactorCleanupStatus.InvalidCode;

        var codeHash = HashCode(normalized);
        var customMatch = await _db.RecoveryCodes
            .AnyAsync(rc => rc.UserId == user.Id && rc.CodeHash == codeHash);
        if (customMatch)
            return null;

        // 仅当自定义码未命中时才检查 Identity token store。
        var result = await userManager.RedeemTwoFactorRecoveryCodeAsync(user, normalized);
        if (result.Succeeded)
            return null;

        // 只有 RecoveryCodeRedemptionFailed 表示“码未命中”；并发与其他错误另行分类。
        return result.Errors.Any(e => e.Code == nameof(IdentityErrorDescriber.RecoveryCodeRedemptionFailed))
            ? TwoFactorCleanupStatus.InvalidCode
            : ClassifyIdentityWrite(result, "redeem the Identity recovery code", user.Id);
    }

    /// <summary>
    /// 应用清理并检查每一次 Identity 写入。返回 null 表示全部写入成功（尚未提交）。
    /// </summary>
    private async Task<TwoFactorCleanupStatus?> ApplyCleanupAsync(
        AppUser user, UserManager<AppUser> userManager, CleanupMode mode)
    {
        var customCodes = await _db.RecoveryCodes
            .Where(rc => rc.UserId == user.Id)
            .ToListAsync();
        if (customCodes.Count > 0)
            _db.RecoveryCodes.RemoveRange(customCodes);

        if (mode == CleanupMode.AdminReset)
        {
            var credentials = await _db.WebAuthnCredentials
                .Where(wc => wc.UserId == user.Id)
                .ToListAsync();
            if (credentials.Count > 0)
                _db.WebAuthnCredentials.RemoveRange(credentials);
        }

        user.TwoFactorEnabled = false;
        user.ConfiguredMethods = TwoFactorMethod.None;
        user.TotpSecretKey = null;
        user.PendingTotpSecretKey = null;
        user.EmailForTwoFactor = null;
        user.TwoFactorSetupCompletedAt = null;

        user.TwoFactorGracePeriodUntil = mode switch
        {
            CleanupMode.AdminReset => null,
            CleanupMode.RecoveryCode => DateTime.UtcNow.AddHours(24),
            _ => user.TwoFactorGracePeriodUntil
        };

        var updateResult = await userManager.UpdateAsync(user);
        if (!updateResult.Succeeded)
            return ClassifyIdentityWrite(updateResult, "update user", user.Id);

        // 以 0 个新码替换，等价于清空 Identity token store 中的全部恢复码。
        // null 表示 Identity 的用户更新未成功，必须按失败处理；不能用同上下文内的
        // 计数为零来替代，因为该计数只是内存中尚未提交的空码集合。
        if (await userManager.GenerateNewTwoFactorRecoveryCodesAsync(user, 0) is null)
        {
            _logger?.LogWarning(
                "Identity did not persist the recovery-code cleanup for user {UserId}.", user.Id);
            return TwoFactorCleanupStatus.StorageFailure;
        }

        if (mode == CleanupMode.AdminReset)
        {
            // 仅在 Identity authenticator key 存在时轮换它：旧密钥立即失效，缺失时不凭空创建。
            var authenticatorKey = await userManager.GetAuthenticatorKeyAsync(user);
            if (!string.IsNullOrEmpty(authenticatorKey))
            {
                var resetResult = await userManager.ResetAuthenticatorKeyAsync(user);
                if (!resetResult.Succeeded)
                    return ClassifyIdentityWrite(resetResult, "rotate the Identity authenticator key", user.Id);
            }
        }

        var stampResult = await userManager.UpdateSecurityStampAsync(user);
        return stampResult.Succeeded
            ? null
            : ClassifyIdentityWrite(stampResult, "rotate the SecurityStamp", user.Id);
    }

    private async Task<TwoFactorCleanupResult> RollBackAsync(
        IDbContextTransaction? transaction, TwoFactorCleanupStatus status)
    {
        var confirmed = await TryRollBackAsync(transaction);

        // 丢弃失败后仍被跟踪的脏修改，避免同一请求后续的 SaveChanges 写入部分清理。
        _db.ChangeTracker.Clear();

        return new TwoFactorCleanupResult(status, OutcomeConfirmed: confirmed);
    }

    private async Task<bool> TryRollBackAsync(IDbContextTransaction? transaction)
    {
        if (transaction is null)
            return true;

        try
        {
            await transaction.RollbackAsync();
            return true;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to roll back the 2FA cleanup transaction.");
            return false;
        }
    }

    /// <summary>
    /// 并发冲突与 SQLite 写锁（busy/locked）需要与普通存储失败区分。
    /// SQLite 异常可能被 DbUpdateException 或其他包装类型嵌套，因此沿 InnerException 链查找。
    /// </summary>
    private static bool IsConflict(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is DbUpdateConcurrencyException)
                return true;
            if (current is SqliteException { SqliteErrorCode: 5 or 6 })
                return true;
        }

        return false;
    }

    /// <summary>
    /// 记录失败的 Identity 写入并按错误码分类：并发冲突可区分，
    /// 其余（含 RecoveryCodeRedemptionFailed 以外的码校验失败）视为存储失败。
    /// </summary>
    private TwoFactorCleanupStatus ClassifyIdentityWrite(
        IdentityResult result, string operation, string userId)
    {
        _logger?.LogWarning(
            "Failed to {Operation} during 2FA cleanup for user {UserId}: {Errors}",
            operation, userId, string.Join("; ", result.Errors.Select(e => e.Description)));

        return result.Errors.Any(e => e.Code == nameof(IdentityErrorDescriber.ConcurrencyFailure))
            ? TwoFactorCleanupStatus.Conflict
            : TwoFactorCleanupStatus.StorageFailure;
    }

    /// <summary>
    /// 恢复码输入规范化（仅用于恢复码页输入）：容忍空格、前后空白和小写。
    /// Identity 恢复码使用连字符分组，连字符必须保留。
    /// </summary>
    private static string NormalizeRecoveryCode(string? code) =>
        string.IsNullOrWhiteSpace(code)
            ? string.Empty
            : code.Replace(" ", string.Empty).Trim().ToUpperInvariant();

    private static string HashCode(string code) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(code)));
}
