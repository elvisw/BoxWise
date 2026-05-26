# Story 1.1: 项目脚手架搭建

Status: review

## Story

As a 开发者，
I want 使用 dotnet CLI 创建完整的解决方案结构，
so that 三个项目 + Directory.Build 基础设施就绪，后续 Story 可在此之上构建。

## Acceptance Criteria

1. **AC-1: 项目创建** — 执行 `dotnet new sln` + `blazorwasm --pwa --empty` + `webapi` + `classlib` 后，生成 `BoxWise.sln` 包含三个项目，均目标 `net10.0`
2. **AC-2: 编译成功** — 添加 Directory.Build 文件和项目引用后，`dotnet build BoxWise.sln` 零错误编译
3. **AC-3: Directory.Build.props** — 根级启用 `Nullable`、`ImplicitUsings`、`TreatWarningsAsErrors`
4. **AC-4: CPM** — `Directory.Packages.props` 启用 Central Package Management，模板默认包的版本集中管理
5. **AC-5: 链式导入** — `src/Directory.Build.props` 使用 `GetPathOfFileAbove` 链式导入根级 `.props`
6. **AC-6: 项目引用** — Client → Shared, Server → Shared 引用正确
7. **AC-7: .gitignore** — 包含 .NET 项目标准忽略项（bin, obj, appsettings.Production.json 等）

## Tasks / Subtasks

- [x] Task 1: 验证开发环境 (AC: #1)
  - [x] 确认 `.NET SDK 10.0.300+` 已安装：`dotnet --version`
  - [x] 确认可用的工作负载：`dotnet workload list`
- [x] Task 2: 创建解决方案和项目 (AC: #1)
  - [x] 在项目根目录执行模板生成命令（见下方 Dev Notes）
  - [x] 验证生成的项目 `.csproj` TargetFramework 均为 `net10.0`
- [x] Task 3: 配置 Directory.Build 基础设施 (AC: #3, #5)
  - [x] 创建根级 `Directory.Build.props`（Nullable + ImplicitUsings + TreatWarningsAsErrors）
  - [x] 创建根级 `Directory.Build.targets`（空壳，预留给后续定制）
  - [x] 创建 `src/Directory.Build.props`（GetPathOfFileAbove 链式导入）
- [x] Task 4: 配置 Central Package Management (AC: #4)
  - [x] 创建根级 `Directory.Packages.props`，启用 CPM
  - [x] 从各个 `.csproj` 提取模板默认包，将版本迁移至 CPM 文件
  - [x] 移除各 `.csproj` 中 `PackageReference` 的 `Version` 属性
- [x] Task 5: 添加项目引用 (AC: #6)
  - [x] Server → Shared 引用
  - [x] Client → Shared 引用
- [x] Task 6: 创建 .gitignore (AC: #7)
  - [x] 使用 `dotnet new gitignore` 生成标准 .NET .gitignore
  - [x] 追加项目特定忽略项
- [x] Task 7: 验证编译 (AC: #2)
  - [x] `dotnet restore BoxWise.slnx`
  - [x] `dotnet build BoxWise.slnx` — 必须零错误零警告

---

## Dev Notes

### 环境要求

- **SDK:** .NET 10.0.300+（预览版，最终版预计 2026 年 11 月发布）
- **验证命令:** `dotnet --version` 输出须为 `10.0.300` 或更高
- **目标框架:** 统一 `net10.0`

### 模板创建命令（严格按顺序执行）

```bash
# 1. 解决方案
dotnet new sln -n BoxWise

# 2. 三个项目
dotnet new blazorwasm --pwa --empty -n BoxWise.Client -o src/BoxWise.Client --framework net10.0
dotnet new webapi -n BoxWise.Server -o src/BoxWise.Server --framework net10.0
dotnet new classlib -n BoxWise.Shared -o src/BoxWise.Shared --framework net10.0

# 3. 加入解决方案
dotnet sln BoxWise.sln add src/BoxWise.Client/BoxWise.Client.csproj
dotnet sln BoxWise.sln add src/BoxWise.Server/BoxWise.Server.csproj
dotnet sln BoxWise.sln add src/BoxWise.Shared/BoxWise.Shared.csproj
```

### Directory.Build.props（根级）

路径：`Directory.Build.props`（与 `.sln` 同级）

```xml
<Project>
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
  </PropertyGroup>
</Project>
```

**注意:** 不要在根级 `.props` 中用 `$(TargetFramework)` 做条件判断——它对单目标项目在评估时尚未求值。需要框架条件判断的场景用 `.targets` 文件。

### Directory.Build.props（src 级）

路径：`src/Directory.Build.props`

使用 `GetPathOfFileAbove` 链式导入根级配置：

```xml
<Project>
  <Import Project="$([MSBuild]::GetPathOfFileAbove('Directory.Build.props', '$(MSBuildThisFileDirectory)../'))" />
</Project>
```

### Directory.Build.targets（根级）

路径：`Directory.Build.targets`

```xml
<Project>
  <!-- 预留：自定义构建目标在后续 Story 中添加 -->
</Project>
```

### Directory.Packages.props — CPM

路径：`Directory.Packages.props`（与 `.sln` 同级）

```xml
<Project>
  <PropertyGroup>
    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
    <CentralPackageTransitivePinningEnabled>true</CentralPackageTransitivePinningEnabled>
  </PropertyGroup>

  <!-- 从模板默认生成的包提取版本至此 -->
  <ItemGroup>
    <!-- 由 blazorwasm 模板生成 -->
    <PackageVersion Include="Microsoft.AspNetCore.Components.WebAssembly" Version="10.0.0-preview.*" />
    <PackageVersion Include="Microsoft.AspNetCore.Components.WebAssembly.DevServer" Version="10.0.0-preview.*" />
    <!-- 由 webapi 模板生成 -->
    <PackageVersion Include="Microsoft.AspNetCore.OpenApi" Version="10.0.0-preview.*" />
  </ItemGroup>
</Project>
```

**关键约束:**
- 实际 `Version` 值以模板生成时写入 `.csproj` 的为准，但必须从 `.csproj` 中**移除 `Version` 属性**，改为在此统一管理
- `.csproj` 中 `PackageReference` 只保留 `Include="..."`，不写 `Version`
- 文件名必须精确：`Directory.Packages.props`（Linux 区分大小写）
- `CentralPackageTransitivePinningEnabled=true` 确保传递依赖也受 CPM 版本约束（安全补丁关键）

### .gitignore

生成命令：`dotnet new gitignore`

追加内容：

```gitignore
# BoxWise 项目特定
appsettings.Production.json
appsettings.*.local.json
/data/
*.db
*.db-shm
*.db-wal
```

### 项目引用配置

创建后，在 `BoxWise.Server.csproj` 和 `BoxWise.Client.csproj` 中各自添加：

```xml
<ItemGroup>
  <ProjectReference Include="..\BoxWise.Shared\BoxWise.Shared.csproj" />
</ItemGroup>
```

**不要**手动修改 `.sln` 文件中的项目引用——`dotnet sln add` 已处理。

### 编译验证

完成所有步骤后：

```bash
dotnet restore BoxWise.sln
dotnet build BoxWise.sln
```

**必须零错误零警告。** `TreatWarningsAsErrors=true` 确保任何警告都会中断构建。

### 最终目录结构（本 Story 完成后）

```
BoxWise/
├── BoxWise.sln
├── Directory.Build.props
├── Directory.Build.targets
├── Directory.Packages.props
├── .gitignore
└── src/
    ├── Directory.Build.props
    ├── BoxWise.Client/
    │   ├── BoxWise.Client.csproj
    │   ├── Program.cs
    │   ├── App.razor
    │   ├── _Imports.razor
    │   ├── Layout/
    │   ├── Pages/
    │   └── wwwroot/
    ├── BoxWise.Server/
    │   ├── BoxWise.Server.csproj
    │   ├── Program.cs
    │   └── appsettings.json
    └── BoxWise.Shared/
        └── BoxWise.Shared.csproj
```

### 关键风险点

1. **SDK 版本敏感性** — .NET 10 尚在预览期，`--framework net10.0` 标志依赖于已安装的 SDK 版本。如果 `dotnet new` 模板不支持此标志，尝试去掉 `--framework` 或在创建后手动修改 `.csproj` 中的 `<TargetFramework>net10.0</TargetFramework>`
2. **CPM 版本精确性** — 模板生成的包版本可能是 `10.0.0-preview.3.xxxxx` 等具体预览版本号，务必以实际生成为准，**不要**使用通配符 `*`
3. **Wasmtool 工作负载** — `blazorwasm` 模板可能需要 `wasm-tools` 工作负载。如果创建失败，运行：`dotnet workload install wasm-tools`
4. **GetPathOfFileAbove 路径** — `src/Directory.Build.props` 中的相对路径 `../` 必须以 `Directory.Build.props` 文件名结尾的目录层级为准

---

## References

| 内容 | 来源 |
|------|------|
| 模板创建命令 | [Source: architecture.md#Starter Template Evaluation] |
| Directory.Build 链式导入 | [Source: architecture.md#构建基础设施] |
| CPM 配置标准 | [Source: architecture.md#AR-1] |
| 完整项目目录结构 | [Source: architecture.md#Complete Project Directory Structure] |
| Naming Conventions | [Source: architecture.md#Naming Patterns] |
| Enforcement Guidelines | [Source: architecture.md#Enforcement Guidelines] |
| Anti-Patterns | [Source: architecture.md#Anti-Patterns (DO NOT USE)] |
| Story AC 定义 | [Source: epics.md#Story 1.1] |
| .NET 10 SDK 信息 | [Source: Web research 2026-05-23] |

## Dev Agent Record

### Agent Model Used

deepseek-v4-pro

### Debug Log References

### Completion Notes List

✅ **所有 7 个任务完成** — 项目脚手架搭建完毕，`dotnet build` 零错误零警告

**实施细节：**
- .NET 10 默认生成 `.slnx`（XML 格式）解决方案文件，替代传统 `.sln` 格式；所有 `dotnet sln` 命令正常兼容
- 安装了 `wasm-tools` 工作负载以支持 Blazor WASM 编译
- 从各 `.csproj` 中移除了 `Nullable` 和 `ImplicitUsings` 属性，统一由根级 `Directory.Build.props` 继承
- 包版本 `10.0.8` 为模板生成的实际版本号，已迁移至 CPM 统一管理
- `TreatWarningsAsErrors=true` 已启用并通过验证

### File List

- `BoxWise.slnx` (new) — 解决方案文件 (.NET 10 XML 格式)
- `Directory.Build.props` (new) — 根级 MSBuild 属性（Nullable, ImplicitUsings, TreatWarningsAsErrors, TargetFramework）
- `Directory.Build.targets` (new) — 根级 MSBuild 目标（空壳预留）
- `Directory.Packages.props` (new) — Central Package Management 配置
- `src/Directory.Build.props` (new) — 链式导入根级配置
- `src/BoxWise.Client/BoxWise.Client.csproj` (new) — Blazor WASM PWA 客户端项目
- `src/BoxWise.Client/Program.cs` (new)
- `src/BoxWise.Client/App.razor` (new)
- `src/BoxWise.Client/_Imports.razor` (new)
- `src/BoxWise.Client/wwwroot/` (new) — PWA 静态资源
- `src/BoxWise.Server/BoxWise.Server.csproj` (new) — Web API 服务端项目
- `src/BoxWise.Server/Program.cs` (new)
- `src/BoxWise.Server/appsettings.json` (new)
- `src/BoxWise.Shared/BoxWise.Shared.csproj` (new) — 共享类库项目
- `.gitignore` (modified) — 使用 `dotnet new gitignore` 重新生成，追加项目特定规则
