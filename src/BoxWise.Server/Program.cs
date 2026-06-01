using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using BoxWise.Server.Data;
using BoxWise.Server.Endpoints;
using BoxWise.Server.Models;
using BoxWise.Server.Configuration;
using BoxWise.Server.Repositories;
using BoxWise.Server.Services;
using Fido2NetLib;
using BoxWise.Server.Services.PasswordValidators;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = 10 * 1024 * 1024; // 10MB
});

// Database
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection")));

// Identity
builder.Services.AddIdentity<AppUser, IdentityRole>(options =>
{
    options.Password.RequiredLength = 8;
    options.Password.RequiredUniqueChars = 0;
    options.Password.RequireDigit = false;
    options.Password.RequireLowercase = false;
    options.Password.RequireNonAlphanumeric = false;
    options.Password.RequireUppercase = false;
})
.AddEntityFrameworkStores<AppDbContext>()
.AddDefaultTokenProviders();

builder.Services.AddScoped<IPasswordValidator<AppUser>, NoNumericOnlyValidator>();
builder.Services.AddScoped<IPasswordValidator<AppUser>, CommonPasswordValidator>();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.None; // Blazor WASM 跨端口 fetch 需要 None
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always; // SameSite=None 必须配合 Secure
    options.ExpireTimeSpan = TimeSpan.FromDays(30);
    options.SlidingExpiration = true;
    options.LoginPath = "/Identity/Account/Login";
    options.Events.OnRedirectToLogin = ctx =>
    {
        if (ctx.Request.Path.StartsWithSegments("/api"))
        {
            ctx.Response.StatusCode = 401;
            return Task.CompletedTask;
        }
        ctx.Response.Redirect(ctx.RedirectUri);
        return Task.CompletedTask;
    };
    options.Events.OnRedirectToAccessDenied = ctx =>
    {
        ctx.Response.StatusCode = 403;
        return Task.CompletedTask;
    };
});

// TwoFactorUserId Cookie — 也需要跨端口 SameSite=None（Blazor WASM:5001 → API:5000）
builder.Services.Configure<CookieAuthenticationOptions>(IdentityConstants.TwoFactorUserIdScheme, options =>
{
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.None;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
});

builder.Services.AddAuthentication();
builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();

    options.AddPolicy("AdminOnly", policy =>
        policy.RequireRole("Admin"));
});

// Data Protection - 持久化到文件系统（TOTP 密钥加密依赖）
var dataProtectionKeysPath = Path.Combine(
    builder.Configuration["DataDirectory"] ?? "data", "keys");
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(dataProtectionKeysPath));

// CORS for Blazor WASM dev
builder.Services.AddCors(options =>
{
    options.AddPolicy("Dev", policy =>
    {
        policy.WithOrigins("https://localhost:5001")
              .AllowCredentials()
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

builder.Services.AddScoped<LocationRepository>();
builder.Services.AddScoped<TagRepository>();
builder.Services.AddScoped<ItemRepository>();

// LLM
builder.Services.AddOptions<LlmOptions>()
    .Bind(builder.Configuration.GetSection(LlmOptions.SectionName))
    .ValidateOnStart();
builder.Services.AddHttpClient<LlmClient>();
builder.Services.AddSingleton<ImageStorageService>();
builder.Services.AddSingleton<ThumbnailService>();
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<ISmtpConfigurationService, SmtpConfigurationService>();
builder.Services.AddScoped<TwoFactorService>();
builder.Services.AddScoped<EmailTwoFactorService>();
builder.Services.AddTransient<Microsoft.AspNetCore.Identity.UI.Services.IEmailSender, IdentityEmailSender>();
builder.Services.AddScoped<RecoveryCodeService>();
builder.Services.AddScoped<WebAuthnService>();

// FIDO2 WebAuthn
var webAuthnOrigin = builder.Configuration.GetValue<string>("WebAuthn:Origin") ?? "https://localhost:5001";
var fido2Config = new Fido2Configuration
{
    ServerDomain = builder.Configuration["WebAuthn:ServerDomain"]
        ?? new Uri(webAuthnOrigin).Host,
    ServerName = "BoxWise",
    Origins = new HashSet<string>
    {
        webAuthnOrigin,
        // 开发环境同时允许两个 localhost 端口
        "https://localhost:5000",
        "https://localhost:5001"
    }
};
builder.Services.AddSingleton<IFido2>(new Fido2NetLib.Fido2(fido2Config));

// Session (WebAuthn 端点需要)
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(5);
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.Cookie.SameSite = SameSiteMode.None; // Blazor WASM 跨端口需要 None（与 auth cookie 一致）
});

// Rate Limiting
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    // 登录端点 - 按 IP
    options.AddFixedWindowLimiter(policyName: "login-per-ip", config =>
    {
        config.PermitLimit = builder.Configuration.GetValue("RateLimit:LoginPermitLimit", 5);
        config.Window = TimeSpan.FromMinutes(builder.Configuration.GetValue("RateLimit:LoginWindowMinutes", 15));
        config.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        config.QueueLimit = 0;
    });

    // Passkey 登录端点 - 按 IP（更宽松：不涉及密码验证，仅发放 challenge）
    options.AddFixedWindowLimiter(policyName: "passkey-login", config =>
    {
        config.PermitLimit = builder.Configuration.GetValue("RateLimit:PasskeyLoginPermitLimit", 30);
        config.Window = TimeSpan.FromMinutes(builder.Configuration.GetValue("RateLimit:PasskeyLoginWindowMinutes", 5));
        config.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        config.QueueLimit = 0;
    });

    // 登录端点 - 按账户（从已认证用户 ID 或请求体用户名提取分区键）
    options.AddPolicy<string>("login-per-account", httpContext =>
    {
        var config = httpContext.RequestServices.GetRequiredService<IConfiguration>();
        // 对于未认证的登录请求，尝试从请求体提取用户名以实现账户级限流
        // 注意：读取请求体可能影响性能，仅用于登录端点
        var partitionKey = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(partitionKey))
        {
            partitionKey = TryExtractUsernameFromBody(httpContext)
                ?? httpContext.Connection.RemoteIpAddress?.ToString()
                ?? "anon";
        }
        return RateLimitPartition.GetFixedWindowLimiter(partitionKey,
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = config.GetValue("RateLimit:LoginPermitLimit", 5),
                Window = TimeSpan.FromMinutes(config.GetValue("RateLimit:LoginWindowMinutes", 15)),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0
            });
    });

    // 2FA TOTP 验证 - 按账户
    options.AddPolicy<string>("2fa-totp", httpContext =>
    {
        var config = httpContext.RequestServices.GetRequiredService<IConfiguration>();
        var userId = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "anonymous";
        return RateLimitPartition.GetFixedWindowLimiter(userId,
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = config.GetValue("RateLimit:TwoFactorTotpPermitLimit", 3),
                Window = TimeSpan.FromSeconds(config.GetValue("RateLimit:TwoFactorTotpWindowSeconds", 30)),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0
            });
    });

    // 2FA 邮箱验证 - 按账户
    options.AddPolicy<string>("2fa-email", httpContext =>
    {
        var config = httpContext.RequestServices.GetRequiredService<IConfiguration>();
        var userId = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "anonymous";
        return RateLimitPartition.GetFixedWindowLimiter(userId,
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = config.GetValue("RateLimit:TwoFactorEmailPermitLimit", 3),
                Window = TimeSpan.FromMinutes(config.GetValue("RateLimit:TwoFactorEmailWindowMinutes", 5)),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0
            });
    });

    // 2FA 恢复码验证 - 按账户
    options.AddPolicy<string>("2fa-recovery", httpContext =>
    {
        var config = httpContext.RequestServices.GetRequiredService<IConfiguration>();
        var userId = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "anonymous";
        return RateLimitPartition.GetFixedWindowLimiter(userId,
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = config.GetValue("RateLimit:TwoFactorRecoveryPermitLimit", 5),
                Window = TimeSpan.FromMinutes(config.GetValue("RateLimit:TwoFactorRecoveryWindowMinutes", 15)),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0
            });
    });

    // 2FA modify 端点 - 按账户
    options.AddPolicy<string>("2fa-modify", httpContext =>
    {
        var config = httpContext.RequestServices.GetRequiredService<IConfiguration>();
        var userId = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
        {
            // 对于 AllowAnonymous 端点（challenge/send-challenge-code），
            // 用户仅持有 TwoFactorUserId Cookie，尝试从中提取用户标识
            try
            {
                var authResult = httpContext.AuthenticateAsync(
                    IdentityConstants.TwoFactorUserIdScheme).GetAwaiter().GetResult();
                if (authResult.Succeeded && authResult.Principal is not null)
                    userId = authResult.Principal.FindFirstValue(ClaimTypes.NameIdentifier);
            }
            catch
            {
                // Cookie 损坏或认证异常时，回退到 anonymous 速率限制
            }
        }
        userId ??= "anonymous";
        return RateLimitPartition.GetFixedWindowLimiter(userId,
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = config.GetValue("RateLimit:TwoFactorModifyPermitLimit", 3),
                Window = TimeSpan.FromMinutes(config.GetValue("RateLimit:TwoFactorModifyWindowMinutes", 5)),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0
            });
    });

    // 邮箱验证码发送 + 验证 - 按用户（每 60s 2 次，允许发送+验证各一次）
    options.AddPolicy<string>("email-verification", httpContext =>
    {
        var config = httpContext.RequestServices.GetRequiredService<IConfiguration>();
        var userId = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "anonymous";
        return RateLimitPartition.GetFixedWindowLimiter(userId,
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = config.GetValue("RateLimit:EmailVerificationPermitLimit", 3),
                Window = TimeSpan.FromSeconds(config.GetValue("RateLimit:EmailVerificationWindowSeconds", 300)),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0
            });
    });
});

builder.Services.AddScoped<CsrfValidationFilter>();

builder.Services.AddRazorPages();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseCors("Dev");
}

// 数据库迁移 + 管理员种子数据（所有环境）
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();

    var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
    var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
    var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();

    // 确保 Admin 角色存在
    if (!await roleManager.RoleExistsAsync("Admin"))
    {
        await roleManager.CreateAsync(new IdentityRole("Admin"));
    }

    // 仅当配置了 Admin__Password 环境变量时才创建/更新管理员账户
    var adminPassword = config["Admin:Password"];
    if (!string.IsNullOrWhiteSpace(adminPassword))
    {
        var adminUsername = config["Admin:Username"] ?? "admin";
        var adminEmail = config["Admin:Email"] ?? "admin@boxwise.local";
        var adminUser = await userManager.FindByNameAsync(adminUsername);

        if (adminUser is null)
        {
            adminUser = new AppUser { UserName = adminUsername, Email = adminEmail };
            // 管理员种子账户使用手动密码哈希，而非 CreateAsync(user, password)：
            // 种子密码来自管理员配置（可信来源），不受面向终端用户的密码验证器
            // （NoNumericOnlyValidator、CommonPasswordValidator）限制——否则强密码如
            // "bd7f2a3c1e" 可能被拒绝。这是有意的设计决策。
            adminUser.PasswordHash = userManager.PasswordHasher.HashPassword(adminUser, adminPassword);
            try
            {
                var result = await userManager.CreateAsync(adminUser);
                if (!result.Succeeded)
                {
                    var errors = string.Join(", ", result.Errors.Select(e => e.Description));
                    app.Logger.LogWarning("Failed to create admin user: {Errors}", errors);
                    adminUser = null;
                }
                else
                {
                    app.Logger.LogInformation("Admin user '{Username}' created", adminUsername);
                }
            }
            catch (DbUpdateException)
            {
                app.Logger.LogInformation("Admin user '{Username}' already created by another instance", adminUsername);
                adminUser = await userManager.FindByNameAsync(adminUsername);
            }
        }
        else
        {
            // 密码变更：仅当哈希不同时才重置
            var passwordChanged = userManager.PasswordHasher.VerifyHashedPassword(
                adminUser, adminUser.PasswordHash ?? "", adminPassword)
                != PasswordVerificationResult.Success;
            if (passwordChanged)
            {
                try
                {
                    // 管理员密码更新同样使用手动密码哈希，
                    // 与创建路径保持一致——种子密码为特权操作，不受验证器限制。
                    adminUser.PasswordHash = userManager.PasswordHasher.HashPassword(adminUser, adminPassword);
                    var updateResult = await userManager.UpdateAsync(adminUser);
                    if (updateResult.Succeeded)
                        app.Logger.LogInformation("Admin password updated for '{Username}'", adminUsername);
                    else
                    {
                        var errors = string.Join(", ", updateResult.Errors.Select(e => e.Description));
                        app.Logger.LogWarning("Failed to update admin password: {Errors}", errors);
                    }
                }
                catch (Exception ex)
                {
                    app.Logger.LogError(ex, "Failed to update admin password for '{Username}'", adminUsername);
                }
            }
        }

        // 分配 Admin 角色
        if (adminUser is not null
            && !await userManager.IsInRoleAsync(adminUser, "Admin"))
        {
            try
            {
                await userManager.AddToRoleAsync(adminUser, "Admin");
                app.Logger.LogInformation("Admin role assigned to '{Username}'", adminUsername);
            }
            catch (DbUpdateException)
            {
                app.Logger.LogInformation("Admin role already assigned to '{Username}' by another instance", adminUsername);
            }
            catch (Exception ex)
            {
                app.Logger.LogWarning(ex, "Failed to assign Admin role to '{Username}'", adminUsername);
            }
        }
    }
    else
    {
        var hasAdmin = await userManager.GetUsersInRoleAsync("Admin");
        if (hasAdmin.Count == 0)
        {
            app.Logger.LogWarning(
                "No admin account found and Admin:Password not configured. "
                + "Set the Admin__Password environment variable to create the admin account.");
        }
    }

}

app.UseHttpsRedirection();
app.MapStaticAssets().AllowAnonymous();

app.UseRateLimiter();

app.UseSession();

app.UseAuthentication();
app.UseAuthorization();

app.MapAuthEndpoints();
app.MapLocationEndpoints();
app.MapImageEndpoints();
app.MapItemEndpoints();
app.MapTagEndpoints();
app.MapAiEndpoints();
app.MapTwoFactorEndpoints();
app.MapTwoFactorModifyEndpoints();
app.MapEmailVerificationEndpoints();
app.MapWebAuthnEndpoints();
app.MapQrCodeEndpoints();
app.MapAdminTwoFactorEndpoints();
app.MapRazorPages(); // 必须在 MapFallbackToFile 之前，否则 /admin 被 SPA 拦截

app.MapFallbackToFile("index.html").AllowAnonymous();

// CLI: dotnet run -- admin reset-2fa --user <username>
if (args.Length >= 3 && args[0] == "admin" && args[1] == "reset-2fa")
{
    var userIndex = Array.IndexOf(args, "--user");
    if (userIndex >= 0 && userIndex + 1 < args.Length)
    {
        var username = args[userIndex + 1];
        await ResetTwoFactorCli(app, username);
        return; // 退出进程，不启动 Web 服务器
    }
    else
    {
        Console.Error.WriteLine("Usage: dotnet run -- admin reset-2fa --user <username>");
        return;
    }
}

app.Run();

static string? TryExtractUsernameFromBody(HttpContext httpContext)
{
    try
    {
        if (!httpContext.Request.HasJsonContentType())
            return null;

        httpContext.Request.EnableBuffering();
        using var reader = new StreamReader(httpContext.Request.Body, Encoding.UTF8, leaveOpen: true);
        var body = reader.ReadToEnd();
        httpContext.Request.Body.Position = 0;

        using var doc = JsonDocument.Parse(body);
        if (doc.RootElement.TryGetProperty("username", out var prop))
            return prop.GetString();
    }
    catch
    {
        // Body reading failed — fall back to IP-based partitioning
    }
    return null;
}

static async Task ResetTwoFactorCli(WebApplication app, string username)
{
    using var scope = app.Services.CreateScope();
    var services = scope.ServiceProvider;

    try
    {
        var userManager = services.GetRequiredService<UserManager<AppUser>>();
        var db = services.GetRequiredService<AppDbContext>();
        var logger = services.GetRequiredService<ILogger<Program>>();

        var user = await userManager.FindByNameAsync(username);
        if (user is null)
        {
            Console.Error.WriteLine($"Error: User '{username}' not found.");
            Environment.Exit(1);
            return;
        }

        // 清除 2FA 设置
        user.ConfiguredMethods = TwoFactorMethod.None;
        user.TotpSecretKey = null;
        user.TwoFactorEnabled = false;
        user.TwoFactorSetupCompletedAt = null;
        user.TwoFactorGracePeriodUntil = null;
        user.EmailForTwoFactor = null;

        // 删除恢复码
        var recoveryCodes = db.RecoveryCodes.Where(rc => rc.UserId == user.Id);
        db.RecoveryCodes.RemoveRange(recoveryCodes);

        // 删除 WebAuthn 凭证
        var webAuthnCreds = db.WebAuthnCredentials.Where(wc => wc.UserId == user.Id);
        db.WebAuthnCredentials.RemoveRange(webAuthnCreds);

        await userManager.UpdateAsync(user);

        // 审计日志
        logger.LogWarning("CLI 2FA reset by {Operator} for user {Username} (Id={UserId}) at {Timestamp}",
            Environment.UserName, username, user.Id, DateTime.UtcNow);
        var result = System.Text.Json.JsonSerializer.Serialize(new { success = true, username });
        Console.WriteLine(result);
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Error: {ex.Message}");
        Environment.Exit(1);
    }
}
