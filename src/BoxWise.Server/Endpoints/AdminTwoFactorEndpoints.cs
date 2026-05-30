using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using BoxWise.Server.Data;
using BoxWise.Server.Models;
using BoxWise.Server.Services;
using BoxWise.Shared.Dtos;

namespace BoxWise.Server.Endpoints;

public static class AdminTwoFactorEndpoints
{
    public static RouteGroupBuilder MapAdminTwoFactorEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin/users/{userId}/two-factor");

        group.MapGet("/status", GetTwoFactorStatusAsync)
            .WithTags("Admin/2FA")
            .ProducesProblem(401)
            .ProducesProblem(403);

        group.MapPost("/reset", ResetTwoFactorAsync)
            .WithTags("Admin/2FA")
            .ProducesProblem(401)
            .ProducesProblem(403)
            .AddEndpointFilter<CsrfValidationFilter>()
            .RequireRateLimiting("login-per-account");

        return group;
    }

    /// <summary>
    /// 获取目标用户的 2FA 状态详情（管理员操作）。
    /// </summary>
    private static async Task<Results<Ok<AdminTwoFactorStatusResponse>, UnauthorizedHttpResult, ForbidHttpResult, NotFound>>
        GetTwoFactorStatusAsync(string userId, HttpContext httpContext,
            UserManager<AppUser> userManager, TwoFactorService twoFactorService)
    {
        var caller = await userManager.GetUserAsync(httpContext.User);
        if (caller is null)
            return TypedResults.Unauthorized();
        if (!await userManager.IsInRoleAsync(caller, "Admin"))
            return TypedResults.Forbid();

        var targetUser = await userManager.FindByIdAsync(userId);
        if (targetUser is null)
            return TypedResults.NotFound();

        var status = await twoFactorService.GetTwoFactorStatusAsync(targetUser);
        return TypedResults.Ok(new AdminTwoFactorStatusResponse(
            UserName: targetUser.UserName ?? "",
            Status: status
        ));
    }

    /// <summary>
    /// 重置目标用户的 2FA（管理员操作）。
    /// 清除所有 2FA 设置、恢复码和 WebAuthn 凭证。
    /// </summary>
    private static async Task<Results<Ok, ProblemHttpResult, UnauthorizedHttpResult, ForbidHttpResult, NotFound>>
        ResetTwoFactorAsync(string userId, HttpContext httpContext,
            UserManager<AppUser> userManager, AppDbContext db,
            ILoggerFactory loggerFactory)
    {
        var caller = await userManager.GetUserAsync(httpContext.User);
        if (caller is null)
            return TypedResults.Unauthorized();
        if (!await userManager.IsInRoleAsync(caller, "Admin"))
            return TypedResults.Forbid();

        var targetUser = await userManager.FindByIdAsync(userId);
        if (targetUser is null)
            return TypedResults.NotFound();

        // 清除 2FA 设置
        targetUser.TotpSecretKey = null;
        targetUser.ConfiguredMethods = TwoFactorMethod.None;
        targetUser.TwoFactorEnabled = false;
        targetUser.TwoFactorSetupCompletedAt = null;
        targetUser.TwoFactorGracePeriodUntil = null;
        targetUser.EmailForTwoFactor = null;

        // 删除所有恢复码
        var recoveryCodes = await db.RecoveryCodes
            .Where(rc => rc.UserId == targetUser.Id)
            .ToListAsync();
        db.RecoveryCodes.RemoveRange(recoveryCodes);

        // 删除所有 WebAuthn 凭证
        var credentials = await db.WebAuthnCredentials
            .Where(wc => wc.UserId == targetUser.Id)
            .ToListAsync();
        db.WebAuthnCredentials.RemoveRange(credentials);

        var logger = loggerFactory.CreateLogger("BoxWise.Admin");
        var updateResult = await userManager.UpdateAsync(targetUser);
        if (!updateResult.Succeeded)
        {
            logger.LogWarning("Failed to update user {UserId} during 2FA reset: {Errors}",
                userId, string.Join("; ", updateResult.Errors.Select(e => e.Description)));
            return TypedResults.Problem("2FA reset failed", statusCode: 500);
        }
        await userManager.UpdateSecurityStampAsync(targetUser);

        logger.LogWarning(
            "Admin {Admin} (Id={AdminId}) reset 2FA for user {User} (Id={UserId}) at {Timestamp}",
            caller.UserName, caller.Id, targetUser.UserName, targetUser.Id, DateTime.UtcNow);

        return TypedResults.Ok();
    }
}
