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
using Microsoft.EntityFrameworkCore;

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
            // 框架原生 API：从 Identity.TwoFactorUserId cookie 读取用户。
            // 之前因 dotnet/aspnetcore#66929 在 .NET 10.0.8 返回 null 而改用 workaround；
            // 已在 .NET 10.0.11 验证框架行为正常，恢复原生调用。
            var user = await _signInManager.GetTwoFactorAuthenticationUserAsync();
            if (user is null)
                return RedirectToPage("./Login");

            // 防御性数据完整性修复（迁移自已退役的 TwoFactorEndpoints.ChallengeAsync）
            var fixDisabled2fa = await AutoFixDataIntegrityAsync(user);
            if (fixDisabled2fa)
            {
                return RedirectToPage("./Login");
            }

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

            returnUrl = string.IsNullOrEmpty(returnUrl) ? Url.Content("~/") : returnUrl;

            var user = await _signInManager.GetTwoFactorAuthenticationUserAsync();
            if (user is null)
                return RedirectToPage("./Login");

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

        /// <summary>
        /// 防御性数据完整性修复：检测并自动修复用户记录中的损坏状态。
        /// 登录时静默修复，不阻塞登录流程——即使修复失败也不影响用户。
        /// </summary>
        private async Task<bool> AutoFixDataIntegrityAsync(AppUser user)
        {
            try
            {
                var needsUpdate = false;
                var twoFactorWasDisabled = false;

                // 1. ConfiguredMethods=None 但 TwoFactorEnabled=true → 自动禁用 2FA
                if (user.ConfiguredMethods == TwoFactorMethod.None && user.TwoFactorEnabled)
                {
                    user.TwoFactorEnabled = false;
                    needsUpdate = true;
                    twoFactorWasDisabled = true;
                    _logger.LogWarning(
                        "Data integrity fix: Auto-disabled 2FA for user {UserId} (ConfiguredMethods=None, TwoFactorEnabled=true)",
                        user.Id);
                }

                // 2. Email 方法但 EffectiveEmailForTwoFactor 为 null → 自动清除 Email 标志
                if (user.ConfiguredMethods.HasFlag(TwoFactorMethod.Email)
                    && string.IsNullOrEmpty(user.EffectiveEmailForTwoFactor))
                {
                    user.ConfiguredMethods &= ~TwoFactorMethod.Email;
                    needsUpdate = true;
                    _logger.LogWarning(
                        "Data integrity fix: Cleared Email 2FA method for user {UserId} (no email available)",
                        user.Id);
                }

                if (needsUpdate)
                {
                    await _userManager.UpdateAsync(user);
                }

                return twoFactorWasDisabled;
            }
            catch (DbUpdateException ex)
            {
                _logger.LogWarning(ex, "Data integrity fix failed (DB update) for user {UserId}", user.Id);
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogWarning(ex, "Data integrity fix failed (invalid operation) for user {UserId}", user.Id);
            }

            return false;
        }
    }
}
