# Epic 1 回顾 — 项目搭建与账户认证

**日期:** 2026-05-24
**Epic:** Epic 1 — 项目搭建与账户认证
**状态:** 完成（3/3 Stories, 全部 review）
**参与角色:** Developer, Architect, Product Owner

---

## Epic 摘要

| 指标 | 数值 |
|------|------|
| Stories 总数 | 3 |
| 完成 Stories | 3 (100%) |
| 新增文件 | 26 |
| 修改文件 | 8 |
| 构建状态 | 0 错误 0 警告 |
| FR 覆盖 | FR-17, FR-18, FR-19 |
| AR 覆盖 | AR-1, AR-2, AR-5, AR-6, AR-8 |

**交付成果:**
- .NET 10 三项目解决方案骨架（Blazor WASM + ASP.NET Core Web API + Shared）
- ASP.NET Core Identity 认证系统（Cookie 认证 + 自定义 CookieAuthenticationStateProvider）
- Server 端 Razor Pages 管理后台（`/admin` 账户列表 + 创建账户）
- Admin 角色授权体系（种子数据 + AdminOnly 策略）

---

## 成功经验

### 1. 架构决策执行到位
所有架构文档中的关键决策（AR-1 ~ AR-8）均被正确实施：
- Minimal API + TypedResults 模式在 Story 1.2 建立，后续故事一致遵循
- CPM 集中包管理避免了版本冲突
- `IEntityTypeConfiguration<T>` 模式预留扩展点

### 2. 种子数据从问题到闭环
Story 1.2 的种子数据只创建了 admin 用户但未分配角色，Story 1.3 修复并完善了角色创建和分配逻辑。代码审查进一步强化：将 `Users.Any()` 检查改为 `FindByNameAsync("admin")` 独立检查，解决了 admin 用户被删除后无法恢复的死锁问题。

### 3. 代码审查机制有效
Story 1.3 的并行代码审查发现了：
- 🔴 3 项严重问题（admin 死锁、用户名空格、record 模型绑定）
- 🟡 4 项改进建议（N+1 查询、密码验证、架构文档同步、代码注释）
- 🟢 4 项可选改进

全部 11 项问题修复后构建依然 0 错误 0 警告。

### 4. DTO 模式统一
所有 DTO 保持 `record`/`class` 类型 + `BoxWise.Shared.Dtos` 命名空间的一致模式。Server ↔ Client 共享 DTOs 避免了重复定义。

---

## 挑战与教训

### 1. Story 状态跟踪偏差
三个 Story 均在 `review` 状态而非 `done`。代码审查流程完成后应将 story 标记为 `done`，以准确反映 Epic 完成度。

**行动项:** 代码审查通过后立即将 story 状态从 `review` → `done`

### 2. 架构文档与实际实现的漂移
architecture.md 中 "Admin Identification: IsAdmin Flag" 描述使用实体布尔字段，但实际实现使用了 ASP.NET Core Identity 的 `IdentityRole`。这是在实施过程中做出的合理技术决策，但架构文档未同步更新。Story 1.3 代码审查中已修复。

**教训:** 架构偏离时即时更新文档。不要让文档债务累积。

### 3. N+1 查询模式
Index 页面对每个用户单独调用 `IsInRoleAsync`，在 N 个用户时产生 N+1 次数据库查询。初版代码未考虑此性能影响。代码审查后改用 `GetUsersInRoleAsync` + `HashSet` 交叉引用。

**教训:** 涉及集合操作的代码默认应考虑批量查询模式。

### 4. Razor Pages 管道顺序依赖
`MapRazorPages()` 必须在 `MapFallbackToFile("index.html")` 之前注册，否则 SPA 回退拦截所有 Razor Pages 路由。这是一个无编译期保护的隐式契约。已添加注释说明。

---

## 技术债务

| 项目 | 严重度 | 说明 |
|------|--------|------|
| Cookie SecurePolicy | 🟡 中等 | 当前为 `SameAsRequest`，生产环境应改为 `Always`（需配置判断） |
| Admin 前端简陋 | 🟢 低 | 纯 HTML + inline CSS，后续可迁移到 MudBlazor 统一风格 |

---

## 下一个 Epic 准备

### Epic 2: 位置体系与标签管理

**依赖 Epic 1 的交付物:**
- ✅ Identity 认证系统 — Epic 2 所有 API 端点依赖认证
- ✅ Admin 角色 — 后续可能需要管理位置/标签（v1 非必须）
- ✅ 项目骨架 + CPM + EF Core — 数据层基础设施就绪

**Epic 2 Stories:**
- 2-1: Location 实体 + 物化路径 CRUD
- 2-2: 位置树浏览 API
- 2-3: 标签系统
- 2-4: 前端位置树选择器 + 标签选择器

**准备就绪评估:**
- ✅ 认证系统就绪，`[Authorize]` 保护所有 API 端点
- ✅ EF Core + SQLite 数据层已连接，迁移流程已验证
- ✅ Entity 配置模式（`IEntityTypeConfiguration<T>`）已建立
- ✅ DTO 共享模式已建立
- ⚠️ 物化路径模式为首次使用，需关注 `Path.StartsWith()` 查询效率

---

## 行动项

1. **将 Epic 1 Stories 标记为 done** — owner: Developer — 代码审查通过后执行
2. **Cookie SecurePolicy 评估** — owner: Developer — 在部署 Story (4-6) 中处理
3. **架构文档即时更新纪律** — owner: 全体 — 决策偏离时立即同步 architecture.md

---

## 结论

Epic 1 成功搭建了 BoxWise 项目的完整骨架。认证系统可用，管理后台就绪，技术栈（.NET 10 + Blazor WASM + SQLite + Identity）稳定运行。三次代码审查的教训将直接应用于 Epic 2，提升后续开发质量。团队已准备好进入位置体系与标签管理的开发。

---

*回顾完成时间: 2026-05-24*
*下一回顾: Epic 2 完成后*
