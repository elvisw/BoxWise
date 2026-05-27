---
baseline_commit: f3936686a896ef5a356fcc502df14cf78dc2f11f
---

# Story 6.1: 测试清理与质量改进

Status: done

## Story

As a 开发者，
I want 删除死代码 UnitTest1.cs、将重复边界验证重构为 Theory、建立统一的参数化测试模式，
so that 后续 Story（6.2-6.5）有干净的基线和可复用的 `[Theory]` 模式作为参考。

## Acceptance Criteria

1. 删除 `src/BoxWise.Server.Tests/UnitTest1.cs`（含空的 `Test1` Fact 方法）
   - 删除后 `dotnet test` 通过，项目编译无错误
2. TagRepositoryTests 新增 3 个 `[Theory]`（使用 `[MemberData]`）覆盖之前遗漏的边界条件：
   - `CreateAsync_InvalidName_ThrowsArgumentException` — 空字符串 + 51 字符超长名
   - `RenameAsync_InvalidName_ThrowsArgumentException` — 空字符串 + 51 字符超长名
   - `GetOrCreateAsync_InvalidName_ThrowsArgumentException` — 空字符串 + 51 字符超长名
3. LocationRepositoryTests 重构 2 个已有 Fact 为 1 个 Theory，并新增 1 个 Theory：
   - `CreateAsync_InvalidName_ThrowsArgumentException` — 合并 `CreateAsync_EmptyName` + `CreateAsync_NameExceedsMaxLength`（空字符串 + 101 字符超长名）
   - `RenameAsync_InvalidName_ThrowsArgumentException`（新增）— 空字符串 + 101 字符超长名
4. 最终 `dotnet test` 全部通过，共 ≥ 5 个 `[Theory]`，覆盖 ≥ 10 个数据组合

## Tasks / Subtasks

- [x] Task 1: 删除死代码 (AC: 1)
  - [x] 删除 `src/BoxWise.Server.Tests/UnitTest1.cs`
  - [x] 运行 `dotnet test` 确认通过

- [x] Task 2: TagRepositoryTests — 新增 CreateAsync 边界 Theory (AC: 2)
  - [x] 添加 `CreateAsync_InvalidName_ThrowsArgumentException` Theory + `[MemberData]`
  - [x] MemberData 提供: `""`（空字符串）、`new string('x', 51)`（超过 50 字符上限）
  - [x] 断言抛出 `ArgumentException`

- [x] Task 3: TagRepositoryTests — 新增 RenameAsync 边界 Theory (AC: 2)
  - [x] 添加 `RenameAsync_InvalidName_ThrowsArgumentException` Theory + `[MemberData]`
  - [x] 每个参数先创建合法 tag，再用无效名调用 RenameAsync
  - [x] MemberData 提供: `""`、`new string('x', 51)`

- [x] Task 4: TagRepositoryTests — 新增 GetOrCreateAsync 边界 Theory (AC: 2)
  - [x] 添加 `GetOrCreateAsync_InvalidName_ThrowsArgumentException` Theory + `[MemberData]`
  - [x] MemberData 提供: `""`、`new string('x', 51)`

- [x] Task 5: LocationRepositoryTests — 重构 CreateAsync 边界 Fact→Theory (AC: 3)
  - [x] 将 `CreateAsync_EmptyName_ThrowsArgumentException` 和 `CreateAsync_NameExceedsMaxLength_ThrowsArgumentException` 合并为 `CreateAsync_InvalidName_ThrowsArgumentException` Theory + `[MemberData]`
  - [x] MemberData 提供: `""`（空）、`new string('x', 101)`（超过 100 字符上限）
  - [x] 删除原有的 2 个独立 Fact 方法

- [x] Task 6: LocationRepositoryTests — 新增 RenameAsync 边界 Theory (AC: 3)
  - [x] 添加 `RenameAsync_InvalidName_ThrowsArgumentException` Theory + `[MemberData]`
  - [x] 每个参数先创建合法 location，再用无效名调用 RenameAsync
  - [x] MemberData 提供: `""`、`new string('x', 101)`

- [x] Task 7: 全量回归验证 (AC: 4)
  - [x] `dotnet test` 全部通过（65 通过，0 失败）
  - [x] 验证 Theory 计数 = 5，MemberData 组合 = 10

## Dev Notes

### 为什么这个 Story 优先

后续 Story 6.2-6.5 将添加大量新测试。先建立 `[Theory]` + `[InlineData]` 模式作为参考，避免新测试全部使用 `[Fact]` 的重复模式。删除死代码是极低风险的清理。

### 涉及的文件

| 操作 | 文件 | 说明 |
|------|------|------|
| **DELETE** | `src/BoxWise.Server.Tests/UnitTest1.cs` | 脚手架占位，空 `Test1()` 方法，无实际测试 |
| **MODIFY** | `src/BoxWise.Server.Tests/Repositories/TagRepositoryTests.cs` | 新增 3 个 Theory（当前 10 个方法全为 Fact） |
| **MODIFY** | `src/BoxWise.Server.Tests/Repositories/LocationRepositoryTests.cs` | 合并 2 Fact→1 Theory + 新增 1 Theory（当前 17 个方法全为 Fact） |

### 当前 TagRepositoryTests 结构（修改前）

```
10 个 Fact 方法:
  CreateAsync: ValidName ✅, DuplicateName ✅, 空名 ❌, >50字符 ❌
  GetOrCreateAsync: ExistingName ✅, NewName ✅, 空名 ❌, >50字符 ❌
  RenameAsync: Success ✅, DuplicateName ✅, NotFound ✅, 空名 ❌, >50字符 ❌
  GetAllAsync: ReturnsAllTags ✅
  DeleteAsync: Success ✅, WithItems ✅, NotFound ✅
```

### 当前 LocationRepositoryTests 结构（修改前）

```
17 个 Fact 方法:
  CreateAsync: RootNode ✅, ChildNode ✅, EmptyName ✅, NameExceedsMaxLength ✅
    → EmptyName 和 NameExceedsMaxLength 将合并为 1 个 Theory
  RenameAsync: UpdatesName ✅, NonExistentId ✅, 空名 ❌, >100字符 ❌
    → 新增 1 个 Theory 覆盖空名+超长
  DeleteAsync: LeafNode ✅, WithChildren ✅
  GetChildrenAsync: ReturnsDirectChildren ✅, NonExistentId ✅
  ResolvePathNamesAsync: 4 个 ✅
  ResolvePathNamesBatchAsync: 4 个 ✅
```

### 验证逻辑（来源代码）

**TagRepository.cs** (`src/BoxWise.Server/Repositories/TagRepository.cs`):
```
CreateAsync: string.IsNullOrWhiteSpace(name) → ArgumentException("标签名称不能为空")
             name.Trim().Length > 50 → ArgumentException("标签名称不能超过 50 个字符")
RenameAsync: 同上
GetOrCreateAsync: 同上
```

**LocationRepository.cs** (`src/BoxWise.Server/Repositories/LocationRepository.cs`):
```
CreateAsync: string.IsNullOrWhiteSpace(name) → ArgumentException("位置名称不能为空")
             name.Trim().Length > 100 → ArgumentException("位置名称不能超过 100 个字符")
RenameAsync: 同上
```

### Theory 模式参考（开发者必须遵循）

**关键约束：** `[InlineData]` 只接受编译时常量。`new string('x', 51)` 不是常量，必须通过 `[MemberData]` 提供。每个测试类使用静态属性/方法返回 `IEnumerable<object[]>`。

```csharp
// === 模式 A: 直接调用抛异常的 Theory ===
// 使用 MemberData 提供非常量参数（如动态生成的长字符串）
public static IEnumerable<object[]> InvalidTagNames =>
    new List<object[]>
    {
        new object[] { "" },
        new object[] { new string('x', 51) }
    };

[Theory]
[MemberData(nameof(InvalidTagNames))]
public async Task CreateAsync_InvalidName_ThrowsArgumentException(string invalidName)
{
    using var db = TestDbContextFactory.Create();
    var repo = new TagRepository(db);

    await Assert.ThrowsAsync<ArgumentException>(() => repo.CreateAsync(invalidName));
}

// === 模式 B: 先创建有效实体，再用无效参数调用 ===
public static IEnumerable<object[]> InvalidTagNamesForRename =>
    new List<object[]>
    {
        new object[] { "" },
        new object[] { new string('x', 51) }
    };

[Theory]
[MemberData(nameof(InvalidTagNamesForRename))]
public async Task RenameAsync_InvalidName_ThrowsArgumentException(string invalidName)
{
    using var db = TestDbContextFactory.Create();
    var repo = new TagRepository(db);
    var tag = await repo.CreateAsync("有效标签");

    await Assert.ThrowsAsync<ArgumentException>(() => repo.RenameAsync(tag.Id, invalidName));
}

// === 模式 C: Location 边界验证（同理使用 MemberData） ===
public static IEnumerable<object[]> InvalidLocationNames =>
    new List<object[]>
    {
        new object[] { "" },
        new object[] { new string('x', 101) }
    };
```

**注意：** LocationRepositoryTests 中原有的 2 个 Fact（`CreateAsync_EmptyName_ThrowsArgumentException` 和 `CreateAsync_NameExceedsMaxLength_ThrowsArgumentException`）删除后，换为 1 个使用上述 MemberData 的 Theory。TagRepositoryTests 中的 3 个新 Theory 同理。

### 关键约束

- **不修改任何源代码**（Repository 实现不动）
- **不修改现有测试的语义**（只重构组织方式）
- **使用 `TestDbContextFactory.Create()`** 创建隔离 DbContext，与现有测试一致
- **遵循现有 AAA 模式**（Arrange-Act-Assert）
- **命名遵循现有约定**：`{方法名}_{场景}_{结果}`

### 预期最终状态

| 测试类 | 修改前 | 删除 | 重构 | 新增 | 修改后 |
|--------|--------|------|------|------|--------|
| UnitTest1.cs | 1 Fact | -1 | — | — | **0（文件删除）** |
| TagRepositoryTests | 10 Fact | — | — | +3 Theory | 10 Fact + 3 Theory = 13 |
| LocationRepositoryTests | 17 Fact | — | -2 Fact +1 Theory | +1 Theory | 15 Fact + 2 Theory = 17 |
| **总计** | **28** | **-1** | **净-1** | **+4** | **30** |

Theory 计数：≥ 5（Tag 3 + Location 2），InlineData 组合：≥ 10（2+2+2+2+2）

### References

- [Source: _bmad-output/specs/spec-test-coverage/SPEC.md#CAP-5]
- [Source: _bmad-output/specs/spec-test-coverage/test-inventory.md#5]
- [Source: _bmad-output/planning-artifacts/epics.md#Epic 6]
- [Source: src/BoxWise.Server/Repositories/TagRepository.cs] — 验证逻辑行 27-32, 54-59, 86-91
- [Source: src/BoxWise.Server/Repositories/LocationRepository.cs] — 验证逻辑行 19-24, 57-62

## Dev Agent Record

### Agent Model Used

claude-sonnet-4-6

### Debug Log References

- 删除 UnitTest1.cs 后 → 57 pass (确认)
- 添加 5 Theory × 2 MemberData 后 → 65 pass

### Completion Notes List

- 删除死代码: UnitTest1.cs (1 空 Fact)
- TagRepositoryTests: +3 Theory (CreateAsync/RenameAsync/GetOrCreateAsync InvalidName)
- LocationRepositoryTests: -2 Fact +1 Theory (CreateAsync InvalidName), +1 Theory (RenameAsync InvalidName)
- 净变化: 28 → 30 测试方法 (58→65 总, 从 65 重新计数确认)
- 5 个 [Theory] 覆盖 10 个 MemberData 组合
- 所有测试通过, 使用 TestDbContextFactory 隔离, AAA 模式

### File List

- `src/BoxWise.Server.Tests/UnitTest1.cs` — 删除
- `src/BoxWise.Server.Tests/Repositories/TagRepositoryTests.cs` — 新增 3 Theory + 3 MemberData 属性
- `src/BoxWise.Server.Tests/Repositories/LocationRepositoryTests.cs` — 合并 2 Fact→1 Theory + 新增 1 Theory + 2 MemberData 属性

### Review Findings

- [x] [Review][Defer] Null 输入未测试 — `IsNullOrWhiteSpace` 守卫也捕获 null，但当前测试仅覆盖 `""` 和超长字符串 [TagRepositoryTests/LocationRepositoryTests]
- [x] [Review][Defer] 纯空白输入未测试 — `"   "`、`"\t"` 同样触发 `IsNullOrWhiteSpace`，未覆盖 [TagRepositoryTests/LocationRepositoryTests]
- [x] [Review][Defer] 失败后状态未验证 — RenameAsync 抛异常后未断言 DB 中实体名称未变 [LocationRepositoryTests]
