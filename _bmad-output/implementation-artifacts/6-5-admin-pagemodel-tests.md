# Story 6.5: Admin PageModel 测试补完

---
baseline_commit: 97c86ba46175067072a3430b6a51546c047065db
---

Status: done

## Story

As a 开发者，
I want 补齐 CreateAccountModel.OnPostAsync 和其余 PageModel 的 OnGetAsync handler 测试，
so that Admin 后台的所有页面 handler 都有回归保护。

## Acceptance Criteria

1. CreateAccountModel.OnPostAsync（≥ 4 测试）：
   - 成功创建用户
   - 空用户名返回错误
   - 弱密码返回错误
   - 重复用户名返回错误
2. EditAccountModel.OnGetAsync — 加载用户信息
3. IndexModel.OnGetAsync — 加载用户列表
4. ChangeUserPasswordModel.OnGetAsync — 加载用户名
5. `dotnet test` 全部通过，新增 ≥ 7 测试（117 → ≥ 124）

## Tasks / Subtasks

- [ ] Task 1: 读取 Admin PageModel 源码确定 handler 签名和依赖
- [ ] Task 2: 新增 CreateAccountModel 测试 (AC: 1) — 4 tests
- [ ] Task 3: 新增 EditAccountModel/IndexModel/ChangeUserPasswordModel OnGet 测试 (AC: 2-4) — 3 tests
- [ ] Task 4: 全量回归验证 (AC: 5)

## Dev Notes

### 前四 Story 关键学习
- TestIdentityFactory 提供 UserManager + RoleManager
- PageModel 测试需要模拟 HttpContext/ModelState/TempData
- 参考现有 AdminUserManagementTests.cs 的 setup 模式
- 不修改源代码，纯测试补充

### 涉及文件
| 操作 | 文件 |
|------|------|
| **MODIFY** | `AdminUserManagementTests.cs` — 新增 7 测试 |

### 预期：117 → ≥ 124

## Dev Agent Record

### Agent Model Used

### Debug Log References

### Completion Notes List

### File List
