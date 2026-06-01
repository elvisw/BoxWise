// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
#nullable disable

using System;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using BoxWise.Server.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;
namespace BoxWise.Server.Areas.Identity.Pages.Account
{
    [AllowAnonymous]
    public class LoginWithRecoveryCodeModel : PageModel
    {
        private readonly SignInManager<AppUser> _signInManager;
        private readonly UserManager<AppUser> _userManager;
        private readonly ILogger<LoginWithRecoveryCodeModel> _logger;

        public LoginWithRecoveryCodeModel(
            SignInManager<AppUser> signInManager,
            UserManager<AppUser> userManager,
            ILogger<LoginWithRecoveryCodeModel> logger)
        {
            _signInManager = signInManager;
            _userManager = userManager;
            _logger = logger;
        }

        /// <summary>
        ///     This API supports the ASP.NET Core Identity default UI infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        [BindProperty]
        public InputModel Input { get; set; }

        /// <summary>
        ///     This API supports the ASP.NET Core Identity default UI infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public string ReturnUrl { get; set; }

        /// <summary>
        ///     This API supports the ASP.NET Core Identity default UI infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public class InputModel
        {
            /// <summary>
            ///     This API supports the ASP.NET Core Identity default UI infrastructure and is not intended to be used
            ///     directly from your code. This API may change or be removed in future releases.
            /// </summary>
            [BindProperty]
            [Required]
            [DataType(DataType.Text)]
            [Display(Name = "Recovery Code")]
            public string RecoveryCode { get; set; }
        }

        public async Task<IActionResult> OnGetAsync(string returnUrl = null)
        {
            // Workaround for dotnet/aspnetcore#66929: GetTwoFactorAuthenticationUserAsync()
            // 在 .NET 10.0.8 中返回 null。手动从 TwoFactorUserId Cookie 提取
            // NameIdentifier claim，绕过有问题的 GetUserId(principal) → FindByIdAsync 路径。
            var authResult = await HttpContext.AuthenticateAsync(IdentityConstants.TwoFactorUserIdScheme);
            if (!authResult.Succeeded || authResult.Principal is null)
                throw new InvalidOperationException($"Unable to load two-factor authentication user.");

            var userId = authResult.Principal.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
                throw new InvalidOperationException($"Unable to load two-factor authentication user.");

            var user = await _userManager.FindByIdAsync(userId);
            if (user is null)
                throw new InvalidOperationException($"Unable to load two-factor authentication user.");

            ReturnUrl = returnUrl;

            return Page();
        }

        public async Task<IActionResult> OnPostAsync(string returnUrl = null)
        {
            if (!ModelState.IsValid)
            {
                return Page();
            }

            // Workaround for dotnet/aspnetcore#66929: GetTwoFactorAuthenticationUserAsync()
            // 在 .NET 10.0.8 中返回 null。手动从 TwoFactorUserId Cookie 提取
            // NameIdentifier claim，绕过有问题的 GetUserId(principal) → FindByIdAsync 路径。
            var authResult = await HttpContext.AuthenticateAsync(IdentityConstants.TwoFactorUserIdScheme);
            if (!authResult.Succeeded || authResult.Principal is null)
                throw new InvalidOperationException($"Unable to load two-factor authentication user.");

            var userIdFromCookie = authResult.Principal.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userIdFromCookie))
                throw new InvalidOperationException($"Unable to load two-factor authentication user.");

            var user = await _userManager.FindByIdAsync(userIdFromCookie);
            if (user is null)
                throw new InvalidOperationException($"Unable to load two-factor authentication user.");

            var recoveryCode = Input.RecoveryCode.Replace(" ", string.Empty);

            var result = await _signInManager.TwoFactorRecoveryCodeSignInAsync(recoveryCode);

            if (result.Succeeded)
            {
                _logger.LogInformation("User with ID '{UserId}' logged in with a recovery code.", user.Id);
                return LocalRedirect(returnUrl ?? Url.Content("~/"));
            }
            if (result.IsLockedOut)
            {
                _logger.LogWarning("User account locked out.");
                return RedirectToPage("./Lockout");
            }
            else
            {
                _logger.LogWarning("Invalid recovery code entered for user with ID '{UserId}' ", user.Id);
                ModelState.AddModelError(string.Empty, "Invalid recovery code entered.");
                return Page();
            }
        }
    }
}
