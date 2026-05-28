using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;


using BoxWise.Server.Models;
using BoxWise.Server.Services;
using BoxWise.Shared.Dtos;
using QRCoder;

namespace BoxWise.Server.Endpoints;

public static class TwoFactorEndpoints
{
    public static RouteGroupBuilder MapTwoFactorEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth/2fa");

        group.MapPost("/re-authenticate", ReAuthenticateAsync)
            .WithTags("2FA")
            .ProducesProblem(401)
            .RequireRateLimiting("login-per-account");

        group.MapPost("/setup-totp", SetupTotpAsync)
            .WithTags("2FA")
            .ProducesProblem(401)
            .AddEndpointFilter<CsrfValidationFilter>();

        group.MapPost("/verify-totp", VerifyTotpAsync)
            .WithTags("2FA")
            .ProducesProblem(401)
            .AddEndpointFilter<CsrfValidationFilter>()
            .RequireRateLimiting("2fa-totp");

        group.MapPost("/challenge", ChallengeAsync)
            .WithTags("2FA")
            .ProducesProblem(401);

        group.MapPost("/verify", VerifyAsync)
            .WithTags("2FA")
            .ProducesProblem(401)
            .RequireRateLimiting("2fa-totp");

        group.MapGet("/status", GetStatusAsync)
            .WithTags("2FA")
            .ProducesProblem(401);

        group.MapPut("/switch-method", SwitchMethodAsync)
            .WithTags("2FA")
            .ProducesProblem(401)
            .AddEndpointFilter<CsrfValidationFilter>();

        // Story 8-2b: 邮箱验证码 2FA
        group.MapPost("/setup-email", SetupEmailAsync)
            .WithTags("2FA")
            .ProducesProblem(401)
            .AddEndpointFilter<CsrfValidationFilter>();

        group.MapPost("/verify-email", VerifyEmailAsync)
            .WithTags("2FA")
            .ProducesProblem(401)
            .AddEndpointFilter<CsrfValidationFilter>();

        // Story 8-2b: 恢复码
        group.MapPost("/recovery/verify", VerifyRecoveryCodeDuringLoginAsync)
            .WithTags("2FA")
            .ProducesProblem(401)
            .RequireRateLimiting("2fa-recovery");

        group.MapPost("/recovery/regenerate", RegenerateRecoveryCodesAsync)
            .WithTags("2FA")
            .ProducesProblem(401)
            .AddEndpointFilter<CsrfValidationFilter>();

        // 登录阶段发送邮箱验证码（邮箱 2FA 挑战时使用）
        group.MapPost("/send-challenge-code", SendChallengeCodeAsync)
            .WithTags("2FA")
            .ProducesProblem(401);

        return group;
    }

    public static RouteGroupBuilder MapQrCodeEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/qrcode");

        group.MapGet("/", GenerateQrCodeAsync)
            .AllowAnonymous()
            .WithTags("QR Code")
            .WithDescription("生成 QR 码图片（用于 TOTP 设置）");

        return group;
    }

    /// <summary>
    /// 已登录用户重新认证密码，获取 SessionToken（设置 2FA 前使用）。
    /// </summary>
    private static async Task<Results<Ok<ReAuthenticateResponse>, UnauthorizedHttpResult, ValidationProblem>>
        ReAuthenticateAsync(ReAuthenticateRequest request,
            HttpContext httpContext, UserManager<AppUser> userManager, TwoFactorService twoFactorService)
    {
        var user = await userManager.GetUserAsync(httpContext.User);
        if (user is null)
            return TypedResults.Unauthorized();

        if (!await userManager.CheckPasswordAsync(user, request.Password))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                { "password", new[] { "密码错误" } }
            });
        }

        var clientIp = httpContext.Connection.RemoteIpAddress?.ToString();
        var sessionToken = twoFactorService.GenerateSessionToken(user.Id, clientIp);
        return TypedResults.Ok(new ReAuthenticateResponse(sessionToken));
    }

    /// <summary>
    /// 生成 TOTP 密钥和二维码 URI，需要 X-Session-Token。
    /// </summary>
    private static async Task<Results<Ok<TwoFactorSetupResponse>, UnauthorizedHttpResult, ValidationProblem>>
        SetupTotpAsync(HttpContext httpContext, UserManager<AppUser> userManager, TwoFactorService twoFactorService)
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
        if (!twoFactorService.ValidateSessionToken(sessionToken, user.Id, clientIp))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                { "sessionToken", new[] { "会话令牌无效或已过期，请重新验证密码" } }
            });
        }

        var (secretKey, qrCodeUri) = await twoFactorService.GenerateTotpSecretAsync(user.Id);
        return TypedResults.Ok(new TwoFactorSetupResponse(secretKey, qrCodeUri));
    }

    /// <summary>
    /// 验证 TOTP 设置码并启用 2FA，成功后自动生成恢复码。
    /// </summary>
    private static async Task<Results<Ok<RecoveryCodesResponse>, UnauthorizedHttpResult, ValidationProblem>>
        VerifyTotpAsync(VerifyTwoFactorRequest request, HttpContext httpContext,
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
        var success = await twoFactorService.VerifyTotpSetupAsync(user, request.Code, sessionToken);
        if (!success)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                { "code", new[] { "验证码无效，请重试" } }
            });
        }

        // 自动生成恢复码
        var codes = await recoveryCodeService.RegenerateRecoveryCodesAsync(user);
        return TypedResults.Ok(new RecoveryCodesResponse(codes));
    }

    /// <summary>
    /// 登录阶段二：发起 2FA 挑战（从 TwoFactorUserIdScheme Cookie 读取用户）。
    /// 当方法为 Email 时自动发送验证码到用户邮箱。
    /// </summary>
    private static async Task<Results<Ok<TwoFactorChallengeResponse>, UnauthorizedHttpResult>>
        ChallengeAsync(SignInManager<AppUser> signInManager,
            EmailTwoFactorService emailTwoFactorService)
    {
        var user = await signInManager.GetTwoFactorAuthenticationUserAsync();
        if (user is null)
            return TypedResults.Unauthorized();

        var methods = new List<string>();
        string? emailToken = null;

        if (user.TwoFactorMethod == TwoFactorMethod.TOTP)
            methods.Add("TOTP");

        if (user.TwoFactorMethod == TwoFactorMethod.Email && !string.IsNullOrEmpty(user.EmailForTwoFactor))
        {
            methods.Add("Email");
            var (code, token) = emailTwoFactorService.GenerateCode(user.Id, user.EmailForTwoFactor);
            emailToken = token;
            _ = emailTwoFactorService.SendVerificationEmailAsync(user.EmailForTwoFactor, code, user.UserName)
                .ContinueWith(t => { if (t.IsFaulted) { /* SMTP 不可用，用户需手动输入 TOTP */ } });
        }

        return TypedResults.Ok(new TwoFactorChallengeResponse(methods, emailToken));
    }

    /// <summary>
    /// 登录阶段二：重新发送邮箱验证码（邮箱 2FA 时使用）。
    /// </summary>
    private static async Task<Results<Ok, UnauthorizedHttpResult>>
        SendChallengeCodeAsync(SignInManager<AppUser> signInManager,
            EmailTwoFactorService emailTwoFactorService)
    {
        var user = await signInManager.GetTwoFactorAuthenticationUserAsync();
        if (user is null)
            return TypedResults.Unauthorized();

        if (string.IsNullOrEmpty(user.EmailForTwoFactor))
            return TypedResults.Ok();

        var (code, _) = emailTwoFactorService.GenerateCode(user.Id, user.EmailForTwoFactor);
        _ = emailTwoFactorService.SendVerificationEmailAsync(user.EmailForTwoFactor, code, user.UserName)
            .ContinueWith(t => { if (t.IsFaulted) { /* SMTP 不可用，用户需检查邮箱配置 */ } });

        return TypedResults.Ok();
    }

    /// <summary>
    /// 登录阶段二：验证 2FA 响应（支持 TOTP 和 Email），成功后颁发完整认证 Cookie。
    /// </summary>
    private static async Task<Results<Ok<AuthUserDto>, UnauthorizedHttpResult, ValidationProblem>>
        VerifyAsync(VerifyTwoFactorRequest request, SignInManager<AppUser> signInManager,
            UserManager<AppUser> userManager, TwoFactorService twoFactorService,
            IConfiguration config, EmailTwoFactorService emailTwoFactorService)
    {
        var user = await signInManager.GetTwoFactorAuthenticationUserAsync();
        if (user is null)
            return TypedResults.Unauthorized();

        bool valid;
        if (user.TwoFactorMethod == TwoFactorMethod.Email && !string.IsNullOrEmpty(user.EmailForTwoFactor))
        {
            if (string.IsNullOrEmpty(request.Token))
                return TypedResults.ValidationProblem(new Dictionary<string, string[]>
                {
                    { "token", new[] { "缺少验证令牌" } }
                });
            valid = emailTwoFactorService.VerifyCode(user.Id, user.EmailForTwoFactor, request.Code, request.Token);
        }
        else
        {
            valid = await twoFactorService.VerifyTotpChallengeAsync(user, request.Code);
        }

        if (!valid)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                { "code", new[] { "验证码无效" } }
            });
        }

        // 清除 TwoFactorUserId Cookie
        await signInManager.Context.SignOutAsync(IdentityConstants.TwoFactorUserIdScheme);

        // 颁发完整认证 Cookie，添加 2FA 已验证声明
        await signInManager.SignInWithClaimsAsync(user, isPersistent: true,
            new[] { new Claim("amr", "2fa") });

        var isAdmin = await userManager.IsInRoleAsync(user, "Admin");
        var adminConfigured = !string.IsNullOrWhiteSpace(config["Admin:Password"]);
        var isSpecificAdmin = adminConfigured
            && string.Equals(user.UserName, config["Admin:Username"] ?? "admin", StringComparison.OrdinalIgnoreCase);

        return TypedResults.Ok(new AuthUserDto(user.UserName!, isAdmin, isSpecificAdmin));
    }

    /// <summary>
    /// 获取当前用户的 2FA 状态。
    /// </summary>
    private static async Task<Results<Ok<TwoFactorStatusDto>, UnauthorizedHttpResult>>
        GetStatusAsync(HttpContext httpContext, UserManager<AppUser> userManager, TwoFactorService twoFactorService)
    {
        var user = await userManager.GetUserAsync(httpContext.User);
        if (user is null)
            return TypedResults.Unauthorized();

        var status = await twoFactorService.GetTwoFactorStatusAsync(user);
        return TypedResults.Ok(status);
    }

    /// <summary>
    /// 切换 2FA 方法（支持 TOTP 和 Email）。
    /// </summary>
    private static async Task<Results<Ok, UnauthorizedHttpResult, ValidationProblem>>
        SwitchMethodAsync(SwitchMethodRequest request, HttpContext httpContext,
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

        if (!Enum.TryParse<TwoFactorMethod>(request.Method, out var method))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                { "method", new[] { "无效的认证方法" } }
            });
        }

        var clientIp = httpContext.Connection.RemoteIpAddress?.ToString();
        var success = await twoFactorService.SwitchMethodAsync(user, method, sessionToken);
        if (!success)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                { "method", new[] { "切换认证方法失败" } }
            });
        }

        return TypedResults.Ok();
    }

    // ===== Story 8-2b: 邮箱验证码 2FA 端点 =====

    /// <summary>
    /// 设置邮箱 2FA：保存邮箱地址并发送验证码。
    /// </summary>
    private static async Task<Results<Ok<EmailTwoFactorSetupResponse>, UnauthorizedHttpResult, ValidationProblem>>
        SetupEmailAsync(SetupEmailTwoFactorRequest request, HttpContext httpContext,
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
        if (!twoFactorService.ValidateSessionToken(sessionToken, user.Id, clientIp))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                { "sessionToken", new[] { "会话令牌无效或已过期，请重新验证密码" } }
            });
        }

        var email = request.Email.Trim();
        if (string.IsNullOrWhiteSpace(email) || email.Length > 256 || !email.Contains('@'))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                { "email", new[] { "请输入有效的邮箱地址" } }
            });
        }

        user.EmailForTwoFactor = email;
        await userManager.UpdateAsync(user);

        // 生成自包含令牌并发送验证码
        var (code, token) = emailTwoFactorService.GenerateCode(user.Id, email);
        var sent = await emailTwoFactorService.SendVerificationEmailAsync(email, code, user.UserName);

        if (!sent)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                { "email", new[] { "验证码发送失败，请检查 SMTP 配置或稍后重试" } }
            });
        }

        return TypedResults.Ok(new EmailTwoFactorSetupResponse(token));
    }

    /// <summary>
    /// 验证邮箱验证码并启用 2FA，成功后自动生成恢复码。
    /// </summary>
    private static async Task<Results<Ok<RecoveryCodesResponse>, UnauthorizedHttpResult, ValidationProblem>>
        VerifyEmailAsync(VerifyTwoFactorRequest request, HttpContext httpContext,
            UserManager<AppUser> userManager, TwoFactorService twoFactorService,
            EmailTwoFactorService emailTwoFactorService,
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
        if (!twoFactorService.ValidateSessionToken(sessionToken, user.Id, clientIp))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                { "sessionToken", new[] { "会话令牌无效或已过期，请重新验证密码" } }
            });
        }

        var email = user.EmailForTwoFactor;
        if (string.IsNullOrWhiteSpace(email))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                { "email", new[] { "请先设置邮箱地址" } }
            });
        }

        if (string.IsNullOrEmpty(request.Token))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                { "token", new[] { "缺少验证令牌" } }
            });
        }

        var valid = emailTwoFactorService.VerifyCode(user.Id, email, request.Code, request.Token);
        if (!valid)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                { "code", new[] { "验证码无效或已过期" } }
            });
        }

        // 如果从另一种方法切换，清除旧密钥
        if (user.TwoFactorMethod != TwoFactorMethod.None)
        {
            user.TotpSecretKey = null;
        }

        user.TwoFactorEnabled = true;
        user.TwoFactorMethod = TwoFactorMethod.Email;
        user.TwoFactorSetupCompletedAt = DateTime.UtcNow;

        var updateResult = await userManager.UpdateAsync(user);
        if (!updateResult.Succeeded)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                { "general", new[] { "启用 2FA 失败" } }
            });
        }

        // 自动生成恢复码
        var codes = await recoveryCodeService.RegenerateRecoveryCodesAsync(user);
        return TypedResults.Ok(new RecoveryCodesResponse(codes));
    }

    // ===== Story 8-2b: 恢复码端点 =====

    /// <summary>
    /// 登录阶段使用恢复码：验证恢复码 → 清除 2FA 设置 → 签发完整认证 Cookie。
    /// </summary>
    private static async Task<Results<Ok<AuthUserDto>, UnauthorizedHttpResult, ValidationProblem>>
        VerifyRecoveryCodeDuringLoginAsync(VerifyTwoFactorRequest request,
            SignInManager<AppUser> signInManager, UserManager<AppUser> userManager,
            RecoveryCodeService recoveryCodeService, IConfiguration config)
    {
        var user = await signInManager.GetTwoFactorAuthenticationUserAsync();
        if (user is null)
            return TypedResults.Unauthorized();

        var valid = await recoveryCodeService.VerifyRecoveryCodeAsync(user, request.Code, userManager);
        if (!valid)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                { "code", new[] { "恢复码无效" } }
            });
        }

        // 清除 TwoFactorUserId Cookie
        await signInManager.Context.SignOutAsync(IdentityConstants.TwoFactorUserIdScheme);

        // 签发完整认证 Cookie，添加 2FA 已验证声明
        await signInManager.SignInWithClaimsAsync(user, isPersistent: true,
            new[] { new Claim("2fa", "verified") });

        var isAdmin = await userManager.IsInRoleAsync(user, "Admin");
        var adminConfigured = !string.IsNullOrWhiteSpace(config["Admin:Password"]);
        var isSpecificAdmin = adminConfigured
            && string.Equals(user.UserName, config["Admin:Username"] ?? "admin", StringComparison.OrdinalIgnoreCase);

        return TypedResults.Ok(new AuthUserDto(user.UserName!, isAdmin, isSpecificAdmin));
    }

    /// <summary>
    /// 重新生成恢复码（旧码全部失效），返回新恢复码明文列表。
    /// </summary>
    private static async Task<Results<Ok<RecoveryCodesResponse>, UnauthorizedHttpResult, ValidationProblem>>
        RegenerateRecoveryCodesAsync(HttpContext httpContext,
            UserManager<AppUser> userManager, RecoveryCodeService recoveryCodeService)
    {
        var user = await userManager.GetUserAsync(httpContext.User);
        if (user is null)
            return TypedResults.Unauthorized();

        var codes = await recoveryCodeService.RegenerateRecoveryCodesAsync(user);
        return TypedResults.Ok(new RecoveryCodesResponse(codes));
    }

    /// <summary>
    /// 生成 QR 码 PNG 图片。
    /// </summary>
    private static IResult GenerateQrCodeAsync(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return TypedResults.Problem("text 参数不能为空", statusCode: 400);

        using var qrGenerator = new QRCodeGenerator();
        using var qrCodeData = qrGenerator.CreateQrCode(text, QRCodeGenerator.ECCLevel.Q);
        using var pngQrCode = new PngByteQRCode(qrCodeData);
        var qrCodeBytes = pngQrCode.GetGraphic(20);

        return Results.File(qrCodeBytes, "image/png");
    }
}
