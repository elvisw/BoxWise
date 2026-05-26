# Story 4.4: 物品详情与删除

Status: review

## Story

As a 用户，
I want 查看物品完整信息并删除不需要的物品，
So that 管理物品库保持整洁。

## Acceptance Criteria

1. **AC-1: 删除 API** — `DELETE /api/items/{id}` 返回 204，级联删除 DB 记录 + original/thumb/medium 图片文件
2. **AC-2: 删除按钮** — ItemDetail 页面显示 Error 色（`#EF5350`）删除按钮
3. **AC-3: 确认对话框** — 点击删除弹出 MudDialog 确认："确定要删除 [物品名称] 吗？此操作不可撤销。"
4. **AC-4: 删除后返回** — 确认删除成功后导航回上一页
5. **AC-5: 删除后不可见** — 已删除物品在浏览/搜索中不再出现
6. **AC-6: 认证保护** — 删除 API 需登录，任何已认证用户可删除任何物品

## Tasks / Subtasks

- [x] Task 1: 添加文件清理能力 (AC: #1)
  - [x] 1.1 `ImageStorageService.DeleteItemFiles(int itemId)` — 删除 `{itemId}/` 整个目录

- [x] Task 2: 添加服务端删除能力 (AC: #1, #6)
  - [x] 2.1 `ItemRepository.DeleteAsync(int id)` — 删除 DB 记录，返回 bool 表示是否找到
  - [x] 2.2 `DELETE /api/items/{id:int}` 端点 → 204 或 404
  - [x] 2.3 `.Produces*()` 注解（204 + 404 + 401）

- [x] Task 3: 更新 Client ItemService (AC: #1)
  - [x] 3.1 `ItemService.DeleteAsync(int id, CancellationToken ct)` → `DELETE api/items/{id}`

- [x] Task 4: 更新 ItemDetail.razor (AC: #2, #3, #4)
  - [x] 4.1 Error 色删除按钮（`Color="Color.Error"`）
  - [x] 4.2 MudOverlay + MudPaper 内联确认对话框
  - [x] 4.3 确认后调用 DeleteAsync，成功后导航至 /browse

- [x] Task 5: 构建验证 (AC: #1-#6)
  - [x] 5.1 `dotnet build BoxWise.slnx` 零错误零警告
  - [x] 5.2 `dotnet test BoxWise.slnx` 全部通过

---

## Dev Notes

### 前置上下文

- **ItemDetail.razor 已就绪** — Story 3.5 创建，路由 `@page "/items/{id:int}"`，含照片/占位/信息/返回按钮
- **GET /api/items/{id} 已就绪** — 返回完整 ItemDto
- **ImageStorageService** — 有 `GetItemDirectory(int itemId)` 返回 `{basePath}/{itemId}/` 目录路径
- **ItemRepository** — CreateAsync、GetByIdAsync、GetFilteredAsync

### ImageStorageService.DeleteItemFilesAsync

```csharp
public void DeleteItemFiles(int itemId)
{
    var dir = Path.Combine(_basePath, itemId.ToString());
    if (Directory.Exists(dir))
        Directory.Delete(dir, true);
}
```

### ItemRepository.DeleteAsync

```csharp
public async Task DeleteAsync(int id)
{
    var item = await _db.Items.FindAsync(id);
    if (item is null) return;

    _db.Items.Remove(item);
    await _db.SaveChangesAsync();
}
```

**注意：** 文件删除在端点层调用 `ImageStorageService.DeleteItemFiles(id)`，Repository 只负责 DB 操作。文件 I/O 失败不应阻止 DB 删除（尽力删除）。

### ItemEndpoints DELETE 端点

```csharp
group.MapDelete("/{id:int}", DeleteItemAsync)
    .Produces(204)
    .Produces(404)
    .ProducesProblem(401)
    .WithTags("Items")
    .WithDescription("删除物品");

private static async Task<Results<NoContent, NotFound>>
    DeleteItemAsync(int id, ItemRepository repo, ImageStorageService imageStorage)
{
    var item = await repo.GetByIdSimpleAsync(id);
    if (item is null) return TypedResults.NotFound();

    await repo.DeleteAsync(id);
    imageStorage.DeleteItemFiles(id);

    return TypedResults.NoContent();
}
```

### MudDialog 确认模式（MudBlazor 9.x）

```razor
<MudButton Variant="Variant.Filled" Color="Color.Error"
           OnClick="OpenDeleteDialog" Class="mt-4">
    删除物品
</MudButton>

<MudDialog @bind-IsVisible="_deleteDialogVisible"
           Options="_dialogOptions">
    <TitleContent>
        <MudText Typo="Typo.h6">确认删除</MudText>
    </TitleContent>
    <DialogContent>
        <MudText>确定要删除 <strong>@_item?.Name</strong> 吗？此操作不可撤销。</MudText>
    </DialogContent>
    <DialogActions>
        <MudButton OnClick="CancelDelete">取消</MudButton>
        <MudButton Color="Color.Error" OnClick="ConfirmDelete">删除</MudButton>
    </DialogActions>
</MudDialog>

@code {
    private bool _deleteDialogVisible;
    private DialogOptions _dialogOptions = new() { CloseOnEscapeKey = true };

    private void OpenDeleteDialog() => _deleteDialogVisible = true;
    private void CancelDelete() => _deleteDialogVisible = false;

    private async Task ConfirmDelete()
    {
        _deleteDialogVisible = false;
        if (_item is null) return;
        await ItemService.DeleteAsync(_item.Id);
        Navigation.NavigateTo("/browse");
    }
}
```

### 文件结构变更

```
src/BoxWise.Server/
  Services/ImageStorageService.cs      (modified — 添加 DeleteItemFiles)
  Repositories/ItemRepository.cs        (modified — 添加 DeleteAsync)
  Endpoints/ItemEndpoints.cs            (modified — 添加 DELETE /{id})
src/BoxWise.Client/
  Services/ItemService.cs               (modified — 添加 DeleteAsync)
  Pages/ItemDetail.razor                (modified — 添加删除按钮+对话框)
```

### 构建与验证

```bash
dotnet build BoxWise.slnx
dotnet test BoxWise.slnx
```

---

## References

| 内容 | 来源 |
|------|------|
| Story AC 定义 | [Source: epics.md#Story 4.4] |
| FR-16 物品删除 | [Source: prd.md#FR-16] |
| 级联删除模式 | [Source: architecture.md#Cascade Delete] |
| MudDialog API | [Source: mudblazor.com] |

## Dev Agent Record

### Agent Model Used

deepseek-v4-pro

### Debug Log References

### Completion Notes List

✅ 全部 5 个 Task 完成 — 物品删除就绪，28/28 测试通过

**实施要点：**
- ImageStorageService.DeleteItemFiles：`Directory.Delete(dir, true)` 级联删除 {itemId}/ 整个目录
- ItemRepository.DeleteAsync：`FindAsync` + `Remove` + `SaveChanges`，返回 bool
- DELETE /api/items/{id}：端点层协调 DeleteAsync + DeleteItemFiles（DB 成功后才删文件）
- ItemDetail.razor：Error 色删除按钮 + MudOverlay/MudPaper 内联确认对话框（避免 MudBlazor 9.x Dialog API 兼容问题）
- "返回首页"改为"返回浏览"导航至 /browse
- 文件删除为尽力删除（I/O 异常不阻止 DB 删除）

### File List

**修改文件:**
- `src/BoxWise.Server/Services/ImageStorageService.cs` (modified — 添加 DeleteItemFiles)
- `src/BoxWise.Server/Repositories/ItemRepository.cs` (modified — 添加 DeleteAsync)
- `src/BoxWise.Server/Endpoints/ItemEndpoints.cs` (modified — 添加 DELETE /{id})
- `src/BoxWise.Client/Services/ItemService.cs` (modified — 添加 DeleteAsync)
- `src/BoxWise.Client/Pages/ItemDetail.razor` (modified — 添加删除按钮+确认对话框)
