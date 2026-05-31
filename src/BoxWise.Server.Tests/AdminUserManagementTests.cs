using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using BoxWise.Server.Models;
using BoxWise.Server.Pages.Admin;

namespace BoxWise.Server.Tests;

public class AdminUserManagementTests
{
    private static readonly IConfiguration Configuration = new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>())
        .Build();
    [Fact]
    public async Task EditAccount_Rename_Succeeds()
    {
        await using var ctx = await TestIdentityFactory.CreateAsync();
        var admin = await CreateAdminAsync(ctx, "admin", "pass1234");
        var target = await CreateUserAsync(ctx, "oldname", "pass1234");

        var model = CreateEditAccountModel(ctx.UserManager, admin);
        model.Username = "newname";
        model.Email = "newname@example.com";
        var result = await model.OnPostAsync(target.Id);

        Assert.IsType<RedirectToPageResult>(result);

        var updated = await ctx.UserManager.FindByIdAsync(target.Id);
        Assert.NotNull(updated);
        Assert.Equal("newname", updated!.UserName);
        Assert.Equal("NEWNAME", updated.NormalizedUserName);
        Assert.Equal("newname@example.com", updated.Email);
        Assert.Equal("newname@example.com", updated.EmailForTwoFactor);
    }

    [Fact]
    public async Task EditAccount_ChangeEmail_Succeeds()
    {
        await using var ctx = await TestIdentityFactory.CreateAsync();
        var admin = await CreateAdminAsync(ctx, "admin", "pass1234");
        var target = await CreateUserWithEmailAsync(ctx, "testuser", "pass1234", "old@example.com");

        var model = CreateEditAccountModel(ctx.UserManager, admin);
        model.Username = "testuser";
        model.Email = "new@example.com";
        var result = await model.OnPostAsync(target.Id);

        Assert.IsType<RedirectToPageResult>(result);

        var updated = await ctx.UserManager.FindByIdAsync(target.Id);
        Assert.NotNull(updated);
        Assert.Equal("new@example.com", updated!.Email);
        Assert.Equal("new@example.com", updated.EmailForTwoFactor);
    }

    [Fact]
    public async Task EditAccount_RenameOnly_EmailUnchanged()
    {
        await using var ctx = await TestIdentityFactory.CreateAsync();
        var admin = await CreateAdminAsync(ctx, "admin", "pass1234");
        var target = await CreateUserWithEmailAsync(ctx, "oldname", "pass1234", "keep@example.com");

        var model = CreateEditAccountModel(ctx.UserManager, admin);
        model.Username = "newname";
        model.Email = "keep@example.com"; // unchanged
        var result = await model.OnPostAsync(target.Id);

        Assert.IsType<RedirectToPageResult>(result);

        var updated = await ctx.UserManager.FindByIdAsync(target.Id);
        Assert.NotNull(updated);
        Assert.Equal("newname", updated!.UserName);
        Assert.Equal("keep@example.com", updated.Email);
        Assert.Equal("keep@example.com", updated.EmailForTwoFactor);
    }

    [Fact]
    public async Task EditAccount_EmptyEmail_ReturnsError()
    {
        await using var ctx = await TestIdentityFactory.CreateAsync();
        var admin = await CreateAdminAsync(ctx, "admin", "pass1234");
        var target = await CreateUserWithEmailAsync(ctx, "testuser", "pass1234", "old@example.com");

        var model = CreateEditAccountModel(ctx.UserManager, admin);
        model.Username = "testuser";
        model.Email = "";
        var result = await model.OnPostAsync(target.Id);

        Assert.IsType<PageResult>(result);
        Assert.NotNull(model.ErrorMessage);
        Assert.Contains("邮箱", model.ErrorMessage);
    }

    [Fact]
    public async Task EditAccount_DuplicateEmail_ReturnsError()
    {
        await using var ctx = await TestIdentityFactory.CreateAsync();
        var admin = await CreateAdminAsync(ctx, "admin", "pass1234");
        var target = await CreateUserWithEmailAsync(ctx, "testuser", "pass1234", "old@example.com");
        await CreateUserWithEmailAsync(ctx, "other", "pass1234", "dup@example.com");

        var model = CreateEditAccountModel(ctx.UserManager, admin);
        model.Username = "testuser";
        model.Email = "dup@example.com";
        var result = await model.OnPostAsync(target.Id);

        Assert.IsType<PageResult>(result);
        Assert.NotNull(model.ErrorMessage);
        Assert.Contains("邮箱", model.ErrorMessage);
    }

    [Fact]
    public async Task EditAccount_EmptyUsername_ReturnsError()
    {
        await using var ctx = await TestIdentityFactory.CreateAsync();
        var admin = await CreateAdminAsync(ctx, "admin", "pass1234");
        var target = await CreateUserAsync(ctx, "oldname", "pass1234");

        var model = CreateEditAccountModel(ctx.UserManager, admin);
        model.Username = "";
        model.Email = "oldname@example.com";
        var result = await model.OnPostAsync(target.Id);

        Assert.IsType<PageResult>(result);
        Assert.NotNull(model.ErrorMessage);
    }

    [Fact]
    public async Task DeleteUser_ValidUser_Succeeds()
    {
        await using var ctx = await TestIdentityFactory.CreateAsync();
        var admin = await CreateAdminAsync(ctx, "admin", "pass1234");
        var target = await CreateUserAsync(ctx, "victim", "pass1234");

        var model = CreateIndexModel(ctx.UserManager, admin);
        var result = await model.OnPostDeleteAsync(target.Id);

        Assert.IsType<RedirectToPageResult>(result);
        Assert.Contains("已删除", model.StatusMessage);

        var deleted = await ctx.UserManager.FindByIdAsync(target.Id);
        Assert.Null(deleted);
    }

    [Fact]
    public async Task DeleteUser_SelfDelete_Refused()
    {
        await using var ctx = await TestIdentityFactory.CreateAsync();
        var admin = await CreateAdminAsync(ctx, "admin", "pass1234");

        var model = CreateIndexModel(ctx.UserManager, admin);
        var result = await model.OnPostDeleteAsync(admin.Id);

        Assert.IsType<RedirectToPageResult>(result);
        Assert.Contains("不能删除", model.StatusMessage);

        var stillExists = await ctx.UserManager.FindByIdAsync(admin.Id);
        Assert.NotNull(stillExists);
    }

    [Fact]
    public async Task ToggleRole_AddAndRemove_Succeeds()
    {
        await using var ctx = await TestIdentityFactory.CreateAsync();
        var admin = await CreateAdminAsync(ctx, "admin", "pass1234");
        var target = await CreateUserAsync(ctx, "member", "pass1234");

        var model = CreateIndexModel(ctx.UserManager, admin);

        // Promote to admin
        await model.OnPostToggleRoleAsync(target.Id);
        Assert.True(await ctx.UserManager.IsInRoleAsync(target, "Admin"));
        Assert.Contains("设为管理员", model.StatusMessage);

        // Demote
        await model.OnPostToggleRoleAsync(target.Id);
        Assert.False(await ctx.UserManager.IsInRoleAsync(target, "Admin"));
        Assert.Contains("取消", model.StatusMessage);
    }

    [Fact]
    public async Task ToggleRole_SelfChange_Refused()
    {
        await using var ctx = await TestIdentityFactory.CreateAsync();
        var admin = await CreateAdminAsync(ctx, "admin", "pass1234");

        var model = CreateIndexModel(ctx.UserManager, admin);
        var result = await model.OnPostToggleRoleAsync(admin.Id);

        Assert.IsType<RedirectToPageResult>(result);
        Assert.Contains("不能修改", model.StatusMessage);
    }

    [Fact]
    public async Task AdminChangePassword_ValidPassword_Succeeds()
    {
        await using var ctx = await TestIdentityFactory.CreateAsync();
        var admin = await CreateAdminAsync(ctx, "admin", "pass1234");
        var target = await CreateUserAsync(ctx, "user1", "oldpass1");

        var model = CreateChangePasswordModel(ctx.UserManager, admin);
        model.NewPassword = "newpass2";
        var result = await model.OnPostAsync(target.Id);

        Assert.IsType<RedirectToPageResult>(result);

        var updated = await ctx.UserManager.FindByIdAsync(target.Id);
        Assert.NotNull(updated);
        var check = await ctx.UserManager.CheckPasswordAsync(updated!, "newpass2");
        Assert.True(check);
    }

    [Fact]
    public async Task AdminChangePassword_EmptyPassword_ReturnsError()
    {
        await using var ctx = await TestIdentityFactory.CreateAsync();
        var admin = await CreateAdminAsync(ctx, "admin", "pass1234");
        var target = await CreateUserAsync(ctx, "user1", "oldpass1");

        var model = CreateChangePasswordModel(ctx.UserManager, admin);
        model.NewPassword = "";
        var result = await model.OnPostAsync(target.Id);

        Assert.IsType<PageResult>(result);
        Assert.NotNull(model.ErrorMessage);
    }

    // ──────── CreateAccountModel ────────

    [Fact]
    public async Task CreateAccount_Success_Redirects()
    {
        await using var ctx = await TestIdentityFactory.CreateAsync();
        var admin = await CreateAdminAsync(ctx, "admin", "pass1234");
        var model = CreateCreateAccountModel(ctx.UserManager, admin);
        model.Input = new() { Username = "newuser", Password = "pass1234", Email = "newuser@example.com" };

        var result = await model.OnPostAsync();

        Assert.IsType<RedirectToPageResult>(result);
        var created = await ctx.UserManager.FindByNameAsync("newuser");
        Assert.NotNull(created);
        Assert.Equal("newuser@example.com", created!.Email);
        Assert.Equal("newuser@example.com", created.EmailForTwoFactor);
    }

    [Fact]
    public async Task CreateAccount_EmptyEmail_ReturnsError()
    {
        await using var ctx = await TestIdentityFactory.CreateAsync();
        var admin = await CreateAdminAsync(ctx, "admin", "pass1234");
        var model = CreateCreateAccountModel(ctx.UserManager, admin);
        model.Input = new() { Username = "newuser", Password = "pass1234", Email = "" };

        var result = await model.OnPostAsync();

        Assert.IsType<PageResult>(result);
        Assert.NotNull(model.ErrorMessage);
        Assert.Contains("邮箱", model.ErrorMessage);
    }

    [Fact]
    public async Task CreateAccount_InvalidEmail_ReturnsError()
    {
        await using var ctx = await TestIdentityFactory.CreateAsync();
        var admin = await CreateAdminAsync(ctx, "admin", "pass1234");
        var model = CreateCreateAccountModel(ctx.UserManager, admin);
        model.Input = new() { Username = "newuser", Password = "pass1234", Email = "not-an-email" };

        var result = await model.OnPostAsync();

        Assert.IsType<PageResult>(result);
        Assert.NotNull(model.ErrorMessage);
        Assert.Contains("邮箱", model.ErrorMessage);
    }

    [Fact]
    public async Task CreateAccount_DuplicateEmail_ReturnsError()
    {
        await using var ctx = await TestIdentityFactory.CreateAsync();
        var admin = await CreateAdminAsync(ctx, "admin", "pass1234");
        await CreateUserWithEmailAsync(ctx, "existing", "pass1234", "dup@example.com");
        var model = CreateCreateAccountModel(ctx.UserManager, admin);
        model.Input = new() { Username = "newuser", Password = "pass1234", Email = "dup@example.com" };

        var result = await model.OnPostAsync();

        Assert.IsType<PageResult>(result);
        Assert.NotNull(model.ErrorMessage);
        Assert.Contains("邮箱", model.ErrorMessage);
    }

    [Fact]
    public async Task CreateAccount_EmptyUsername_ReturnsError()
    {
        await using var ctx = await TestIdentityFactory.CreateAsync();
        var admin = await CreateAdminAsync(ctx, "admin", "pass1234");
        var model = CreateCreateAccountModel(ctx.UserManager, admin);
        model.Input = new() { Username = "", Password = "pass1234", Email = "test@example.com" };

        var result = await model.OnPostAsync();

        Assert.IsType<PageResult>(result);
        Assert.NotNull(model.ErrorMessage);
    }

    [Fact]
    public async Task CreateAccount_WeakPassword_ReturnsError()
    {
        await using var ctx = await TestIdentityFactory.CreateAsync();
        var admin = await CreateAdminAsync(ctx, "admin", "pass1234");
        var model = CreateCreateAccountModel(ctx.UserManager, admin);
        model.Input = new() { Username = "testuser", Password = "ab", Email = "test@example.com" };

        var result = await model.OnPostAsync();

        Assert.IsType<PageResult>(result);
        Assert.NotNull(model.ErrorMessage);
    }

    [Fact]
    public async Task CreateAccount_DuplicateUsername_ReturnsError()
    {
        await using var ctx = await TestIdentityFactory.CreateAsync();
        var admin = await CreateAdminAsync(ctx, "admin", "pass1234");
        await CreateUserAsync(ctx, "existing", "pass1234");
        var model = CreateCreateAccountModel(ctx.UserManager, admin);
        model.Input = new() { Username = "existing", Password = "pass1234", Email = "test@example.com" };

        var result = await model.OnPostAsync();

        Assert.IsType<PageResult>(result);
        Assert.NotNull(model.ErrorMessage);
    }

    // ──────── OnGetAsync 补完 ────────

    [Fact]
    public async Task EditAccount_OnGet_LoadsUser()
    {
        await using var ctx = await TestIdentityFactory.CreateAsync();
        var admin = await CreateAdminAsync(ctx, "admin", "pass1234");
        var target = await CreateUserAsync(ctx, "target", "pass1234");
        var model = CreateEditAccountModel(ctx.UserManager, admin);

        var result = await model.OnGetAsync(target.Id);

        Assert.IsType<PageResult>(result);
        Assert.Equal("target", model.Username);
    }

    [Fact]
    public async Task Index_OnGet_LoadsUsers()
    {
        await using var ctx = await TestIdentityFactory.CreateAsync();
        var admin = await CreateAdminAsync(ctx, "admin", "pass1234");
        await CreateUserAsync(ctx, "user1", "pass1234");
        var model = CreateIndexModel(ctx.UserManager, admin);

        await model.OnGetAsync();

        // OnGetAsync 调用 LoadUsersAsync()，不应抛异常
        Assert.NotEmpty(model.Users);
    }

    [Fact]
    public async Task ChangePassword_OnGet_LoadsUser()
    {
        await using var ctx = await TestIdentityFactory.CreateAsync();
        var admin = await CreateAdminAsync(ctx, "admin", "pass1234");
        var target = await CreateUserAsync(ctx, "target", "pass1234");
        var model = CreateChangePasswordModel(ctx.UserManager, admin);

        var result = await model.OnGetAsync(target.Id);

        Assert.IsType<PageResult>(result);
        Assert.Equal("target", model.TargetUsername);
    }

    private static async Task<AppUser> CreateUserAsync(TestIdentityContext ctx, string name, string password)
    {
        var user = new AppUser { UserName = name };
        await ctx.UserManager.CreateAsync(user, password);
        return user;
    }

    private static async Task<AppUser> CreateUserWithEmailAsync(TestIdentityContext ctx, string name, string password, string email)
    {
        var user = new AppUser { UserName = name, Email = email };
        await ctx.UserManager.CreateAsync(user, password);
        return user;
    }

    private static async Task<AppUser> CreateAdminAsync(TestIdentityContext ctx, string name, string password)
    {
        var admin = await CreateUserAsync(ctx, name, password);
        await ctx.UserManager.AddToRoleAsync(admin, "Admin");
        return admin;
    }

    private static EditAccountModel CreateEditAccountModel(UserManager<AppUser> userManager, AppUser currentUser)
    {
        var logger = NullLogger<EditAccountModel>.Instance;
        var model = new EditAccountModel(userManager, logger);
        SetupPageContext(model, currentUser);
        return model;
    }

    private static IndexModel CreateIndexModel(UserManager<AppUser> userManager, AppUser currentUser)
    {
        var logger = NullLogger<IndexModel>.Instance;
        var model = new IndexModel(userManager, logger, Configuration);
        SetupPageContext(model, currentUser);
        return model;
    }

    private static ChangeUserPasswordModel CreateChangePasswordModel(UserManager<AppUser> userManager, AppUser currentUser)
    {
        var logger = NullLogger<ChangeUserPasswordModel>.Instance;
        var model = new ChangeUserPasswordModel(userManager, logger, Configuration);
        SetupPageContext(model, currentUser);
        return model;
    }

    private static CreateAccountModel CreateCreateAccountModel(UserManager<AppUser> userManager, AppUser currentUser)
    {
        var logger = NullLogger<CreateAccountModel>.Instance;
        var model = new CreateAccountModel(userManager, logger);
        SetupPageContext(model, currentUser);
        return model;
    }

    private static void SetupPageContext(PageModel model, AppUser currentUser)
    {
        var identity = new System.Security.Claims.ClaimsIdentity(
            IdentityConstants.ApplicationScheme);
        identity.AddClaim(new System.Security.Claims.Claim(
            System.Security.Claims.ClaimTypes.NameIdentifier, currentUser.Id));
        identity.AddClaim(new System.Security.Claims.Claim(
            System.Security.Claims.ClaimTypes.Name, currentUser.UserName ?? ""));

        var httpContext = new DefaultHttpContext
        {
            User = new System.Security.Claims.ClaimsPrincipal(identity)
        };

        httpContext.Features.Set<IHttpRequestFeature>(new HttpRequestFeature());
        httpContext.Features.Set<IHttpResponseFeature>(new HttpResponseFeature());

        var modelState = new ModelStateDictionary();
        var tempData = new TempDataDictionary(httpContext, new NullTempDataProvider());
        var urlHelper = new UrlHelper(new ActionContext(
            httpContext, new Microsoft.AspNetCore.Routing.RouteData(), new PageActionDescriptor()));

        model.PageContext = new PageContext(new ActionContext(
            httpContext,
            new Microsoft.AspNetCore.Routing.RouteData(),
            new PageActionDescriptor(),
            modelState))
        {
            ViewData = new ViewDataDictionary(new EmptyModelMetadataProvider(), modelState)
        };

        model.TempData = tempData;
        model.Url = urlHelper;
    }
}

internal sealed class NullTempDataProvider : ITempDataProvider
{
    public IDictionary<string, object?> LoadTempData(HttpContext context) => new Dictionary<string, object?>();
    public void SaveTempData(HttpContext context, IDictionary<string, object?> values) { }
}
