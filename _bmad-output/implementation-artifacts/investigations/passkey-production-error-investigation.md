# Investigation: 生产环境通行密钥注册失败

## Hand-off Brief

1. **What happened.** 生产环境未配置 `WebAuthn:Origin` / `WebAuthn:ServerDomain`，FIDO2 默认使用 `localhost` 作为 RP ID，浏览器因域名不匹配拒绝 `navigator.credentials.create()`，Blazor WASM 客户端捕获 JSException 后显示"浏览器验证失败"。
2. **Where the case stands.** 根因已确认（Hypothesis #1 Confirmed），置信度 High。需创建 `appsettings.Production.json` 或配置 Docker 环境变量。
3. **What's needed next.** 在生产环境补全 `WebAuthn:Origin` 和 `WebAuthn:ServerDomain` 配置，重启服务后验证。

## Case Info

| Field            | Value                                                                      |
| ---------------- | -------------------------------------------------------------------------- |
| Ticket           | N/A                                                                        |
| Date opened      | 2026-06-09                                                                 |
| Status           | Concluded                                                                  |
| System           | ASP.NET Core 10 + Blazor WASM + Fido2NetLib + 生产部署（Caddy 反向代理）     |
| Evidence sources | 源码审查 (`Program.cs`, `WebAuthnSetup.razor`, `webauthn.js`, `deployment-guide.md`, `webauthn-setup-guide.md`) |

## Problem Statement

用户报告：开发测试环境下 passkey（通行密钥）申请和登录正常，生产环境无法正常工作。申请通行密钥报错 `浏览器验证失败，请确保设备支持通行密钥功能`。

## Evidence Inventory

| Source   | Status    | Notes     |
| -------- | --------- | --------- |
| `WebAuthnSetup.razor` 错误捕获逻辑 | Available | `src\BoxWise.Client\Components\WebAuthnSetup.razor:125-128` — `catch (JSException)` 显示中文错误信息 |
| `webauthn.js` createCredential 实现 | Available | `src\BoxWise.Client\wwwroot\js\webauthn.js:24-28` — 调用 `navigator.credentials.create()` |
| `Program.cs` FIDO2 配置 | Available | `src\BoxWise.Server\Program.cs:161-174` — `WebAuthn:Origin` 默认 `https://localhost:5001`，`ServerDomain` 默认从 Origin 解析 |
| `WebAuthnEndpoints.cs` 注册端点 | Available | `src\BoxWise.Server\Endpoints\WebAuthnEndpoints.cs` — `/api/auth/webauthn/register-begin` 生成 CredentialCreateOptions |
| `WebAuthnService.cs` | Available | `src\BoxWise.Server\Services\WebAuthnService.cs` — `StartRegistration()` 调用 `_fido2.RequestNewCredential()` |
| 生产环境实际配置 | **Missing** | 需确认 `WebAuthn:Origin` / `WebAuthn:ServerDomain` 在 Docker/环境变量中的配置状态 |
| 生产环境浏览器 WebAuthn 错误详情 | **Missing** | `JSException` 被通用 catch 吞掉了原始 `DOMException` 信息（如 `SecurityError: The relying party ID is not a registrable domain suffix...`） |
| 部署指南 | Available | `docs/deployment-guide.md:51-52` — 明确标注生产环境 `WebAuthn__Origin` 和 `WebAuthn__ServerDomain` 为必需配置 |

## Investigation Backlog

| # | Path to Explore | Priority | Status | Notes |
| - | --------------- | -------- | ------ | ----- |
| 1 | 确认生产环境 `WebAuthn:Origin` / `WebAuthn:ServerDomain` 环境变量配置 | High | Open | 如未配置或配置错误，则根因确认 |
| 2 | 改进错误日志：在 `catch (JSException)` 中暴露原始 `ex.Message` | Medium | Open | 当前吞掉了原始错误，导致排查困难 |
| 3 | 检查 `Origins` 集合是否包含生产域名 | Medium | Open | 即使 `WebAuthn:Origin` 配置了，`Origins` 中的 localhost 硬编码虽然无害但需确认 |

## Timeline of Events

| Time        | Event               | Source                | Confidence            |
| ----------- | ------------------- | --------------------- | --------------------- |
| 2026-06-09 | 用户报告生产环境通行密钥注册报错 | 用户报告 | Confirmed |
| 2026-06-09 | 定位错误源头为 `WebAuthnSetup.razor:127` 通用 JSException catch | 源码 | Confirmed |

## Confirmed Findings

### Finding 1: 错误被通用 catch 吞掉原始信息

**Evidence:** `src\BoxWise.Client\Components\WebAuthnSetup.razor:125-128`

**Detail:** `catch (JSException)` 块（不含任何条件过滤）显示通用中文提示"浏览器验证失败，请确保设备支持通行密钥功能"。两个特定异常（"用户取消了操作"、"超时"）在之前被单独处理，其余所有 JSException（包括 `SecurityError`、`NotAllowedError`、`InvalidStateError` 等）都被归为此通用错误。

### Finding 2: FIDO2 配置默认值仅适用于 localhost 开发环境

**Evidence:** `src\BoxWise.Server\Program.cs:161-174`

**Detail:**
- `webAuthnOrigin` 默认 `"https://localhost:5001"`（如未配置 `WebAuthn:Origin`）
- `ServerDomain` 默认从 `webAuthnOrigin` URI 提取 Host → `"localhost"`（如未配置 `WebAuthn:ServerDomain`）
- `Origins` 集合硬编码包含 `"https://localhost:5000"` 和 `"https://localhost:5001"`

### Finding 3: WebAuthn spec 要求 RP ID 必须匹配页面域名

**Evidence:** WebAuthn Level 3 specification, `navigator.credentials.create()` algorithm.

**Detail:** 浏览器在 `navigator.credentials.create()` 中会检查 `rp.id`（即 `ServerDomain`）是否为当前页面域名的注册域后缀。如果 RP ID = `"localhost"` 而页面域名为 `boxwise.example.com`，浏览器抛出 `SecurityError`。同时，当前 `eTLD+1`（如 `example.com`）必须匹配。

### Finding 4: 部署文档明确规定生产环境必须配置

**Evidence:** `docs/deployment-guide.md:51-52`, `docs/webauthn-setup-guide.md`

**Detail:** 文档明确标注 `WebAuthn__Origin` 和 `WebAuthn__ServerDomain` 为生产环境必需配置，且提供了 Docker Compose 配置示例。

## Deduced Conclusions

### Deduction 1: 生产环境 WebAuthn 配置缺失

**Based on:** Finding 2, Finding 3, Finding 4

**Reasoning:**
1. 开发环境正常工作 → 说明代码逻辑本身没问题
2. 生产环境报 `JSException` → `navigator.credentials.create()` 失败
3. 最可能的原因是 RP ID (ServerDomain) 不匹配：配置默认值是 `"localhost"`，生产域名为真实域名
4. 如果 `WebAuthn:Origin` 未配置，`Origins` 集合也不包含生产 origin → 双重拦截

**Conclusion:** 生产环境大概率未设置 `WebAuthn:Origin` 和 `WebAuthn:ServerDomain` 环境变量，导致 FIDO2 ServerDomain = `"localhost"`，浏览器 WebAuthn API 因 RP ID 不匹配而拒绝创建凭据。

## Hypothesized Paths

### Hypothesis 1: 生产环境未配置 WebAuthn:Origin / WebAuthn:ServerDomain（默认值 localhost）

**Status:** Confirmed

**Theory:** 生产环境未设置 `WebAuthn__Origin` 和 `WebAuthn__ServerDomain` 环境变量，也未创建 `appsettings.Production.json`。代码回退到默认值 `https://localhost:5001` → `ServerDomain = "localhost"`。浏览器端调用 `navigator.credentials.create()` 时，RP ID 为 `"localhost"` 但页面域名为实际生产域名，浏览器抛出 `SecurityError`。

**Resolution:** 用户确认忘记配置 `appsettings.Production.json`。生产环境缺少 WebAuthn 的 Origin 和 ServerDomain 配置，FIDO2 使用默认值 `localhost` 作为 RP ID，导致浏览器因域名不匹配拒绝 WebAuthn 操作。

### Hypothesis 2: RP ID 使用了错误的域名格式（如子域名不匹配、带 www）

**Status:** Open

**Theory:** 用户配置了 `WebAuthn:Origin` / `WebAuthn:ServerDomain`，但域名格式不符合 WebAuthn 规范。例如浏览器访问 `app.boxwise.example.com`，但 `ServerDomain` 配置为 `boxwise.example.com`（或反之）。WebAuthn 要求 RP ID 是当前 `eTLD+1` 或其父域名。

**Would confirm:** 确认配置值与实际访问域名不匹配。

**Would refute:** 配置值与实际访问域名一致。

## Missing Evidence

| Gap              | Impact                               | How to Obtain   |
| ---------------- | ------------------------------------ | --------------- |
| 生产环境实际 `WebAuthn:Origin` / `WebAuthn:ServerDomain` 配置 | 确认或排除 Hypothesis 1 | 检查 Docker Compose 文件或 `docker inspect` 或服务器环境变量 |
| 浏览器 WebAuthn 原始错误详情 | 精确确认 `DOMException` 类型（`SecurityError` vs `NotAllowedError` 等） | 改进错误日志（Backlog #2），或用 `playwright-cli` 在类似配置下复现 |
| 生产环境访问的实际域名 | 验证 RP ID 匹配逻辑 | 用户确认或部署配置 |

## Source Code Trace

| Element       | Detail                                      |
| ------------- | ------------------------------------------- |
| Error origin  | `src\BoxWise.Client\Components\WebAuthnSetup.razor:127` — `catch (JSException)` |
| Trigger       | 用户在 Settings 页面点击"开始注册" → `StartRegistrationAsync()` → `webauthn.createCredential(optionsJson)` → `navigator.credentials.create()` 抛出 `DOMException` |
| Condition     | FIDO2 `ServerDomain` (RP ID) 与浏览器页面域名不匹配，或页面 origin 不在 `Origins` 白名单中 |
| Related files | `src\BoxWise.Server\Program.cs:161-174` (FIDO2配置), `src\BoxWise.Server\Services\WebAuthnService.cs:34-62` (StartRegistration), `src\BoxWise.Client\wwwroot\js\webauthn.js:24-28` (createCredential), `docs\deployment-guide.md` (部署配置) |

## Reproduction Plan

1. 以生产配置启动 Docker（不设置 `WebAuthn__Origin` / `WebAuthn__ServerDomain`）
2. 通过生产域名 HTTPS 访问应用
3. 登录后进入 Settings → 通行密钥管理 → 开始注册
4. 预期：浏览器 WebAuthn 弹窗不出现，显示通用错误"浏览器验证失败，请确保设备支持通行密钥功能"
5. 使用 `playwright-cli` 可在浏览器 DevTools Console 中捕获 `DOMException` 详情

## Conclusion

**Confidence:** High

根因确认：生产环境未配置 `WebAuthn:Origin` 和 `WebAuthn:ServerDomain`。`appsettings.Production.json` 不存在，也未通过 Docker 环境变量注入。代码回退到默认值 `ServerDomain = "localhost"`，浏览器执行 `navigator.credentials.create()` 时因 RP ID (`localhost`) 与实际生产域名不匹配而抛出 `SecurityError`。

修复方式：在生产环境创建 `src/BoxWise.Server/appsettings.Production.json` 并配置 `WebAuthn:Origin` 和 `WebAuthn:ServerDomain`，或通过 Docker 环境变量 `WebAuthn__Origin` / `WebAuthn__ServerDomain` 注入。

## Recommended Next Steps

### Fix direction

1. **生产环境配置补全**（如确认 Hypothesis 1）：
   - 在 Docker Compose 或环境变量中设置 `WebAuthn__Origin=https://<实际域名>` 和 `WebAuthn__ServerDomain=<域名（不含协议端口）>`
   - 重启服务

2. **改进错误诊断**（无论 Hypothesis 1 是否成立）：
   - 在 `WebAuthnSetup.razor` 的泛化 `catch (JSException ex)` 块中，将 `ex.Message` 输出到浏览器控制台（`Console.Error.WriteLine`）或附加到错误信息中
   - 便于未来排查具体 DOMException 类型

### Diagnostic

1. 在生产服务器执行：`docker inspect <container> | grep -i webauthn` 或检查 `docker-compose.yml` 中的 environment 段
2. 如使用 `playwright-cli`，在生产环境（或相同配置的 staging）上打开浏览器 Console，观察 WebAuthn 调用时抛出的具体异常

## Side Findings

- `WebAuthnSetup.razor:125-128` 捕获所有非特定 JSException 时丢失了原始错误信息，降低了可调试性。建议至少记录 `ex.Message` 到控制台日志。（Finding 1 衍生）
- `Program.cs:167-173` 中 `Origins` 硬编码了 localhost 两个端口，生产环境下这些是冗余的但无害。
