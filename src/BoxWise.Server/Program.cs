using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using BoxWise.Server.Data;
using BoxWise.Server.Endpoints;
using BoxWise.Server.Models;
using BoxWise.Server.Configuration;
using BoxWise.Server.Repositories;
using BoxWise.Server.Services;
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
builder.Services.AddScoped<TwoFactorService>();

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

app.UseAuthentication();
app.UseAuthorization();

app.MapAuthEndpoints();
app.MapLocationEndpoints();
app.MapImageEndpoints();
app.MapItemEndpoints();
app.MapTagEndpoints();
app.MapAiEndpoints();
app.MapRazorPages(); // 必须在 MapFallbackToFile 之前，否则 /admin 被 SPA 拦截

app.MapFallbackToFile("index.html").AllowAnonymous();

app.Run();
