using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using BoxWise.Server.Data;
using BoxWise.Server.Models;

namespace BoxWise.Server.Pages.Admin;

[Authorize(Policy = "AdminOnly")]
public class ResetTwoFactorModel : PageModel
{
    private readonly UserManager<AppUser> _userManager;
    private readonly AppDbContext _db;
    private readonly ILogger<ResetTwoFactorModel> _logger;

    public ResetTwoFactorModel(
        UserManager<AppUser> userManager,
        AppDbContext db,
        ILogger<ResetTwoFactorModel> logger)
    {
        _userManager = userManager;
        _db = db;
        _logger = logger;
    }

    public string TargetUsername { get; set; } = "";
    public string? ErrorMessage { get; set; }

    public async Task<IActionResult> OnGetAsync(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return NotFound();

        var user = await _userManager.FindByIdAsync(id);
        if (user is null)
            return NotFound();

        TargetUsername = user.UserName ?? "";
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return NotFound();

        var targetUser = await _userManager.FindByIdAsync(id);
        if (targetUser is null)
            return NotFound();

        TargetUsername = targetUser.UserName ?? "";

        // 清除 2FA 设置
        targetUser.TotpSecretKey = null;
        targetUser.ConfiguredMethods = TwoFactorMethod.None;
        targetUser.TwoFactorEnabled = false;
        targetUser.TwoFactorSetupCompletedAt = null;
        targetUser.TwoFactorGracePeriodUntil = null;
        targetUser.EmailForTwoFactor = null;

        // 删除所有恢复码
        var recoveryCodes = await _db.RecoveryCodes
            .Where(rc => rc.UserId == targetUser.Id)
            .ToListAsync();
        _db.RecoveryCodes.RemoveRange(recoveryCodes);

        // 删除所有 WebAuthn 凭证
        var credentials = await _db.WebAuthnCredentials
            .Where(wc => wc.UserId == targetUser.Id)
            .ToListAsync();
        _db.WebAuthnCredentials.RemoveRange(credentials);

        await _userManager.UpdateAsync(targetUser);
        await _userManager.UpdateSecurityStampAsync(targetUser);

        _logger.LogWarning(
            "Admin {Admin} (Id={AdminId}) reset 2FA for user {User} (Id={UserId}) at {Timestamp}",
            User.Identity?.Name, User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value,
            targetUser.UserName, targetUser.Id, DateTime.UtcNow);

        TempData["StatusMessage"] = $"已重置 '{targetUser.UserName}' 的双因素认证";
        return RedirectToPage("/Admin/Index");
    }
}
