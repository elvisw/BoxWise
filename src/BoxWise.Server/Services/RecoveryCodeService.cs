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
    /// 验证恢复码，成功返回 true + 删除所有恢复码 + 清除所有 2FA 设置。
    /// </summary>
    public async Task<bool> VerifyRecoveryCodeAsync(AppUser user, string code, UserManager<AppUser> userManager)
    {
        var codeHash = HashCode(code);
        var match = await _db.RecoveryCodes
            .AnyAsync(rc => rc.UserId == user.Id && rc.CodeHash == codeHash);

        if (!match)
            return false;

        // 清除所有恢复码
        var allCodes = await _db.RecoveryCodes
            .Where(rc => rc.UserId == user.Id)
            .ToListAsync();
        _db.RecoveryCodes.RemoveRange(allCodes);

        // 清除所有 2FA 设置
        user.TwoFactorEnabled = false;
        user.TwoFactorMethod = TwoFactorMethod.None;
        user.TotpSecretKey = null;
        user.EmailForTwoFactor = null;
        user.TwoFactorSetupCompletedAt = null;
        user.TwoFactorGracePeriodUntil = null;

        await userManager.UpdateAsync(user);
        await _db.SaveChangesAsync();

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
    /// 检查用户是否存在恢复码。
    /// </summary>
    public async Task<bool> HasRecoveryCodesAsync(AppUser user)
    {
        return await _db.RecoveryCodes.AnyAsync(rc => rc.UserId == user.Id);
    }

    private static string HashCode(string code) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(code)));
}
