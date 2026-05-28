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
            .WithDescription("用户登录")
            .ProducesProblem(401);

        group.MapPost("/logout", LogoutAsync)
            .WithTags("Auth")
            .WithDescription("用户登出")
            .ProducesProblem(401);

        group.MapGet("/me", GetCurrentUserAsync)
            .WithTags("Auth")
            .WithDescription("获取当前用户信息")
            .ProducesProblem(401);

        group.MapPut("/me", UpdateProfileAsync)
            .WithTags("Auth")
            .WithDescription("修改当前用户信息")
            .ProducesProblem(401);

        group.MapPut("/me/password", ChangePasswordAsync)
            .WithTags("Auth")
            .WithDescription("修改当前用户密码")
            .ProducesProblem(401);

        return group;
    }

    private static async Task<Results<Ok<AuthUserDto>, UnauthorizedHttpResult, ValidationProblem>>
        LoginAsync(LoginRequest request, SignInManager<AppUser> signInManager, UserManager<AppUser> userManager,
        IConfiguration config)
    {
        var user = await userManager.FindByNameAsync(request.Username);
        if (user is null)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                { "credentials", new[] { "用户名或密码错误" } }
            });
        }

        var result = await signInManager.PasswordSignInAsync(
            user, request.Password, isPersistent: true, lockoutOnFailure: false);

        if (!result.Succeeded)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                { "credentials", new[] { "用户名或密码错误" } }
            });
        }

        var isAdmin = await userManager.IsInRoleAsync(user, "Admin");
        var adminConfigured = !string.IsNullOrWhiteSpace(config["Admin:Password"]);
        var isSpecificAdmin = adminConfigured
            && string.Equals(request.Username, config["Admin:Username"] ?? "admin", StringComparison.OrdinalIgnoreCase);

        return TypedResults.Ok(new AuthUserDto(request.Username, isAdmin, isSpecificAdmin));
    }

    private static async Task<Ok> LogoutAsync(SignInManager<AppUser> signInManager)
    {
        await signInManager.SignOutAsync();
        return TypedResults.Ok();
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

        return TypedResults.Ok(new AuthUserDto(user.UserName, isAdmin, isSpecificAdmin));
    }

    private static async Task<Results<Ok<AuthUserDto>, ValidationProblem>>
        UpdateProfileAsync(UpdateProfileRequest request,
        UserManager<AppUser> userManager, HttpContext httpContext, IConfiguration config)
    {
        var user = await userManager.GetUserAsync(httpContext.User);
        if (user?.UserName is null)
            return Unauthorized();

        var newUsername = request.NewUsername.Trim();

        if (string.IsNullOrWhiteSpace(newUsername))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                { "username", new[] { "用户名不能为空" } }
            });
        }

        if (newUsername.Length > 50)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                { "username", new[] { "用户名不能超过 50 个字符" } }
            });
        }

        var existingUser = await userManager.FindByNameAsync(newUsername);
        if (existingUser is not null && existingUser.Id != user.Id)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                { "username", new[] { $"用户名 '{newUsername}' 已被占用" } }
            });
        }

        var result = await userManager.SetUserNameAsync(user, newUsername);

        if (!result.Succeeded)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                { "username", result.Errors.Select(e => e.Description).ToArray() }
            });
        }

        var stillAdmin = await userManager.IsInRoleAsync(user, "Admin");
        var adminConfigured = !string.IsNullOrWhiteSpace(config["Admin:Password"]);
        var isSpecificAdmin = adminConfigured
            && string.Equals(user.UserName, config["Admin:Username"] ?? "admin", StringComparison.OrdinalIgnoreCase);

        return TypedResults.Ok(new AuthUserDto(user.UserName, stillAdmin, isSpecificAdmin));
    }

    private static async Task<Results<Ok, ValidationProblem>>
        ChangePasswordAsync(ChangePasswordRequest request,
        UserManager<AppUser> userManager, HttpContext httpContext)
    {
        var user = await userManager.GetUserAsync(httpContext.User);
        if (user?.UserName is null)
            return Unauthorized();

        if (string.IsNullOrWhiteSpace(request.CurrentPassword))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                { "currentPassword", new[] { "当前密码不能为空" } }
            });
        }

        if (string.IsNullOrWhiteSpace(request.NewPassword) || request.NewPassword.Length < 4)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                { "newPassword", new[] { "新密码长度至少为 4 个字符" } }
            });
        }

        var result = await userManager.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword);

        if (!result.Succeeded)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                { "currentPassword", result.Errors.Select(e => e.Description).ToArray() }
            });
        }

        return TypedResults.Ok();
    }

    private static ValidationProblem Unauthorized()
        => TypedResults.ValidationProblem(new Dictionary<string, string[]>
        {
            { "auth", new[] { "未登录" } }
        });
}
