using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using BoxWise.Server.Models;
using BoxWise.Server.Services;

namespace BoxWise.Server.Pages.Admin;

[Authorize(Policy = "AdminOnly")]
public class ResetTwoFactorModel : PageModel
{
    private readonly UserManager<AppUser> _userManager;
    private readonly RecoveryCodeService _recoveryCodeService;
    private readonly ILogger<ResetTwoFactorModel> _logger;

    public ResetTwoFactorModel(
        UserManager<AppUser> userManager,
        RecoveryCodeService recoveryCodeService,
        ILogger<ResetTwoFactorModel> logger)
    {
        _userManager = userManager;
        _recoveryCodeService = recoveryCodeService;
        _logger = logger;
    }

    [TempData]
    public string? StatusMessage { get; set; }

    public string TargetUsername { get; set; } = "";

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

        var currentUserId = _userManager.GetUserId(User);
        if (id == currentUserId)
        {
            StatusMessage = "不能重置自己的双因素认证。请让其他管理员操作。";
            return RedirectToPage("/Admin/Index");
        }

        var targetUser = await _userManager.FindByIdAsync(id);
        if (targetUser is null)
            return NotFound();

        TargetUsername = targetUser.UserName ?? "";

        var result = await _recoveryCodeService.ResetForAdminAsync(targetUser, _userManager);
        if (!result.Succeeded)
        {
            _logger.LogWarning(
                "Failed to reset 2FA for user {UserId}: {Status} (outcome confirmed: {Confirmed})",
                id, result.Status, result.OutcomeConfirmed);
            StatusMessage = result.OutcomeConfirmed
                ? "2FA 重置失败，未做任何更改，请稍后重试"
                : "2FA 重置结果未确认，请刷新后核对用户状态";
            return RedirectToPage("/Admin/Index");
        }

        _logger.LogWarning(
            "Admin {Admin} (Id={AdminId}) reset 2FA for user {User} (Id={UserId}) at {Timestamp}",
            User.Identity?.Name, User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value,
            targetUser.UserName, targetUser.Id, DateTime.UtcNow);

        StatusMessage = $"已重置 '{targetUser.UserName}' 的双因素认证";
        return RedirectToPage("/Admin/Index");
    }
}
