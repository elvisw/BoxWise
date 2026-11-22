# Epic 3 回顾 — 物品录入与智能识别

**日期:** 2026-05-24
**Epic:** Epic 3 — 物品录入与智能识别
**状态:** 完成（5/5 Stories, 全部 review）
**FR 覆盖:** FR-1~8, FR-20 | **AR 覆盖:** AR-4

---

## Epic 摘要

| 指标 | 数值 |
|------|------|
| Stories 总数 | 5 |
| 完成 Stories | 5 (100%) |
| 新增文件 | 22 |
| 修改文件 | 16 |
| 构建状态 | 0 错误 0 警告 |
| 测试覆盖 | 22 tests (ItemRepository 6, Location 10, Tag 5, Misc 1) |
| 代码审查发现问题 | 27（全部修复） |

**交付成果:**
- Item 实体 + ItemTag 多对多 + ItemDto + LocationName 字段
- 图片上传管线（SkiaSharp MIT 许可证，300px+1200px 缩略图，后台异步生成）
- 物品录入 API（POST /api/items） + 详情 API（GET /api/items/{id}）
- AI 识别客户端（OpenAI 兼容 Vision API，15s 超时，regex JSON 兜底解析）
- 前端录入页面（ItemEntry.razor，拍照/跳过双入口，连续收纳模式）
- 前端详情页面（ItemDetail.razor，照片/位置名称/录入者）

---

## 成功经验

### 1. SkiaSharp 替代 ImageSharp — 正确的技术决策
ImageSharp 4.0 强制商业许可证 → 立即切换为 SkiaSharp 3.119.2（MIT）。代码改动仅集中在 `ThumbnailService.cs` 一个文件，架构文档同步更新。这是"在撞墙时快速转向"的典型案例。

### 2. 代码审查质量持续提升
Epic 3 五轮代码审查共发现 27 个问题，全部修复后才进入下一个 Story：
- 🔴 严重：JSON 大小写敏感 × 1、PhotoPath 未写入 × 1、大文件未保护 × 2、流未释放 × 1
- 🟡 中等：DTO 命名 × 2、双重查询 × 1、Select 状态缺失 × 1、位置只显示 ID × 1
- 🟢 建议：注释 × 4、测试覆盖 × 3、其他 × 10

审查发现密度呈下降趋势（3.1 为 8 项，3.5 为 4 项），说明团队在学习。

### 3. `ItemDto` 字段级设计演进
Epic 3.1 定义了 9 字段 ItemDto，Epic 3.5 审查发现位置只显示 ID → 新增 LocationName 字段 → 双向同步更新 POST 和 GET 端点映射。DTO 随需求演进而非一次定死，体现了敏捷设计。

### 4. 前端基础设施复用
Story 3.4 录入页面直接复用了 Story 2.4 的 LocationTree.razor 和 TagFilter.razor 组件，通过 EventCallback 模式通信（Blazor WASM 不支持 `@bind-` 自定义属性名）。AppState.ContinuousLocationId 跨组件共享连续收纳状态。

---

## 挑战与教训

### 1. ImageSharp 商业许可证意外
Story 3.1 实现中使用 ImageSharp 4.0 构建失败（需要许可证密钥）。虽然架构文档指定了 ImageSharp，但许可证限制未被提前识别。最终 SkiaSharp 是更好的长期选择——MIT 许可证、API 类似、跨平台。

**教训:** 引入新 NuGet 包前检查许可证兼容性（特别是主版本号 ≥4 的开源项目）。

### 2. Story 状态再次积压
与前两个 Epic 相同，5 个 Story 均停留在 `review` 而非 `done`。代码审查通过后未推进状态。

**教训:** 急需自动化"审查通过 → done"的流程。

### 3. 前端组件浏览器验证缺失
Story 3.4 和 3.5 的 Blazor 组件仅通过 `dotnet build` 验证，未经浏览器实际渲染。MudBlazor 初始化渲染、`locationTree` 初始选中值加载等问题仅在理论层面被代码审查捕获，但运行时行为未验证。

**教训:** Epic 4 前端 Story 需包含浏览器 E2E 验证步骤。

### 4. ItemDto 演进导致批量修改
Epic 3.5 添加 LocationName 字段时，所有创建 ItemDto 的位置（POST 和 GET 端点）均需同步更新。positional record 的参数顺序对调用点不友好——一旦插入新参数，所有调用方需重新排列。

**教训:** 考虑将高频变化的 DTO 从 positional record 改为 `{ get; init; }` 属性风格记录。

---

## 技术债务

| 项目 | 严重度 | 说明 |
|------|--------|------|
| 202 后立即查看详情页存在竞态条件 | 🟡 | 上传返回 202，缩略图后台生成，ThumbPath 延迟写入 |
| 双服务器模式图片 URL 相对路径 | 🟡 | Client (5001) 的 `<img src>` 无法访问 Server (5000) 的图片 |
| GetByIdAsync 零测试覆盖 | 🟡 | ItemRepository 的新增查询无单元测试 |
| 位置名称仅在 GET 详情端点填充 | 🟢 | POST 创建时 LocationName 固定为 null |
| Blazor WASM @bind- 限制 | 🟢 | 不支持自定义属性名绑定，只能用 EventCallback 手动模式 |

---

## Epic 2 回顾行动项跟进

| 行动项（Epic 2 Retro） | 状态 |
|---|---|
| 推进 Story 状态至 done | ❌ 仍未完成（积压加剧） |
| 前端组件浏览器验证 | ❌ Epic 3 仍未引入 |
| 并发安全 checklist | ✅ Epic 3 所有 Repository 方法均有 DbUpdateException 捕获 |
| 架构文档实时同步 | ✅ ImageSharp→SkiaSharp 已同步 |

---

## 下一个 Epic 准备

### Epic 4: 查找浏览与生产部署

**依赖 Epic 3 的交付物:**
- ✅ Item 实体 + ItemDto — 搜索/浏览/详情页的数据基础
- ✅ 图片上传管线 — 缩略图网格展示的图片源
- ✅ 位置体系 + 标签系统 — 筛选器的数据源
- ✅ 前端录入页 — 物品创建入口

**Epic 4 Stories (6):**
- 4-1: 搜索功能（GET /api/items?q= 模糊搜索）
- 4-2: 缩略图网格浏览（Browse.razor + ItemCard.razor）
- 4-3: 位置与标签筛选
- 4-4: 物品详情与删除
- 4-5: PWA 离线支持
- 4-6: Docker 容器化部署

**准备就绪评估:**
- ✅ 所有 API 基础就绪（Location/Tag/Item CRUD）
- ✅ 前端组件库充实（LocationTree, TagFilter, ImageUploader, ContinuityBanner, ItemDetail）
- ⚠️ 搜索功能需新增 EF Core LIKE 查询（新领域）
- ⚠️ PWA Service Worker 策略（现有模板但未配置缓存策略）
- ⚠️ Docker 部署（需 Caddyfile + docker-compose.yml）

---

## 行动项

1. **自动化 Story 状态推进** — owner: Developer — Epic 4 启动前建立
2. **前端浏览器验证纳入 Story 任务** — owner: Developer — 每个前端 Story 添加逐项验证步骤
3. **`ItemDto` 改为属性风格 record** — owner: Developer — Epic 4 第一个 Story 时重构
4. **`GetByIdAsync` 补充单元测试** — owner: Developer — Epic 4 启动时

---

## 结论

Epic 3 成功建立了 BoxWise 的物品管理核心——从拍照采集、AI 识别、位置分配到入库保存的完整闭环。5 个 Story 全部交付，22 个单元测试通过，ImageSharp→SkiaSharp 的技术转向快速果断。27 项代码审查发现问题全部修复。物品库已可以开始积累数据，为 Epic 4（搜索/浏览/部署）提供了坚实的物品数据基础。

*回顾完成时间: 2026-05-24*
*下一回顾: Epic 4 完成后*
