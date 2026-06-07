using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using BoxWise.Server.Models;
using BoxWise.Shared.Dtos;

namespace BoxWise.Server.Pages.Admin;

[Authorize(Policy = "AdminOnly")]
public class IndexModel : PageModel
{
    private readonly UserManager<AppUser> _userManager;
    private readonly ILogger<IndexModel> _logger;
    private readonly IConfiguration _config;
    private const string UnknownMethod = "未知";

    public IndexModel(UserManager<AppUser> userManager, ILogger<IndexModel> logger, IConfiguration config)
    {
        _userManager = userManager;
        _logger = logger;
        _config = config;
    }

    public List<UserListItemDto> Users { get; set; } = null!;
    public bool IsAdminPasswordManagedByEnv =>
        !string.IsNullOrWhiteSpace(_config["Admin:Password"]);

    [TempData]
    public string? StatusMessage { get; set; }

    public async Task OnGetAsync()
    {
        await LoadUsersAsync();
    }

    public async Task<IActionResult> OnPostDeleteAsync(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return NotFound();

        var currentUserId = _userManager.GetUserId(User);
        if (id == currentUserId)
        {
            StatusMessage = "不能删除当前登录的管理员账户";
            return RedirectToPage();
        }

        var user = await _userManager.FindByIdAsync(id);
        if (user is null)
            return NotFound();

        try
        {
            var result = await _userManager.DeleteAsync(user);
            if (!result.Succeeded)
            {
                StatusMessage = string.Join("; ", result.Errors.Select(e => e.Description));
                return RedirectToPage();
            }
        }
        catch (DbUpdateException)
        {
            StatusMessage = $"无法删除账户 '{user.UserName}'：该账户有关联数据，请先清理后重试";
            return RedirectToPage();
        }

        _logger.LogInformation("Admin '{AdminName}' deleted user '{UserName}'", User.Identity?.Name, user.UserName);
        StatusMessage = $"账户 '{user.UserName}' 已删除";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostToggleRoleAsync(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return NotFound();

        var currentUserId = _userManager.GetUserId(User);
        if (id == currentUserId)
        {
            StatusMessage = "不能修改当前登录管理员自己的角色";
            return RedirectToPage();
        }

        var user = await _userManager.FindByIdAsync(id);
        if (user is null)
            return NotFound();

        var isAdmin = await _userManager.IsInRoleAsync(user, "Admin");
        IdentityResult roleResult;
        if (isAdmin)
        {
            roleResult = await _userManager.RemoveFromRoleAsync(user, "Admin");
            StatusMessage = roleResult.Succeeded
                ? $"已取消 '{user.UserName}' 的管理员角色"
                : $"操作失败：{string.Join("; ", roleResult.Errors.Select(e => e.Description))}";
        }
        else
        {
            roleResult = await _userManager.AddToRoleAsync(user, "Admin");
            StatusMessage = roleResult.Succeeded
                ? $"已将 '{user.UserName}' 设为管理员"
                : $"操作失败：{string.Join("; ", roleResult.Errors.Select(e => e.Description))}";
        }

        return RedirectToPage();
    }

    private async Task LoadUsersAsync()
    {
        var allUsers = await _userManager.Users.ToListAsync();
        var adminUsers = await _userManager.GetUsersInRoleAsync("Admin");
        var adminUserNames = new HashSet<string>(adminUsers.Select(u => u.UserName ?? ""));

        Users = allUsers.Select(u => {
            // 精确值匹配 — 与 TwoFactorMethod [Flags] 枚举值一一对应。
            // None=0, TOTP=1, Email=2, WebAuthn=4.
            // 7=TOTP|Email|WebAuthn, 6=Email|WebAuthn, 5=TOTP|WebAuthn, 4=WebAuthn, 3=TOTP|Email.
            // 如枚举值变更，此处需同步更新。
            var methodDisplay = u.ConfiguredMethods switch
            {
                (TwoFactorMethod)7 => "TOTP + Email + WebAuthn",
                (TwoFactorMethod)6 => "Email + WebAuthn",
                (TwoFactorMethod)5 => "TOTP + WebAuthn",
                (TwoFactorMethod)4 => "WebAuthn",
                (TwoFactorMethod)3 => "TOTP + Email",
                TwoFactorMethod.Email => "Email",
                TwoFactorMethod.TOTP => "TOTP",
                TwoFactorMethod.None => null,
                _ => u.ConfiguredMethods.ToString()
            };
            return new UserListItemDto(
                u.Id,
                u.UserName ?? "",
                adminUserNames.Contains(u.UserName ?? ""),
                u.TwoFactorEnabled,
                methodDisplay ?? (u.TwoFactorEnabled ? UnknownMethod : null)
            );
        }).ToList();
    }
}
