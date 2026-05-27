---
baseline_commit: 1517d8ebd73bc33b7eb90764aecf5e8572901fad
---

# Story 6.4: Endpoint 层测试建立

Status: done

## Story

As a 开发者，
I want 为 Auth/Item/Tag/Location 四个核心 Endpoint 建立 handler 级别测试，
so that 请求-响应完整路径有回归保护。

## Acceptance Criteria

1. AuthEndpoints（≥ 8 测试）：
   - LoginAsync: 成功 / 错误密码 / 用户不存在
   - LogoutAsync: 成功
   - GetCurrentUserAsync: 已认证返回用户 + IsAdmin
   - UpdateProfileAsync: 成功 / 重复用户名
   - ChangePasswordAsync: 正确旧密码成功 / 错误旧密码失败
2. TagEndpoints（≥ 6 测试）：
   - GetAllTagsAsync / CreateTagAsync: 成功 + 空名 + 重复
   - RenameTagAsync: 成功 / 不存在 / 重复
   - DeleteTagAsync: 成功 / 不存在
3. LocationEndpoints（≥ 7 测试）：
   - GetAllLocationsAsync / GetChildrenAsync: 成功
   - CreateLocationAsync: 根 + 子 + 空名
   - RenameLocationAsync: 成功 / 不存在
   - DeleteLocationAsync: 叶节点成功 / 有子节点拒绝
4. ItemEndpoints（≥ 8 测试）：
   - CreateItemAsync: 成功 / 缺名 / 无效位置
   - SearchItemsAsync: 无参 / 关键词 / 位置筛选
   - GetItemByIdAsync: 存在 / 404
   - DeleteItemAsync: 成功 / 404
5. `dotnet test` 全部通过，新增 ≥ 30 测试（87 → ≥ 117）

## Tasks / Subtasks

- [ ] Task 1: AuthEndpoints 测试 (AC: 1)
  - [ ] 新建 `Endpoints/AuthEndpointsTests.cs`
  - [ ] 使用 `TestIdentityFactory.CreateAsync()` 获取 UserManager + SignInManager
  - [ ] 8 测试: Login(3) + Logout(1) + GetCurrentUser(1) + UpdateProfile(1) + ChangePassword(2)

- [ ] Task 2: TagEndpoints 测试 (AC: 2)
  - [ ] 新建 `Endpoints/TagEndpointsTests.cs`
  - [ ] 使用 `TestDbContextFactory.Create()` 创建 TagRepository
  - [ ] 6 测试: GetAll(1) + Create(2) + Rename(2) + Delete(1)

- [ ] Task 3: LocationEndpoints 测试 (AC: 3)
  - [ ] 新建 `Endpoints/LocationEndpointsTests.cs`
  - [ ] 7 测试: GetAll(1) + GetChildren(1) + Create(3) + Rename(1) + Delete(1)

- [ ] Task 4: ItemEndpoints 测试 (AC: 4)
  - [ ] 新建 `Endpoints/ItemEndpointsTests.cs`
  - [ ] 8 测试: Create(3) + Search(3) + GetById(1) + Delete(1)

- [ ] Task 5: 全量回归验证 (AC: 5)
  - [ ] `dotnet test` 全部通过，新增 ≥ 30 测试

## Dev Notes

### 前三个 Story 关键学习

- 使用 TestIdentityFactory/TestDbContextFactory 获取隔离实例
- EF Core InMemory 对 CreatedByUser Include 有限制
- 遵循 AAA + 现有命名约定

### Endpoint 测试模式

所有 Endpoint handler 是 `static` 方法。直接传入 mock 参数调用：

```csharp
// AuthEndpoints — 需要 TestIdentityFactory
var ctx = await TestIdentityFactory.CreateAsync();
var result = await AuthEndpoints.LoginAsync(
    new LoginRequest("user", "pass"),
    ctx.SignInManager, ctx.UserManager, config);
Assert.IsType<Ok<AuthUserDto>>(result);

// TagEndpoints — 直接传 TagRepository
using var db = TestDbContextFactory.Create();
var repo = new TagRepository(db);
var result = await TagEndpoints.GetAllTagsAsync(repo);
```

### 涉及文件（全部新建）

| 文件 | 测试数 |
|------|--------|
| `Endpoints/AuthEndpointsTests.cs` | 8 |
| `Endpoints/TagEndpointsTests.cs` | 6 |
| `Endpoints/LocationEndpointsTests.cs` | 7 |
| `Endpoints/ItemEndpointsTests.cs` | 8 |
| **合计** | **29** |

### 预期最终状态：87 → ≥ 116

### References

- [Source: SPEC CAP-3]
- [Source: test-inventory.md §3]
- [Source: TestIdentityFactory.cs]
- [Source: src/BoxWise.Server/Endpoints/AuthEndpoints.cs]
- [Source: src/BoxWise.Server/Endpoints/TagEndpoints.cs]
- [Source: src/BoxWise.Server/Endpoints/LocationEndpoints.cs]
- [Source: src/BoxWise.Server/Endpoints/ItemEndpoints.cs]

## Dev Agent Record

### Agent Model Used

### Debug Log References

### Completion Notes List

### File List
