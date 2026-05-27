---
title: '重构路径解析 — 性能优化 + 去重 + 缺陷修复 + 可测试性'
type: 'refactor'
created: '2026-05-27'
status: 'done'
baseline_commit: '9f9e6b3'
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** 路径解析存在 4 个关联问题：(1) SearchItemsAsync 每次全量加载位置表做路径名解析，O(n) 内存开销随数据增长；(2) LocationRepository 存在两套重复的路径解析逻辑（async DB 版 + static 字典版）；(3) 已删除位置的 ID 在路径中降级显示为数字而非 "?"；(4) 静态方法无法单独测试。

**Approach:** 统一为 `ResolvePathNamesAsync`（单路径）+ `ResolvePathNamesBatchAsync`（批量路径）两个公共方法；SearchItemsAsync 改为仅查询涉及的位置 ID；删除 static 版本；缺失 ID 统一显示 "?"；新增 LocationRepository 路径解析单元测试。

## Boundaries & Constraints

**Always:**
- 不改变 API 响应格式（ItemDto、ItemSummaryDto 字段不变）
- 批量解析使用单次 DB 查询
- 缺失位置 ID 统一显示 "?"（不再降级为数字）
- `dotnet build` + `dotnet test` 通过

**Ask First:**
- 无

**Never:**
- 不改变 DTO 签名
- 不引入缓存层（保持查询最新数据）
- 不修改 Location 实体的 Path 格式

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| 单路径解析 | idPath = `/1/3/5/` | 返回 `客厅/柜子/架子` | N/A |
| 批量路径解析 | 多条 idPath 含重叠 ID | 一次 DB 查询，全部解析 | N/A |
| 路径含已删除位置 ID | idPath 含不存在于 DB 的 ID | 该位置显示 "?" | 静默 |
| 空路径 | idPath = null/"" | 返回 null | N/A |
| 纯分隔符路径 | idPath = `///` | 返回 null | 静默 |
| 非数字段路径 | idPath = `/abc/1/` | "?" / "客厅" | 静默 |

</frozen-after-approval>

## Code Map

- `src/BoxWise.Server/Repositories/LocationRepository.cs` -- 核心变更：删除 static ResolvePathNames，新增 ResolvePathNamesBatchAsync，修复 "?" 回退
- `src/BoxWise.Server/Endpoints/ItemEndpoints.cs` -- SearchItemsAsync 改用 ResolvePathNamesBatchAsync（替换 GetAllAsync + static 调用）
- `src/BoxWise.Server.Tests/LocationRepositoryTests.cs` -- 新增路径解析测试

## Tasks & Acceptance

**Execution:**
- [x] `src/BoxWise.Server/Repositories/LocationRepository.cs` -- 新增 `ResolvePathNamesBatchAsync(IEnumerable<string?> idPaths)` 批量解析方法，提取路径中所有唯一 ID 做单次 DB 查询；删除 `internal static ResolvePathNames`；`ResolvePathNamesAsync` 中对找不到名称的 ID 统一显示 "?"
- [x] `src/BoxWise.Server/Endpoints/ItemEndpoints.cs` -- SearchItemsAsync 中 `GetAllAsync()` + `ResolvePathNames()` 替换为 `ResolvePathNamesBatchAsync()` 单次调用
- [x] `src/BoxWise.Server.Tests/LocationRepositoryTests.cs` -- 新增测试：正常路径解析、空路径、含不存在 ID、纯分隔符路径、批量解析多路径

**Acceptance Criteria:**
- Given 路径含已删除位置 ID，when 解析路径名称，then 该位置显示 "?" 而非数字
- Given SearchItemsAsync 返回 10 个物品，when 调用端点，then 只查询这些物品路径中出现的位置 ID（不超过 10 次 key lookup）
- Given 单路径和多路径均通过 ResolvePathNamesAsync/ResolvePathNamesBatchAsync 解析，then 不存在 static 版本的重复逻辑
- Given ResolvePathNamesAsync 和 ResolvePathNamesBatchAsync 是公共实例方法，when 编写单元测试，then 可独立测试路径解析逻辑

## Suggested Review Order

**核心：统一路径解析逻辑**

- LocationRepository 新增 ResolvePathNamesBatchAsync，删除 static ResolvePathNames，统一 "?" 回退
  [`LocationRepository.cs:116`](../../src/BoxWise.Server/Repositories/LocationRepository.cs#L116)

- SearchItemsAsync 由 GetAllAsync + static 调用替换为单次 ResolvePathNamesBatchAsync
  [`ItemEndpoints.cs:115`](../../src/BoxWise.Server/Endpoints/ItemEndpoints.cs#L115)

**测试覆盖**

- 9 个路径解析测试：正常、空值、已删除ID、纯分隔符、批量、重叠ID、一致性问题
  [`LocationRepositoryTests.cs:125`](../../src/BoxWise.Server.Tests/Repositories/LocationRepositoryTests.cs#L125)

## Verification

**Commands:**
- `dotnet build BoxWise.slnx` -- 编译通过
- `dotnet test BoxWise.slnx` -- 全部测试通过，含新增路径解析测试
