# Investigation: 生产环境 Blazor WASM 加载卡住

## Hand-off Brief

1. **What happened.** `https://im.elvisw.com/` 页面显示 Loading 圆圈不动，Blazor WASM 应用无法启动。根本原因是发布输出不完整：`index.html` 中 `{fingerprint}` 占位符未被替换，且 `_framework/` 静态资源目录未部署到生产服务器。
2. **Where the case stands.** 根因已确认为发布/部署问题。需要检查生产服务器的实际文件结构并修复部署流程。
3. **What's needed next.** 在服务器上检查发布的 `wwwroot/` 目录，确认 `_framework/` 是否存在；修正 `dotnet publish` 命令并重新部署。

## Case Info

| Field            | Value                                                                      |
| ---------------- | -------------------------------------------------------------------------- |
| Ticket           | N/A                                                                        |
| Date opened      | 2026-06-04                                                                 |
| Status           | Active                                                                     |
| System           | Debian 生产服务器 (Docker/Caddy + ASP.NET Core)                             |
| Evidence sources | Playwright 浏览器直接访问 `https://im.elvisw.com/`，HTML 源码，网络请求      |

## Problem Statement

用户部署到 Debian 生产环境后访问 `https://im.elvisw.com/`，页面只显示一个静止的 loading 圆圈，应用无法启动。Windows 测试环境正常。

## Evidence Inventory

| Source   | Status    | Notes     |
| -------- | --------- | --------- |
| 浏览器页面截图 | Available | 页面标题正常显示"箱知 BoxWise"，body 中只有 Loading 文字 |
| HTML 源码 | Available | `index.html` 中 `<script src="_framework/blazor.webassembly#[.{fingerprint}].js">` 占位符未替换 |
| 浏览器 Console | Available | 1 error: `Unexpected token '<'`；1 warning: `<link rel=preload> has an invalid 'href'` |
| 网络请求 | Available | `/_framework/blazor.webassembly` 返回 200 但内容是 HTML（SPA fallback） |
| `_framework/blazor.webassembly.js` | Available | 同样返回 200 HTML，说明文件不存在 |
| 生产服务器文件系统 | Missing | 需要 SSH 查看实际发布的文件结构 |

## Investigation Backlog

| # | Path to Explore | Priority | Status | Notes |
| - | --------------- | -------- | ------ | ----- |
| 1 | 检查生产服务器 `wwwroot/_framework/` 目录是否存在 | High | Open | 确认发布产物是否完整 |
| 2 | 确认 Docker 部署时使用的 `dotnet publish` 命令 | High | Open | 检查是否为 Release 配置 |
| 3 | 检查 `index.html` 在生产服务器上的实际内容 | Medium | Open | 确认是否与浏览器看到的一致 |
| 4 | 检查 `BoxWise.Client.csproj` 中 Blazor WASM 发布配置 | Medium | Open | 排查构建配置问题 |

## Timeline of Events

| Time        | Event               | Source                | Confidence            |
| ----------- | ------------------- | --------------------- | --------------------- |
| 2026-06-04 17:27 | 浏览器访问 `https://im.elvisw.com/` | Playwright goto | Confirmed |
| 2026-06-04 17:27 | 页面返回 HTML 200，标题正常 | Network request #1 | Confirmed |
| 2026-06-04 17:27 | 浏览器尝试加载 `_framework/blazor.webassembly#[.{fingerprint}].js` | Network request #5 | Confirmed |
| 2026-06-04 17:27 | `_framework/blazor.webassembly` 返回 HTML 而非 JS | Playwright fetch | Confirmed |
| 2026-06-04 17:27 | Console 报错 `Unexpected token '<'` | Browser console | Confirmed |

## Confirmed Findings

### Finding 1: `{fingerprint}` 占位符未被替换

**Evidence:** 浏览器中获取的 HTML 源码第 41 行：
```html
<script src="_framework/blazor.webassembly#[.{fingerprint}].js"></script>
```
这与项目源码 `src/BoxWise.Client/wwwroot/index.html:41` 完全相同。

**Detail:** Blazor WASM 发布时，MSBuild targets 应将 `{fingerprint}` 替换为实际的哈希值（例如 `abc1234`），生成 `_framework/blazor.webassembly#abc1234.js`。但发布的 index.html 保留了原始占位符，说明发布过程未正确执行 Blazor WASM 的构建目标。

### Finding 2: `_framework/` 静态文件不存在

**Evidence:** Playwright `fetch('/_framework/blazor.webassembly.js')` 返回 `status: 200, contentType: text/html` — 服务器返回的是 index.html 而非 JavaScript 文件。

**Detail:** 生产服务器上 `_framework/` 目录下的 Blazor WASM 运行时文件（`blazor.webassembly.js`、`blazor.boot.json`、`dotnet.wasm` 等）不存在。所有请求都被 SPA fallback 规则捕获，返回 index.html。

### Finding 3: `Unexpected token '<'` 错误

**Evidence:** Browser console 错误日志。

**Detail:** 浏览器请求 `_framework/blazor.webassembly`（`#` 后内容不发送到服务器），期望得到 JavaScript，但 SPA fallback 返回了 `<!DOCTYPE html>`，JavaScript 引擎无法解析 HTML 标签。

## Deduced Conclusions

### Deduction 1: 发布产物不完整

**Based on:** Finding 1, Finding 2

**Reasoning:** `{fingerprint}` 占位符未替换 和 `_framework/` 文件缺失同时发生，说明 `dotnet publish` 的 Blazor WASM 构建目标没有正常运行或产物没有正确部署到生产服务器。

**Conclusion:** 发布流程存在缺陷 — 要么 `dotnet publish` 命令缺少必要的参数，要么发布后 `_framework/` 目录没有被复制到正确位置。

## Hypothesized Paths

### Hypothesis 1: Docker 构建未使用 Release 配置或缺少 Blazor WASM 发布目标

**Status:** Open

**Theory:** Dockerfile 或 `docker-compose.yml` 中的构建命令使用了 `dotnet build` 而非 `dotnet publish`，或 publish 命令未正确触发 Blazor WASM 的静态文件生成。

**Supporting indicators:** `{fingerprint}` 未被替换是典型的内置模板原始状态；`_framework/` 目录完全不存在。

**Would confirm:** 在服务器上检查 `app/wwwroot/_framework/` 是否存在；检查容器中 `/app/wwwroot/` 的文件列表。

**Would refute:** 如果服务器上 `_framework/` 目录存在且 `index.html` 中 fingerprint 已被替换。

### Hypothesis 2: Caddy SPA fallback 配置过于激进

**Status:** Open (次要因素)

**Theory:** Caddy 的 SPA fallback 规则将所有 404 都重定向到 `index.html`，包括对不存在静态文件的请求。这是 Blazor WASM 部署时的常见配置陷阱。

**Supporting indicators:** `_framework/blazor.webassembly.js` 返回 HTML 而非 404。

**Would confirm:** 查看 Caddyfile 中的 `try_files` 或 `route` 配置。

**Would refute:** 如果 Caddyfile 正确地先尝试文件系统再 fallback。

## Missing Evidence

| Gap              | Impact                               | How to Obtain   |
| ---------------- | ------------------------------------ | --------------- |
| 生产服务器 `wwwroot/` 目录文件列表 | 确认 `_framework/` 是否真的缺失 | SSH 到服务器 `ls -la /opt/boxwise/wwwroot/_framework/` 或 Docker `docker exec` |
| 使用的 `dotnet publish` 命令 | 确认构建命令是否正确 | 查看 Dockerfile 或 CI/CD 配置 |
| Caddyfile 内容 | 确认 SPA fallback 配置 | SSH 查看 Caddy 配置 |

## Source Code Trace

| Element       | Detail                                      |
| ------------- | ------------------------------------------- |
| Error origin  | 浏览器端：`_framework/blazor.webassembly#[.{fingerprint}].js` 请求返回 HTML |
| Trigger       | 浏览器加载 `index.html` → 解析 script 标签 → GET 请求 |
| Condition     | 服务器上 `_framework/` 静态文件缺失 + SPA fallback 返回 index.html |
| Related files | `src/BoxWise.Client/wwwroot/index.html:41` — 占位符源头；Dockerfile；Caddyfile |

## Conclusion

**Confidence:** High

**Root Cause:** 发布到生产服务器时，`_framework/` 目录（Blazor WASM 运行时文件）未被部署，且 `index.html` 中的 `{fingerprint}` 占位符未被替换。浏览器请求 `_framework/blazor.webassembly#[.{fingerprint}].js` 时（`#` 后为 URL fragment，不发送到服务器），实际请求了 `_framework/blazor.webassembly`，该文件不存在，Caddy SPA fallback 返回了 `index.html`（HTML 内容），导致 JavaScript 引擎抛出 `Unexpected token '<'`。

属于**部署流程缺陷**，非代码 Bug。

## Recommended Next Steps

### Fix direction

1. **修正构建命令：** 确保使用 `dotnet publish`（非 `dotnet build`）且配置为 Release
2. **确认发布输出：** 发布后 `wwwroot/_framework/` 目录应包含 `blazor.webassembly.js`、`blazor.boot.json`、`dotnet.wasm` 等文件
3. **修正部署流程：** 确保完整的 `publish/` 目录被复制到服务器

### Diagnostic

1. SSH 到生产服务器，检查 `wwwroot/_framework/` 是否存在及其内容
2. 检查 Dockerfile 中的制作命令
3. 在服务器上运行 `find /app -name "*.wasm" -o -name "blazor.boot.json"` 查看运行时文件位置

## Reproduction Plan

1. 访问 `https://im.elvisw.com/` → 页面显示静止 Loading
2. 打开浏览器 DevTools → Console 看到 `Unexpected token '<'`
3. Network 面板 → `_framework/blazor.webassembly` 返回 HTML
4. View Source → `<script src="_framework/blazor.webassembly#[.{fingerprint}].js">` — 占位符未替换
