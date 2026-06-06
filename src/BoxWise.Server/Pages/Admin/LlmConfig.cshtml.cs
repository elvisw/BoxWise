using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using BoxWise.Server.Data;
using BoxWise.Server.Models;

namespace BoxWise.Server.Pages.Admin;

[Authorize(Policy = "AdminOnly")]
public class LlmConfigModel : PageModel
{
    private readonly AppDbContext _db;

    public LlmConfigModel(AppDbContext db)
    {
        _db = db;
    }

    [BindProperty]
    public string BaseUrl { get; set; } = "";

    [BindProperty]
    public string ApiKey { get; set; } = "";

    [BindProperty]
    public string Model { get; set; } = "doubao-seed-2-0-pro-260215";

    [BindProperty]
    public int TimeoutSeconds { get; set; } = 30;

    public bool HasApiKey { get; private set; }

    public string? StatusMessage { get; private set; }
    public string? ErrorMessage { get; private set; }

    public async Task OnGetAsync()
    {
        var entity = await _db.LlmConfigs.FindAsync(1);
        if (entity is not null)
        {
            BaseUrl = entity.BaseUrl ?? "";
            Model = entity.Model;
            TimeoutSeconds = entity.TimeoutSeconds;
            HasApiKey = !string.IsNullOrWhiteSpace(entity.ApiKey);
        }
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (string.IsNullOrWhiteSpace(BaseUrl))
        {
            ErrorMessage = "BaseUrl 不能为空";
            return Page();
        }

        if (string.IsNullOrWhiteSpace(Model))
            Model = "doubao-seed-2-0-pro-260215";

        TimeoutSeconds = Math.Clamp(TimeoutSeconds, 5, 120);

        try
        {
            var entity = await _db.LlmConfigs.FindAsync(1);
            if (entity is null)
            {
                entity = new LlmConfig { Id = 1 };
                _db.LlmConfigs.Add(entity);
            }

            entity.BaseUrl = BaseUrl.Trim();
            if (!string.IsNullOrWhiteSpace(ApiKey))
                entity.ApiKey = ApiKey.Trim();
            entity.Model = Model.Trim();
            entity.TimeoutSeconds = TimeoutSeconds;

            await _db.SaveChangesAsync();
            StatusMessage = "LLM 配置已保存";
            HasApiKey = !string.IsNullOrWhiteSpace(entity.ApiKey);
            ApiKey = ""; // Clear from BindProperty to prevent HTML source leakage
        }
        catch (DbUpdateException)
        {
            ErrorMessage = "保存失败，请重试";
        }

        return Page();
    }
}
