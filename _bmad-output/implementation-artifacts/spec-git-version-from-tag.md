---
title: '从 Git Tag 自动获取版本号'
type: 'feature'
created: '2026-05-28'
status: 'done'
route: 'one-shot'
---

<frozen-after-approval>

## Intent

**Problem:** 版本号在 `Directory.Build.props` 中硬编码为 `1.0.0`，发布时需要手动更新，容易遗忘导致关于页面显示过期版本号。

**Approach:** 构建时通过 MSBuild Target 执行 `git describe --tags` 自动获取版本号，去除 `v` 前缀后设置 `Version`/`InformationalVersion` 属性，无 tag 时回退到 `1.0.0`。

## Suggested Review Order

1. `Directory.Build.props:6` -- 移除硬编码 `<Version>1.0.0</Version>`
2. `Directory.Build.targets:2-17` -- 新增 `SetVersionFromGit` MSBuild Target
3. `CLAUDE.md:160-189` -- 版本管理文档 + 项目树更新
4. `Pages/About.razor:93-95` -- 验证：无需改动，已读 `AssemblyInformationalVersionAttribute`

## Code Map

- `Directory.Build.props` -- 移除硬编码 Version 属性
- `Directory.Build.targets` -- 新增 SetVersionFromGit target（git describe → Version/InformationalVersion）
- `CLAUDE.md` -- 版本管理章节 + 项目树文件说明
- `src/BoxWise.Client/Pages/About.razor` -- 无需改动，已正确读取 AssemblyInformationalVersionAttribute

## Tasks & Acceptance

**Execution:**
- [x] `Directory.Build.props` -- 移除 `<Version>1.0.0</Version>` -- 不再硬编码版本号
- [x] `Directory.Build.targets` -- 新增 `SetVersionFromGit` target，BeforeTargets="BeforeBuild"，执行 `git describe --tags --abbrev=7 --always`，正则提取 semver 作为 Version，完整输出作为 InformationalVersion，fallback 1.0.0，同步设置 AssemblyVersion/FileVersion，无 git 时 Warning
- [x] `CLAUDE.md` -- 新增"版本管理"章节（工作原理、映射规则表、发版流程、Docker 注意事项），更新项目树中 Directory.Build.targets 描述

**Acceptance Criteria:**
- Given HEAD 指向 tag `v0.2.1`，when `dotnet build`，then `AssemblyInformationalVersionAttribute` = `v0.2.1`，`AssemblyVersion` = `0.2.1.0`
- Given HEAD 在 tag `v0.2.1` 之后 21 commits，when `dotnet build`，then `AssemblyInformationalVersionAttribute` = `v0.2.1-21-g061b014`
- Given 仓库无 tag 或无 git，when `dotnet build`，then 显示 MSBuild Warning，版本号回退到 `1.0.0`
- Given 两个测试项目执行 `dotnet test`，when 版本逻辑变更后，then 全部 190 测试通过

## Verification

**Commands:**
- `dotnet build --no-restore` -- expected: 0 warnings, 0 errors
- `dotnet test BoxWise.slnx` -- expected: 190 passed, 0 failed
</frozen-after-approval>
