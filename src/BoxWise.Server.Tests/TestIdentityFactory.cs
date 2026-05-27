using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
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

        services.AddIdentityCore<AppUser>(options =>
        {
            options.Password.RequireDigit = false;
            options.Password.RequireLowercase = false;
            options.Password.RequireNonAlphanumeric = false;
            options.Password.RequireUppercase = false;
            options.Password.RequiredLength = 4;
        })
        .AddRoles<IdentityRole>()
        .AddEntityFrameworkStores<AppDbContext>()
        .AddDefaultTokenProviders();

        services.AddDataProtection();
        services.AddLogging(b => b.AddConsole());

        var provider = services.BuildServiceProvider();

        var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.EnsureCreatedAsync();

        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>();

        await roleManager.CreateAsync(new IdentityRole("Admin"));

        return new TestIdentityContext(provider, scope, userManager, roleManager);
    }
}

public class TestIdentityContext : IAsyncDisposable
{
    public ServiceProvider Provider { get; }
    public IServiceScope Scope { get; }
    public UserManager<AppUser> UserManager { get; }
    public RoleManager<IdentityRole> RoleManager { get; }

    public TestIdentityContext(
        ServiceProvider provider,
        IServiceScope scope,
        UserManager<AppUser> userManager,
        RoleManager<IdentityRole> roleManager)
    {
        Provider = provider;
        Scope = scope;
        UserManager = userManager;
        RoleManager = roleManager;
    }

    public async ValueTask DisposeAsync()
    {
        Scope.Dispose();
        await Provider.DisposeAsync();
    }
}
