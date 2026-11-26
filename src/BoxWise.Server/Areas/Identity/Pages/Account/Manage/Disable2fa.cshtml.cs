// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
#nullable disable

using System;
using System.Threading.Tasks;
using BoxWise.Server.Models;
using BoxWise.Server.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;

namespace BoxWise.Server.Areas.Identity.Pages.Account.Manage
{
    public class Disable2faModel : PageModel
    {
        private readonly UserManager<AppUser> _userManager;
        private readonly SignInManager<AppUser> _signInManager;
        private readonly RecoveryCodeService _recoveryCodeService;
        private readonly ILogger<Disable2faModel> _logger;

        public Disable2faModel(
            UserManager<AppUser> userManager,
            SignInManager<AppUser> signInManager,
            RecoveryCodeService recoveryCodeService,
            ILogger<Disable2faModel> logger)
        {
            _userManager = userManager;
            _signInManager = signInManager;
            _recoveryCodeService = recoveryCodeService;
            _logger = logger;
        }

        /// <summary>
        ///     This API supports the ASP.NET Core Identity default UI infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        [TempData]
        public string StatusMessage { get; set; }

        public async Task<IActionResult> OnGet()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return NotFound($"Unable to load user with ID '{_userManager.GetUserId(User)}'.");
            }

            if (!await _userManager.GetTwoFactorEnabledAsync(user))
            {
                throw new InvalidOperationException($"Cannot disable 2FA for user as it's not currently enabled.");
            }

            return Page();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return NotFound($"Unable to load user with ID '{_userManager.GetUserId(User)}'.");
            }

            // 单事务：清空两套恢复码与 2FA 字段并轮换 SecurityStamp，
            // 保留 Identity authenticator key、通行密钥和现有宽限期。
            var result = await _recoveryCodeService.DisableTwoFactorAsync(user, _userManager);
            if (!result.Succeeded)
            {
                _logger.LogWarning(
                    "Failed to disable 2FA for user with ID '{UserId}': {Status} (outcome confirmed: {Confirmed})",
                    _userManager.GetUserId(User), result.Status, result.OutcomeConfirmed);
                ModelState.AddModelError(string.Empty, result.OutcomeConfirmed
                    ? "关闭双重验证失败，未做任何更改，请稍后重试。"
                    : "关闭双重验证的结果未确认，请刷新页面核对当前状态。");
                return Page();
            }

            // 提交成功后再刷新当前登录，避免失败时触发额外的 cookie 写入。
            await _signInManager.RefreshSignInAsync(user);

            _logger.LogInformation("User with ID '{UserId}' has disabled 2fa.", _userManager.GetUserId(User));
            StatusMessage = "双重验证已关闭，全部恢复码已作废。验证器密钥与通行密钥保留，其他设备的登录会话会在安全戳校验时失效。";
            return RedirectToPage("./TwoFactorAuthentication");
        }
    }
}
