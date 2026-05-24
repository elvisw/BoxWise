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

        group.MapPost("/login", LoginAsync)
            .AllowAnonymous()
            .WithTags("Auth")
            .WithDescription("用户登录");

        group.MapPost("/logout", LogoutAsync)
            .WithTags("Auth")
            .WithDescription("用户登出");

        group.MapGet("/me", GetCurrentUserAsync)
            .WithTags("Auth")
            .WithDescription("获取当前用户信息");

        return group;
    }

    private static async Task<Results<Ok<AuthUserDto>, UnauthorizedHttpResult, ValidationProblem>>
        LoginAsync(LoginRequest request, SignInManager<AppUser> signInManager, UserManager<AppUser> userManager)
    {
        var result = await signInManager.PasswordSignInAsync(
            request.Username, request.Password, isPersistent: true, lockoutOnFailure: false);

        if (!result.Succeeded)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                { "credentials", new[] { "用户名或密码错误" } }
            });
        }

        var user = await userManager.FindByNameAsync(request.Username);
        var isAdmin = user != null && await userManager.IsInRoleAsync(user, "Admin");

        return TypedResults.Ok(new AuthUserDto(request.Username, isAdmin));
    }

    private static async Task<Ok> LogoutAsync(SignInManager<AppUser> signInManager)
    {
        await signInManager.SignOutAsync();
        return TypedResults.Ok();
    }

    private static async Task<Ok<AuthUserDto>> GetCurrentUserAsync(
        UserManager<AppUser> userManager, HttpContext httpContext)
    {
        var user = await userManager.GetUserAsync(httpContext.User);
        if (user?.UserName is null)
        {
            // 不应到达（[Authorize] 保护），安全回退
            return TypedResults.Ok(new AuthUserDto(string.Empty, false));
        }

        var isAdmin = await userManager.IsInRoleAsync(user, "Admin");
        return TypedResults.Ok(new AuthUserDto(user.UserName, isAdmin));
    }
}
