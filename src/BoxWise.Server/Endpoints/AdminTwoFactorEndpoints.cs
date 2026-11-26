using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
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
    /// 在单个事务内清除 2FA 设置、两套恢复码与 WebAuthn 凭证，并轮换 SecurityStamp；
    /// 仅在 Identity authenticator key 存在时轮换它。
    /// </summary>
    private static async Task<Results<Ok, ProblemHttpResult, UnauthorizedHttpResult, ForbidHttpResult, NotFound>>
        ResetTwoFactorAsync(string userId, HttpContext httpContext,
            UserManager<AppUser> userManager, RecoveryCodeService recoveryCodeService,
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

        var logger = loggerFactory.CreateLogger("BoxWise.Admin");
        var result = await recoveryCodeService.ResetForAdminAsync(targetUser, userManager);
        if (!result.Succeeded)
        {
            logger.LogWarning(
                "Failed to reset 2FA for user {UserId}: {Status} (outcome confirmed: {Confirmed})",
                userId, result.Status, result.OutcomeConfirmed);

            // 保持既有 500/ProblemDetails 约定，仅区分“已回滚”与“提交结果未知”，
            // 避免调用方在结果未知时误以为可以安全重试或状态未变。
            return TypedResults.Problem(
                detail: result.OutcomeConfirmed
                    ? "2FA reset failed; no changes were committed."
                    : "2FA reset outcome is unconfirmed; verify the account state before retrying.",
                statusCode: StatusCodes.Status500InternalServerError);
        }

        logger.LogWarning(
            "Admin {Admin} (Id={AdminId}) reset 2FA for user {User} (Id={UserId}) at {Timestamp}",
            caller.UserName, caller.Id, targetUser.UserName, targetUser.Id, DateTime.UtcNow);

        return TypedResults.Ok();
    }
}
