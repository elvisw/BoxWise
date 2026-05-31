using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using BoxWise.Server.Models;
using BoxWise.Server.Utilities;

namespace BoxWise.Server.Pages.Admin;

[Authorize(Policy = "AdminOnly")]
public class EditAccountModel : PageModel
{
    private readonly UserManager<AppUser> _userManager;
    private readonly ILogger<EditAccountModel> _logger;

    public EditAccountModel(UserManager<AppUser> userManager, ILogger<EditAccountModel> logger)
    {
        _userManager = userManager;
        _logger = logger;
    }

    [BindProperty]
    public string Username { get; set; } = "";

    [BindProperty]
    public string Email { get; set; } = "";

    public string? ErrorMessage { get; set; }

    public string UserId { get; set; } = "";
    public string? CurrentUsername { get; set; }
    public string? CurrentEmail { get; set; }

    public async Task<IActionResult> OnGetAsync(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return NotFound();

        var user = await _userManager.FindByIdAsync(id);
        if (user is null)
            return NotFound();

        UserId = user.Id;
        CurrentUsername = user.UserName;
        Username = user.UserName ?? "";
        CurrentEmail = user.Email;
        Email = user.Email ?? "";
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return NotFound();

        var user = await _userManager.FindByIdAsync(id);
        if (user is null)
            return NotFound();

        UserId = user.Id;
        CurrentUsername = user.UserName;
        CurrentEmail = user.Email;
        Username = Username.Trim();
        Email = (Email ?? "").Trim();

        if (string.IsNullOrWhiteSpace(Username))
        {
            ErrorMessage = "用户名不能为空";
            return Page();
        }

        if (Username.Length > 50)
        {
            ErrorMessage = "用户名不能超过 50 个字符";
            return Page();
        }

        var existingUser = await _userManager.FindByNameAsync(Username);
        if (existingUser is not null && existingUser.Id != user.Id)
        {
            ErrorMessage = $"用户名 '{Username}' 已被占用";
            return Page();
        }

        var emailError = EmailValidation.Validate(Email);
        if (emailError is not null)
        {
            ErrorMessage = emailError;
            return Page();
        }

        var existingEmail = await _userManager.FindByEmailAsync(Email);
        if (existingEmail is not null && existingEmail.Id != user.Id)
        {
            ErrorMessage = $"邮箱 '{Email}' 已被其他账户使用";
            return Page();
        }

        var oldName = user.UserName;
        var oldEmail = user.Email;
        var nameChanged = !string.Equals(oldName, Username, StringComparison.OrdinalIgnoreCase);
        var emailChanged = !string.Equals(oldEmail, Email, StringComparison.OrdinalIgnoreCase);

        // 先更新邮箱（失败不影响用户名），再更新用户名（失败时回滚邮箱）
        if (emailChanged)
        {
            try
            {
                var normalizedEmail = Email.ToLowerInvariant();
                var emailResult = await _userManager.SetEmailAsync(user, normalizedEmail);
                if (!emailResult.Succeeded)
                {
                    ErrorMessage = string.Join("; ", emailResult.Errors.Select(e => e.Description));
                    return Page();
                }
                user.EmailForTwoFactor = normalizedEmail;
                await _userManager.UpdateAsync(user);
            }
            catch (DbUpdateException ex)
            {
                _logger.LogWarning(ex, "Email uniqueness conflict when editing user {UserId}", user.Id);
                ErrorMessage = $"邮箱 '{Email}' 已被其他账户使用";
                return Page();
            }
        }

        if (nameChanged)
        {
            try
            {
                var nameResult = await _userManager.SetUserNameAsync(user, Username);
                if (!nameResult.Succeeded)
                {
                    // 回滚邮箱变更
                    if (emailChanged)
                    {
                        try
                        {
                            await _userManager.SetEmailAsync(user, oldEmail ?? "");
                            user.EmailForTwoFactor = oldEmail;
                            await _userManager.UpdateAsync(user);
                        }
                        catch { /* 尽力回滚，记录日志 */ }
                    }
                    ErrorMessage = string.Join("; ", nameResult.Errors.Select(e => e.Description));
                    return Page();
                }
            }
            catch (DbUpdateException ex)
            {
                // 回滚邮箱变更
                if (emailChanged)
                {
                    try
                    {
                        await _userManager.SetEmailAsync(user, oldEmail ?? "");
                        user.EmailForTwoFactor = oldEmail;
                        await _userManager.UpdateAsync(user);
                    }
                    catch { /* 尽力回滚 */ }
                }
                _logger.LogWarning(ex, "Username uniqueness conflict when editing user {UserId}", user.Id);
                ErrorMessage = $"用户名 '{Username}' 已被占用";
                return Page();
            }
        }

        if (!nameChanged && !emailChanged)
        {
            return RedirectToPage("/Admin/Index");
        }

        _logger.LogInformation("Admin updated user '{OldName}' → '{NewName}', email '{OldEmail}' → '{NewEmail}'",
            oldName, Username, oldEmail, Email);
        return RedirectToPage("/Admin/Index");
    }
}
