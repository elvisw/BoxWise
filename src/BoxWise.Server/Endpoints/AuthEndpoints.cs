using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

using BoxWise.Server.Models;
using BoxWise.Server.Services;
using BoxWise.Server.Services.PasswordValidators;
using BoxWise.Server.Utilities;
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
            try
            {
                await userManager.UpdateAsync(user);
            }
            catch (DbUpdateConcurrencyException ex)
            {
                // 并发首次登录：另一请求已更新 ConcurrencyStamp，读取最新状态继续
                logger.LogWarning(ex, "Concurrency conflict on first login init for user {UserId}", user.Id);
                user = (await userManager.FindByIdAsync(user.Id)) ?? user;
            }
        }

        // 检查 2FA 状态
        var passwordWeak = request.Password.Length < 8
            || request.Password.All(char.IsDigit)
            || CommonPasswordValidator.IsCommon(request.Password);

        // 尽早计算管理员状态（2FA 路径和正常路径均需使用）
        var isAdmin = await userManager.IsInRoleAsync(user, "Admin");
        var adminConfigured = !string.IsNullOrWhiteSpace(config["Admin:Password"]);
        var isSpecificAdmin = adminConfigured
            && string.Equals(request.Username, config["Admin:Username"] ?? "admin", StringComparison.OrdinalIgnoreCase);

        if (user.TwoFactorEnabled)
        {
            // 已启用 2FA → 签发 TwoFactorUserId Cookie，进入阶段二
            await IssueTwoFactorUserIdCookieAsync(signInManager, user);
            return TypedResults.Ok(new LoginResponse(null, null, null, passwordWeak, RequiresTwoFactor: true, Email: user.Email,
                PasswordManagedByEnv: isAdmin && isSpecificAdmin));
        }

        // 检查强制 2FA 宽限期
        if (user.TwoFactorGracePeriodUntil.HasValue
            && user.TwoFactorGracePeriodUntil.Value <= DateTime.UtcNow)
        {
            // 宽限期已过且 2FA 未启用 → 允许登录但引导用户前往设置页完成 2FA 配置
            // 清理可能残留的 TOTP 密钥（例如之前开始设置但未完成的）
            var needsCleanup = !string.IsNullOrEmpty(user.TotpSecretKey);
            if (needsCleanup)
            {
                user.TotpSecretKey = null;
                try { await userManager.UpdateAsync(user); }
                catch (DbUpdateConcurrencyException ex)
                {
                    logger.LogWarning(ex, "并发冲突：宽限期过期后清理 TOTP 密钥时失败，将在下次登录时重试。UserId: {UserId}", user.Id);
                }
            }
            await signInManager.SignInAsync(user, isPersistent: true);
            return TypedResults.Ok(new LoginResponse(request.Username, isAdmin, isSpecificAdmin,
                passwordWeak, RequiresTwoFactor: false, RequiresTwoFactorSetup: true,
                Email: user.Email, PasswordManagedByEnv: isAdmin && isSpecificAdmin));
        }

        // 宽限期未设置或未到期 → 非强制 2FA，直接登录
        await signInManager.SignInAsync(user, isPersistent: true);

        return TypedResults.Ok(new LoginResponse(request.Username, isAdmin, isSpecificAdmin,
            passwordWeak, RequiresTwoFactor: false, Email: user.Email,
            PasswordManagedByEnv: isAdmin && isSpecificAdmin));
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

    private static async Task<Results<Ok<AuthUserDto>, ValidationProblem, ProblemHttpResult>>
        UpdateProfileAsync(UpdateProfileRequest request,
        UserManager<AppUser> userManager, HttpContext httpContext, IConfiguration config,
        ILoggerFactory loggerFactory)
    {
        var user = await userManager.GetUserAsync(httpContext.User);
        if (user?.UserName is null)
            return Unauthorized();

        // 处理用户名更新（NewUsername 为 null 时跳过）
        if (request.NewUsername is not null)
        {
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
        }

        // 处理邮箱更新
        if (request.NewEmail is not null)
        {
            var email = request.NewEmail.Trim();

            var emailError = EmailValidation.Validate(email);
            if (emailError is not null)
            {
                return TypedResults.ValidationProblem(new Dictionary<string, string[]>
                {
                    { "email", new[] { emailError } }
                });
            }

            if (string.IsNullOrEmpty(request.OperationToken))
            {
                return TypedResults.Problem("邮箱修改需要验证码确认", statusCode: 400);
            }

            // 验证 operation token
            var (tokenOk, verifiedEmail) = ValidateOperationToken(request.OperationToken, user.Id,
                protectionProvider: httpContext.RequestServices.GetRequiredService<IDataProtectionProvider>());
            if (!tokenOk)
            {
                return TypedResults.Problem("操作已过期，请重新验证", statusCode: 400);
            }

            if (!string.Equals(verifiedEmail, email, StringComparison.OrdinalIgnoreCase))
            {
                return TypedResults.Problem("邮箱不匹配", statusCode: 400);
            }

            var existingEmailUser = await userManager.FindByEmailAsync(email);
            if (existingEmailUser is not null && existingEmailUser.Id != user.Id)
            {
                return TypedResults.ValidationProblem(new Dictionary<string, string[]>
                {
                    { "email", new[] { "该邮箱已被其他账户使用" } }
                });
            }

            // 使用 SetEmailAsync 确保 NormalizedEmail 和 EmailConfirmed 正确更新
            // 再单独同步 EmailForTwoFactor
            var oldEmail = user.Email;
            try
            {
                var setEmailResult = await userManager.SetEmailAsync(user, email.ToLowerInvariant());
                if (!setEmailResult.Succeeded)
                {
                    return TypedResults.ValidationProblem(new Dictionary<string, string[]>
                    {
                        { "email", setEmailResult.Errors.Select(e => e.Description).ToArray() }
                    });
                }
                user.EmailForTwoFactor = email.ToLowerInvariant();

                var updateResult = await userManager.UpdateAsync(user);
                if (!updateResult.Succeeded)
                {
                    return TypedResults.ValidationProblem(new Dictionary<string, string[]>
                    {
                        { "email", updateResult.Errors.Select(e => e.Description).ToArray() }
                    });
                }
            }
            catch (DbUpdateException ex)
            {
                var logger = loggerFactory.CreateLogger("BoxWise.Auth");
                logger.LogWarning(ex, "Email uniqueness conflict for user {UserId} when setting email", user.Id);
                return TypedResults.ValidationProblem(new Dictionary<string, string[]>
                {
                    { "email", new[] { "此邮箱已被其他用户使用" } }
                });
            }

            // 旧邮箱通知（SendChangeNotificationAsync 内部有完整 try/catch，await 安全）
            if (!string.IsNullOrEmpty(oldEmail))
            {
                var emailService = httpContext.RequestServices.GetRequiredService<EmailTwoFactorService>();
                await emailService.SendChangeNotificationAsync(oldEmail, user.UserName);
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

    private static string GetClientIp(HttpContext httpContext)
        => httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    private static ValidationProblem Unauthorized()
        => TypedResults.ValidationProblem(new Dictionary<string, string[]>
        {
            { "auth", new[] { "未登录" } }
        });

    private static (bool Ok, string VerifiedEmail) ValidateOperationToken(
        string operationToken, string userId, IDataProtectionProvider protectionProvider)
    {
        try
        {
            var protector = protectionProvider.CreateProtector(EmailVerificationEndpoints.OperationTokenPurpose);
            var payload = protector.Unprotect(operationToken);
            var parts = payload.Split('|');
            if (parts.Length < 3)
                return (false, string.Empty);

            var tokenUserId = parts[0];
            var boundEmail = parts[1];
            var expiry = DateTime.Parse(parts[2], null, System.Globalization.DateTimeStyles.RoundtripKind);

            if (tokenUserId != userId)
                return (false, string.Empty);

            if (expiry <= DateTime.UtcNow)
                return (false, string.Empty);

            // 防止 operation token 重放攻击（同一 token 仅可消费一次）
            if (!EmailTwoFactorService.TryConsumeOperationToken(operationToken))
                return (false, string.Empty);

            return (true, boundEmail);
        }
        catch
        {
            return (false, string.Empty);
        }
    }
}
