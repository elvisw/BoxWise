# Epic 2 回顾 — 位置体系与标签管理

**日期:** 2026-05-24
**Epic:** Epic 2 — 位置体系与标签管理
**状态:** 完成（4/4 Stories, 全部 review）
**FR 覆盖:** FR-5, FR-14, FR-15 | **AR 覆盖:** AR-3

---

## Epic 摘要

| 指标 | 数值 |
|------|------|
| Stories 总数 | 4 |
| 完成 Stories | 4 (100%) |
| 新增文件 | 27 |
| 修改文件 | 10 |
| 构建状态 | 0 错误 0 警告 |
| 代码审查发现问题 | 33（全部修复） |

**交付成果:**
- Location 实体 + 物化路径 CRUD（`POST/PUT/DELETE /api/locations`）
- 位置树浏览 API（`GET /api/locations` + `GET /api/locations/{id}/children`）
- Tag 实体 + 标签系统（`GET/POST /api/tags` + TagRepository.GetOrCreateAsync）
- 前端可复用组件（LocationTree.razor + TagFilter.razor + HTTP 服务层）

---

## 成功经验

### 1. 物化路径模式一次做对
架构文档定义的材料化路径（`Path TEXT NOT NULL`，格式 `/1/2/3/`）在 Story 2.1 中正确实现。查询策略分层清晰：一级子节点用 `ParentId` 直查（O(log N)），子树查询用 `Path LIKE` 留待后续 Story。迁移生成的外部键约束（`DeleteBehavior.Restrict`）和 Path B-tree 索引均正确。

### 2. 代码审查持续改进效果显著
Epic 2 四轮代码审查共发现 33 个问题：
- 🔴 严重：TOCTOU 竞态 × 3、TypedResults 嵌套 × 2、EventCallback 不传播 × 1
- 🟡 中等：校验不一致 × 5、缺少注解 × 3、N+1 查询 × 2、O(n²) 递归 × 1
- 🟢 建议：代码风格 × 6、文档同步 × 4、加载状态 × 2、其他 × 4

每轮审查后的问题都立即修复，下一个 Story 不复现——说明审查-修复循环真正发挥了作用。

### 3. MudBlazor 9.x API 源码验证
Story 2.4（前端组件）遭遇 MudBlazor 9.x 与 8.x 的重大 API 变更。通过直接从 GitHub 源码验证 API（使用 `open-websearch`），发现并修复了：
- `ActivatedValue` → `SelectedValue`（MudTreeView 重命名）
- `Filter`/`MultiSelection` → `SelectionMode` enum（MudChipSet 重构）
- `List<T>` → `IReadOnlyCollection<T>`（SelectedValues 类型变更）

这些发现已记录在 CLAUDE.md 中，供后续 Story 参考。

### 4. Repository 模式一致性
LocationRepository、TagRepository 遵循统一的命名约定（CreateAsync/RenameAsync/DeleteAsync/GetAllAsync）、相同的异常模式（ArgumentException → 400、KeyNotFoundException → 404）、一致的 DI 注册（AddScoped）。新加入的开发者可以基于任何一个 Repository 理解所有。

---

## 挑战与教训

### 1. Story 状态跟踪再次积压
与 Epic 1 相同，Epic 2 的 4 个 Story 均停留在 `review` 状态而非 `done`。代码审查通过后需要手动将状态推进至 `done`。

**行动项：** 制定明确的"审查通过 → done"流程，或在 BMad 工作流中让 `code-review` 工作流自动推进状态。

### 2. 前端开发缺少浏览器验证
Story 2.4 的 Blazor 组件仅通过 `dotnet build` 验证编译通过，未经浏览器实际渲染。MudBlazor 的运行时行为（如 MudChipSet 点击交互、MudTreeView 展开折叠）未在 E2E 测试中覆盖。

**教训：** 后续前端 Story 需要启动 Client 并在浏览器中验证组件交互。

### 3. 并发安全意识偏晚
TOCTOU 竞态条件（`AnyAsync` + `SaveChangesAsync` 之间的窗口期）在 Story 2.1（DeleteAsync）和 Story 2.3（CreateAsync/GetOrCreateAsync）中重复出现。直到代码审查阶段才发现并修复。如果在实现阶段就考虑并发安全，可以避免返工。

**教训：** 设计阶段显式列出并发场景作为 checklist 项。

### 4. 架构文档格式同步延迟
architecture.md 中物化路径格式示例 `"001/003/007/"` 与实际实现 `"/1/3/7/"` 不一致，直到 Story 2.1 代码审查才发现。Epic 1 也有类似问题（IsAdmin 字段 vs IdentityRole）。

**教训：** 实施每个 Story 后即时对照架构文档检查，而非等待代码审查。

---

## 技术债务

| 项目 | 严重度 | 说明 | 来源 Story |
|------|--------|------|-----------|
| HttpClient BaseAddress 硬编码 | 🟡 | `https://localhost:5000/` 在生产须修改 | 2.4 |
| 缺少 CancellationToken 支持 | 🟡 | Blazor 组件销毁后 HTTP 请求未取消 | 2.4 |
| 缺少单元测试框架 | 🟡 | 项目无常驻测试项目 | Epic 1 |
| SortOrder 仅默认 0 | 🟢 | API 可设置，但 CreateAsync 中未赋值新位置 SortOrder | 2.1 |

---

## Epic 1 回顾行动项跟进

| 行动项（Epic 1 Retro） | 状态 |
|---|---|
| 将 Epic 1 Stories 标记为 done | ⏳ 待执行 |
| Cookie SecurePolicy 评估 | ⏳ 延至部署 Story (4-6) |
| 架构文档即时更新纪律 | ✅ Epic 2 中已改善（每 Story 审查后同步） |
| Story 前 N+1 查询预防 | ✅ TagRepository 用 `FirstOrDefaultAsync` 而非循环 |

---

## 下一个 Epic 准备

### Epic 3: 物品录入与智能识别

**依赖 Epic 2 的交付物:**
- ✅ Location 实体 + CRUD（Story 2.1）— Item 实体引用 LocationId
- ✅ 位置树浏览 API（Story 2.2）— 录入页位置选择器
- ✅ Tag 系统（Story 2.3）— 物品标签关联
- ✅ LocationTree.razor + TagFilter.razor（Story 2.4）— 录入 UI 组件

**Epic 3 Stories:**
- 3-1: Item 实体 + 图片上传管线
- 3-2: 物品录入 API + 位置分配 + 入库保存
- 3-3: AI 识别集成 + 降级策略
- 3-4: 前端录入页面
- 3-5: 物品详情展示 + 录入者标识

**关键依赖与技术准备:**
- ✅ Item.LocationId 外键需要 Location 表存在 → 已就绪
- ✅ ItemTag 多对多中间表 → 需在 Story 3.1 创建（Tag 表已就绪）
- ✅ 前端位置选择器 → LocationTree 组件已就绪
- ✅ 前端标签选择器 → TagFilter 组件已就绪
- ⚠️ ImageSharp 缩略图生成 — 首次使用，需验证 Linux 兼容性
- ⚠️ AI API 集成 — 需配置文件管理 API key

**准备就绪评估:** Epic 3 所需的所有 Epic 2 基础组件均已就绪。位置系统和标签系统稳定可用。主要挑战在于 AI 集成（需配置外部 API）和图片处理管线。

---

## 行动项

1. **推进 Story 状态至 done** — owner: Developer — 立即
2. **前端组件浏览器验证** — owner: Developer — Story 3.4 前完成
3. **并发安全 checklist** — owner: Developer — 每个 Story 设计阶段检查
4. **架构文档实时同步** — owner: 全体 — 每个 Story 完成后对照

---

## 结论

Epic 2 成功建立了 BoxWise 的收纳体系基础设施——位置层级树和标签分类系统全部可用，前端可复用组件就绪。四轮代码审查发现的 33 个问题全部修复，代码质量和架构一致性持续提升。整个数据层（Location + Tag + Identity）已稳固，为 Epic 3（物品录入与 AI 集成）提供了坚实基础。

*回顾完成时间: 2026-05-24*
*下一回顾: Epic 3 完成后*
