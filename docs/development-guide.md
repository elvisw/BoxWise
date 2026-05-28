# 开发指南

> BoxWise — 本地开发、构建与测试

## 环境要求

- .NET 10 SDK
- 任意操作系统（Windows / macOS / Linux）

## 快速开始

```bash
# 1. 克隆仓库
git clone <repo-url> && cd BoxWise

# 2. 构建
dotnet build

# 3. 运行 Server（API + Admin + SPA 回退）
cd src/BoxWise.Server && dotnet run
# → https://localhost:5000

# 4. 运行 Client（Blazor WASM 开发服务器，热重载）
cd src/BoxWise.Client && dotnet run
# → https://localhost:5001 （推荐日常 UI 开发使用）
```

## 端口与使用场景

| 地址 | 用途 | 热重载 | 场景 |
|------|------|--------|------|
| `https://localhost:5001` | Blazor WASM 页面 | 有 | **日常 UI 开发** |
| `https://localhost:5000` | API + Admin + SPA 回退 | 无 | 测试 Admin / 集成 |

- Client (5001) → Server (5000) 跨源请求通过 Cookie 认证
- Admin 后台 (`/admin`) 仅在 5000 端口可用

## 构建命令

```bash
dotnet build                          # Debug 构建
dotnet build -c Release               # Release 构建
dotnet publish src/BoxWise.Server -c Release -o publish  # 发布部署
```

## 测试

```bash
# 运行所有测试
dotnet test BoxWise.slnx

# 运行特定测试项目
dotnet test src/BoxWise.Server.Tests

# 运行特定测试类
dotnet test src/BoxWise.Server.Tests --filter "FullyQualifiedName~LocationRepositoryTests"
```

**测试框架:** xUnit 2.9.3 + EF Core InMemory Database
**测试模式:** 每个测试独立创建 DbContext（GUID 命名），覆盖正常路径 + 边界条件

## 数据库

```bash
# EF Core 迁移（在 Server 目录下操作）
cd src/BoxWise.Server
dotnet ef migrations add <MigrationName>
dotnet ef database update
```

- 数据库文件: `data/boxwise.db`（自动创建）
- 启动时自动执行迁移: `Program.cs` 中 `db.Database.MigrateAsync()`

## 管理员创建

```bash
# 设置环境变量后启动 Server
export Admin__Password="your-password"
# 可选: export Admin__Username="admin"
dotnet run
```

## 代码风格

- **Target Framework:** net10.0
- **Nullable:** enable
- **ImplicitUsings:** enable
- **WarningsAsErrors:** true
- **API 风格:** Minimal API + TypedResults + ProblemDetails
- **DTO:** Positional records
- **Repository:** 返回 Entity，端点负责映射
- **异常处理:** ArgumentException → Problem(400), KeyNotFoundException → NotFound()
