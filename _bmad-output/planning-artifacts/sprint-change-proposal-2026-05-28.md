# Sprint Change Proposal — 物品编辑功能

**项目:** 箱知 · BoxWise
**日期:** 2026-05-28
**变更范围:** Minor — 新增 Epic 7
**提出者:** Elvis

---

## 1. Issue Summary

**问题陈述：** 用户录入物品后，无法修改物品的任何字段（名称、位置、标签、备注）。如果录入信息有误或收纳位置变更，唯一选择是删除后重新录入——这是破坏性操作，会丢失录入时间和录入者信息。

**触发场景：** 用户在日常使用中发现录入错误或收纳位置变更时，需要编辑功能。该功能在 PRD §6.2 被标记为 "v2 待验证"，现确认需要提前实现。

**问题类型：** 新需求浮现（原定 v2 功能，用户确认需要）

---

## 2. Impact Analysis

### Epic Impact

| Epic | 影响 | 说明 |
|------|------|------|
| Epic 1-6 (已完成) | 无影响 | 编辑功能是纯增量，不影响已完成工作 |
| **新增 Epic 7** | 新增 | 物品编辑 — 3 个 Story |

### Artifact Conflicts

| 工件 | 冲突 | 所需变更 |
|------|------|---------|
| PRD §6.2 | "物品编辑功能 — 待 v2" 列为 Out of Scope | 移入 In Scope |
| PRD §4.3 FR-10 | "[ASSUMPTION: v1 不做编辑功能]" | 移除假设，新增 FR-21 |
| Architecture §API | 路由表缺少 `PUT /api/items/{id}` | 新增路由 |
| UX Design §ItemDetail | 详情页仅定义只读展示 | 新增编辑模式交互 |
| Epics §FR Coverage | 无 FR 对应编辑功能 | 新增 FR-21 → Epic 7 |

### Technical Impact

| 层 | 变更 |
|------|------|
| Shared | 新增 `UpdateItemRequest` DTO |
| Server/Repository | `ItemRepository` 新增 `UpdateAsync` |
| Server/Endpoints | `ItemEndpoints` 新增 `PUT /api/items/{id}` |
| Client/Service | `ItemService` 新增 `UpdateAsync` |
| Client/Pages | `ItemDetail.razor` 新增内联编辑模式 |
| Tests | Repository + Endpoint 测试 |

---

## 3. Recommended Approach

**选择：Option 1 — 直接调整（新增 Epic 7）**

| 维度 | 评估 |
|------|------|
| 工作量 | 中等 — 3 个 Story |
| 风险 | 低 — CRUD 模式的自然扩展，不破坏现有功能 |
| 时间线 | 新增 1 个 Epic |
| 架构一致性 | 完全遵循已有 Repository + Minimal API + MudBlazor 模式 |

**设计决策：**
- **照片不在编辑范围内** —— 照片替换通过删除→重新录入实现
- **内联编辑模式** —— 在 ItemDetail.razor 切换编辑/展示模式，用户选择
- **编辑权限** —— 任何已认证用户可编辑任何物品（与删除权限一致）
- **Note 清空语义** —— 前端空输入框发送 `""`，后端将 `""` 或 `null` 均存为 `null`
- **Tags 校验** —— 严格模式，任一 Tag 不存在→400（与 CreateAsync 一致）
- **EF Core Tags 更新** —— 必须 `.Include(i => i.Tags)` 加载现有集合，使用 `.Clear()` + `.AddRange()`，禁止直接赋值

---

## 4. Detailed Change Proposals

### 4.1 Epic 结构

**新增 Epic 7: 物品编辑**

| Story | 内容 | 层 |
|-------|------|-----|
| 7-1 | Repository UpdateAsync + PUT 端点 + UpdateItemRequest DTO | Server/Shared |
| 7-2 | ItemDetail.razor 内联编辑模式 | Client |
| 7-3 | Repository + Endpoint 测试 | Tests |

### 4.2 PRD Changes

**OLD (PRD §6.2):**
> - 物品编辑功能 —— 待 v2 验证需求

**NEW:**
> *(移除该行 — 编辑功能已移入 In Scope)*

**NEW (PRD §4.x):**
> #### FR-21: 物品编辑
> 用户可以在物品详情页编辑已录入物品的名称、位置、标签和备注。
> **Consequences (testable):**
> - 详情页提供"编辑"按钮，点击后切换为编辑模式
> - 编辑模式下可修改名称、位置、标签、备注
> - 保存后字段更新，返回展示模式
> - 取消后丢弃修改，返回展示模式
> - 照片不在编辑范围内
> - 清空备注输入框时，后端将空字符串转为 null 存储
> - Tags 校验为严格模式：任一 Tag 不存在→400（与创建行为一致）
> - 位置不存在→400，物品不存在→404

### 4.3 Architecture Changes

**NEW API Route:**
```
PUT /api/items/{id}  认证  编辑物品（名称/位置/标签/备注）
  .Produces<ItemDto>(200)
  .ProducesProblem(400)
  .Produces(404)
  .ProducesProblem(401)
```

### 4.4 DTO Changes

**NEW: `UpdateItemRequest`**
```csharp
public record UpdateItemRequest(string Name, int LocationId, List<int> TagIds, string? Note);
```

### 4.5 Repository Changes

**NEW: `ItemRepository.UpdateAsync`**
```csharp
public async Task<Item?> UpdateAsync(int id, string name, int locationId,
    List<int> tagIds, string? note)
```

**实现注意事项：**
- Tags 多对多更新必须：`.Include(i => i.Tags)` 加载现有集合 → `item.Tags.Clear()` → `item.Tags.AddRange(newTags)` → 禁止直接赋值 `item.Tags = newList`（EF Core 隐式连接表陷阱）
- Note 空字符串统一存 `null`
- 校验规则与 `CreateAsync` 一致：名称为空→ArgumentException、名称超长→ArgumentException、位置不存在→ArgumentException、Tag 不存在→ArgumentException
- 物品不存在时返回 `null`（端点映射为 404）

### 4.6 Client Changes

**ItemDetail.razor:**
- 新增 `_editing` 状态标志控制展示/编辑模式
- 展示模式：新增"编辑"按钮
- 编辑模式：字段切换为可编辑控件 + "保存"/"取消"按钮

**ItemService:**
- 新增 `UpdateAsync(int id, UpdateItemRequest request)` → 返回 `Task<ItemDto?>`（更新后的完整 DTO，`ItemDetail.razor` 直接使用避免二次 GET）

---

## 5. Implementation Handoff

**变更范围等级：Minor** — 可由 Developer agent 直接实现

**实施顺序：**
1. Story 7-1: DTO + Repository + 端点（后端基础）
2. Story 7-2: 前端内联编辑 UI
3. Story 7-3: 测试

**成功标准：**
- 从详情页可进入编辑模式，修改名称/位置/标签/备注
- 保存后字段更新且返回展示模式
- 取消后丢弃修改
- 所有现有测试仍通过，新测试覆盖 UpdateAsync + PUT 端点
