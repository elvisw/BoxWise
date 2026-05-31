using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using BoxWise.Server.Models;
using BoxWise.Server.Utilities;
using BoxWise.Shared.Dtos;

namespace BoxWise.Server.Pages.Admin;

[Authorize(Policy = "AdminOnly")]
public class CreateAccountModel : PageModel
{
    private readonly UserManager<AppUser> _userManager;
    private readonly ILogger<CreateAccountModel> _logger;

    public CreateAccountModel(UserManager<AppUser> userManager, ILogger<CreateAccountModel> logger)
    {
        _userManager = userManager;
        _logger = logger;
    }

    [BindProperty]
    public CreateAccountRequest Input { get; set; } = new();

    public string? ErrorMessage { get; set; }

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync()
    {
        Input.Username = Input.Username.Trim();
        Input.Email = Input.Email.Trim();

        if (string.IsNullOrWhiteSpace(Input.Username) || string.IsNullOrWhiteSpace(Input.Password))
        {
            ErrorMessage = "用户名和密码不能为空";
            return Page();
        }

        if (Input.Username.Length > 50)
        {
            ErrorMessage = "用户名不能超过 50 个字符";
            return Page();
        }

        if (Input.Password.Length < 4)
        {
            ErrorMessage = "密码长度至少为 4 个字符";
            return Page();
        }

        var emailError = EmailValidation.Validate(Input.Email);
        if (emailError is not null)
        {
            ErrorMessage = emailError;
            return Page();
        }

        var existingUser = await _userManager.FindByNameAsync(Input.Username);
        if (existingUser is not null)
        {
            ErrorMessage = $"用户名 '{Input.Username}' 已存在";
            return Page();
        }

        var existingEmail = await _userManager.FindByEmailAsync(Input.Email);
        if (existingEmail is not null)
        {
            ErrorMessage = $"邮箱 '{Input.Email}' 已被其他账户使用";
            return Page();
        }

        var user = new AppUser
        {
            UserName = Input.Username,
            Email = Input.Email,
            EmailForTwoFactor = Input.Email
        };
        var result = await _userManager.CreateAsync(user, Input.Password);

        if (!result.Succeeded)
        {
            ErrorMessage = string.Join("; ", result.Errors.Select(e => e.Description));
            return Page();
        }

        _logger.LogInformation("Admin user '{AdminName}' created account '{Username}' with email '{Email}'",
            User.Identity?.Name, Input.Username, Input.Email);

        return RedirectToPage("/Admin/Index");
    }
}
