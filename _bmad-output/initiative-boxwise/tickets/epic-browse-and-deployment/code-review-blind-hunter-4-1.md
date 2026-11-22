# Blind Hunter — Story 4.1 搜索功能

你只收到 diff，没有项目上下文、没有规格说明、没有外部文档。请仅凭以下 diff 找出潜在的安全漏洞、逻辑错误、并发问题和代码异味。

## Diff

```
diff --git a/src/BoxWise.Shared/Dtos/ItemSummaryDto.cs b/src/BoxWise.Shared/Dtos/ItemSummaryDto.cs
new file mode 100644
index 0000000..5a3c0af
--- /dev/null
+++ b/src/BoxWise.Shared/Dtos/ItemSummaryDto.cs
@@ -0,0 +1,9 @@
+namespace BoxWise.Shared.Dtos;
+
+public record ItemSummaryDto(
+    int Id,
+    string Name,
+    string? ThumbPath,
+    string? LocationPath,
+    IReadOnlyList<string> TagNames,
+    DateTime CreatedAt);

diff --git a/src/BoxWise.Server/Repositories/ItemRepository.cs b/src/BoxWise.Server/Repositories/ItemRepository.cs
index 33bf0d6..c86a5a2 100644
--- a/src/BoxWise.Server/Repositories/ItemRepository.cs
+++ b/src/BoxWise.Server/Repositories/ItemRepository.cs
@@ -59,4 +59,24 @@ public class ItemRepository
             .Include(i => i.Location)
             .FirstOrDefaultAsync(i => i.Id == id);
     }
+
+    public async Task<List<Item>> SearchAsync(string query)
+    {
+        if (string.IsNullOrWhiteSpace(query))
+            return [];
+
+        var q = query.Trim();
+
+        return await _db.Items
+            .Include(i => i.Location)
+            .Include(i => i.Tags)
+            .Where(i => i.Name.Contains(q)
+                        || (i.Note != null && i.Note.Contains(q))
+                        || i.Tags.Any(t => t.Name.Contains(q)))
+            .OrderByDescending(i => i.Name.StartsWith(q))
+            .ThenBy(i => i.Name)
+            .Take(50)
+            .AsSplitQuery()
+            .ToListAsync();
+    }
 }

diff --git a/src/BoxWise.Server/Endpoints/ItemEndpoints.cs b/src/BoxWise.Server/Endpoints/ItemEndpoints.cs
index 231cb67..697e39c 100644
--- a/src/BoxWise.Server/Endpoints/ItemEndpoints.cs
+++ b/src/BoxWise.Server/Endpoints/ItemEndpoints.cs
@@ -26,6 +26,12 @@ public static class ItemEndpoints
             .WithTags("Items")
             .WithDescription("获取物品详情");
 
+        group.MapGet("/", SearchItemsAsync)
+            .Produces<ItemSummaryDto[]>(200)
+            .ProducesProblem(401)
+            .WithTags("Items")
+            .WithDescription("搜索物品（关键词模糊匹配名称/备注/标签）");
+
         return group;
     }
 
@@ -75,4 +81,21 @@ public static class ItemEndpoints
 
         return TypedResults.Ok(dto);
     }
+
+    private static async Task<Ok<ItemSummaryDto[]>>
+        SearchItemsAsync(string? q, ItemRepository repo, HttpContext httpContext)
+    {
+        var items = string.IsNullOrWhiteSpace(q)
+            ? []
+            : await repo.SearchAsync(q);
+
+        var dtos = items.Select(i => new ItemSummaryDto(
+            i.Id, i.Name, i.ThumbPath,
+            i.Location?.Path,
+            i.Tags.Select(t => t.Name).ToList(),
+            i.CreatedAt)).ToArray();
+
+        httpContext.Response.Headers["X-Total-Count"] = dtos.Length.ToString();
+        return TypedResults.Ok(dtos);
+    }
 }

diff --git a/src/BoxWise.Client/Services/ItemService.cs b/src/BoxWise.Client/Services/ItemService.cs
index 149bccb..4d0da0e 100644
--- a/src/BoxWise.Client/Services/ItemService.cs
+++ b/src/BoxWise.Client/Services/ItemService.cs
@@ -18,4 +18,11 @@ public class ItemService
         if (!response.IsSuccessStatusCode) return null;
         return await response.Content.ReadFromJsonAsync<ItemDto>(cancellationToken);
     }
+
+    public async Task<List<ItemSummaryDto>?> SearchAsync(string query, CancellationToken cancellationToken = default)
+    {
+        var response = await _http.GetAsync($"api/items?q={Uri.EscapeDataString(query)}", cancellationToken);
+        if (!response.IsSuccessStatusCode) return null;
+        return await response.Content.ReadFromJsonAsync<List<ItemSummaryDto>>(cancellationToken);
+    }
 }

diff --git a/src/BoxWise.Client/Components/SearchBar.razor b/src/BoxWise.Client/Components/SearchBar.razor
new file mode 100644
index 0000000..dd5c6e8
--- /dev/null
+++ b/src/BoxWise.Client/Components/SearchBar.razor
@@ -0,0 +1,43 @@
+@implements IDisposable
+
+<MudTextField @bind-Value="_searchText"
+              Placeholder="搜索物品..."
+              Adornment="Adornment.Start"
+              AdornmentIcon="@Icons.Material.Filled.Search"
+              Immediate="true"
+              Variant="Variant.Outlined"
+              FullWidth="true" />
+
+@code {
+    [Parameter]
+    public EventCallback<string> SearchTextChanged { get; set; }
+
+    private string _searchText = "";
+    private System.Threading.Timer? _debounceTimer;
+
+    protected override void OnInitialized()
+    {
+        _debounceTimer = new System.Threading.Timer(_ =>
+        {
+            InvokeAsync(() => SearchTextChanged.InvokeAsync(_searchText));
+        }, null, Timeout.Infinite, Timeout.Infinite);
+    }
+
+    protected override void OnParametersSet()
+    {
+        // Trigger debounce when _searchText changes via MudTextField binding
+        if (_searchText != _lastDebouncedText)
+        {
+            _lastDebouncedText = _searchText;
+            _debounceTimer?.Change(300, Timeout.Infinite);
+        }
+    }
+
+    private string _lastDebouncedText = "";
+
+    public void Dispose()
+    {
+        _debounceTimer?.Dispose();
+    }
+}

diff --git a/src/BoxWise.Client/Pages/Home.razor b/src/BoxWise.Client/Pages/Home.razor
index 9001e0b..d6a5b01 100644
--- a/src/BoxWise.Client/Pages/Home.razor
+++ b/src/BoxWise.Client/Pages/Home.razor
@@ -1,7 +1,162 @@
-﻿@page "/"
+@page "/"
+@attribute [Authorize]
+@using Microsoft.AspNetCore.Authorization
 
-<PageTitle>Home</PageTitle>
+@inject AppState AppState
+@inject NavigationManager Navigation
+@inject ItemService ItemService
 
-<h1>Hello, world!</h1>
+<MudText Typo="Typo.h4" Class="mb-4">箱知 · BoxWise</MudText>
 
-Welcome to your new app.
+@if (AppState.IsLoggedIn)
+{
+    <MudText Typo="Typo.body1" Class="mb-2">
+        欢迎，@AppState.CurrentUserName
+    </MudText>
+
+    <SearchBar SearchTextChanged="OnSearchTextChanged" />
+
+    <MudDivider Class="my-4" />
+
+    @if (_isSearching)
+    {
+        <div class="d-flex justify-center my-8">
+            <MudProgressCircular Color="Color.Primary" Indeterminate="true" />
+        </div>
+    }
+    else if (!string.IsNullOrEmpty(_query) && _results is not null)
+    {
+        if (_results.Count > 0)
+        {
+            <MudText Typo="Typo.caption" Class="mb-2">
+                找到 @_results.Count 件物品
+            </MudText>
+
+            <MudList T="ItemSummaryDto">
+                @foreach (var item in _results)
+                {
+                    <MudListItem T="ItemSummaryDto" OnClick='() => Navigation.NavigateTo($"/items/{item.Id}")' Class="pa-3">
+                        <div class="d-flex align-center" style="gap: 12px; width: 100%;">
+                            @if (!string.IsNullOrEmpty(item.ThumbPath))
+                            {
+                                <img src="api/images/@item.Id?type=thumb"
+                                     alt="@item.Name"
+                                     style="width: 72px; height: 72px; object-fit: cover; border-radius: 4px;" />
+                            }
+                            else
+                            {
+                                <MudIcon Icon="@Icons.Material.Filled.Image"
+                                         Size="Size.Large"
+                                         Style="width: 72px; height: 72px; color: #BDBDBD;" />
+                            }
+                            <div class="flex-grow-1">
+                                <MudText Typo="Typo.h6">@item.Name</MudText>
+                                @if (!string.IsNullOrEmpty(item.LocationPath))
+                                {
+                                    <MudText Typo="Typo.caption" Color="Color.Default">
+                                        @FormatLocationPath(item.LocationPath)
+                                    </MudText>
+                                }
+                                @if (item.TagNames.Count > 0)
+                                {
+                                    <div class="d-flex flex-wrap mt-1" style="gap: 4px;">
+                                        @foreach (var tag in item.TagNames)
+                                        {
+                                            <MudChip T="string" Size="Size.Small" Variant="Variant.Outlined" Color="Color.Info">@tag</MudChip>
+                                        }
+                                    </div>
+                                }
+                            </div>
+                            <MudIcon Icon="@Icons.Material.Filled.ChevronRight" Color="Color.Default" />
+                        </div>
+                    </MudListItem>
+                }
+            </MudList>
+        }
+        else
+        {
+            <div class="d-flex flex-column align-center justify-center my-8" style="color: #9E9E9E;">
+                <MudIcon Icon="@Icons.Material.Filled.SearchOff" Size="Size.Large" />
+                <MudText Typo="Typo.body1" Class="mt-2">未找到匹配的物品</MudText>
+                <MudText Typo="Typo.caption">试试其他关键词</MudText>
+            </div>
+        }
+    }
+    else
+    {
+        <div class="d-flex flex-column align-center justify-center my-8" style="color: #9E9E9E;">
+            <MudIcon Icon="@Icons.Material.Filled.ManageSearch" Size="Size.Large" />
+            <MudText Typo="Typo.body1" Class="mt-2">输入关键词搜索物品</MudText>
+            <MudText Typo="Typo.caption">按名称、备注或标签模糊匹配</MudText>
+        </div>
+    }
+
+    <MudGrid Class="mt-4">
+        <MudItem xs="6">
+            <MudButton Variant="Variant.Filled" Color="Color.Primary"
+                       OnClick='() => Navigation.NavigateTo("/entry")'
+                       FullWidth="true" Class="pa-8">
+                <MudStack>
+                    <MudIcon Icon="@Icons.Material.Filled.AddBox" Size="Size.Large" />
+                    <MudText Typo="Typo.h6">录入物品</MudText>
+                </MudStack>
+            </MudButton>
+        </MudItem>
+        <MudItem xs="6">
+            <MudPaper Elevation="2" Class="pa-8 text-center" Style="background:#E0E0E0">
+                <MudIcon Icon="@Icons.Material.Filled.Search" Size="Size.Large" Color="Color.Default" />
+                <MudText Typo="Typo.h6">浏览物品</MudText>
+                <MudText Typo="Typo.caption">即将推出</MudText>
+            </MudPaper>
+        </MudItem>
+    </MudGrid>
+
+    @if (AppState.IsAdmin)
+    {
+        <MudButton Variant="Variant.Outlined" Color="Color.Primary"
+                   Href="/admin" Class="mt-4" FullWidth="true">
+            管理后台
+        </MudButton>
+    }
+}
+else
+{
+    <MudText Typo="Typo.body1" Class="mb-4">
+        请登录以管理您的物品库
+    </MudText>
+    <MudButton Variant="Variant.Filled" Color="Color.Primary"
+               OnClick='() => Navigation.NavigateTo("/login")'>
+        前往登录
+    </MudButton>
+}
+
+@code {
+    private string _query = "";
+    private List<ItemSummaryDto>? _results;
+    private bool _isSearching;
+
+    private async Task OnSearchTextChanged(string query)
+    {
+        _query = query ?? "";
+
+        if (string.IsNullOrWhiteSpace(_query))
+        {
+            _results = null;
+            _isSearching = false;
+            return;
+        }
+
+        _isSearching = true;
+        StateHasChanged();
+
+        _results = await ItemService.SearchAsync(_query);
+
+        _isSearching = false;
+    }
+
+    private static string FormatLocationPath(string? path)
+    {
+        if (string.IsNullOrEmpty(path)) return "";
+        return string.Join(" → ", path.Split('/', StringSplitOptions.RemoveEmptyEntries));
+    }
+}
+```
