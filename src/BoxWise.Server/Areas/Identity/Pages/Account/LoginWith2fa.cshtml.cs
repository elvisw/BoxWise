// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
#nullable disable

using System;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;
using BoxWise.Server.Models;
using Microsoft.AspNetCore.Identity;

namespace BoxWise.Server.Areas.Identity.Pages.Account
{
    [AllowAnonymous]
    public class LoginWith2faModel : PageModel
    {
        private readonly SignInManager<AppUser> _signInManager;
        private readonly UserManager<AppUser> _userManager;
        private readonly ILogger<LoginWith2faModel> _logger;

        public LoginWith2faModel(
            SignInManager<AppUser> signInManager,
            UserManager<AppUser> userManager,
            ILogger<LoginWith2faModel> logger)
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
        public bool RememberMe { get; set; }

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
            [Required]
            [StringLength(7, ErrorMessage = "The {0} must be at least {2} and at max {1} characters long.", MinimumLength = 6)]
            [DataType(DataType.Text)]
            [Display(Name = "Authenticator code")]
            public string TwoFactorCode { get; set; }

            /// <summary>
            ///     This API supports the ASP.NET Core Identity default UI infrastructure and is not intended to be used
            ///     directly from your code. This API may change or be removed in future releases.
            /// </summary>
            [Display(Name = "Remember this machine")]
            public bool RememberMachine { get; set; }
        }

        public async Task<IActionResult> OnGetAsync(bool rememberMe, string returnUrl = null)
        {
            // Workaround for dotnet/aspnetcore#66929: GetTwoFactorAuthenticationUserAsync()
            // 在 .NET 10.0.8 中返回 null——内部 UserManager.GetUserId 返回了 UserName
            // 而非 UserId，导致 FindByIdAsync 用用户名查 GUID 列。手动从 TwoFactorUserId
            // Cookie 提取 NameIdentifier claim，绕过有问题的调用路径。
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
            RememberMe = rememberMe;

            return Page();
        }

        public async Task<IActionResult> OnPostAsync(bool rememberMe, string returnUrl = null)
        {
            if (!ModelState.IsValid)
            {
                return Page();
            }

            returnUrl = returnUrl ?? Url.Content("~/");

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

            var authenticatorCode = Input.TwoFactorCode.Replace(" ", string.Empty).Replace("-", string.Empty);

            var result = await _signInManager.TwoFactorAuthenticatorSignInAsync(authenticatorCode, rememberMe, Input.RememberMachine);

            if (result.Succeeded)
            {
                _logger.LogInformation("User with ID '{UserId}' logged in with 2fa.", user.Id);
                return LocalRedirect(returnUrl);
            }
            else if (result.IsLockedOut)
            {
                _logger.LogWarning("User with ID '{UserId}' account locked out.", user.Id);
                return RedirectToPage("./Lockout");
            }
            else
            {
                _logger.LogWarning("Invalid authenticator code entered for user with ID '{UserId}'.", user.Id);
                ModelState.AddModelError(string.Empty, "Invalid authenticator code.");
                return Page();
            }
        }
    }
}
