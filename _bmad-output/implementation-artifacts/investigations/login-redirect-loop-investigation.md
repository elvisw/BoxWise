# Investigation: 登录后重定向回登录页

## Hand-off Brief

1. **What happened.** 用户登录成功后（`POST /api/auth/login` 返回 200 + `AuthUserDto`），Blazor WASM 客户端调用 `GET /api/auth/me` 检查认证状态时返回 401，因为跨源请求（localhost:5001 → localhost:5000）中浏览器未携带 Cookie。
2. **Where the case stands.** 根因已确认为 **Confirmed**：Blazor WASM 的 `HttpClient` 默认 `credentials` 模式为 `'same-origin'`，跨端口请求不会发送 Cookie，导致 `/api/auth/me` 无法识别已登录用户。
3. **What's needed next.** 为 Client 的 `HttpClient` 添加 `credentials: 'include'` 配置，建议通过 `DelegatingHandler` + `SetBrowserRequestCredentials(BrowserRequestCredentials.Include)` 实现。

## Case Info

| Field            | Value                                                                                       |
| ---------------- | ------------------------------------------------------------------------------------------- |
| Ticket           | N/A                                                                                         |
| Date opened      | 2026-05-25                                                                                  |
| Status           | Concluded                                                                                   |
| System           | .NET 10, Blazor WASM, Server: localhost:5000, Client: localhost:5001                        |
| Evidence sources | 浏览器控制台错误日志, 源代码 (AuthEndpoints.cs, AuthService.cs, CookieAuthenticationStateProvider.cs, Program.cs) |

## Problem Statement

使用初始用户登录后，页面又回到登录页面。浏览器控制台显示 `GET https://localhost:5000/api/auth/me` 返回 `401 (Unauthorized)`，`DenyAnonymousAuthorizationRequirement` 未满足。

## Evidence Inventory

| Source                                   | Status    | Notes                                                |
| ---------------------------------------- | --------- | ----------------------------------------------------- |
| 浏览器控制台错误日志                     | Available | 401 on /api/auth/me, ERR_ABORTED                      |
| AuthEndpoints.cs (Server)                | Available | Login 设置 `isPersistent: true`，正确调用 SignInManager |
| Program.cs (Server)                      | Available | Cookie: SameSite=None, Secure; CORS: AllowCredentials  |
| AuthService.cs (Client)                  | Available | login 成功后调用 NotifyAuthenticationStateChanged      |
| CookieAuthenticationStateProvider.cs      | Available | GET /api/auth/me 检查认证状态                          |
| Program.cs (Client)                      | Available | `new HttpClient { BaseAddress }` — 无 credentials 配置 |
| CLIENT_HTTP_CREDENTIALS (缺失)           | Confirmed | WASM HttpClient 未配置跨源 credentials: include       |

## Investigation Backlog

| # | Path to Explore                                   | Priority | Status | Notes                                                                 |
| - | ------------------------------------------------- | -------- | ------ | --------------------------------------------------------------------- |
| 1 | Client HttpClient 是否携带 Cookie                 | High     | Done   | Confirmed: 跨源 fetch 默认 credentials='same-origin'，不同端口=不同源 |
| 2 | Server Cookie/CORS 配置是否兼容跨源 Cookie 发送   | High     | Done   | SameSite=None + Secure + AllowCredentials 配置正确                    |
| 3 | 是否有 DelegatingHandler 设置 credentials         | High     | Done   | Confirmed: 没有 — 直接 `new HttpClient` 无 handler                    |

## Timeline of Events

| Time       | Event                                                        | Source                                          | Confidence |
| ---------- | ------------------------------------------------------------ | ----------------------------------------------- | ---------- |
| 1          | 用户提交登录表单                                             | 用户报告                                        | Confirmed  |
| 2          | `POST /api/auth/login` 成功 (200) + Cookie 设置              | AuthEndpoints.cs:33-34, Server 配置             | Confirmed  |
| 3          | `AuthService` 调用 `NotifyAuthenticationStateChanged()`      | AuthService.cs:27                               | Confirmed  |
| 4          | Blazor 重新渲染，触发 `GetAuthenticationStateAsync()`        | CookieAuthenticationStateProvider.cs:16         | Confirmed  |
| 5          | `GET /api/auth/me` → 浏览器未携带 Cookie（跨源）               | 控制台 401 错误                                 | Confirmed  |
| 6          | Server 返回 401 → 客户端判定未认证 → 显示登录页              | CookieAuthenticationStateProvider.cs:43         | Confirmed  |

## Confirmed Findings

### Finding 1: Server 端 Cookie 和 CORS 配置正确

**Evidence:** `src/BoxWise.Server/Program.cs:32-71`

**Detail:**
- `SameSiteMode.None` + `CookieSecurePolicy.Always` — 允许跨站点携带 Cookie（src/BoxWise.Server/Program.cs:35-36）
- CORS `AllowCredentials()` + `WithOrigins("https://localhost:5001")` — 允许跨源请求携带凭据（src/BoxWise.Server/Program.cs:67-68）
- `isPersistent: true` — 持久化登录 Cookie（src/BoxWise.Server/Endpoints/AuthEndpoints.cs:34）

### Finding 2: Client HttpClient 未配置跨源 credentials

**Evidence:** `src/BoxWise.Client/Program.cs:14-17`

**Detail:**
```csharp
builder.Services.AddScoped(sp => new HttpClient
{
    BaseAddress = new Uri(apiBaseUrl)
});
```
直接 `new HttpClient()` 构造，无 `DelegatingHandler`，无 `WebAssemblyHttpRequestMessage` 配置。Blazor WASM 在底层调用浏览器 `fetch` API，默认 `credentials: 'same-origin'`。Client 运行在 `localhost:5001`，请求 `localhost:5000` 属于**跨源**（端口不同），浏览器不会携带 Cookie。

### Finding 3: 登录 API 调用成功但不携带返回的 Cookie

**Evidence:** `src/BoxWise.Client/Services/AuthService.cs:21` — `PostAsJsonAsync("api/auth/login", ...)`

**Detail:** `POST /api/auth/login` 本身也不携带 Cookie，但它是 `AllowAnonymous()` 的，所以成功返回 200。Server 在响应 `Set-Cookie` 头中设置了 Cookie，但浏览器在**后续跨源请求**中不会自动附带它，因为 fetch 默认 `credentials: 'same-origin'`。

## Deduced Conclusions

### Deduction 1: 登录 Cookie 被 Set 但后续请求未发送

**Based on:** Finding 1, Finding 2, Finding 3

**Reasoning:** Server 在登录响应中正确设置了 Cookie（SameSite=None 允许跨站点）。但因为 Client 的 HttpClient 未配置 `credentials: 'include'`，浏览器在发起 `GET /api/auth/me` 跨源请求时不会附带该 Cookie。Server 看不到认证 Cookie，返回 401。

**Conclusion:** 根因是 Client HttpClient 缺少 `credentials: 'include'` 配置。

## Source Code Trace

| Element       | Detail                                                                                    |
| ------------- | ----------------------------------------------------------------------------------------- |
| Error origin  | `src/BoxWise.Client/Services/CookieAuthenticationStateProvider.cs:20` — `GET api/auth/me` |
| Trigger       | Blazor WASM 初始化或认证状态变更时调用 `GetAuthenticationStateAsync()`                      |
| Condition     | `HttpClient` 跨源请求不携带 Cookie → Server 返回 401 → 客户端判定为未认证                    |
| Related files | `src/BoxWise.Client/Program.cs:14-17`, `src/BoxWise.Client/Services/AuthService.cs:21,27`  |

## Conclusion

**Confidence:** High

**Root cause:** Blazor WASM Client 的 `HttpClient` 在跨源请求（`localhost:5001` → `localhost:5000`）时，底层 `fetch` API 默认 `credentials: 'same-origin'`，不发送认证 Cookie。Server 端的 SameSite/CORS 配置是正确的。

## Recommended Next Steps

### Fix direction

为 Client 的 `HttpClient` 添加一个 `DelegatingHandler`，在每个请求上设置 `request.SetBrowserRequestCredentials(BrowserRequestCredentials.Include)`，使浏览器在跨源请求中也携带 Cookie。

### Diagnostic

无需额外诊断 — 根因已确认。修复后验证：登录 → 页面不跳回登录页 → 控制台 `/api/auth/me` 返回 200 + 用户信息。

## Reproduction Plan

1. 启动 Server (`cd src/BoxWise.Server && dotnet run`) — 监听 `https://localhost:5000`
2. 启动 Client (`cd src/BoxWise.Client && dotnet run`) — 监听 `https://localhost:5001`
3. 浏览器打开 `https://localhost:5001`
4. 输入用户名密码登录
5. **Expect:** 成功跳转到主页
6. **Actual (bug):** 停留在登录页，控制台显示 401

## Follow-up: 2026-05-25

### New Evidence

完整源码确认（详见 Confirmed Findings）。

### Additional Findings

`Program.cs:46` — `AuthService.cs` 中定义了一个**私有** `record AuthUserDto`，与 `Shared/Dtos/AuthUserDto.cs` 中的同名 DTO 重复但无关。不是本 bug 的原因。

### Updated Hypotheses

Hypothesis #1 — **Confirmed**: Blazor WASM HttpClient 未配置跨源 credentials，导致 Cookie 未发送。

### Updated Conclusion

见上方 Conclusion 和 Fix direction。建议立即修复。
