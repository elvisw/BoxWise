// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
#nullable disable

using System;
using System.ComponentModel.DataAnnotations;
using System.Text;
using System.Threading.Tasks;
using BoxWise.Server.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;

namespace BoxWise.Server.Areas.Identity.Pages.Account
{
    [AllowAnonymous]
    public class ResetPasswordModel : PageModel
    {
        private readonly UserManager<AppUser> _userManager;
        private readonly IEmailSender _emailSender;

        public ResetPasswordModel(UserManager<AppUser> userManager, IEmailSender emailSender)
        {
            _userManager = userManager;
            _emailSender = emailSender;
        }

        [BindProperty]
        public InputModel Input { get; set; }

        public string MaskedEmail { get; set; }

        public class InputModel
        {
            [Required]
            public string UserId { get; set; }

            [Required(ErrorMessage = "请输入新密码")]
            [StringLength(100, ErrorMessage = "密码长度至少为 {2} 个字符。", MinimumLength = 8)]
            [DataType(DataType.Password)]
            [Display(Name = "新密码")]
            public string Password { get; set; }

            [DataType(DataType.Password)]
            [Display(Name = "确认新密码")]
            [Compare("Password", ErrorMessage = "密码和确认密码不匹配。")]
            public string ConfirmPassword { get; set; }

            [Required]
            public string Code { get; set; }
        }

        public async Task<IActionResult> OnGetAsync(string userId, string code)
        {
            if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(code))
            {
                return BadRequest("重置密码需要提供用户标识和验证码。");
            }

            var user = await _userManager.FindByIdAsync(userId);
            if (user == null)
            {
                return RedirectToPage("./ResetPasswordConfirmation");
            }

            MaskedEmail = MaskEmail(user.Email ?? "");

            Input = new InputModel
            {
                UserId = userId,
                Code = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(code))
            };
            return Page();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            var user = await _userManager.FindByIdAsync(Input.UserId);
            if (user == null)
            {
                return RedirectToPage("./ResetPasswordConfirmation");
            }

            MaskedEmail = MaskEmail(user.Email ?? "");

            if (!ModelState.IsValid)
            {
                return Page();
            }

            var result = await _userManager.ResetPasswordAsync(user, Input.Code, Input.Password);
            if (result.Succeeded)
            {
                await _emailSender.SendEmailAsync(
                    user.Email,
                    "BoxWise - 密码已重置",
                    "<p>您好，</p><p>您的 BoxWise 账户密码已被重置。如果不是您本人操作，请立即联系管理员。</p>");

                return RedirectToPage("./ResetPasswordConfirmation");
            }

            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(string.Empty, error.Description);
            }
            return Page();
        }

        private static string MaskEmail(string email)
        {
            if (string.IsNullOrEmpty(email) || !email.Contains('@'))
                return email;

            var parts = email.Split('@');
            var name = parts[0];
            var domain = parts[1];

            if (name.Length <= 2)
                return $"{name[0]}***@{domain}";

            return $"{name[0]}***{name[^1]}@{domain}";
        }
    }
}
