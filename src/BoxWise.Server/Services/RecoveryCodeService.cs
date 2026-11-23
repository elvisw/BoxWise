using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using BoxWise.Server.Data;
using BoxWise.Server.Models;
using OtpNet;

namespace BoxWise.Server.Services;

public class RecoveryCodeService
{
    private readonly AppDbContext _db;

    public RecoveryCodeService(AppDbContext db)
    {
        _db = db;
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
        // 删除旧码
        var oldCodes = await _db.RecoveryCodes
            .Where(rc => rc.UserId == user.Id)
            .ToListAsync();
        _db.RecoveryCodes.RemoveRange(oldCodes);

        // 存入新码的哈希
        var recoveryCodes = codes.Select(c => new RecoveryCode
        {
            UserId = user.Id,
            CodeHash = HashCode(c)
        }).ToList();

        _db.RecoveryCodes.AddRange(recoveryCodes);
        await _db.SaveChangesAsync();
    }

    /// <summary>
    /// 验证自定义恢复码，成功返回 true 并关闭 2FA。
    /// 保持“先校验、后消耗”的顺序：只有哈希命中后才清除状态。
    /// </summary>
    public async Task<bool> VerifyRecoveryCodeAsync(AppUser user, string code, UserManager<AppUser> userManager)
    {
        var codeHash = HashCode(code);
        var match = await _db.RecoveryCodes
            .AnyAsync(rc => rc.UserId == user.Id && rc.CodeHash == codeHash);

        if (!match)
            return false;

        return await DisableTwoFactorAsync(user, userManager);
    }

    /// <summary>
    /// 关闭用户的双重验证：清除两套恢复码（自定义表与 Identity token store）与 2FA 字段，
    /// 并轮换 SecurityStamp。重新启用 2FA 时会重新生成恢复码。
    /// </summary>
    public async Task<bool> DisableTwoFactorAsync(AppUser user, UserManager<AppUser> userManager)
    {
        var allCodes = await _db.RecoveryCodes
            .Where(rc => rc.UserId == user.Id)
            .ToListAsync();
        if (allCodes.Count > 0)
            _db.RecoveryCodes.RemoveRange(allCodes);

        user.TwoFactorEnabled = false;
        user.ConfiguredMethods = TwoFactorMethod.None;
        user.TotpSecretKey = null;
        user.EmailForTwoFactor = null;
        user.TwoFactorSetupCompletedAt = null;
        user.TwoFactorGracePeriodUntil = DateTime.UtcNow.AddHours(24);

        try
        {
            var updateResult = await userManager.UpdateAsync(user);
            if (!updateResult.Succeeded)
                return false;

            await _db.SaveChangesAsync();

            // 关闭 2FA 后两套恢复码都应失效：自定义码已删除，
            // Identity token store 使用 0 个新码替换，等价于清空全部恢复码。
            var remainingIdentityCodes = await userManager.GenerateNewTwoFactorRecoveryCodesAsync(user, 0);
            if (remainingIdentityCodes is null
                || await userManager.CountRecoveryCodesAsync(user) != 0)
            {
                throw new InvalidOperationException(
                    "Failed to clear Identity recovery codes while disabling two-factor authentication.");
            }

            // 使已有登录会话失效：2FA 状态发生了安全相关变更。
            await userManager.UpdateSecurityStampAsync(user);
        }
        catch (DbUpdateConcurrencyException)
        {
            // 并发请求已消耗恢复码或更新了用户
            return false;
        }

        return true;
    }

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
    /// 用于 modify 流程中的身份验证（区别于登录时的核选项 VerifyRecoveryCodeAsync）。
    /// </summary>
    public async Task<bool> ValidateRecoveryCodeAsync(AppUser user, string code)
    {
        if (string.IsNullOrWhiteSpace(code))
            return false;
        var codeHash = HashCode(code);
        return await _db.RecoveryCodes.AnyAsync(rc => rc.UserId == user.Id && rc.CodeHash == codeHash);
    }

    private static string HashCode(string code) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(code)));
}
