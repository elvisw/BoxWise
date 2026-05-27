using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using BoxWise.Server.Models;

namespace BoxWise.Server.Pages.Admin;

[Authorize(Policy = "AdminOnly")]
public class ChangeUserPasswordModel : PageModel
{
    private readonly UserManager<AppUser> _userManager;
    private readonly ILogger<ChangeUserPasswordModel> _logger;

    public ChangeUserPasswordModel(UserManager<AppUser> userManager, ILogger<ChangeUserPasswordModel> logger)
    {
        _userManager = userManager;
        _logger = logger;
    }

    [BindProperty]
    public string NewPassword { get; set; } = "";

    public string? ErrorMessage { get; set; }

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

        var user = await _userManager.FindByIdAsync(id);
        if (user is null)
            return NotFound();

        TargetUsername = user.UserName ?? "";

        if (string.IsNullOrWhiteSpace(NewPassword))
        {
            ErrorMessage = "密码不能为空";
            return Page();
        }

        if (NewPassword.Length < 4)
        {
            ErrorMessage = "密码长度至少为 4 个字符";
            return Page();
        }

        var token = await _userManager.GeneratePasswordResetTokenAsync(user);
        var result = await _userManager.ResetPasswordAsync(user, token, NewPassword);

        if (!result.Succeeded)
        {
            ErrorMessage = string.Join("; ", result.Errors.Select(e => e.Description));
            return Page();
        }

        await _userManager.UpdateSecurityStampAsync(user);
        _logger.LogInformation("Admin '{AdminName}' reset password for user '{UserName}'", User.Identity?.Name, user.UserName);
        TempData["StatusMessage"] = $"已成功修改 '{user.UserName}' 的密码";
        return RedirectToPage("/Admin/Index");
    }
}
