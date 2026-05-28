using System.Security.Claims;
using System.Threading.RateLimiting;
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
    options.Events.OnRedirectToLogin = ctx =>
    {
        ctx.Response.StatusCode = 401;
        return Task.CompletedTask;
    };
    options.Events.OnRedirectToAccessDenied = ctx =>
    {
        ctx.Response.StatusCode = 403;
        return Task.CompletedTask;
    };
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
builder.Services.AddScoped<TwoFactorService>();
builder.Services.AddScoped<EmailTwoFactorService>();
builder.Services.AddScoped<RecoveryCodeService>();
builder.Services.AddScoped<WebAuthnService>();

// FIDO2 WebAuthn
var fido2Config = new Fido2Configuration
{
    ServerDomain = builder.Configuration["WebAuthn:ServerDomain"]
        ?? new Uri(builder.Configuration.GetValue<string>("WebAuthn:Origin") ?? "https://localhost:5001").Host,
    ServerName = "BoxWise",
    Origins = new HashSet<string>
    {
        builder.Configuration.GetValue<string>("WebAuthn:Origin") ?? "https://localhost:5001"
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
    options.Cookie.SameSite = SameSiteMode.Strict;
});

// Rate Limiting
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    // 登录端点 - 按 IP
    options.AddFixedWindowLimiter(policyName: "login-per-ip", config =>
    {
        config.PermitLimit = builder.Configuration.GetValue<int>("RateLimit:LoginPermitLimit");
        config.Window = TimeSpan.FromMinutes(builder.Configuration.GetValue<int>("RateLimit:LoginWindowMinutes"));
        config.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        config.QueueLimit = 0;
    });

    // 登录端点 - 按账户（从已认证用户 ID 提取分区键）
    options.AddPolicy<string>("login-per-account", httpContext =>
    {
        var userId = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "unknown";
        var config = httpContext.RequestServices.GetRequiredService<IConfiguration>();
        return RateLimitPartition.GetFixedWindowLimiter(userId,
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = config.GetValue<int>("RateLimit:LoginPermitLimit"),
                Window = TimeSpan.FromMinutes(config.GetValue<int>("RateLimit:LoginWindowMinutes")),
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
                PermitLimit = config.GetValue<int>("RateLimit:TwoFactorTotpPermitLimit"),
                Window = TimeSpan.FromSeconds(config.GetValue<int>("RateLimit:TwoFactorTotpWindowSeconds")),
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
                PermitLimit = config.GetValue<int>("RateLimit:TwoFactorEmailPermitLimit"),
                Window = TimeSpan.FromMinutes(config.GetValue<int>("RateLimit:TwoFactorEmailWindowMinutes")),
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
                PermitLimit = config.GetValue<int>("RateLimit:TwoFactorRecoveryPermitLimit"),
                Window = TimeSpan.FromMinutes(config.GetValue<int>("RateLimit:TwoFactorRecoveryWindowMinutes")),
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
        var adminUser = await userManager.FindByNameAsync(adminUsername);

        if (adminUser is null)
        {
            adminUser = new AppUser { UserName = adminUsername };
            var result = await userManager.CreateAsync(adminUser, adminPassword);
            if (!result.Succeeded)
            {
                var errors = string.Join(", ", result.Errors.Select(e => e.Description));
                app.Logger.LogWarning("Failed to create admin user: {Errors}", errors);
                adminUser = null; // 未持久化，跳过后续角色分配
            }
            else
            {
                app.Logger.LogInformation("Admin user '{Username}' created", adminUsername);
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
                var token = await userManager.GeneratePasswordResetTokenAsync(adminUser);
                var resetResult = await userManager.ResetPasswordAsync(adminUser, token, adminPassword);
                if (resetResult.Succeeded)
                    app.Logger.LogInformation("Admin password updated for '{Username}'", adminUsername);
                else
                {
                    var errors = string.Join(", ", resetResult.Errors.Select(e => e.Description));
                    app.Logger.LogWarning("Failed to reset admin password: {Errors}", errors);
                }
            }
        }

        // 分配 Admin 角色
        if (adminUser is not null
            && !await userManager.IsInRoleAsync(adminUser, "Admin"))
        {
            await userManager.AddToRoleAsync(adminUser, "Admin");
            app.Logger.LogInformation("Admin role assigned to '{Username}'", adminUsername);
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
        user.TwoFactorMethod = TwoFactorMethod.None;
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
        await db.SaveChangesAsync();

        // 审计日志
        logger.LogWarning("CLI 2FA reset for user {Username} at {Timestamp}", username, DateTime.UtcNow);
        Console.WriteLine($"2FA successfully reset for user '{username}'.");
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Error: {ex.Message}");
        Environment.Exit(1);
    }
}
