using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;

using BoxWise.Server.Models;
using BoxWise.Shared.Dtos;

namespace BoxWise.Server.Endpoints;

public static class AuthEndpoints
{
    public static RouteGroupBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth");

        group.MapGet("/me", GetCurrentUserAsync)
            .WithTags("Auth")
            .WithDescription("获取当前用户信息")
            .ProducesProblem(401);

        return group;
    }

    private static async Task<Ok<AuthUserDto>> GetCurrentUserAsync(
        UserManager<AppUser> userManager, HttpContext httpContext, IConfiguration config)
    {
        var user = await userManager.GetUserAsync(httpContext.User);
        if (user?.UserName is null)
        {
            // 不应到达（[Authorize] 保护），安全回退
            return TypedResults.Ok(new AuthUserDto(string.Empty, false));
        }

        var isAdmin = await userManager.IsInRoleAsync(user, "Admin");
        var adminConfigured = !string.IsNullOrWhiteSpace(config["Admin:Password"]);
        var isSpecificAdmin = adminConfigured
            && string.Equals(user.UserName, config["Admin:Username"] ?? "admin", StringComparison.OrdinalIgnoreCase);

        return TypedResults.Ok(new AuthUserDto(user.UserName, isAdmin, isSpecificAdmin, Email: user.Email));
    }
}
