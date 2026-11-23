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
using BoxWise.Server.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Logging;
namespace BoxWise.Server.Areas.Identity.Pages.Account
{
    [AllowAnonymous]
    public class LoginWithRecoveryCodeModel : PageModel
    {
        private readonly SignInManager<AppUser> _signInManager;
        private readonly UserManager<AppUser> _userManager;
        private readonly RecoveryCodeService _recoveryCodeService;
        private readonly ILogger<LoginWithRecoveryCodeModel> _logger;

        public LoginWithRecoveryCodeModel(
            SignInManager<AppUser> signInManager,
            UserManager<AppUser> userManager,
            RecoveryCodeService recoveryCodeService,
            ILogger<LoginWithRecoveryCodeModel> logger)
        {
            _signInManager = signInManager;
            _userManager = userManager;
            _recoveryCodeService = recoveryCodeService;
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
            // 框架原生 API：从 Identity.TwoFactorUserId cookie 读取用户。
            // 之前因 dotnet/aspnetcore#66929 在 .NET 10.0.8 返回 null 而改用 workaround；
            // 已在 .NET 10.0.11 验证框架行为正常，恢复原生调用。
            var user = await _signInManager.GetTwoFactorAuthenticationUserAsync();
            if (user is null)
                return RedirectToPage("./Login");
            ReturnUrl = returnUrl;
            return Page();
        }

        [EnableRateLimiting("login-per-ip")]
        public async Task<IActionResult> OnPostAsync(string returnUrl = null)
        {
            if (!ModelState.IsValid)
            {
                return Page();
            }

            var user = await _signInManager.GetTwoFactorAuthenticationUserAsync();
            if (user is null)
                return RedirectToPage("./Login");

            // 恢复码为 base32 大写字符；容忍用户输入空格和小写。
            var recoveryCode = Input.RecoveryCode
                .Replace(" ", string.Empty)
                .Trim()
                .ToUpperInvariant();

            var success = await _recoveryCodeService.VerifyRecoveryCodeAsync(user, recoveryCode, _userManager);

            if (success)
            {
                _logger.LogInformation(
                    "User with ID '{UserId}' disabled two-factor authentication with a recovery code.", user.Id);

                // 清除 2FA 会话 cookie：恢复码只用于关闭 2FA，不建立登录会话。
                await HttpContext.SignOutAsync(IdentityConstants.TwoFactorUserIdScheme);
                TempData["StatusMessage"] = "双重验证已关闭，全部恢复码已作废。请使用密码重新登录。";
                return RedirectToPage("./Login");
            }

            _logger.LogWarning("Invalid recovery code entered for user with ID '{UserId}'.", user.Id);
            ModelState.AddModelError(string.Empty, "恢复码无效。");
            return Page();
        }
    }
}
