using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using BoxWise.Server.Data;
using BoxWise.Server.Models;

namespace BoxWise.Server.Tests;

public static class TestIdentityFactory
{
    public static async Task<TestIdentityContext> CreateAsync()
    {
        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(options =>
            options.UseInMemoryDatabase(Guid.NewGuid().ToString()));
        return await BuildAsync(services, createSchema: false);
    }

    /// <summary>
    /// 在真实 SQLite 数据库上构建 Identity 服务。事务与回滚语义只有该 provider 才能验证。
    /// </summary>
    public static async Task<TestIdentityContext> CreateAsync(
        SqliteTestDatabase database, params IInterceptor[] interceptors)
    {
        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(options =>
        {
            options.UseSqlite(database.ConnectionString);
            if (interceptors.Length > 0)
                options.AddInterceptors(interceptors);
        });
        return await BuildAsync(services, createSchema: true);
    }

    private static async Task<TestIdentityContext> BuildAsync(
        ServiceCollection services, bool createSchema)
    {
        services.AddHttpContextAccessor();

        services.AddIdentity<AppUser, IdentityRole>(options =>
        {
            options.Password.RequireDigit = false;
            options.Password.RequireLowercase = false;
            options.Password.RequireNonAlphanumeric = false;
            options.Password.RequireUppercase = false;
            options.Password.RequiredLength = 4;
        })
        .AddEntityFrameworkStores<AppDbContext>()
        .AddDefaultTokenProviders();

        services.ConfigureApplicationCookie(options =>
        {
            options.Events.OnRedirectToLogin = context =>
            {
                context.Response.StatusCode = 401;
                return Task.CompletedTask;
            };
            options.Events.OnRedirectToAccessDenied = context =>
            {
                context.Response.StatusCode = 401;
                return Task.CompletedTask;
            };
        });

        services.AddDataProtection();
        services.AddLogging(b => b.AddConsole());

        var provider = services.BuildServiceProvider();

        var httpContextAccessor = provider.GetRequiredService<IHttpContextAccessor>();
        var responseBody = new MemoryStream();
        var defaultHttpContext = new DefaultHttpContext
        {
            RequestServices = provider,
            Response = { Body = responseBody }
        };
        httpContextAccessor.HttpContext = defaultHttpContext;

        var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if (createSchema)
            await db.Database.EnsureCreatedAsync();

        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        var signInManager = scope.ServiceProvider.GetRequiredService<SignInManager<AppUser>>();
        signInManager.Context = defaultHttpContext;

        if (!await roleManager.RoleExistsAsync("Admin"))
            await roleManager.CreateAsync(new IdentityRole("Admin"));

        return new TestIdentityContext(provider, scope, userManager, roleManager, signInManager, responseBody);
    }
}

public class TestIdentityContext : IAsyncDisposable
{
    private readonly MemoryStream? _responseBody;

    public ServiceProvider Provider { get; }
    public IServiceScope Scope { get; }
    public UserManager<AppUser> UserManager { get; }
    public RoleManager<IdentityRole> RoleManager { get; }
    public SignInManager<AppUser> SignInManager { get; }

    public TestIdentityContext(
        ServiceProvider provider,
        IServiceScope scope,
        UserManager<AppUser> userManager,
        RoleManager<IdentityRole> roleManager,
        SignInManager<AppUser> signInManager,
        MemoryStream? responseBody = null)
    {
        Provider = provider;
        Scope = scope;
        UserManager = userManager;
        RoleManager = roleManager;
        SignInManager = signInManager;
        _responseBody = responseBody;
    }

    public AppDbContext Db => Scope.ServiceProvider.GetRequiredService<AppDbContext>();

    public async ValueTask DisposeAsync()
    {
        _responseBody?.Dispose();
        Scope.Dispose();
        await Provider.DisposeAsync();
    }
}
