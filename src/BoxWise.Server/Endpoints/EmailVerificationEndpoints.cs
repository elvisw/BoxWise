using System.Security.Claims;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using BoxWise.Server.Models;
using BoxWise.Server.Services;
using BoxWise.Server.Utilities;
using BoxWise.Shared.Dtos;

namespace BoxWise.Server.Endpoints;

public static class EmailVerificationEndpoints
{
    private const string SendCodePurpose = "email-change";
    internal const string OperationTokenPurpose = "email-operation-token";

    public static RouteGroupBuilder MapEmailVerificationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth/email");

        group.MapPost("/send-code", SendCodeAsync)
            .WithTags("Email Verification")
            .ProducesProblem(401)
            .RequireRateLimiting("email-verification")
            .AddEndpointFilter<CsrfValidationFilter>();

        group.MapPost("/verify-code", VerifyCodeAsync)
            .WithTags("Email Verification")
            .ProducesProblem(401)
            .RequireRateLimiting("email-verification")
            .AddEndpointFilter<CsrfValidationFilter>();

        return group;
    }

    /// <summary>
    /// 向新邮箱发送验证码。需要 X-Session-Token（密码重新认证后获得）。
    /// 预检查邮箱唯一性。返回 verification token。
    /// </summary>
    private static async Task<Results<Ok<EmailVerificationSendResponse>, UnauthorizedHttpResult, ValidationProblem>>
        SendCodeAsync(SendEmailCodeRequest request, HttpContext httpContext,
            UserManager<AppUser> userManager, TwoFactorService twoFactorService,
            EmailTwoFactorService emailTwoFactorService)
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
        // 用户已启用 2FA → 必须使用 modify session token（purpose="2fa-modify"）
        // 用户未启用 2FA → 使用普通 session token（purpose="2fa-setup"）
        var expectedPurpose = user.TwoFactorEnabled ? "2fa-modify" : "2fa-setup";
        if (!twoFactorService.ValidateSessionToken(sessionToken, user.Id, clientIp, expectedPurpose))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                { "sessionToken", new[] { "会话令牌无效或已过期，请重新验证身份" } }
            });
        }

        var email = request.Email?.Trim() ?? "";
        var emailError = EmailValidation.Validate(email);
        if (emailError is not null)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                { "email", new[] { emailError } }
            });
        }

        // 检查邮箱是否唯一
        var existingEmailUser = await userManager.FindByEmailAsync(email);
        if (existingEmailUser is not null && existingEmailUser.Id != user.Id)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                { "email", new[] { "该邮箱已被其他账户使用" } }
            });
        }

        var (code, token) = emailTwoFactorService.GenerateCode(user.Id, email);
        var sent = await emailTwoFactorService.SendVerificationEmailAsync(email, code, user.UserName, SendCodePurpose);

        if (!sent)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                { "email", new[] { "邮件发送失败，请稍后再试" } }
            });
        }

        return TypedResults.Ok(new EmailVerificationSendResponse(token));
    }

    /// <summary>
    /// 验证验证码。成功时返回 Data Protection operation token。
    /// 此端点不需要 X-Session-Token（登录 cookie 已提供身份，密码重新认证在 send-code 完成）。
    /// </summary>
    private static async Task<Results<Ok<EmailVerifyCodeResponse>, UnauthorizedHttpResult, ValidationProblem>>
        VerifyCodeAsync(VerifyEmailCodeRequest request, HttpContext httpContext,
            UserManager<AppUser> userManager, EmailTwoFactorService emailTwoFactorService,
            IDataProtectionProvider protectionProvider)
    {
        var user = await userManager.GetUserAsync(httpContext.User);
        if (user is null)
            return TypedResults.Unauthorized();

        if (string.IsNullOrEmpty(request.Code) || request.Code.Length != 6)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                { "code", new[] { "请输入有效的 6 位验证码" } }
            });
        }

        if (string.IsNullOrEmpty(request.Token))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                { "token", new[] { "缺少验证令牌" } }
            });
        }

        // 从自包含令牌中提取邮箱
        // 令牌格式: userId|email|code|expiry
        var emailProtector = protectionProvider.CreateProtector("BoxWise.EmailTwoFactor");
        string tokenEmail;
        try
        {
            var payload = emailProtector.Unprotect(request.Token);
            var parts = payload.Split('|');
            if (parts.Length < 4)
                return TypedResults.ValidationProblem(new Dictionary<string, string[]>
                {
                    { "token", new[] { "验证令牌无效" } }
                });
            tokenEmail = parts[1];
        }
        catch
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                { "token", new[] { "验证令牌无效" } }
            });
        }

        var valid = emailTwoFactorService.VerifyCodeOnce(user.Id, tokenEmail, request.Code, request.Token);
        if (!valid)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                { "code", new[] { "验证码无效或已过期，请重新发送" } }
            });
        }

        // 生成 operation token（自包含：userId|verifiedEmail|expiry）
        var operationProtector = protectionProvider.CreateProtector(OperationTokenPurpose);
        var operationToken = operationProtector.Protect(
            $"{user.Id}|{tokenEmail}|{DateTime.UtcNow.AddMinutes(5):O}");

        return TypedResults.Ok(new EmailVerifyCodeResponse(operationToken, tokenEmail));
    }
}

