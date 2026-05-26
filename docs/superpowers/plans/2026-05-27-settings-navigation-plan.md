# Settings Page & Navigation Restructure — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add Settings page with 4th bottom nav tab, move location management + new tag management into it, remove logout from AppBar.

**Architecture:** New `/settings` Blazor page with list-style entry points opening MudDialog modals. Backend tag CRUD extended with Rename/Delete endpoints + ItemCount in TagDto. Tag model gains reverse navigation `ICollection<Item> Items`.

**Tech Stack:** ASP.NET Core 10 Minimal API, EF Core 10, Blazor WASM, MudBlazor 9.x, xUnit + InMemory DB

---

### Task 1: Tag Model — Add Items Navigation Property

**Files:**
- Modify: `src/BoxWise.Server/Models/Tag.cs`
- Modify: `src/BoxWise.Server/Data/Configurations/ItemConfiguration.cs`

- [ ] **Step 1: Add Items collection to Tag model**

```csharp
// src/BoxWise.Server/Models/Tag.cs
namespace BoxWise.Server.Models;

public class Tag
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public ICollection<Item> Items { get; set; } = new List<Item>();
}
```

- [ ] **Step 2: Update ItemConfiguration to wire Tag reverse navigation**

```csharp
// src/BoxWise.Server/Data/Configurations/ItemConfiguration.cs (lines 39-41)
builder.HasMany(x => x.Tags)
    .WithMany(t => t.Items)
    .UsingEntity("ItemTag");
```

- [ ] **Step 3: Build to verify no compilation errors**

Run: `dotnet build BoxWise.slnx`
Expected: Build succeeded, 0 errors

- [ ] **Step 4: Commit**

```bash
git add src/BoxWise.Server/Models/Tag.cs src/BoxWise.Server/Data/Configurations/ItemConfiguration.cs
git commit -m "feat: Tag 模型添加 Items 反向导航属性，更新 ItemConfiguration"
```

---

### Task 2: TagRepository — Add RenameAsync, DeleteAsync, Update GetAllAsync

**Files:**
- Modify: `src/BoxWise.Server/Repositories/TagRepository.cs`

- [ ] **Step 1: Add RenameAsync method after line 47 (end of CreateAsync)**

```csharp
// src/BoxWise.Server/Repositories/TagRepository.cs — add after CreateAsync
public async Task<Tag> RenameAsync(int id, string name)
{
    if (string.IsNullOrWhiteSpace(name))
        throw new ArgumentException("标签名称不能为空");

    name = name.Trim();
    if (name.Length > 50)
        throw new ArgumentException("标签名称不能超过 50 个字符");

    var tag = await _db.Tags.FindAsync(id)
        ?? throw new KeyNotFoundException("标签不存在");

    var exists = await _db.Tags.AnyAsync(t => t.Name == name && t.Id != id);
    if (exists)
        throw new ArgumentException($"标签 '{name}' 已存在");

    tag.Name = name;
    await _db.SaveChangesAsync();
    return tag;
}
```

- [ ] **Step 2: Add DeleteAsync method after RenameAsync**

```csharp
// src/BoxWise.Server/Repositories/TagRepository.cs — add after RenameAsync
public async Task DeleteAsync(int id)
{
    var tag = await _db.Tags.FindAsync(id)
        ?? throw new KeyNotFoundException("标签不存在");

    _db.Tags.Remove(tag);
    await _db.SaveChangesAsync();
}
```

- [ ] **Step 3: Update GetAllAsync to Include Items**

Replace the existing `GetAllAsync` method:

```csharp
// src/BoxWise.Server/Repositories/TagRepository.cs — replace GetAllAsync
public async Task<List<Tag>> GetAllAsync()
{
    return await _db.Tags
        .Include(t => t.Items)
        .OrderBy(t => t.Name)
        .ToListAsync();
}
```

- [ ] **Step 4: Verify EF Core Include using exists**

Ensure `using Microsoft.EntityFrameworkCore;` is at top of TagRepository.cs.

- [ ] **Step 5: Build to verify**

Run: `dotnet build BoxWise.slnx`
Expected: Build succeeded, 0 errors

- [ ] **Step 6: Commit**

```bash
git add src/BoxWise.Server/Repositories/TagRepository.cs
git commit -m "feat: TagRepository 添加 RenameAsync/DeleteAsync，GetAllAsync 包含 Items"
```

---

### Task 3: Tag DTO Changes — TagDto ItemCount + RenameTagRequest

**Files:**
- Modify: `src/BoxWise.Shared/Dtos/TagDto.cs`
- Create: `src/BoxWise.Shared/Dtos/RenameTagRequest.cs`
- Modify: `src/BoxWise.Server/Endpoints/TagEndpoints.cs`

- [ ] **Step 1: Add ItemCount to TagDto**

```csharp
// src/BoxWise.Shared/Dtos/TagDto.cs
namespace BoxWise.Shared.Dtos;

public record TagDto(int Id, string Name, int ItemCount);
```

- [ ] **Step 2: Create RenameTagRequest DTO**

```csharp
// src/BoxWise.Shared/Dtos/RenameTagRequest.cs
namespace BoxWise.Shared.Dtos;

public record RenameTagRequest(string Name);
```

- [ ] **Step 3: Update TagEndpoints line 34 — GetAllTagsAsync mapping**

```csharp
// Replace: var dtos = tags.Select(t => new TagDto(t.Id, t.Name)).ToList();
var dtos = tags.Select(t => new TagDto(t.Id, t.Name, t.Items.Count)).ToList();
```

- [ ] **Step 4: Update TagEndpoints line 44 — CreateTagAsync mapping**

```csharp
// Replace: var dto = new TagDto(tag.Id, tag.Name);
var dto = new TagDto(tag.Id, tag.Name, 0);
```

- [ ] **Step 5: Build to verify**

Run: `dotnet build BoxWise.slnx`
Expected: Build succeeded, 0 errors

- [ ] **Step 6: Commit**

```bash
git add src/BoxWise.Shared/Dtos/TagDto.cs src/BoxWise.Shared/Dtos/RenameTagRequest.cs src/BoxWise.Server/Endpoints/TagEndpoints.cs
git commit -m "feat: TagDto 添加 ItemCount，新建 RenameTagRequest DTO"
```

---

### Task 4: TagEndpoints — Add Rename + Delete Endpoints

**Files:**
- Modify: `src/BoxWise.Server/Endpoints/TagEndpoints.cs`

- [ ] **Step 1: Register new endpoints in MapTagEndpoints**

Add the following before the `return group;` line inside `MapTagEndpoints`:

```csharp
// src/BoxWise.Server/Endpoints/TagEndpoints.cs — add before return group;
group.MapPut("/{id:int}", RenameTagAsync)
    .Produces<TagDto>(200)
    .ProducesProblem(400)
    .ProducesProblem(401)
    .Produces(404)
    .WithTags("Tags")
    .WithDescription("重命名标签");

group.MapDelete("/{id:int}", DeleteTagAsync)
    .Produces(204)
    .ProducesProblem(400)
    .ProducesProblem(401)
    .Produces(404)
    .WithTags("Tags")
    .WithDescription("删除标签");
```

- [ ] **Step 2: Add RenameTagAsync handler after CreateTagAsync method**

```csharp
// src/BoxWise.Server/Endpoints/TagEndpoints.cs — add after CreateTagAsync
private static async Task<Results<Ok<TagDto>, NotFound, ProblemHttpResult>>
    RenameTagAsync(int id, RenameTagRequest request, TagRepository repo)
{
    try
    {
        var tag = await repo.RenameAsync(id, request.Name);
        var dto = new TagDto(tag.Id, tag.Name, tag.Items.Count);
        return TypedResults.Ok(dto);
    }
    catch (KeyNotFoundException)
    {
        return TypedResults.NotFound();
    }
    catch (ArgumentException ex)
    {
        return TypedResults.Problem(ex.Message, statusCode: 400);
    }
}
```

- [ ] **Step 3: Add DeleteTagAsync handler after RenameTagAsync**

```csharp
// src/BoxWise.Server/Endpoints/TagEndpoints.cs — add after RenameTagAsync
private static async Task<Results<NoContent, NotFound>>
    DeleteTagAsync(int id, TagRepository repo)
{
    try
    {
        await repo.DeleteAsync(id);
        return TypedResults.NoContent();
    }
    catch (KeyNotFoundException)
    {
        return TypedResults.NotFound();
    }
}
```

- [ ] **Step 4: Build to verify**

Run: `dotnet build BoxWise.slnx`
Expected: Build succeeded, 0 errors

- [ ] **Step 5: Commit**

```bash
git add src/BoxWise.Server/Endpoints/TagEndpoints.cs
git commit -m "feat: TagEndpoints 添加 PUT/DELETE 端点（重命名/删除标签）"
```

---

### Task 5: TagRepositoryTests — 5 New Tests

**Files:**
- Modify: `src/BoxWise.Server.Tests/Repositories/TagRepositoryTests.cs`

- [ ] **Step 1: Add RenameAsync_Success test at end of class**

```csharp
[Fact]
public async Task RenameAsync_Success()
{
    using var db = TestDbContextFactory.Create();
    var repo = new TagRepository(db);
    var tag = await repo.CreateAsync("旧名称");

    var result = await repo.RenameAsync(tag.Id, "新名称");

    Assert.Equal("新名称", result.Name);
    Assert.Equal(tag.Id, result.Id);
}
```

- [ ] **Step 2: Add RenameAsync_DuplicateName_Throws**

```csharp
[Fact]
public async Task RenameAsync_DuplicateName_Throws()
{
    using var db = TestDbContextFactory.Create();
    var repo = new TagRepository(db);
    await repo.CreateAsync("标签A");
    var tagB = await repo.CreateAsync("标签B");

    await Assert.ThrowsAsync<ArgumentException>(() => repo.RenameAsync(tagB.Id, "标签A"));
}
```

- [ ] **Step 3: Add RenameAsync_NotFound_Throws**

```csharp
[Fact]
public async Task RenameAsync_NotFound_Throws()
{
    using var db = TestDbContextFactory.Create();
    var repo = new TagRepository(db);

    await Assert.ThrowsAsync<KeyNotFoundException>(() => repo.RenameAsync(999, "不存在"));
}
```

- [ ] **Step 4: Add DeleteAsync_Success**

```csharp
[Fact]
public async Task DeleteAsync_Success()
{
    using var db = TestDbContextFactory.Create();
    var repo = new TagRepository(db);
    var tag = await repo.CreateAsync("待删除");

    await repo.DeleteAsync(tag.Id);

    var exists = db.Tags.Any(t => t.Id == tag.Id);
    Assert.False(exists);
}
```

- [ ] **Step 5: Add DeleteAsync_NotFound_Throws**

```csharp
[Fact]
public async Task DeleteAsync_NotFound_Throws()
{
    using var db = TestDbContextFactory.Create();
    var repo = new TagRepository(db);

    await Assert.ThrowsAsync<KeyNotFoundException>(() => repo.DeleteAsync(999));
}
```

- [ ] **Step 6: Run tag tests**

Run: `dotnet test BoxWise.slnx --filter "FullyQualifiedName~TagRepositoryTests"`
Expected: 10 tests pass (5 existing + 5 new)

- [ ] **Step 7: Commit**

```bash
git add src/BoxWise.Server.Tests/Repositories/TagRepositoryTests.cs
git commit -m "test: TagRepository 添加 Rename/Delete 5 个单元测试"
```

---

### Task 6: TagService Client — Add CUD Methods

**Files:**
- Modify: `src/BoxWise.Client/Services/TagService.cs`

- [ ] **Step 1: Add CUD methods after GetAllAsync**

```csharp
// src/BoxWise.Client/Services/TagService.cs — add after GetAllAsync
public async Task<TagDto?> CreateAsync(CreateTagRequest request)
{
    var response = await _http.PostAsJsonAsync("api/tags", request);
    if (!response.IsSuccessStatusCode) return null;
    return await response.Content.ReadFromJsonAsync<TagDto>();
}

public async Task<TagDto?> RenameAsync(int id, RenameTagRequest request)
{
    var response = await _http.PutAsJsonAsync($"api/tags/{id}", request);
    if (!response.IsSuccessStatusCode) return null;
    return await response.Content.ReadFromJsonAsync<TagDto>();
}

public async Task<bool> DeleteAsync(int id)
{
    var response = await _http.DeleteAsync($"api/tags/{id}");
    return response.IsSuccessStatusCode;
}
```

- [ ] **Step 2: Build to verify**

Run: `dotnet build BoxWise.slnx`
Expected: Build succeeded, 0 errors

- [ ] **Step 3: Commit**

```bash
git add src/BoxWise.Client/Services/TagService.cs
git commit -m "feat: TagService 客户端添加 Create/Rename/Delete 方法"
```

---

### Task 7: TagManageDialog — New Component

**Files:**
- Create: `src/BoxWise.Client/Components/TagManageDialog.razor`

- [ ] **Step 1: Create TagManageDialog.razor**

```csharp
@namespace BoxWise.Client.Components
@using BoxWise.Shared.Dtos
@inject TagService TagService

<MudDialog>
    <TitleContent>
        <MudText Typo="Typo.h6">管理标签</MudText>
    </TitleContent>
    <DialogContent>
        @if (!string.IsNullOrEmpty(_error))
        {
            <MudText Color="Color.Error" Class="mb-2">@_error</MudText>
        }

        @if (_isCreating)
        {
            <div class="d-flex align-center gap-2 mb-3">
                <MudTextField @bind-Value="_createName" Label="标签名称"
                              Variant="Variant.Outlined" Immediate="true" Class="flex-grow-1" />
                <MudButton OnClick="SaveCreateAsync" Color="Color.Primary"
                           Variant="Variant.Filled" Disabled="@(_saving)">添加</MudButton>
                <MudButton OnClick="CancelEdit">取消</MudButton>
            </div>
        }
        else
        {
            <MudButton OnClick="StartCreate" Variant="Variant.Filled"
                       Color="Color.Primary" Class="mb-3" StartIcon="@Icons.Material.Filled.Add">
                添加标签
            </MudButton>
        }

        @if (_tags.Count == 0 && !_isCreating)
        {
            <MudText Typo="Typo.body2" Color="Color.Default">暂无标签，录入物品时可自动创建</MudText>
        }

        @foreach (var tag in _tags)
        {
            <div class="d-flex align-center py-1">
                @if (_renamingId == tag.Id)
                {
                    <MudTextField @bind-Value="_renameName" Label="新名称"
                                  Variant="Variant.Outlined" Immediate="true" Class="flex-grow-1" />
                    <MudIconButton Icon="@Icons.Material.Filled.Check" Size="Size.Small"
                                   Color="Color.Success" OnClick="() => SaveRenameAsync(tag.Id)" />
                    <MudIconButton Icon="@Icons.Material.Filled.Close" Size="Size.Small"
                                   OnClick="CancelEdit" />
                }
                else if (_deletingId == tag.Id)
                {
                    <MudText Class="flex-grow-1">确认删除标签 <strong>@tag.Name</strong>？（将解除所有物品关联）</MudText>
                    <MudButton OnClick="() => ConfirmDeleteAsync(tag.Id)"
                               Color="Color.Error" Variant="Variant.Filled"
                               Size="Size.Small" Class="mr-2">删除</MudButton>
                    <MudButton OnClick="CancelEdit" Size="Size.Small">取消</MudButton>
                }
                else
                {
                    <MudIcon Icon="@Icons.Material.Filled.Label" Size="Size.Small" Class="mr-2" Color="Color.Secondary" />
                    <MudText Class="flex-grow-1">@tag.Name</MudText>
                    <MudText Typo="Typo.caption" Color="Color.Default" Class="mr-2">@tag.ItemCount 件</MudText>
                    @if (!_isCreating && _renamingId is null && _deletingId is null)
                    {
                        <MudIconButton Icon="@Icons.Material.Filled.Edit" Size="Size.Small"
                                       OnClick="() => StartRename(tag)" title="重命名" />
                        <MudIconButton Icon="@Icons.Material.Filled.Delete" Size="Size.Small"
                                       OnClick="() => StartDelete(tag)" Color="Color.Error" title="删除" />
                    }
                }
            </div>
        }
    </DialogContent>
    <DialogActions>
        <MudButton OnClick="Close">完成</MudButton>
    </DialogActions>
</MudDialog>

@code {
    [CascadingParameter]
    private IMudDialogInstance MudDialog { get; set; } = null!;

    private List<TagDto> _tags = [];
    private string? _error;
    private bool _saving;
    private bool _isCreating;

    private string _createName = "";
    private int? _renamingId;
    private string _renameName = "";
    private int? _deletingId;

    protected override async Task OnInitializedAsync()
    {
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        _tags = await TagService.GetAllAsync();
    }

    private void StartCreate()
    {
        _error = null;
        _createName = "";
        _isCreating = true;
    }

    private async Task SaveCreateAsync()
    {
        if (string.IsNullOrWhiteSpace(_createName)) return;

        _error = null;
        _saving = true;
        try
        {
            var request = new CreateTagRequest(_createName.Trim());
            var result = await TagService.CreateAsync(request);
            if (result is not null)
            {
                CancelEdit();
                _hasChanges = true;
                await LoadAsync();
            }
            else
            {
                _error = "创建失败，标签可能已存在";
            }
        }
        catch
        {
            _error = "创建失败";
        }
        finally
        {
            _saving = false;
        }
    }

    private void StartRename(TagDto tag)
    {
        _error = null;
        _renamingId = tag.Id;
        _renameName = tag.Name;
    }

    private async Task SaveRenameAsync(int id)
    {
        if (string.IsNullOrWhiteSpace(_renameName)) return;

        _error = null;
        try
        {
            var request = new RenameTagRequest(_renameName.Trim());
            var result = await TagService.RenameAsync(id, request);
            if (result is not null)
            {
                CancelEdit();
                _hasChanges = true;
                await LoadAsync();
            }
            else
            {
                _error = "重命名失败，名称可能已存在";
            }
        }
        catch
        {
            _error = "重命名失败";
        }
    }

    private void StartDelete(TagDto tag)
    {
        _error = null;
        _deletingId = tag.Id;
    }

    private async Task ConfirmDeleteAsync(int id)
    {
        _error = null;
        try
        {
            var ok = await TagService.DeleteAsync(id);
            if (ok)
            {
                CancelEdit();
                _hasChanges = true;
                await LoadAsync();
            }
            else
            {
                _error = "删除失败";
            }
        }
        catch
        {
            _error = "删除失败";
        }
    }

    private void CancelEdit()
    {
        _isCreating = false;
        _renamingId = null;
        _deletingId = null;
        _createName = "";
        _error = null;
    }

    private bool _hasChanges;

    private void Close()
    {
        MudDialog.Close(DialogResult.Ok(_hasChanges));
    }
}
```

- [ ] **Step 2: Build to verify**

Run: `dotnet build BoxWise.slnx`
Expected: Build succeeded, 0 errors

- [ ] **Step 3: Commit**

```bash
git add src/BoxWise.Client/Components/TagManageDialog.razor
git commit -m "feat: 新建 TagManageDialog 标签管理弹窗组件"
```

---

### Task 8: Settings.razor — New Page

**Files:**
- Create: `src/BoxWise.Client/Pages/Settings.razor`
- Modify: `src/BoxWise.Client/wwwroot/css/app.css`

- [ ] **Step 1: Create Settings.razor**

```csharp
@page "/settings"
@attribute [Authorize]
@using Microsoft.AspNetCore.Authorization

@inject AuthService AuthService
@inject NavigationManager Navigation
@inject IDialogService DialogService

<MudText Typo="Typo.h5" Class="mb-4">设置</MudText>

<div class="bw-settings-list">

    <MudPaper Class="pa-4 mb-1 bw-settings-item" Elevation="0" OnClick="OpenLocationManageDialog">
        <div class="d-flex align-center">
            <MudIcon Icon="@Icons.Material.Filled.FolderOpen" Size="Size.Medium" Class="mr-3" Color="Color.Primary" />
            <div class="flex-grow-1">
                <MudText Typo="Typo.body1" Style="font-weight:500;">位置管理</MudText>
                <MudText Typo="Typo.caption" Color="Color.Default">管理收纳位置层级</MudText>
            </div>
            <MudIcon Icon="@Icons.Material.Filled.ChevronRight" Color="Color.Default" />
        </div>
    </MudPaper>

    <MudPaper Class="pa-4 mb-1 bw-settings-item" Elevation="0" OnClick="OpenTagManageDialog">
        <div class="d-flex align-center">
            <MudIcon Icon="@Icons.Material.Filled.Label" Size="Size.Medium" Class="mr-3" Color="Color.Secondary" />
            <div class="flex-grow-1">
                <MudText Typo="Typo.body1" Style="font-weight:500;">标签管理</MudText>
                <MudText Typo="Typo.caption" Color="Color.Default">管理物品分类标签</MudText>
            </div>
            <MudIcon Icon="@Icons.Material.Filled.ChevronRight" Color="Color.Default" />
        </div>
    </MudPaper>

    <MudDivider Class="my-3" />

    <MudPaper Class="pa-4 mb-1 bw-settings-item" Elevation="0" OnClick="LogoutAsync">
        <div class="d-flex align-center">
            <MudIcon Icon="@Icons.Material.Filled.Logout" Size="Size.Medium" Class="mr-3" Color="Color.Error" />
            <div class="flex-grow-1">
                <MudText Typo="Typo.body1" Color="Color.Error" Style="font-weight:500;">退出登录</MudText>
                <MudText Typo="Typo.caption" Color="Color.Default">切换到其他账户</MudText>
            </div>
            <MudIcon Icon="@Icons.Material.Filled.ChevronRight" Color="Color.Default" />
        </div>
    </MudPaper>

    <MudDivider Class="my-3" />

    <MudText Typo="Typo.caption" Color="Color.Default" Class="mb-2 ml-2">以下功能将在后续版本提供</MudText>

    <MudPaper Class="pa-4 bw-settings-item bw-settings-disabled" Elevation="0">
        <div class="d-flex align-center">
            <MudIcon Icon="@Icons.Material.Filled.Person" Size="Size.Medium" Class="mr-3" Color="Color.Default" />
            <div class="flex-grow-1">
                <MudText Typo="Typo.body1" Color="Color.Default" Style="font-weight:500;">账户信息</MudText>
                <MudText Typo="Typo.caption" Color="Color.Default">查看当前账户详情</MudText>
            </div>
            <MudIcon Icon="@Icons.Material.Filled.ChevronRight" Color="Color.Default" />
        </div>
    </MudPaper>

</div>

@code {
    private async Task OpenLocationManageDialog()
    {
        var options = new DialogOptions { CloseButton = true, MaxWidth = MaxWidth.Small, FullWidth = true };
        await DialogService.ShowAsync<LocationManageDialog>("管理位置", options);
    }

    private async Task OpenTagManageDialog()
    {
        var options = new DialogOptions { CloseButton = true, MaxWidth = MaxWidth.Small, FullWidth = true };
        await DialogService.ShowAsync<TagManageDialog>("管理标签", options);
    }

    private async Task LogoutAsync()
    {
        try { await AuthService.LogoutAsync(); }
        catch { }
        finally { Navigation.NavigateTo("/", forceLoad: true); }
    }
}
```

- [ ] **Step 2: Add settings CSS to app.css**

Append to `src/BoxWise.Client/wwwroot/css/app.css`:

```css
.bw-settings-item {
    border-radius: 8px;
    transition: background-color 0.15s;
}
.bw-settings-item:hover {
    background-color: rgba(0,0,0,0.04);
}
.bw-settings-disabled {
    opacity: 0.4;
    pointer-events: none;
}
```

- [ ] **Step 3: Build to verify**

Run: `dotnet build BoxWise.slnx`
Expected: Build succeeded, 0 errors

- [ ] **Step 4: Commit**

```bash
git add src/BoxWise.Client/Pages/Settings.razor src/BoxWise.Client/wwwroot/css/app.css
git commit -m "feat: 新建设置页 Settings.razor（位置管理/标签管理/退出登录/账户预留）"
```

---

### Task 9: MainLayout — 4 Tabs + Simplify AppBar

**Files:**
- Modify: `src/BoxWise.Client/Layout/MainLayout.razor`

- [ ] **Step 1: Remove AuthService injection**

Remove line:
```csharp
@inject AuthService AuthService
```

- [ ] **Step 2: Simplify AppBar — remove logout button, reduce back button**

Replace the content inside `<MudStack Row="true" AlignItems="AlignItems.Center" Class="flex-grow-1" Style="width:100%;">`:

**Old (lines 14-36):**
```razor
            @if (AppState.IsLoggedIn && !_isHomePage)
            {
                <MudIconButton Icon="@Icons.Material.Filled.ArrowBack"
                               Size="Size.Medium"
                               Color="Color.Inherit"
                               aria-label="返回"
                               OnClick="GoBack"
                               Class="mr-2" />
            }
            <MudText Typo="Typo.h6" Class="flex-grow-1">📦 箱知 · BoxWise</MudText>
            @if (AppState.IsLoggedIn)
            {
                <MudText Typo="Typo.subtitle1" Class="d-none d-sm-block mr-2" Style="color:inherit;">
                    @AppState.CurrentUserName
                </MudText>
                <MudIconButton Icon="@Icons.Material.Filled.Logout"
                               Size="Size.Medium"
                               Color="Color.Inherit"
                               aria-label="退出登录"
                               OnClick="LogoutAsync" />
            }
```

**New:**
```razor
            @if (AppState.IsLoggedIn && !_isHomePage)
            {
                <MudIconButton Icon="@Icons.Material.Filled.ArrowBack"
                               Size="Size.Small"
                               Color="Color.Inherit"
                               aria-label="返回"
                               OnClick="GoBack" />
            }
            <MudText Typo="Typo.h6" Class="flex-grow-1">📦 箱知 · BoxWise</MudText>
            @if (AppState.IsLoggedIn)
            {
                <MudText Typo="Typo.subtitle1" Class="d-none d-sm-block" Style="color:inherit;">
                    @AppState.CurrentUserName
                </MudText>
            }
```

- [ ] **Step 3: Add 4th Settings tab to bottom nav**

Append after the Browse tab `</div>` (before `</MudPaper>`):

```razor
        <div class="bw-nav-item @IsActive("/settings")" role="button" tabindex="0"
             @onclick="GoSettings" @onkeydown="HandleSettingsKey" @onkeydown:preventDefault>
            <MudIcon Icon="@Icons.Material.Filled.Settings" Size="Size.Large" />
            <MudText Typo="Typo.caption">设置</MudText>
        </div>
```

- [ ] **Step 4: Add Settings navigation methods**

Add after `GoBrowse()`:

```csharp
private void GoSettings() => NavigateTo("/settings");

private void HandleSettingsKey(Microsoft.AspNetCore.Components.Web.KeyboardEventArgs e)
{
    if (e.Key is "Enter" or " ") GoSettings();
}
```

- [ ] **Step 5: Remove LogoutAsync method**

Remove the entire `LogoutAsync` method (was ~lines 145-159).

- [ ] **Step 6: Build to verify**

Run: `dotnet build BoxWise.slnx`
Expected: Build succeeded, 0 errors

- [ ] **Step 7: Commit**

```bash
git add src/BoxWise.Client/Layout/MainLayout.razor
git commit -m "feat: 底部导航新增设置Tab，顶栏移除退出登录并精简后退按钮"
```

---

### Task 10: Browse.razor — Remove Gear Icon

**Files:**
- Modify: `src/BoxWise.Client/Pages/Browse.razor`

- [ ] **Step 1: Remove IDialogService injection**

Remove line 6:
```csharp
@inject IDialogService DialogService
```

- [ ] **Step 2: Revert LocationTree header to plain title**

Replace the `MudItem` for location filter (lines 9-16) with:

```razor
    <MudItem xs="12" sm="6">
        <MudText Typo="Typo.subtitle2" Class="mb-1">按位置</MudText>
        <LocationTree SelectedLocationId="_locationId"
                      SelectedLocationIdChanged="OnLocationChanged" />
    </MudItem>
```

- [ ] **Step 3: Remove @ref and OpenLocationManageDialog**

In `@code` block, remove:
- `private LocationTree? _locationTree;` 
- The entire `OpenLocationManageDialog` method

- [ ] **Step 4: Build to verify**

Run: `dotnet build BoxWise.slnx`
Expected: Build succeeded, 0 errors

- [ ] **Step 5: Commit**

```bash
git add src/BoxWise.Client/Pages/Browse.razor
git commit -m "refactor: 浏览页移除位置管理齿轮入口（已迁移至设置页）"
```

---

### Task 11: Final Verification

- [ ] **Step 1: Run full build**

Run: `dotnet build BoxWise.slnx`
Expected: Build succeeded, 0 errors, 0 warnings

- [ ] **Step 2: Run all tests**

Run: `dotnet test BoxWise.slnx`
Expected: All tests pass (28 existing + 5 new = 33 tests)

- [ ] **Step 3: Verify git status is clean**

Run: `git status`
Expected: Working tree clean

- [ ] **Step 4: Review commit log**

Run: `git log --oneline -12`
Expected: 10 new commits on top of existing history

---

## File Change Summary

| File | Action | Purpose |
|------|--------|---------|
| `src/BoxWise.Server/Models/Tag.cs` | Modify | Add `Items` nav property |
| `src/BoxWise.Server/Data/Configurations/ItemConfiguration.cs` | Modify | Wire `WithMany(t => t.Items)` |
| `src/BoxWise.Server/Repositories/TagRepository.cs` | Modify | RenameAsync, DeleteAsync, GetAllAsync Include |
| `src/BoxWise.Server/Endpoints/TagEndpoints.cs` | Modify | PUT/DELETE endpoints, ItemCount mapping |
| `src/BoxWise.Shared/Dtos/TagDto.cs` | Modify | Add `int ItemCount` |
| `src/BoxWise.Shared/Dtos/RenameTagRequest.cs` | Create | New DTO record |
| `src/BoxWise.Server.Tests/Repositories/TagRepositoryTests.cs` | Modify | 5 new tests |
| `src/BoxWise.Client/Services/TagService.cs` | Modify | Create/Rename/Delete methods |
| `src/BoxWise.Client/Components/TagManageDialog.razor` | Create | Tag management dialog |
| `src/BoxWise.Client/Pages/Settings.razor` | Create | Settings page (4 entries) |
| `src/BoxWise.Client/wwwroot/css/app.css` | Modify | `.bw-settings-*` styles |
| `src/BoxWise.Client/Layout/MainLayout.razor` | Modify | 4 tabs + simplified AppBar |
| `src/BoxWise.Client/Pages/Browse.razor` | Modify | Remove gear icon |
