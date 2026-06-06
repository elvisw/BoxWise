using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using System.Net;
using Microsoft.EntityFrameworkCore;
using BoxWise.Server.Data;
using BoxWise.Server.Endpoints;
using BoxWise.Server.Models;
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

var env = builder.Environment;

builder.Services.ConfigureApplicationCookie(options =>
{
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = GetSameSiteMode(env);
    options.Cookie.SecurePolicy = GetSecurePolicy(env);
    options.ExpireTimeSpan = TimeSpan.FromDays(30);
    options.SlidingExpiration = true;
    options.LoginPath = "/Identity/Account/Login";
    options.AccessDeniedPath = "/";
    options.Events.OnRedirectToLogin = ctx =>
    {
        if (ctx.Request.Path.StartsWithSegments("/api"))
        {
            ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
            ctx.Response.ContentType = "application/problem+json";
            return ctx.Response.WriteAsync(JsonSerializer.Serialize(new
            {
                type = "https://tools.ietf.org/html/rfc9110#section-15.5.2",
                title = "Unauthorized",
                status = 401,
                detail = "Authentication is required to access this resource."
            }));
        }
        ctx.Response.Redirect(ctx.RedirectUri);
        return Task.CompletedTask;
    };
    options.Events.OnRedirectToAccessDenied = ctx =>
    {
        if (ctx.Request.Path.StartsWithSegments("/api"))
        {
            ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
            ctx.Response.ContentType = "application/problem+json";
            return ctx.Response.WriteAsync(JsonSerializer.Serialize(new
            {
                type = "https://tools.ietf.org/html/rfc9110#section-15.5.4",
                title = "Forbidden",
                status = 403,
                detail = "You do not have permission to access this resource."
            }));
        }
        ctx.Response.Redirect(ctx.RedirectUri);
        return Task.CompletedTask;
    };
});

// TwoFactorUserId Cookie — 开发环境跨端口需要 SameSite=None
builder.Services.Configure<CookieAuthenticationOptions>(IdentityConstants.TwoFactorUserIdScheme, options =>
{
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = GetSameSiteMode(env);
    options.Cookie.SecurePolicy = GetSecurePolicy(env);
});

// TwoFactorRememberMe Cookie — 与其他 Cookie 保持一致的 SameSite/SecurePolicy
builder.Services.Configure<CookieAuthenticationOptions>(IdentityConstants.TwoFactorRememberMeScheme, options =>
{
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = GetSameSiteMode(env);
    options.Cookie.SecurePolicy = GetSecurePolicy(env);
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
// 使用 Path.GetFullPath 解析为绝对路径，防止工作目录变化导致密钥丢失。
// 注意：使用 IsNullOrWhiteSpace 而非 ?? ，因为空字符串/纯空白配置值也会绕过回退。
var dataDir = builder.Configuration["DataDirectory"];
if (string.IsNullOrWhiteSpace(dataDir)) dataDir = "data";
var dataProtectionKeysPath = Path.GetFullPath(Path.Combine(dataDir, "keys"));
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
builder.Services.AddSingleton<ImageStorageService>();
builder.Services.AddSingleton<ThumbnailService>();
builder.Services.AddSingleton<ThumbnailBackgroundService>();
builder.Services.AddHostedService<ThumbnailBackgroundService>(sp => sp.GetRequiredService<ThumbnailBackgroundService>());
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
    options.Cookie.SameSite = GetSameSiteMode(env); // 开发环境跨端口需要 None（与 auth cookie 一致）
    options.Cookie.SecurePolicy = GetSecurePolicy(env);
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

});

builder.Services.Configure<HostOptions>(options =>
    options.ShutdownTimeout = TimeSpan.FromSeconds(30));

builder.Services.AddScoped<CsrfValidationFilter>();

// Forwarded Headers — 生产环境 Caddy 反向代理需要正确的 Request.IsHttps
// 安全：仅信任本地回环 + Docker 默认桥接网络，而非任意代理
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownProxies.Add(IPAddress.Loopback);
    options.KnownProxies.Add(IPAddress.IPv6Loopback);
    // Docker 桥接子网（Caddy 反向代理在 compose 网络中；default bridge + compose project networks）
    options.KnownIPNetworks.Add(new System.Net.IPNetwork(IPAddress.Parse("172.17.0.0"), 16));
    options.KnownIPNetworks.Add(new System.Net.IPNetwork(IPAddress.Parse("172.18.0.0"), 16));
    options.KnownIPNetworks.Add(new System.Net.IPNetwork(IPAddress.Parse("172.19.0.0"), 16));
});

builder.Services.AddRazorPages();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseCors("Dev");
}
else
{
    app.UseForwardedHeaders();
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
                var roleResult = await userManager.AddToRoleAsync(adminUser, "Admin");
                if (roleResult.Succeeded)
                {
                    app.Logger.LogInformation("Admin role assigned to '{Username}'", adminUsername);
                }
                else
                {
                    var errorDetail = roleResult.Errors.Any()
                        ? string.Join("; ", roleResult.Errors.Select(e => e.Description))
                        : "no error details available";
                    app.Logger.LogWarning("Failed to assign Admin role to '{Username}': {Errors}",
                        adminUsername, errorDetail);
                }
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
app.MapWebAuthnEndpoints();
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

// 开发环境 HTTP + SameSite=None 检查：SameSite=None 要求 Secure 标志，
// 但 Secure Cookie 在 HTTP 连接上会被浏览器拒绝，导致应用完全不可用。
// 开发环境默认使用 HTTPS（launchSettings.json 中配置），此警告仅在手动改为 HTTP 时触发。
// 注意：解析分号分隔的多 URL 列表，仅在所有 URL 均为 HTTP（无 HTTPS）时才告警。
if (env.IsDevelopment())
{
    var configuredUrls = builder.Configuration.GetValue<string>("ASPNETCORE_URLS")
        ?? builder.Configuration.GetValue<string>("urls")
        ?? string.Empty;
    var urls = configuredUrls.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    var hasHttps = urls.Any(u => u.StartsWith("https://", StringComparison.OrdinalIgnoreCase));
    var hasHttp = urls.Any(u => u.StartsWith("http://", StringComparison.OrdinalIgnoreCase));
    if (hasHttp && !hasHttps)
    {
        app.Logger.LogWarning(
            "SameSite=None is configured (Development) but only HTTP URLs detected (no HTTPS). "
            + "Browsers require Secure (HTTPS) for SameSite=None cookies — "
            + "cookies will be silently rejected, and the app will be unusable. "
            + "Use HTTPS URLs (e.g., https://localhost:5000) instead.");
    }
}

app.Run();

static SameSiteMode GetSameSiteMode(IWebHostEnvironment env) =>
    env.IsDevelopment() ? SameSiteMode.None : SameSiteMode.Lax;

static CookieSecurePolicy GetSecurePolicy(IWebHostEnvironment env) =>
    env.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;

static string? TryExtractUsernameFromBody(HttpContext httpContext)
{
    try
    {
        if (!httpContext.Request.HasJsonContentType())
            return null;

        httpContext.Request.EnableBuffering();
        // 注意：速率限制分区解析器为同步委托，无法使用 ReadToEndAsync。
        // 登录请求体（~100 字节）极小，同步读取对线程池影响可忽略。
        // 限制最大读取 4096 字节，防止恶意大请求体导致每次限流检查时内存耗尽。
        using var reader = new StreamReader(httpContext.Request.Body, Encoding.UTF8, leaveOpen: true);
        var buffer = new char[4096];
        var charsRead = reader.ReadBlock(buffer, 0, buffer.Length);
        var body = new string(buffer, 0, charsRead);
        httpContext.Request.Body.Position = 0;

        using var doc = JsonDocument.Parse(body);
        if (doc.RootElement.TryGetProperty("username", out var prop))
            return prop.GetString();
    }
    catch (JsonException)
    {
        // 请求体不是有效 JSON — 回退到 IP 分区
    }
    catch (IOException)
    {
        // 请求体读取失败（连接中断等） — 回退到 IP 分区
    }
    catch (Exception ex)
    {
        // 其他未预期异常 — 记录日志以便诊断，然后回退到 IP 分区
        // 使用 GetService（非抛出）防止 DI 解析失败覆盖原始异常
        var logger = httpContext.RequestServices.GetService<ILogger<Program>>();
        logger?.LogWarning(ex, "Failed to extract username from login body for rate limiting");
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
