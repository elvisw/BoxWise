using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using BoxWise.Server.Models;

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

    public string? ErrorMessage { get; set; }

    public string UserId { get; set; } = "";
    public string? CurrentUsername { get; set; }

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
        Username = Username.Trim();

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

        var oldName = user.UserName;
        var result = await _userManager.SetUserNameAsync(user, Username);

        if (!result.Succeeded)
        {
            ErrorMessage = string.Join("; ", result.Errors.Select(e => e.Description));
            return Page();
        }

        _logger.LogInformation("Admin renamed user '{OldName}' to '{NewName}'", oldName, Username);
        return RedirectToPage("/Admin/Index");
    }
}
