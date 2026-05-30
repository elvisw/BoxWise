using System.Net.Mail;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

using BoxWise.Server.Models;
using BoxWise.Server.Services.PasswordValidators;
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
            .ProducesProblem(401)
            .RequireRateLimiting("login-per-account");

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

    private static async Task<Results<Ok<LoginResponse>, UnauthorizedHttpResult, ValidationProblem>>
        LoginAsync(LoginRequest request, SignInManager<AppUser> signInManager, UserManager<AppUser> userManager,
        IConfiguration config, ILoggerFactory loggerFactory, HttpContext httpContext)
    {
        var logger = loggerFactory.CreateLogger("BoxWise.Auth");
        var user = await userManager.FindByNameAsync(request.Username);
        if (user is null)
        {
            logger.LogWarning("Failed login attempt from {IpAddress} for non-existent user {Username}",
                GetClientIp(httpContext), request.Username);
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                { "credentials", new[] { "用户名或密码错误" } }
            });
        }

        var result = await signInManager.CheckPasswordSignInAsync(
            user, request.Password, lockoutOnFailure: false);

        if (!result.Succeeded)
        {
            logger.LogWarning("Failed login attempt from {IpAddress} for user {Username}",
                GetClientIp(httpContext), request.Username);
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                { "credentials", new[] { "用户名或密码错误" } }
            });
        }

        // 首次登录初始化 2FA 宽限期（24 小时），同时清理残留的 TOTP 密钥
        if (!user.TwoFactorEnabled && !user.TwoFactorGracePeriodUntil.HasValue)
        {
            user.TwoFactorGracePeriodUntil = DateTime.UtcNow.AddHours(24);
            // 清理可能残留的 TOTP 密钥（例如之前开始设置但未完成的）
            if (!string.IsNullOrEmpty(user.TotpSecretKey))
                user.TotpSecretKey = null;
            await userManager.UpdateAsync(user);
        }

        // 检查 2FA 状态
        var passwordWeak = request.Password.Length < 8
            || request.Password.All(char.IsDigit)
            || CommonPasswordValidator.IsCommon(request.Password);

        if (user.TwoFactorEnabled)
        {
            // 已启用 2FA → 签发 TwoFactorUserId Cookie，进入阶段二
            await IssueTwoFactorUserIdCookieAsync(signInManager, user);
            return TypedResults.Ok(new LoginResponse(null, null, null, passwordWeak, RequiresTwoFactor: true, Email: user.Email));
        }

        var isAdmin = await userManager.IsInRoleAsync(user, "Admin");
        var adminConfigured = !string.IsNullOrWhiteSpace(config["Admin:Password"]);
        var isSpecificAdmin = adminConfigured
            && string.Equals(request.Username, config["Admin:Username"] ?? "admin", StringComparison.OrdinalIgnoreCase);

        // 检查强制 2FA 宽限期
        if (user.TwoFactorGracePeriodUntil.HasValue
            && user.TwoFactorGracePeriodUntil.Value <= DateTime.UtcNow)
        {
            // 宽限期已过且 2FA 未启用 → 允许登录但引导用户前往设置页完成 2FA 配置
            // 清理可能残留的 TOTP 密钥（例如之前开始设置但未完成的）
            if (!string.IsNullOrEmpty(user.TotpSecretKey))
                user.TotpSecretKey = null;
            await userManager.UpdateAsync(user);
            await signInManager.SignInAsync(user, isPersistent: true);
            return TypedResults.Ok(new LoginResponse(request.Username, isAdmin, isSpecificAdmin,
                passwordWeak, RequiresTwoFactor: false, RequiresTwoFactorSetup: true,
                Email: user.Email));
        }

        // 宽限期未设置或未到期 → 非强制 2FA，直接登录
        await signInManager.SignInAsync(user, isPersistent: true);

        return TypedResults.Ok(new LoginResponse(request.Username, isAdmin, isSpecificAdmin,
            passwordWeak, RequiresTwoFactor: false, Email: user.Email));
    }

    /// <summary>
    /// 签发 TwoFactorUserId Cookie（标记密码已验证，等待 2FA）。
    /// </summary>
    private static async Task IssueTwoFactorUserIdCookieAsync(SignInManager<AppUser> signInManager, AppUser user)
    {
        var twoFactorIdentity = new ClaimsIdentity(IdentityConstants.TwoFactorUserIdScheme);
        twoFactorIdentity.AddClaim(new Claim(ClaimTypes.NameIdentifier, user.Id));
        twoFactorIdentity.AddClaim(new Claim(ClaimTypes.Name, user.UserName ?? ""));
        twoFactorIdentity.AddClaim(new Claim("SessionToken", Guid.NewGuid().ToString()));

        await signInManager.Context.SignInAsync(IdentityConstants.TwoFactorUserIdScheme,
            new ClaimsPrincipal(twoFactorIdentity),
            new AuthenticationProperties { IsPersistent = false });
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

        return TypedResults.Ok(new AuthUserDto(user.UserName, isAdmin, isSpecificAdmin, Email: user.Email));
    }

    private static async Task<Results<Ok<AuthUserDto>, ValidationProblem>>
        UpdateProfileAsync(UpdateProfileRequest request,
        UserManager<AppUser> userManager, HttpContext httpContext, IConfiguration config,
        ILoggerFactory loggerFactory)
    {
        var user = await userManager.GetUserAsync(httpContext.User);
        if (user?.UserName is null)
            return Unauthorized();

        var newUsername = request.NewUsername?.Trim() ?? "";

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

        // 处理邮箱更新
        if (request.NewEmail is not null)
        {
            var email = request.NewEmail.Trim();
            if (string.IsNullOrWhiteSpace(email))
            {
                var clearResult = await userManager.SetEmailAsync(user, null);
                if (!clearResult.Succeeded)
                {
                    return TypedResults.ValidationProblem(new Dictionary<string, string[]>
                    {
                        { "email", clearResult.Errors.Select(e => e.Description).ToArray() }
                    });
                }
            }
            else
            {
                if (email.Length > 256 || !IsValidEmail(email))
                {
                    return TypedResults.ValidationProblem(new Dictionary<string, string[]>
                    {
                        { "email", new[] { "请输入有效的邮箱地址" } }
                    });
                }

                var existingEmailUser = await userManager.FindByEmailAsync(email);
                if (existingEmailUser is not null && existingEmailUser.Id != user.Id)
                {
                    return TypedResults.ValidationProblem(new Dictionary<string, string[]>
                    {
                        { "email", new[] { "此邮箱已被其他用户使用" } }
                    });
                }

                try
                {
                    var emailResult = await userManager.SetEmailAsync(user, email);
                    if (!emailResult.Succeeded)
                    {
                        return TypedResults.ValidationProblem(new Dictionary<string, string[]>
                        {
                            { "email", emailResult.Errors.Select(e => e.Description).ToArray() }
                        });
                    }
                }
                catch (DbUpdateException ex)
                {
                    // TOCTOU 并发防护：FindByEmailAsync 和 SetEmailAsync 之间的窗口期
                    // 内另有请求设置了相同邮箱。NormalizedEmail 唯一索引确保数据完整性。
                    var logger = loggerFactory.CreateLogger("BoxWise.Auth");
                    logger.LogWarning(ex, "Email uniqueness conflict for user {UserId} when setting email", user.Id);
                    return TypedResults.ValidationProblem(new Dictionary<string, string[]>
                    {
                        { "email", new[] { "此邮箱已被其他用户使用" } }
                    });
                }

                // 邮箱变更后需清除已验证标记，并要求重新确认
                user.EmailConfirmed = false;
                try
                {
                    await userManager.UpdateAsync(user);
                }
                catch (DbUpdateException ex)
                {
                    // EmailConfirmed 同步失败不阻断主流程
                    // ——核心邮箱更改已通过 SetEmailAsync 持久化
                    var logger = loggerFactory.CreateLogger("BoxWise.Auth");
                    logger.LogWarning(ex, "Failed to update EmailConfirmed for user {UserId}", user.Id);
                }
            }
        }

        var stillAdmin = await userManager.IsInRoleAsync(user, "Admin");
        var adminConfigured = !string.IsNullOrWhiteSpace(config["Admin:Password"]);
        var isSpecificAdmin = adminConfigured
            && string.Equals(user.UserName, config["Admin:Username"] ?? "admin", StringComparison.OrdinalIgnoreCase);

        return TypedResults.Ok(new AuthUserDto(user.UserName, stillAdmin, isSpecificAdmin, Email: user.Email));
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

        if (string.IsNullOrWhiteSpace(request.NewPassword) || request.NewPassword.Length < 8 || request.NewPassword.Length > 128)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                { "newPassword", new[] { "新密码长度须在 8 到 128 个字符之间" } }
            });
        }

        var result = await userManager.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword);

        if (!result.Succeeded)
        {
            var passwordErrors = result.Errors.ToList();
            var currentPwdErrors = passwordErrors.Where(e => e.Code == "PasswordMismatch").ToArray();
            var newPwdErrors = passwordErrors.Where(e => e.Code != "PasswordMismatch").ToArray();
            var errors = new Dictionary<string, string[]>();
            if (currentPwdErrors.Length > 0)
                errors["currentPassword"] = currentPwdErrors.Select(e => e.Description).ToArray();
            if (newPwdErrors.Length > 0)
                errors["newPassword"] = newPwdErrors.Select(e => e.Description).ToArray();
            return TypedResults.ValidationProblem(errors);
        }

        await userManager.UpdateSecurityStampAsync(user);

        return TypedResults.Ok();
    }

    private static bool IsValidEmail(string email)
    {
        if (email is null) return false;
        try
        {
            var addr = new MailAddress(email);
            return addr.Address == email;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static string GetClientIp(HttpContext httpContext)
        => httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    private static ValidationProblem Unauthorized()
        => TypedResults.ValidationProblem(new Dictionary<string, string[]>
        {
            { "auth", new[] { "未登录" } }
        });
}
