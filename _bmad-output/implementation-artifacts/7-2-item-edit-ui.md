# Story 7.2: 物品编辑 — 前端内联编辑 UI

Status: done

## Story

As a 用户，
I want 在物品详情页直接编辑物品的名称、位置、标签和备注，
so that 修正录入错误或更新收纳位置时不需要删除重建。

## Acceptance Criteria

**AC-1: 详情页增加编辑按钮**
- 展示模式下，在"返回浏览"和"删除物品"按钮之间显示"编辑"按钮（Primary 色）
- 点击"编辑"按钮 → 切换至编辑模式

**AC-2: 编辑模式 — 字段可编辑**
- 名称：`MudTextField` 绑定编辑值，必填
- 位置：复用 `LocationTree` 组件，通过 `SelectedLocationId` + `SelectedLocationIdChanged` 双向绑定
- 标签：复用 `TagFilter` 组件，通过 `SelectedTagIds` + `SelectedTagIdsChanged` 双向绑定
- 备注：`MudTextField` 绑定编辑值，可选
- 照片区域：保持只读（照片不在编辑范围）

**AC-3: 编辑模式 — 保存**
- 显示"保存"按钮（Success 色）+ "取消"按钮（Default 色），替代"编辑"和"删除"按钮
- 名称非空 → "保存"按钮 enabled；名称为空 → disabled
- 点击"保存" → 调用 `ItemService.UpdateAsync(Id, request)` → 成功后刷新 `_item` 状态 → 返回展示模式
- 保存失败（返回 null）→ 不切换模式，可重试

**AC-4: 编辑模式 — 取消**
- 点击"取消" → 丢弃编辑值，恢复原始值 → 返回展示模式
- 编辑值需在进入编辑模式时从当前 `_item` 复制（避免直接修改绑定影响展示）

**AC-5: 展示模式回归**
- 保存或取消后：隐藏编辑控件，恢复展示布局
- 更新后的数据立即反映在展示视图中（名称、位置路径、标签、备注）
- 照片区域不变

**AC-6: 安全退化**
- `LocationTree` 加载失败 → 仍可编辑其他字段，位置保持原值
- `TagFilter` 加载失败 → 仍可编辑其他字段，标签保持原值

## Tasks / Subtasks

- [ ] Task 1: 添加编辑状态变量和初始化逻辑 (AC: 1, 4)
  - [ ] 1.1 添加 `_editing` bool
  - [ ] 1.2 添加 `_editName` / `_editNote` / `_editLocationId` / `_editTagIds` 编辑副本字段
  - [ ] 1.3 添加 `_isSaving` bool（保存中禁用按钮）
  - [ ] 1.4 `EnterEditMode()` — 从 `_item` 复制值到编辑副本
  - [ ] 1.5 `CancelEdit()` — 丢弃编辑值，`_editing = false`

- [ ] Task 2: 实现编辑模式 UI (AC: 2)
  - [ ] 2.1 照片保持只读展示（编辑模式下同上）
  - [ ] 2.2 名称：`<MudTextField @bind-Value="_editName" Label="物品名称" Required="true" />`
  - [ ] 2.3 位置：`<LocationTree SelectedLocationId="_editLocationId" SelectedLocationIdChanged="v => _editLocationId = v" />`
  - [ ] 2.4 标签：`<TagFilter SelectedTagIds="_editTagIds" SelectedTagIdsChanged="v => _editTagIds = v" />`
  - [ ] 2.5 备注：`<MudTextField @bind-Value="_editNote" Label="备注" Lines="3" />`

- [ ] Task 3: 实现保存/取消逻辑 (AC: 3, 4, 5)
  - [ ] 3.1 "保存"按钮 disabled 条件：`string.IsNullOrWhiteSpace(_editName) || _isSaving`
  - [ ] 3.2 `SaveAsync()` — 构造 `UpdateItemRequest` → 调用 `ItemService.UpdateAsync(Id, request)` → 成功则 `_item = result; _editing = false`
  - [ ] 3.3 "取消"按钮 → `CancelEdit()`
  - [ ] 3.4 保存中显示 "保存中..."（`_isSaving = true` 期间）

- [ ] Task 4: 调整展示模式按钮布局 (AC: 1)
  - [ ] 4.1 编辑模式下隐藏"编辑"/"删除"按钮，显示"保存"/"取消"
  - [ ] 4.2 "返回浏览"按钮在编辑模式下仍可见

## Dev Notes

### 关键复用组件接口

**LocationTree** (`src/BoxWise.Client/Components/LocationTree.razor`):
```razor
<LocationTree SelectedLocationId="_editLocationId"
              SelectedLocationIdChanged="v => _editLocationId = v" />
```
- `SelectedLocationId`: `int?` — 当前选中的位置 ID
- `SelectedLocationIdChanged`: `EventCallback<int?>` — 选中变化回调

**TagFilter** (`src/BoxWise.Client/Components/TagFilter.razor`):
```razor
<TagFilter SelectedTagIds="_editTagIds"
           SelectedTagIdsChanged="v => _editTagIds = v" />
```
- `SelectedTagIds`: `IReadOnlyCollection<int>` — 选中的标签 ID 集合
- `SelectedTagIdsChanged`: `EventCallback<IReadOnlyCollection<int>>` — 变化回调

### 数据流

```
编辑按钮 → EnterEditMode()（复制 _item 值到编辑副本）
  → 用户修改
  → 保存：ItemService.UpdateAsync(Id, request) → 成功 → _item = 返回值 → _editing = false
  → 取消：CancelEdit() → _editing = false
```

### 现有 ItemDetail.razor 结构（需保留）

- Route: `@page "/items/{id:int}"`
- `@attribute [Authorize]`
- DI: `ItemService`, `IDialogService`, `NavigationManager`, `HttpClient`
- `OnInitializedAsync`: 调用 `ItemService.GetByIdAsync(Id)` 加载 `_item`
- `FormatLocationPath`: 静态辅助方法
- `OpenDeleteDialogAsync` + `ConfirmDeleteAsync`: 删除逻辑（编辑模式下隐藏但不移除代码）

### Story 7-1 交付物

- `ItemService.UpdateAsync(int id, UpdateItemRequest request)` → `Task<ItemDto?>` — 已就绪
- `UpdateItemRequest(string Name, int LocationId, List<int> TagIds, string? Note)` — 已就绪

### MudBlazor 9.x 注意事项

- MudTreeView: `SelectedValue` + `SelectedValueChanged`（非 `ActivatedValue`）
- MudChipSet: `SelectionMode="SelectionMode.MultiSelection"`
- MUD0002 分析器会自动捕获 v8 API 使用

### 文件清单

| 操作 | 文件 |
|------|------|
| **修改** | `src/BoxWise.Client/Pages/ItemDetail.razor` |

### 参考

- Sprint Change Proposal: `_bmad-output/planning-artifacts/sprint-change-proposal-2026-05-28.md`
- Story 7-1: `_bmad-output/implementation-artifacts/7-1-item-edit-backend.md`
- LocationTree: `src/BoxWise.Client/Components/LocationTree.razor`
- TagFilter: `src/BoxWise.Client/Components/TagFilter.razor`
- MudBlazor 9.x API: 见 `CLAUDE.md` § MudBlazor 9.x API 参考

## Dev Agent Record

### Agent Model Used

### Debug Log References

### Completion Notes List

### File List
