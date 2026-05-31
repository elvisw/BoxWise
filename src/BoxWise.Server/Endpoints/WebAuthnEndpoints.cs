using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using BoxWise.Server.Data;
using BoxWise.Server.Models;
using System.Text.Json;
using Fido2NetLib;
using BoxWise.Server.Services;
using BoxWise.Shared.Dtos;

namespace BoxWise.Server.Endpoints;

public static class WebAuthnEndpoints
{
    public static RouteGroupBuilder MapWebAuthnEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth/webauthn");

        group.MapGet("/available", IsAvailableAsync)
            .WithTags("2FA")
            .ProducesProblem(401);

        group.MapPost("/register-begin", RegisterBeginAsync)
            .WithTags("2FA")
            .ProducesProblem(401);

        group.MapPost("/verify-begin", VerifyBeginAsync)
            .WithTags("2FA")
            .ProducesProblem(401);

        group.MapPost("/register-complete", RegisterCompleteAsync)
            .WithTags("2FA")
            .ProducesProblem(401);

        group.MapPost("/verify-complete", VerifyCompleteAsync)
            .WithTags("2FA")
            .ProducesProblem(401);

        group.MapGet("/credentials", GetCredentialsAsync)
            .WithTags("2FA")
            .ProducesProblem(401);

        group.MapDelete("/credentials/{id:int}", DeleteCredentialAsync)
            .WithTags("2FA")
            .ProducesProblem(401);

        // Passkey 无密码登录（匿名访问，速率限制防止滥用）
        group.MapPost("/login-begin", LoginBeginAsync)
            .WithTags("2FA")
            .AllowAnonymous()
            .RequireRateLimiting("login-per-ip");

        group.MapPost("/login-complete", LoginCompleteAsync)
            .WithTags("2FA")
            .AllowAnonymous()
            .RequireRateLimiting("login-per-ip");

        return group;
    }

    private static Ok<WebAuthnAvailableResponse> IsAvailableAsync(HttpContext httpContext)
    {
        var origin = $"{httpContext.Request.Scheme}://{httpContext.Request.Host}";
        var available = WebAuthnService.IsOriginSupported(origin);
        return TypedResults.Ok(new WebAuthnAvailableResponse(available, origin));
    }

    private static async Task<Results<Ok<object>, ProblemHttpResult>>
        RegisterBeginAsync(UserManager<AppUser> userManager,
        WebAuthnService webAuthnService, HttpContext httpContext)
    {
        var user = await userManager.GetUserAsync(httpContext.User);
        if (user?.UserName is null)
            return TypedResults.Problem("未登录", statusCode: 401);

        try
        {
            var options = await webAuthnService.StartRegistration(user);
            httpContext.Session.SetString("WebAuthnRegisterOptions", options.ToJson());
            return TypedResults.Ok<object>(options);
        }
        catch (InvalidOperationException ex)
        {
            return TypedResults.Problem(ex.Message, statusCode: 400);
        }
    }

    private static async Task<Results<Ok<object>, ProblemHttpResult>>
        VerifyBeginAsync(UserManager<AppUser> userManager,
        WebAuthnService webAuthnService, HttpContext httpContext)
    {
        var user = await userManager.GetUserAsync(httpContext.User);
        if (user?.UserName is null)
            return TypedResults.Problem("未登录", statusCode: 401);

        var options = await webAuthnService.StartVerification(user);
        if (options is null)
            return TypedResults.Problem("未注册 WebAuthn 凭证", statusCode: 400);

        httpContext.Session.SetString("WebAuthnVerifyOptions", options.ToJson());
        return TypedResults.Ok<object>(options);
    }

    private static async Task<Ok<List<WebAuthnCredentialDto>>> GetCredentialsAsync(
        UserManager<AppUser> userManager, WebAuthnService webAuthnService, HttpContext httpContext)
    {
        var user = await userManager.GetUserAsync(httpContext.User);
        if (user?.UserName is null)
            return TypedResults.Ok(new List<WebAuthnCredentialDto>());
        var credentials = await webAuthnService.GetCredentialsAsync(user);
        var dtos = credentials.Select(c => new WebAuthnCredentialDto(
            c.Id, c.DeviceName, c.CreatedAt)).ToList();
        return TypedResults.Ok(dtos);
    }

    private static async Task<Results<Ok, ProblemHttpResult>> DeleteCredentialAsync(
        int id, UserManager<AppUser> userManager, WebAuthnService webAuthnService, HttpContext httpContext)
    {
        var user = await userManager.GetUserAsync(httpContext.User);
        if (user?.UserName is null)
            return TypedResults.Problem("未登录", statusCode: 401);
        var success = await webAuthnService.RemoveCredentialAsync(user, id);
        if (!success) return TypedResults.Problem("凭证不存在", statusCode: 404);
        return TypedResults.Ok();
    }

    private static async Task<Results<Ok<RecoveryCodesResponse>, ProblemHttpResult>> RegisterCompleteAsync(
        UserManager<AppUser> userManager, WebAuthnService webAuthnService,
        RecoveryCodeService recoveryCodeService, HttpContext httpContext)
    {
        var user = await userManager.GetUserAsync(httpContext.User);
        if (user?.UserName is null)
            return TypedResults.Problem("未登录", statusCode: 401);

        var optionsJson = httpContext.Session.GetString("WebAuthnRegisterOptions");
        if (string.IsNullOrEmpty(optionsJson))
            return TypedResults.Problem("注册会话已过期", statusCode: 400);

        var options = CredentialCreateOptions.FromJson(optionsJson);
        var body = await new StreamReader(httpContext.Request.Body).ReadToEndAsync();
        var attestation = JsonSerializer.Deserialize<AuthenticatorAttestationRawResponse>(body,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        if (attestation is null)
            return TypedResults.Problem("无效的请求数据", statusCode: 400);

        var deviceName = (httpContext.Request.Headers["X-Device-Name"].FirstOrDefault() ?? "未知设备").Trim();
        var success = await webAuthnService.CompleteRegistration(user, attestation, options, deviceName);
        if (!success) return TypedResults.Problem("WebAuthn 注册失败", statusCode: 400);

        // 始终更新 ConfiguredMethods（无论是否首个 2FA 方法）
        user.ConfiguredMethods |= TwoFactorMethod.WebAuthn;
        if (!user.TwoFactorEnabled)
        {
            user.TwoFactorEnabled = true;
            user.TwoFactorSetupCompletedAt = DateTime.UtcNow;
        }
        var updateResult = await userManager.UpdateAsync(user);
        if (!updateResult.Succeeded)
            return TypedResults.Problem("保存用户配置失败", statusCode: 500);

        // 生成恢复码并返回
        var codes = await recoveryCodeService.RegenerateRecoveryCodesAsync(user);
        httpContext.Session.Remove("WebAuthnRegisterOptions");
        return TypedResults.Ok(new RecoveryCodesResponse(codes));
    }

    private static async Task<Results<Ok, ProblemHttpResult>> VerifyCompleteAsync(
        UserManager<AppUser> userManager, WebAuthnService webAuthnService,
        HttpContext httpContext)
    {
        var user = await userManager.GetUserAsync(httpContext.User);
        if (user?.UserName is null)
            return TypedResults.Problem("未登录", statusCode: 401);

        var optionsJson = httpContext.Session.GetString("WebAuthnVerifyOptions");
        if (string.IsNullOrEmpty(optionsJson))
            return TypedResults.Problem("验证会话已过期", statusCode: 400);

        var options = AssertionOptions.FromJson(optionsJson);
        var body = await new StreamReader(httpContext.Request.Body).ReadToEndAsync();
        var assertion = JsonSerializer.Deserialize<AuthenticatorAssertionRawResponse>(body,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        if (assertion is null)
            return TypedResults.Problem("无效的请求数据", statusCode: 400);

        var success = await webAuthnService.CompleteVerification(user, assertion, options);
        if (!success) return TypedResults.Problem("WebAuthn 验证失败", statusCode: 400);

        httpContext.Session.Remove("WebAuthnVerifyOptions");
        return TypedResults.Ok();
    }

    // ===== Passkey 无密码登录（匿名访问）=====

    private static async Task<Results<Ok<object>, ProblemHttpResult>> LoginBeginAsync(
        WebAuthnService webAuthnService, HttpContext httpContext)
    {
        var options = webAuthnService.StartLogin();
        httpContext.Session.SetString("WebAuthnLoginOptions", options.ToJson());
        return TypedResults.Ok<object>(options);
    }

    private static async Task<Results<Ok<AuthUserDto>, ProblemHttpResult>> LoginCompleteAsync(
        WebAuthnService webAuthnService, SignInManager<AppUser> signInManager,
        UserManager<AppUser> userManager, HttpContext httpContext)
    {
        var optionsJson = httpContext.Session.GetString("WebAuthnLoginOptions");
        if (string.IsNullOrEmpty(optionsJson))
            return TypedResults.Problem("登录会话已过期", statusCode: 400);

        var options = AssertionOptions.FromJson(optionsJson);
        var body = await new StreamReader(httpContext.Request.Body).ReadToEndAsync();
        var assertion = JsonSerializer.Deserialize<AuthenticatorAssertionRawResponse>(body,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        if (assertion is null)
            return TypedResults.Problem("无效的请求数据", statusCode: 400);

        var user = await webAuthnService.CompleteLoginAsync(assertion, options);
        if (user is null)
            return TypedResults.Problem("通行密钥验证失败", statusCode: 400);

        httpContext.Session.Remove("WebAuthnLoginOptions");
        await signInManager.SignInAsync(user, isPersistent: true);

        var isAdmin = await userManager.IsInRoleAsync(user, "Admin");
        return TypedResults.Ok(new AuthUserDto(user.UserName!, isAdmin, Email: user.Email));
    }
}
