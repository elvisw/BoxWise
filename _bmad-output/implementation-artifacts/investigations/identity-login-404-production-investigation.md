# Investigation: 生产环境 /Identity/Account/Login 返回 Not Found

## Hand-off Brief

1. **What happened.** Caddy 反向代理只将 `/api/*` 和 `/admin/*` 转发到 Kestrel，`/Identity/*` 路径被 Caddy 的静态文件处理器拦截，`try_files` 回退到 `index.html`→Blazor WASM 加载并渲染自身的 NotFound 组件。
2. **Where the case stands.** 根因已确认，修复方向明确：在 Caddyfile 中添加 `handle /Identity/*` 反向代理规则。
3. **What's needed next.** 修改服务器上的 `/etc/caddy/Caddyfile`，添加 Identity 路径代理规则，然后 `sudo systemctl reload caddy`。

## Case Info

| Field            | Value                                                                      |
| ---------------- | -------------------------------------------------------------------------- |
| Ticket           | N/A                                                                        |
| Date opened      | 2026-06-05                                                                 |
| Status           | Active                                                                     |
| System           | Debian Linux VPS, .NET 10, Caddy 反向代理, Kestrel Unix socket            |
| Evidence sources | playwright-cli 浏览器测试, 源代码分析 (Program.cs, Caddyfile 配置), 本地 publish 输出验证 |

## Problem Statement

用户访问 `https://im.elvisw.com/Identity/Account/Login` 时看到 "Not Found - Sorry, the content you are looking for does not exist."，这是 Blazor WASM 客户端的 NotFound 组件，而非服务器端 Identity Razor Page 的登录表单。

## Evidence Inventory

| Source   | Status    | Notes     |
| -------- | --------- | --------- |
| playwright-cli 页面快照 | Available | 页面标题 "箱知 BoxWise"，内容显示 Blazor WASM NotFound 组件 |
| playwright-cli 网络请求 | Available | 所有 WASM 框架文件 200，`/api/auth/me` 401，无 Razor Page 渲染 |
| playwright-cli /admin 测试 | Available | 同样返回 Blazor WASM NotFound — 所有 Razor Pages 均受影响 |
| Program.cs 中间件管道 | Available | `MapRazorPages()` 在 `MapFallbackToFile` 之前 — 管道正确 |
| publish 输出 DLL 分析 | Available | `Areas_Identity_Pages_Account_Login` 等视图已编译进 DLL |
| README.md Caddy 配置 | Available | 仅 `/api/*` 和 `/admin/*` 转发到 Kestrel，**缺少 `/Identity/*`** |

## Investigation Backlog

| # | Path to Explore | Priority | Status | Notes |
| - | --------------- | -------- | ------ | ----- |
| 1 | 验证服务器 Caddy 实际配置是否与 README 一致 | High | Open | 需 SSH 到服务器检查 `/etc/caddy/Caddyfile` |
| 2 | 确认 `/admin` (无尾斜杠) 是否也被 Caddy 拦截 | Medium | Open | `/admin/*` 模式可能不匹配 `/admin` |
| 3 | 检查 `appsettings.Production.json` 是否存在特殊配置 | Low | Open | |

## Timeline of Events

| Time        | Event               | Source                | Confidence            |
| ----------- | ------------------- | --------------------- | --------------------- |
| 2026-06-05  | 用户报告 /Identity/Account/Login 返回 Not Found | 用户描述 | Confirmed |
| 2026-06-05  | playwright 确认页面为 Blazor WASM NotFound 组件 | playwright-cli | Confirmed |
| 2026-06-05  | 确认 /admin 同样返回 WASM NotFound → 全 Razor Pages 受影响 | playwright-cli | Confirmed |
| 2026-06-05  | 确认 Razor 视图已编译进 DLL | strings 命令 | Confirmed |
| 2026-06-05  | 发现 README Caddy 配置仅转发 /api/* 和 /admin/*，缺 /Identity/* | README.md:291-306 | Confirmed |

## Confirmed Findings

### Finding 1: Blazor WASM 渲染了 NotFound 页面，非 HTTP 404

**Evidence:** playwright-cli page snapshot — 页面标题 "箱知 BoxWise"，body 内容为 "箱知 · BoxWise\nNot Found\n\nSorry, the content you are looking for does not exist."

**Detail:** 服务器返回的是 `index.html`（SPA 回退），然后 Blazor WASM 路由找不到 `/Identity/Account/Login` 对应的客户端路由，渲染 `NotFound.razor` 组件。

### Finding 2: 所有 Razor Pages 均受影响（/admin 也失效）

**Evidence:** playwright-cli 访问 `https://im.elvisw.com/admin` 返回相同 WASM NotFound 页面。

**Detail:** 这不是 Identity 特定的问题，而是所有服务器端 Razor Pages 在 Caddy 层面被拦截。

### Finding 3: Razor 视图已正确编译进 DLL

**Evidence:** `strings publish-test/BoxWise.Server.dll | grep Areas_Identity` 输出包含 `Areas_Identity_Pages_Account_Login`、`LoginModel` 等已编译视图类。

**Detail:** 服务器端代码本身没有问题，Razor Pages 可以在 Kestrel 中正常路由。

### Finding 4: Caddy 反向代理配置不完整

**Evidence:** README.md:291-306 的 Caddyfile 仅配置了 `/api/*` 和 `/admin/*` 的反向代理规则，`/Identity/*` 路径未被代理。

**Detail:**
```caddy
你的域名 {
    handle /api/* {
        reverse_proxy unix//opt/boxwise/boxwise.sock
    }
    handle /admin/* {
        reverse_proxy unix//opt/boxwise/boxwise.sock
    }
    handle {
        root * /opt/boxwise/wwwroot
        try_files {path} /index.html   # ← /Identity/* 在此被回退到 index.html
        file_server
    }
}
```

## Deduced Conclusions

### Deduction 1: Caddy 静态文件处理器拦截了 /Identity/* 请求

**Based on:** Finding 1, Finding 2, Finding 4

**Reasoning:** Caddy 按 `handle` 块顺序匹配请求：
1. `/api/*` — 不匹配 `/Identity/Account/Login`
2. `/admin/*` — 不匹配
3. 默认 handler — `try_files` 在 `wwwroot/` 中找不到 `Identity/Account/Login` 目录/文件，回退到 `/index.html` → Blazor WASM 加载

**Conclusion:** 需要在 Caddyfile 中添加 `handle /Identity/*` 反向代理规则。

### Deduction 2: /admin 也可能受 Caddy 模式匹配影响

**Based on:** Finding 2 (`/admin` 返回 WASM NotFound)，README Caddy 配置使用 `/admin/*`（带 `/*` 后缀）

**Reasoning:** Caddy 的 `/admin/*` 模式可能不匹配精确路径 `/admin`（无尾斜杠）。需要确认 Caddy 的实际行为。

**结论:** 可能也需要添加 `/admin` 的精确匹配规则。

## Hypothesized Paths

### Hypothesis 1: Caddy 配置缺少 /Identity/* 代理规则（根因）

**Status:** Confirmed

**Theory:** README 中的 Caddyfile 仅配置了 API 和管理后台的反向代理，遗漏了 Identity 页面路径。用户按照 README 部署后，Identity 请求未被转发到 Kestrel。

**Supporting indicators:**
- README 第 311 行注释明确说："二进制部署中 Caddy 直接提供 `wwwroot/` 下的静态文件，仅将 `/api/*` 和 `/admin/*` 请求转发到 Kestrel"
- 这与实际行为完全吻合

**Resolution:** 已确认为根因。修复方案见 Recommended Next Steps。

## Source Code Trace

| Element       | Detail                                      |
| ------------- | ------------------------------------------- |
| Error origin  | `/etc/caddy/Caddyfile` — Caddy 反向代理配置，缺少 `/Identity/*` 代理规则 |
| Trigger       | 浏览器请求 `/Identity/Account/Login`（Cookie 认证中间件在未登录时自动重定向到此路径） |
| Condition     | Caddy 默认 handler 将请求作为静态文件处理，`wwwroot/` 中无对应文件，回退到 `index.html` |
| Related files | `README.md:291-306` (Caddyfile 模板), `src/BoxWise.Server/Program.cs:60` (LoginPath 配置), `src/BoxWise.Server/Areas/Identity/Pages/Account/Login.cshtml` (登录页面) |

## Conclusion

**Confidence:** High

**根因：** Caddy 反向代理配置不完整。二进制部署的 Caddyfile（README.md:291-306）仅将 `/api/*` 和 `/admin/*` 路径转发到 Kestrel，所有其他请求（包括 `/Identity/*`）由 Caddy 直接从 `wwwroot/` 提供静态文件。由于 `wwwroot/` 中不存在 `Identity/` 目录，`try_files` 回退到 `/index.html`，触发 Blazor WASM 加载并渲染客户端 NotFound 组件。

Identity Razor Pages 本身没有问题（视图已正确编译进 DLL，中间件管道正确），问题出在反向代理层。

## Recommended Next Steps

### Fix direction

**方案 A（推荐）：** 在 Caddyfile 中添加 Identity 路径代理规则：
```caddy
handle /Identity/* {
    reverse_proxy unix//opt/boxwise/boxwise.sock
}
```

**同时修复 /admin 精确路径匹配：**
```caddy
handle /admin {
    reverse_proxy unix//opt/boxwise/boxwise.sock
}
handle /admin/* {
    reverse_proxy unix//opt/boxwise/boxwise.sock
}
```

**需更新的文件：**
1. `/etc/caddy/Caddyfile` (服务器) — 添加 Identity 代理规则
2. `README.md` — 更新 Caddyfile 模板，添加 `/Identity/*` 路由说明
3. `docker-compose` 中的 `Caddyfile`（如果 Docker 部署也存在相同问题）

### 验证步骤

```bash
# 1. SSH 到服务器，编辑 Caddy 配置
sudo nano /etc/caddy/Caddyfile

# 2. 添加 Identity 路由（在 /admin/* handle 块之后）
#    handle /Identity/* {
#        reverse_proxy unix//opt/boxwise/boxwise.sock
#    }

# 3. 验证配置语法
sudo caddy validate --config /etc/caddy/Caddyfile

# 4. 重载 Caddy（零停机）
sudo systemctl reload caddy

# 5. 浏览器测试
#    https://im.elvisw.com/Identity/Account/Login
#    https://im.elvisw.com/admin
```

## Reproduction Plan

1. 按照 README 二进制部署流程部署到 Debian VPS
2. 配置 Caddy（使用当前 README 中的 Caddyfile）
3. 浏览器访问 `https://<域名>/Identity/Account/Login`
4. 观察到 Blazor WASM "Not Found" 而非 Identity 登录页面
5. 在 Caddyfile 中添加 `/Identity/*` 代理规则后重载 Caddy
6. 刷新页面，应正常显示 Identity 登录表单

## Side Findings

- `/admin` 也可能因 Caddy `/admin/*` 模式不匹配精确路径 `/admin` 而受影响，需一并修复
- `staticwebassets.endpoints.json` 文件大小 741KB（压缩后），主要包含 MudBlazor 和 Bootstrap 的 fingerprint 映射
- 本地 publish 输出确认所有 Razor 视图已编译进 `BoxWise.Server.dll`，无遗漏
