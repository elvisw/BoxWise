# 测试覆盖清单

**目标项目：** `src/BoxWise.Server.Tests/`
**当前状态：** 58 测试总计 = 52 有意义 + 5 辅助类中的非测试方法 + 1 占位死代码

---

## 1. Repository 层

### 1.1 ItemRepositoryTests（11 测试，2 方法缺失）

| 方法 | Happy-path | 异常路径 | 缺口 |
|------|-----------|---------|------|
| `CreateAsync` | ✅ `_ValidInput_Succeeds` | ✅ 空名/超长名/无效位置/无效标签 | 完整 |
| `CreateAsync` | ✅ `_EmptyTagIds_Succeeds` | — | 完整 |
| `GetFilteredAsync` | ✅ 5 测试覆盖无参/位置/标签/组合/关键词 | — | 完整 |
| **`GetByIdAsync`** | ❌ | ❌ | **缺失：存在+不存在+含标签+含位置信息** |
| **`DeleteAsync`** | ❌ | ❌ | **缺失：存在删除+不存在+含标签级联+含图片文件** |

**新增测试目标：** ≥ 6（每缺失方法 3 个）

### 1.2 TagRepositoryTests（10 测试，方法全覆盖，缺少边界条件）

| 方法 | Happy-path | 异常路径 | 缺口 |
|------|-----------|---------|------|
| `CreateAsync` | ✅ `_ValidName_Succeeds` | ✅ 重复名 | **缺失：空名、超长名（>50 字符）** |
| `GetOrCreateAsync` | ✅ 已存在/新建 | — | **缺失：空名、超长名** |
| `GetAllAsync` | ✅ `_ReturnsAllTags` | — | 完整 |
| `RenameAsync` | ✅ 成功 | ✅ 重复名/不存在 | **缺失：空名、超长名** |
| `DeleteAsync` | ✅ 成功 | ✅ 不存在 | 完整（含级联删除） |

**新增测试目标：** ≥ 3（参数化 Theory 覆盖空名+超长名）

### 1.3 LocationRepositoryTests（17 测试，最完善，少量缺口）

| 方法 | Happy-path | 异常路径 | 缺口 |
|------|-----------|---------|------|
| `CreateAsync` | ✅ 根/子节点路径生成 | ✅ 空名/超长名 | **缺失：不存在的父节点、超过 MaxDepth（10 层）** |
| `RenameAsync` | ✅ 成功 | ✅ 不存在 | 完整 |
| `DeleteAsync` | ✅ 叶节点成功 | ✅ 有子节点拒绝 | **缺失：有 Item 关联时删除（与有子节点是不同的代码路径）** |
| `GetChildrenAsync` | ✅ 直接子节点 | ✅ 不存在 | 完整 |
| `ResolvePathNamesAsync` | ✅ 4 测试覆盖各种边界 | — | 完整 |
| `ResolvePathNamesBatchAsync` | ✅ 4 测试覆盖批量场景 | — | 完整 |
| **`GetAllAsync`** | ❌ | ❌ | **缺失：返回扁平列表+SortOrder 排序** |

**新增测试目标：** ≥ 4

---

## 2. Service 层（3 个类，均无测试）

### 2.1 ImageStorageService（0 测试）

| 方法 | 逻辑复杂度 | 可测试性 | 建议测试 |
|------|-----------|---------|---------|
| `GetItemDirectory` | 低 | 高 — 纯路径拼接 | ✅ |
| `SaveOriginalAsync` | 中 | 高 — 文件 I/O，需临时目录 | ✅ |
| `GetOriginalPath` | 低 | 高 — 纯路径拼接 | 可选 |
| `GetThumbPath` | 低 | 高 — 纯路径拼接 | 可选 |
| `GetMediumPath` | 低 | 高 — 纯路径拼接 | 可选 |
| `DeleteItemFiles` | 中 | 高 — 文件 I/O，需临时目录 | ✅ |

**新增测试目标：** ≥ 4

### 2.2 LlmClient（0 测试）

| 方法 | 逻辑复杂度 | 可测试性 | 建议测试 |
|------|-----------|---------|---------|
| `RecognizeAsync` | 高 | 中 — 需要 Mock HttpClient | ✅ |
| `TryParse` (private) | 高 | 中 — 通过 RecognizedAsync 间接测试 | ✅ |
| `GetMimeType` (private) | 低 | 通过 RecognizedAsync 间接测试 | ✅ |
| 配置检查（未配置时返回 null） | 低 | 高 | ✅ |

**新增测试目标：** ≥ 5（含 JSON 正常解析、fallback 正则解析、空配置返回 null、HTTP 超时、无效响应）

### 2.3 ThumbnailService（0 测试）

**复杂度：** 高 — `GenerateInBackground` 使用 `Task.Run` + `IServiceScopeFactory` 解析 DbContext + SkiaSharp 位图操作

**降级为手动验证。** 不在此次补完范围内。

**新增测试目标：** 0

---

## 3. Endpoint 层（6 个文件，均无测试）

### 3.1 AuthEndpoints（0 测试）⭐ 最高优先级

| Handler | 测试要点 |
|---------|---------|
| `LoginAsync` | 正确密码成功、错误密码失败、用户不存在、账号已锁定 |
| `LogoutAsync` | 成功登出 |
| `GetCurrentUserAsync` | 已认证返回用户+IsAdmin、未认证 |
| `UpdateProfileAsync` | 成功改名、重复用户名、空用户名 |
| `ChangePasswordAsync` | 正确旧密码成功、错误旧密码失败、太短新密码失败 |

**已有 AuthEndpointsTests 测试 UserManager 直接调用，缺少 HTTP Endpoint 级别的 handler 调用。**

**新增测试目标：** ≥ 8

### 3.2 ItemEndpoints（0 测试）

| Handler | 测试要点 |
|---------|---------|
| `CreateItemAsync` | 成功创建、缺少名称、无效位置、用户关联 |
| `GetItemByIdAsync` | 存在返回详情、不存在返回 404 |
| `SearchItemsAsync` | 无参数搜索、关键词搜索、位置筛选、标签筛选、组合筛选 |
| `DeleteItemAsync` | 成功删除+清理图片、不存在返回 404 |

**新增测试目标：** ≥ 8

### 3.3 TagEndpoints（0 测试）

| Handler | 测试要点 |
|---------|---------|
| `GetAllTagsAsync` | 返回所有标签列表 |
| `CreateTagAsync` | 成功创建、空名称、重复名称 |
| `RenameTagAsync` | 成功改名、不存在、重复名称 |
| `DeleteTagAsync` | 成功删除、不存在 |

**新增测试目标：** ≥ 7

### 3.4 LocationEndpoints（0 测试）

| Handler | 测试要点 |
|---------|---------|
| `GetAllLocationsAsync` | 返回扁平列表 |
| `CreateLocationAsync` | 成功创建根节点、成功创建子节点、空名称、超长名称 |
| `RenameLocationAsync` | 成功改名、不存在 |
| `DeleteLocationAsync` | 成功删除叶节点、有子节点拒绝 |
| `GetChildrenAsync` | 返回直接子节点、不存在父节点 |

**新增测试目标：** ≥ 7

### 3.5 ImageEndpoints（0 测试）— 降级优先级

原因：需要构建 `IFormFile` mock，复杂度较高。

### 3.6 AiEndpoints（0 测试）— 降级优先级

原因：需要对 `LlmClient` 的完整 mock 链路，期望在 CAP-2 完成后自然覆盖。

---

## 4. Admin PageModel 层

### 4.1 现有覆盖率

| PageModel | 已测试 | 缺失 |
|-----------|--------|------|
| `CreateAccountModel` | ❌ 0 测试 | **OnPostAsync（成功+空用户名+弱密码+重复用户名）** |
| `EditAccountModel` | OnPost 改名+空名 | OnGetAsync（加载用户信息） |
| `IndexModel` | ❌ | OnGetAsync（LoadUsersAsync 加载列表） |
| `ChangeUserPasswordModel` | OnPost 成功+空密码 | OnGetAsync（加载用户名） |
| `DeleteUser` | OnPost 成功+自删拒绝 | — |

### 4.2 AdminUserManagementTests（8 测试，需补充）

**新增测试目标：** ≥ 5

---

## 5. 测试质量

### 5.1 死代码删除

| 文件 | 操作 |
|------|------|
| `src/BoxWise.Server.Tests/UnitTest1.cs` | 删除（仅有空 Fact Test1 方法） |

### 5.2 Theory 重构

当前 52 个测试全部使用 `[Fact]`，零个 `[Theory]`。以下场景适合参数化：

| 原测试组 | 重构为 |
|---------|--------|
| Tag CreateAsync/RenameAsync 空名+超长名 | `[Theory]` + `[InlineData]` |
| Location CreateAsync 边界验证 | `[Theory]` + `[InlineData]` |
| Auth 密码错误/太短/空 | `[Theory]` + `[InlineData]` |
| ItemEndpoints/TagEndpoints 空名+超长名 | `[Theory]` + `[InlineData]` |

**新增测试目标：** ≥ 3 个 Theory，覆盖 ≥ 10 个数据组合

---

## 6. 汇总

| 层级 | 当前测试 | 目标新增 | 目标总数 |
|------|---------|---------|---------|
| Repository 补完 | 38 | ≥ 13 | ≥ 51 |
| Service（ImageStorage + LlmClient） | 0 | ≥ 9 | ≥ 9 |
| Endpoint（4 个优先） | 0 | ≥ 30 | ≥ 30 |
| Admin PageModel 补完 | 8 | ≥ 5 | ≥ 13 |
| 测试清理（UnitTest1 删除） | -1 | — | 净变化 |
| **合计** | **52** | **≥ 57** | **≥ 100** |

> SPEC.md 中 Success signal 写 ≥ 85 是保守估计。实际按此清单可达 ≥ 100。
