using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using BoxWise.Server.Models;
using BoxWise.Shared.Dtos;

namespace BoxWise.Server.Pages.Admin;

[Authorize(Policy = "AdminOnly")]
public class IndexModel : PageModel
{
    private readonly UserManager<AppUser> _userManager;

    public IndexModel(UserManager<AppUser> userManager)
    {
        _userManager = userManager;
    }

    public List<UserListItemDto> Users { get; set; } = null!;

    public async Task OnGetAsync()
    {
        var allUsers = await _userManager.Users.ToListAsync();
        var adminUsers = await _userManager.GetUsersInRoleAsync("Admin");
        var adminUserNames = new HashSet<string>(adminUsers.Select(u => u.UserName ?? ""));

        Users = allUsers.Select(u => new UserListItemDto(
            u.UserName ?? "",
            adminUserNames.Contains(u.UserName ?? "")
        )).ToList();
    }
}
