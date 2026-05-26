# Settings Page & Navigation Restructure

**Date:** 2026-05-27
**Status:** Approved

## 1. Motivation

- 位置管理入口藏在浏览页齿轮图标中，难以发现
- 标签管理只有后端 API，前端完全没有管理 UI
- 未来需要集中管理设置类功能（账户信息等）
- 退出登录按钮占用顶栏空间，功能上更适合放在设置页

## 2. Design Decisions

| 决策 | 选择 | 理由 |
|------|------|------|
| 设置入口 | 底部导航第 4 个 Tab | 与首页/录入/浏览同级，易发现 |
| 设置页形式 | 列表式入口 + 弹窗 | 当前功能用弹窗，架构支持未来子页面 `/settings/xxx` |
| 位置管理 | 弹窗（已有组件） | `LocationManageDialog` 已实现，接入即可 |
| 标签管理 | 新增弹窗，完整 CRUD | 后端缺 Rename/Delete，需补齐 |
| 退出登录 | 从顶栏移至设置页 | 设置页集中管理，顶栏精简 |
| 浏览页齿轮 | 移除 | 设置入口已在底部导航，无需重复 |

## 3. Navigation Changes

### 3.1 Bottom Nav: 3 → 4 Tabs

```
Before:  首页 | 录入 | 浏览
After:   首页 | 录入 | 浏览 | 设置
```

### 3.2 AppBar Simplification

| 元素 | Before | After |
|------|--------|-------|
| 后退按钮 | `Size.Medium` + `mr-2` | `Size.Small`，无多余 margin |
| 标题 | 保留 | 保留 |
| 用户名 | 保留 | 保留 |
| 退出登录 | `MudIconButton` 在顶栏右侧 | **移除**，移至设置页 |

### 3.3 Browse Page

- 移除 `IDialogService` 注入
- 移除齿轮 `MudIconButton`
- 移除 `OpenLocationManageDialog` 方法
- `LocationTree` 的 `@ref` 保留（未来可能仍有刷新需求）

## 4. New Files

### 4.1 `src/BoxWise.Client/Pages/Settings.razor`

设置页，路由 `/settings`。列表式布局，每项为可点击行：

- **位置管理** → 打开 `LocationManageDialog`
- **标签管理** → 打开 `TagManageDialog`（新建）
- **退出登录** → 红色文字，点击执行登出
- **账户信息** → 灰色禁用，标注"后续版本提供"

### 4.2 `src/BoxWise.Client/Components/TagManageDialog.razor`

标签管理弹窗（MudDialog），功能：

- 显示所有标签列表（名称 + 物品计数）
- **创建**：顶部输入框 + 添加按钮
- **重命名**：行内编辑（✏️ 图标 → 文本框 → 确认/取消）
- **删除**：行内确认（🗑️ 图标 → 确认提示 → 删除）
- 操作后自动刷新列表

## 5. Modified Files

### 5.1 `MainLayout.razor`

- 底部导航：添加第 4 个 Tab"设置"（`/settings`）
- 顶栏：移除退出登录 `MudIconButton`，后退按钮改为 `Size.Small` 去 `mr-2`
- 注册 `/settings` 路由的 nav 可见性

### 5.2 `Browse.razor`

- 移除 `@inject IDialogService`
- 移除齿轮图标 button + `OpenLocationManageDialog` 方法
- `LocationTree` 的 `@ref` 保留 — 导航切换时 Blazor 自动重建组件实例，无需手动刷新

### 5.3 `Tag.cs` Model (Server)

添加反向导航属性以支持 ItemCount 查询：

```csharp
public ICollection<Item> Items { get; set; } = new List<Item>();
```

### 5.4 `ItemConfiguration.cs` (Server)

更新 Many-to-Many 配置，连接 Tag 反向导航：

```csharp
builder.HasMany(x => x.Tags)
    .WithMany(t => t.Items)
    .UsingEntity("ItemTag");
```

### 5.5 `TagEndpoints.cs` (Server)

新增端点：

| 方法 | 端点 | 功能 | 错误处理 |
|------|------|------|---------|
| PUT | `/api/tags/{id}` | 重命名标签 | 不存在→404，重名→400 |
| DELETE | `/api/tags/{id}` | 删除标签（解除物品关联） | 不存在→404 |

### 5.6 `TagRepository.cs` (Server)

新增方法：

- `RenameAsync(int id, string name)` — 校验唯一性后重命名
- `DeleteAsync(int id)` — 删除标签，级联删除 `ItemTag` 中间表记录

### 5.7 `TagService.cs` (Client)

新增方法：

- `CreateAsync(CreateTagRequest)` → `POST /api/tags`
- `RenameAsync(int id, RenameTagRequest)` → `PUT /api/tags/{id}`
- `DeleteAsync(int id)` → `DELETE /api/tags/{id}`

### 5.8 `TagDto.cs` (Shared)

添加 `ItemCount` 字段：

```csharp
public record TagDto(int Id, string Name, int ItemCount);
```

### 5.9 `RenameTagRequest.cs` (Shared, new)

```csharp
public record RenameTagRequest(string Name);
```

### 5.10 `TagRepository.cs` — GetAllAsync

利用新增的 `Tag.Items` 导航属性计算 ItemCount：

```csharp
return await _db.Tags
    .OrderBy(t => t.Name)
    .Select(t => new { t.Id, t.Name, ItemCount = t.Items.Count })
    .ToListAsync();
```

### 5.11 `TagEndpoints.cs` — GetAllTagsAsync

修改映射为返回带 ItemCount 的 TagDto。同步更新 `TagFilter.razor` 中所有引用 `TagDto` 的代码（构造函数参数增加 ItemCount）。

### 5.12 `TagRepositoryTests.cs` (Test)

新增测试：

- `RenameAsync_Success` — 正常重命名
- `RenameAsync_DuplicateName_Throws` — 重名校验
- `RenameAsync_NotFound_Throws` — 不存在
- `DeleteAsync_Success` — 正常删除
- `DeleteAsync_NotFound_Throws` — 不存在

## 6. Future Extension Points

- `/settings/account` — 账户信息子页面（路由架构已支持）
- `/settings/ai` — AI 配置（如有需要）
- 设置页列表项通过 `NavigationManager` 导航到子页面，或通过 `IDialogService` 打开弹窗

## 7. Implementation Order

1. **Tag Model + Config** — Tag.Items 导航属性 + ItemConfiguration 更新
2. **Backend Tag CRUD** — TagRepository (Rename/Delete/GetAll ItemCount) + TagEndpoints + DTO 更新
3. **Tag Unit Tests** — TagRepositoryTests 新增 5 个测试
4. **Client Tag Service** — TagService CUD 方法
5. **TagManageDialog** — 新建弹窗组件
6. **Settings.razor** — 新建设置页
7. **MainLayout** — 4 Tab + 精简顶栏
8. **Browse** — 移除齿轮入口
9. **TagFilter/TagDto 引用更新** — 所有 TagDto 构造调用增加 ItemCount
10. **Build & Test** — 验证编译 + 运行全部测试
