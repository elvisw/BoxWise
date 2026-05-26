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
- 移除 `LocationTree` 的 `@ref`

### 5.3 `TagEndpoints.cs` (Server)

新增端点：

| 方法 | 端点 | 功能 |
|------|------|------|
| PUT | `/api/tags/{id}` | 重命名标签 |
| DELETE | `/api/tags/{id}` | 删除标签（解除物品关联） |

### 5.4 `TagRepository.cs` (Server)

新增方法：

- `RenameAsync(int id, string name)` — 校验唯一性后重命名
- `DeleteAsync(int id)` — 删除标签，级联删除 `ItemTag` 中间表记录

### 5.5 `TagService.cs` (Client)

新增方法：

- `CreateAsync(CreateTagRequest)` → `POST /api/tags`
- `RenameAsync(int id, RenameTagRequest)` → `PUT /api/tags/{id}`
- `DeleteAsync(int id)` → `DELETE /api/tags/{id}`

### 5.6 `TagDto.cs` (Shared)

添加 `ItemCount` 字段：

```csharp
public record TagDto(int Id, string Name, int ItemCount);
```

### 5.7 `RenameTagRequest.cs` (Shared, new)

```csharp
public record RenameTagRequest(string Name);
```

### 5.8 `TagRepository.cs` — GetAllAsync

修改查询，Include ItemTags 并计算 ItemCount：

```csharp
return await _db.Tags
    .OrderBy(t => t.Name)
    .Select(t => new { t.Id, t.Name, ItemCount = t.ItemTags.Count })
    .ToListAsync();
```

### 5.9 `TagEndpoints.cs` — GetAllTagsAsync

修改为返回带 ItemCount 的 TagDto。

## 6. Future Extension Points

- `/settings/account` — 账户信息子页面（路由架构已支持）
- `/settings/ai` — AI 配置（如有需要）
- 设置页列表项通过 `NavigationManager` 导航到子页面，或通过 `IDialogService` 打开弹窗

## 7. Implementation Order

1. **Backend Tag CRUD** — TagRepository + TagEndpoints + TagDto
2. **Client Tag Service** — TagService CUD methods
3. **TagManageDialog** — 新建弹窗组件
4. **Settings.razor** — 新建设置页
5. **MainLayout** — 4 Tab + 精简顶栏
6. **Browse** — 移除齿轮入口
7. **Build & Test** — 验证编译 + 运行测试
