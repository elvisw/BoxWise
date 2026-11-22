# Blind Hunter — Story 4.4 Item Detail & Delete

You receive ONLY the diff below. No project context, no spec, no story file.

## Diff

```
diff --git a/src/BoxWise.Client/Pages/ItemDetail.razor b/src/BoxWise.Client/Pages/ItemDetail.razor
index c4fa7f2..bf46adc 100644
--- a/src/BoxWise.Client/Pages/ItemDetail.razor
+++ b/src/BoxWise.Client/Pages/ItemDetail.razor
@@ -47,16 +47,37 @@ else if (_item is not null)
         录入时间: @_item.CreatedAt.ToString("yyyy-MM-dd HH:mm")
     </MudText>
 
-    <MudButton Variant="Variant.Filled" Color="Color.Primary"
-               OnClick='() => Navigation.NavigateTo("/")' Class="mt-4">
-        返回首页
-    </MudButton>
+    <div class="d-flex" style="gap:8px;">
+        <MudButton Variant="Variant.Filled" Color="Color.Primary"
+                   OnClick='() => Navigation.NavigateTo("/browse")' Class="mt-4">
+            返回浏览
+        </MudButton>
+        <MudButton Variant="Variant.Filled" Color="Color.Error"
+                   OnClick="OpenDeleteDialog" Class="mt-4">
+            删除物品
+        </MudButton>
+    </div>
+
+    @if (_showDeleteDialog)
+    {
+        <MudOverlay Visible="true" DarkBackground="true">
+            <MudPaper Elevation="4" Class="pa-6" Style="max-width:400px;margin:auto;margin-top:20vh;">
+                <MudText Typo="Typo.h6" Class="mb-4">确认删除</MudText>
+                <MudText Class="mb-4">确定要删除 <strong>@_item?.Name</strong> 吗？此操作不可撤销。</MudText>
+                <div class="d-flex justify-end" style="gap:8px;">
+                    <MudButton OnClick="CancelDelete">取消</MudButton>
+                    <MudButton Color="Color.Error" OnClick="ConfirmDelete">删除</MudButton>
+                </div>
+            </MudPaper>
+        </MudOverlay>
+    }
 }
 
 @code {
     private ItemDto? _item;
     private bool _loading = true;
     private bool _notFound;
+    private bool _showDeleteDialog;
 
     [Parameter] public int Id { get; set; }
 
@@ -76,4 +97,15 @@ else if (_item is not null)
             _loading = false;
         }
     }
+
+    private void OpenDeleteDialog() => _showDeleteDialog = true;
+    private void CancelDelete() => _showDeleteDialog = false;
+
+    private async Task ConfirmDelete()
+    {
+        _showDeleteDialog = false;
+        if (_item is null) return;
+        await ItemService.DeleteAsync(Id);
+        Navigation.NavigateTo("/browse");
+    }
 }
diff --git a/src/BoxWise.Client/Services/ItemService.cs b/src/BoxWise.Client/Services/ItemService.cs
index 50d020a..f2e60db 100644
--- a/src/BoxWise.Client/Services/ItemService.cs
+++ b/src/BoxWise.Client/Services/ItemService.cs
@@ -12,6 +12,12 @@ public class ItemService
         _http = http;
     }
 
+    public async Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default)
+    {
+        var response = await _http.DeleteAsync($"api/items/{id}", cancellationToken);
+        return response.IsSuccessStatusCode;
+    }
+
     public async Task<ItemDto?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
     {
         var response = await _http.GetAsync($"api/items/{id}", cancellationToken);
diff --git a/src/BoxWise.Server/Endpoints/ItemEndpoints.cs b/src/BoxWise.Server/Endpoints/ItemEndpoints.cs
index 4232f82..5fab788 100644
--- a/src/BoxWise.Server/Endpoints/ItemEndpoints.cs
+++ b/src/BoxWise.Server/Endpoints/ItemEndpoints.cs
@@ -2,6 +2,7 @@ using Microsoft.AspNetCore.Http.HttpResults;
 using Microsoft.AspNetCore.Identity;
 using BoxWise.Server.Models;
 using BoxWise.Server.Repositories;
+using BoxWise.Server.Services;
 using BoxWise.Shared.Dtos;
 
 namespace BoxWise.Server.Endpoints;
@@ -32,6 +33,13 @@ public static class ItemEndpoints
             .WithTags("Items")
             .WithDescription("搜索/筛选/浏览物品（可选参数 q/locationId/tagId）");
 
+        group.MapDelete("/{id:int}", DeleteItemAsync)
+            .Produces(204)
+            .Produces(404)
+            .ProducesProblem(401)
+            .WithTags("Items")
+            .WithDescription("删除物品（级联删除图片文件）");
+
         return group;
     }
 
@@ -101,4 +109,15 @@ public static class ItemEndpoints
         httpContext.Response.Headers["X-Total-Count"] = dtos.Length.ToString();
         return TypedResults.Ok(dtos);
     }
+
+    private static async Task<Results<NoContent, NotFound>>
+        DeleteItemAsync(int id, ItemRepository repo, ImageStorageService imageStorage)
+    {
+        var deleted = await repo.DeleteAsync(id);
+        if (!deleted) return TypedResults.NotFound();
+
+        imageStorage.DeleteItemFiles(id);
+
+        return TypedResults.NoContent();
+    }
 }
diff --git a/src/BoxWise.Server/Repositories/ItemRepository.cs b/src/BoxWise.Server/Repositories/ItemRepository.cs
index 2cb2338..4a302f3 100644
--- a/src/BoxWise.Server/Repositories/ItemRepository.cs
+++ b/src/BoxWise.Server/Repositories/ItemRepository.cs
@@ -60,6 +60,16 @@ public class ItemRepository
             .FirstOrDefaultAsync(i => i.Id == id);
     }
 
+    public async Task<bool> DeleteAsync(int id)
+    {
+        var item = await _db.Items.FindAsync(id);
+        if (item is null) return false;
+
+        _db.Items.Remove(item);
+        await _db.SaveChangesAsync();
+        return true;
+    }
+
     public async Task<List<Item>> GetFilteredAsync(int? locationId, List<int>? tagIds, string? query)
     {
         IQueryable<Item> q = _db.Items
diff --git a/src/BoxWise.Server/Services/ImageStorageService.cs b/src/BoxWise.Server/Services/ImageStorageService.cs
index 82a5d39..5a2ee48 100644
--- a/src/BoxWise.Server/Services/ImageStorageService.cs
+++ b/src/BoxWise.Server/Services/ImageStorageService.cs
@@ -36,4 +36,11 @@ public class ImageStorageService
 
     public string GetMediumPath(int itemId)
         => Path.Combine(_basePath, itemId.ToString(), "medium.jpg");
+
+    public void DeleteItemFiles(int itemId)
+    {
+        var dir = Path.Combine(_basePath, itemId.ToString());
+        if (Directory.Exists(dir))
+            Directory.Delete(dir, true);
+    }
 }
```

Review this diff adversarially. Look for bugs, security issues, concurrency problems, error handling gaps, anti-patterns, and correctness issues. Output findings as a Markdown list with severity labels [CRITICAL], [HIGH], [MEDIUM], [LOW].
