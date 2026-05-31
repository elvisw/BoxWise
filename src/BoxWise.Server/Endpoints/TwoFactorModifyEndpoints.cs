using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using BoxWise.Server.Models;
using BoxWise.Server.Services;
using BoxWise.Shared.Dtos;

namespace BoxWise.Server.Endpoints;

public static class TwoFactorModifyEndpoints
{
    public static RouteGroupBuilder MapTwoFactorModifyEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth/2fa/modify");

        group.MapPost("/authenticate", AuthenticateForModifyAsync)
            .WithTags("2FA/Modify")
            .ProducesProblem(401)
            .RequireRateLimiting("2fa-modify")
            .AddEndpointFilter<CsrfValidationFilter>();

        group.MapPost("/totp", ResetTotpAsync)
            .WithTags("2FA/Modify")
            .ProducesProblem(401)
            .RequireRateLimiting("2fa-modify")
            .AddEndpointFilter<CsrfValidationFilter>();

        group.MapPost("/totp/verify", VerifyTotpResetAsync)
            .WithTags("2FA/Modify")
            .ProducesProblem(401)
            .RequireRateLimiting("2fa-modify")
            .AddEndpointFilter<CsrfValidationFilter>();

        group.MapPost("/recovery/regenerate", RegenerateRecoveryCodesForModifyAsync)
            .WithTags("2FA/Modify")
            .ProducesProblem(401)
            .RequireRateLimiting("2fa-modify")
            .AddEndpointFilter<CsrfValidationFilter>();

        return group;
    }

    /// <summary>
    /// 2FA 修改模式的身份验证。用户使用已配置的任一 2FA 方法验证身份。
    /// 成功后返回 modify session token（purpose="2fa-modify"，15 分钟有效）。
    /// </summary>
    private static async Task<Results<Ok<ReAuthenticateResponse>, UnauthorizedHttpResult, ValidationProblem>>
        AuthenticateForModifyAsync(VerifyTwoFactorRequest request,
            HttpContext httpContext, UserManager<AppUser> userManager,
            TwoFactorService twoFactorService, EmailTwoFactorService emailTwoFactorService,
            RecoveryCodeService recoveryCodeService)
    {
        var user = await userManager.GetUserAsync(httpContext.User);
        if (user is null)
            return TypedResults.Unauthorized();

        if (!user.TwoFactorEnabled)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                { "general", new[] { "2FA 未启用" } }
            });
        }

        bool valid;
        switch (request.Method)
        {
            case "TOTP":
                if (!user.ConfiguredMethods.HasFlag(TwoFactorMethod.TOTP))
                    return TypedResults.ValidationProblem(new Dictionary<string, string[]>
                    {
                        { "method", new[] { "该方法未配置" } }
                    });
                valid = await twoFactorService.VerifyTotpChallengeAsync(user, request.Code);
                break;
            case "Email":
                if (!user.ConfiguredMethods.HasFlag(TwoFactorMethod.Email))
                    return TypedResults.ValidationProblem(new Dictionary<string, string[]>
                    {
                        { "method", new[] { "该方法未配置" } }
                    });
                var effectiveEmail = user.EffectiveEmailForTwoFactor;
                if (string.IsNullOrEmpty(effectiveEmail))
                    return TypedResults.ValidationProblem(new Dictionary<string, string[]>
                    {
                        { "method", new[] { "邮箱 2FA 未完整配置" } }
                    });
                if (string.IsNullOrEmpty(request.Token))
                    return TypedResults.ValidationProblem(new Dictionary<string, string[]>
                    {
                        { "token", new[] { "缺少验证令牌" } }
                    });
                valid = emailTwoFactorService.VerifyCode(user.Id, effectiveEmail, request.Code, request.Token);
                break;
            case "RecoveryCode":
                valid = await recoveryCodeService.ValidateRecoveryCodeAsync(user, request.Code);
                break;
            default:
                return TypedResults.ValidationProblem(new Dictionary<string, string[]>
                {
                    { "method", new[] { "无效的验证方法" } }
                });
        }

        if (!valid)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                { "code", new[] { "验证码无效" } }
            });
        }

        var clientIp = httpContext.Connection.RemoteIpAddress?.ToString();
        var sessionToken = twoFactorService.GenerateSessionToken(user.Id, clientIp, "2fa-modify");
        return TypedResults.Ok(new ReAuthenticateResponse(sessionToken));
    }

    /// <summary>
    /// 重置 TOTP 密钥（第一步：生成新密钥暂存于 PendingTotpSecretKey）。
    /// 旧密钥保持有效直到 verify 确认。
    /// 需要 modify session token（purpose="2fa-modify"）。
    /// </summary>
    private static async Task<Results<Ok<TwoFactorSetupResponse>, UnauthorizedHttpResult, ValidationProblem>>
        ResetTotpAsync(HttpContext httpContext, UserManager<AppUser> userManager, TwoFactorService twoFactorService)
    {
        var sessionToken = httpContext.Request.Headers["X-Session-Token"].FirstOrDefault();
        if (string.IsNullOrEmpty(sessionToken))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                { "sessionToken", new[] { "缺少会话令牌" } }
            });
        }

        var user = await userManager.GetUserAsync(httpContext.User);
        if (user is null)
            return TypedResults.Unauthorized();

        var clientIp = httpContext.Connection.RemoteIpAddress?.ToString();
        if (!twoFactorService.ValidateSessionToken(sessionToken, user.Id, clientIp, "2fa-modify"))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                { "sessionToken", new[] { "会话令牌无效或已过期，请重新验证身份" } }
            });
        }

        if (!user.ConfiguredMethods.HasFlag(TwoFactorMethod.TOTP))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                { "method", new[] { "您尚未配置 TOTP 2FA" } }
            });
        }

        var (secretKey, qrCodeUri) = await twoFactorService.GeneratePendingTotpSecretAsync(user.Id);
        return TypedResults.Ok(new TwoFactorSetupResponse(secretKey, qrCodeUri));
    }

    /// <summary>
    /// 验证 TOTP 修改设置（第二步：确认用户已保存新密钥）。
    /// 通过后将 PendingTotpSecretKey 提升为 TotpSecretKey。
    /// 需要 modify session token（purpose="2fa-modify"）。
    /// </summary>
    private static async Task<Results<Ok, UnauthorizedHttpResult, ValidationProblem>>
        VerifyTotpResetAsync(VerifyTwoFactorRequest request, HttpContext httpContext,
            UserManager<AppUser> userManager, TwoFactorService twoFactorService)
    {
        var sessionToken = httpContext.Request.Headers["X-Session-Token"].FirstOrDefault();
        if (string.IsNullOrEmpty(sessionToken))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                { "sessionToken", new[] { "缺少会话令牌" } }
            });
        }

        var user = await userManager.GetUserAsync(httpContext.User);
        if (user is null)
            return TypedResults.Unauthorized();

        // VerifyPendingTotpSetupAsync 内部校验 session token（purpose="2fa-modify"）
        var clientIp = httpContext.Connection.RemoteIpAddress?.ToString();
        var success = await twoFactorService.VerifyPendingTotpSetupAsync(user, request.Code, sessionToken, clientIp);
        if (!success)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                { "code", new[] { "验证码无效，请重试" } }
            });
        }

        return TypedResults.Ok();
    }

    /// <summary>
    /// 重新生成恢复码（旧码全部失效）。需要 modify session token（purpose="2fa-modify"）。
    /// </summary>
    private static async Task<Results<Ok<RecoveryCodesResponse>, UnauthorizedHttpResult, ValidationProblem>>
        RegenerateRecoveryCodesForModifyAsync(HttpContext httpContext,
            UserManager<AppUser> userManager, TwoFactorService twoFactorService,
            RecoveryCodeService recoveryCodeService)
    {
        var sessionToken = httpContext.Request.Headers["X-Session-Token"].FirstOrDefault();
        if (string.IsNullOrEmpty(sessionToken))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                { "sessionToken", new[] { "缺少会话令牌" } }
            });
        }

        var user = await userManager.GetUserAsync(httpContext.User);
        if (user is null)
            return TypedResults.Unauthorized();

        var clientIp = httpContext.Connection.RemoteIpAddress?.ToString();
        if (!twoFactorService.ValidateSessionToken(sessionToken, user.Id, clientIp, "2fa-modify"))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                { "sessionToken", new[] { "会话令牌无效或已过期，请重新验证身份" } }
            });
        }

        var codes = await recoveryCodeService.RegenerateRecoveryCodesAsync(user);
        return TypedResults.Ok(new RecoveryCodesResponse(codes));
    }
}
