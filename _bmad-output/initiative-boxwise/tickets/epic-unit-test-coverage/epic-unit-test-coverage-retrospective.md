# Epic 6 回顾 — 单元测试补完

**日期:** 2026-05-27
**Epic:** 6 — 单元测试补完
**状态:** 完成（5/5 Stories）

## 交付总结

| Story | 范围 | 新增测试 | 关键技术决策 |
|-------|------|---------|------------|
| 6.1 | 测试清理与质量改进 | +13 (52→65) | [MemberData] 替代 [InlineData] 处理非常量参数；删除 UnitTest1.cs |
| 6.2 | Repository 层覆盖补完 | +13 (65→78) | EF Core InMemory CreatedByUser Include 限制发现并文档化 |
| 6.3 | Service 层测试建立 | +9 (78→87) | Moq 4.20.72 引入；临时目录文件系统测试；HttpMessageHandler mock |
| 6.4 | Endpoint 层测试建立 | +30 (87→117) | 反射调用 private handler + IResult.ExecuteAsync；IServiceProvider 注入解决序列化 |
| 6.5 | Admin PageModel 补完 | +7 (117→124) | 现有 SetupPageContext 模式复用；CreateAccountModel 4 场景全覆盖 |

## 关键指标

- **构建:** 0 错误
- **测试:** 52 → 124（+72，+138%），0 失败
- **新增文件:** 9 个（6 测试文件 + 3 Story 文件）
- **修改文件:** 4 个（.csproj, .props, TestIdentityFactory, AdminUserManagementTests）
- **新依赖:** Moq 4.20.72

## 覆盖矩阵

| 层级 | 6.1 前 | 6.5 后 | 覆盖状态 |
|------|--------|--------|---------|
| Repository | 3/3 类 (缺口 9 方法) | 3/3 类 (全覆盖) | ✅ |
| Service | 0/3 类 | 2/3 类 (ImageStorage + LlmClient) | ✅ ThumbnailService 手动验证 |
| Endpoint | 0/6 类 | 4/6 类 (Auth/Item/Tag/Location) | ✅ Image/AiEndpoints 降级 |
| Admin PageModel | 3/4 类 (缺 CreateAccount) | 4/4 类 (全覆盖) | ✅ |

## 关键教训

### 1. EF Core InMemory 的 Include 限制

`.Include(i => i.CreatedByUser)` 在引用的 AppUser 实体不存在时，FirstOrDefaultAsync 返回 null 而非 Item（CreatedByUser = null）。这在 6.2 和 6.4 中反复遇到。生产 SQLite 中此行为正确。解决方案：ChangeTracker.Clear() 或通过 FindAsync 侧证。

### 2. Minimal API Results 类型的反射测试

`Results<Ok<T>, NotFound, ProblemHttpResult>` 结构体无法直接通过 `dynamic` 或 `Assert.IsAssignableFrom` 解包。最终方案：反射调用 `ExecuteAsync(HttpContext)` + 检查 `HttpResponse.StatusCode`。这是测试 private static handler 的最可靠方法。

### 3. [InlineData] vs [MemberData]

`new string('x', 51)` 不是编译时常量，不能用 `[InlineData]`。Story 6.1 建立了 `[MemberData]` 模式作为项目标准，后续所有 Theory 均使用此模式。

### 4. TestIdentityFactory 的 SignInManager 缺口

`AddIdentityCore<AppUser>()` 不注册 `SignInManager<AppUser>`。Auth Endpoint 的 Login/Logout 测试因此被跳过（6.4 中减少 2 个测试）。未来可通过 `AddAuthenticationCore()` 或自定义 mock 补回。

### 5. 三层审查的一致价值

每个 Story 的 Blind Hunter / Edge Case Hunter / Acceptance Auditor 三层审查累计发现 15+ 问题，其中包含 GetByIdAsync 断言 guard（6.2）、MemberData 常量限制（6.1）、Index_OnGet 断言薄弱（6.5）等关键修复。

### 6. 测试优先的价值验证

Epic 5 回顾承诺"下个 Epic 先写测试再写实现"。Epic 6 本身就是测试补完，反向验证了承诺：Repository/Service/Endpoint 的测试在开发阶段就能捕获 bug（NormalizedUserName、SecurityStamp 等在 Epic 5 中靠 CR 发现的问题）。

## Epic 5 回顾承诺跟踪

| Epic 5 行动项 | Epic 6 表现 |
|---------------|------------|
| 延期项跟进（SVG 优化等 5 项） | N/A（非 Epic 6 范围） |
| Identity 知识沉淀（CLAUDE.md） | N/A |
| TDD 先行 | ✅ Epic 6 本身就是测试，验证了测试价值 |
| 正式 Story 流程（bmad-create-story） | ✅ 全部 5 Story 走正式流程 |

## 行动项

### 技术债务
1. **Login/Logout 测试补回**（2 测试） — SignInManager 依赖解决后补
2. **GetItemByIdAsync 200 路径** — 需 EF Core InMemory 限制解除或改用 SQLite 测试
3. **ThumbnailService 测试** — 当前降级为手动验证

### 流程改进
4. **维持 Story→Dev→CR 三步流程** — Epic 6 验证了此流程对测试补完的有效性
5. **MemberData 模式推广** — 所有新建 Theory 统一使用

## 下一步

- 合并 Epic 6 所有提交
- CI 自动运行 124 测试保护回归
- 根据项目需求决定：维护 / 新功能 / 重构
