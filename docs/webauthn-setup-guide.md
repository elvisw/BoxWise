# WebAuthn 通行密钥配置指南

## 概述

BoxWise 支持 WebAuthn（通行密钥）作为无密码登录方式。用户可以使用设备的指纹、面容或硬件密钥（如 YubiKey）登录，无需输入密码或验证码。

WebAuthn 基于 [FIDO2 标准](https://fidoalliance.org/fido2/)，凭证与注册时的 **origin（协议 + 域名 + 端口）** 绑定。

> **定位：** 通行密钥用于无密码登录，**不是密码登录的第二因素（2FA）**。密码登录的第二因素当前仅支持 TOTP，详见[安全模型与已知限制](#安全模型与已知限制)。

---

## 测试环境配置

### 前提条件

- **HTTPS 必需**：WebAuthn API 在大多数浏览器中仅支持 HTTPS 或 `localhost`
- **支持的浏览器**：Chrome 67+、Firefox 60+、Edge 18+、Safari 14+

### 开发环境启动

BoxWise 开发环境默认已配置 `localhost` HTTPS，开箱即用：

```bash
# 启动 Server（5000 端口）
cd src/BoxWise.Server
dotnet run
```

### 双端口注意事项 ⚠️

开发环境有两个端口：

| 端口 | 地址 | 用途 |
|------|------|------|
| 5000 | `https://localhost:5000` | Server（API + Admin） |
| 5001 | `https://localhost:5001` | Client（Blazor WASM 热重载） |

**WebAuthn 凭证与 origin 绑定。** 如果在 `localhost:5001` 上注册通行密钥，在 `localhost:5000` 上验证时可能失败，因为端口不同导致 origin 不匹配。

**建议：** 开发时统一使用 `https://localhost:5000` 进行 WebAuthn 注册和验证。启动 Server 后直接访问 5000 端口即可。

### 配置

项目默认配置位于 `Program.cs`：

```csharp
var webAuthnOrigin = builder.Configuration.GetValue<string>("WebAuthn:Origin") ?? "https://localhost:5001";
var webAuthnOrigins = new HashSet<string> { webAuthnOrigin };
if (builder.Environment.IsDevelopment())
{
    // 仅开发环境允许两个 localhost 端口
    webAuthnOrigins.Add("https://localhost:5000");
    webAuthnOrigins.Add("https://localhost:5001");
}
var fido2Config = new Fido2Configuration
{
    RPID = builder.Configuration["WebAuthn:ServerDomain"]
        ?? new Uri(webAuthnOrigin).Host,
    RPName = "BoxWise",
    Origins = webAuthnOrigins
};
```

可通过 `appsettings.Development.json` 覆盖 `WebAuthn:Origin`：

```json
{
  "WebAuthn": {
    "Origin": "https://localhost:5000",
    "ServerDomain": "localhost"
  }
}
```

**注意：** 生产环境的 `Origins` 只包含 `WebAuthn:Origin` 配置的 origin；`https://localhost:5000` 和 `https://localhost:5001` 仅在开发环境（`builder.Environment.IsDevelopment()`）自动加入，不要依赖这两项做生产验证。`WebAuthn:Origin` 同时用于解析 `RPID` 的 fallback 值。

---

## 生产环境配置

### 1. HTTPS 域名

生产环境必须使用 HTTPS 域名。WebAuthn 在非 `localhost` 环境下强制要求 TLS。

`WebAuthn:Origin` 必须填写**用户浏览器实际访问的外部 origin**：协议 + 域名 + 非默认端口（如 `https://boxwise.example.com` 或 `https://boxwise.example.com:8443`），**不能包含路径、查询串或末尾斜杠**。不要用内网主机名、容器名或反向代理监听地址替代公开 origin——浏览器只认地址栏里的地址。

使用 Caddy 的反向代理配置示例（`Caddyfile`）：

```
boxwise.example.com {
    reverse_proxy localhost:5000
}
```

### 2. WebAuthn 配置

**方式一：配置文件（推荐）**

创建 `src/BoxWise.Server/appsettings.Production.json`：

```json
{
  "WebAuthn": {
    "Origin": "https://boxwise.example.com",
    "ServerDomain": "boxwise.example.com"
  }
}
```

> **文件位置：** 必须放在 Server 项目根目录（`src/BoxWise.Server/`），与 `appsettings.json` 同级。构建后自动复制到发布目录。

**方式二：Docker 环境变量**

Docker 环境下通过环境变量注入配置：

```yaml
# docker-compose.yml
environment:
  - WebAuthn__Origin=https://boxwise.example.com
  - WebAuthn__ServerDomain=boxwise.example.com
```

> **注意：** 环境变量用双下划线 `__` 替代 JSON 层级分隔符。`appsettings.Production.json` 挂载也适用于 Docker（通过 `docker-compose.yml` 的 `volumes` 挂载）。

| 配置项 | 说明 | 示例 |
|--------|------|------|
| `WebAuthn:Origin` | 生产启用通行密钥时**必需**。用户访问网站的完整外部 origin（协议 + 域名 + 非默认端口，无路径） | `https://boxwise.example.com` |
| `WebAuthn:ServerDomain` | RP ID（不含协议、端口或路径）；**建议显式固定**。未配置时代码从 `WebAuthn:Origin` 的主机名推导 | `boxwise.example.com` |

> **RP ID 说明：** `WebAuthn:ServerDomain` 未配置时，代码执行 `new Uri(webAuthnOrigin).Host` 推导 RP ID，因此它不是无条件启动必填项。但升级或调整 Origin 时主机名可能变化，显式固定可避免已注册凭据因 RP ID 改变而失效。已部署实例必须**沿用现有 RP ID**，不要换域名或改写该项。

### 3. 反向代理注意事项

- **不要修改 Host 头**：确保反向代理将原始请求的 Host 头传递给后端
- **TLS 终止**：WebAuthn 的 origin 检查依赖浏览器感知的协议。如果使用 Cloudflare 等 CDN 做 TLS 终止，确保 `Origin` 配置与浏览器地址栏一致
- **端口**：如果使用非标准端口（如 8443），origin 必须包含端口号
- **不要用内网反代地址**：`WebAuthn:Origin` 要写公开访问地址，不能写 `http://boxwise:5000`、`http://127.0.0.1:5000` 这类反向代理内部地址

### 4. 升级与验收

- **沿用现有 RP ID**：已有通行密钥的实例升级时，保持 `WebAuthn:ServerDomain` 与 `WebAuthn:Origin` 不变。更换域名或改写 RP ID 会让已注册凭据全部失效，用户需重新注册。
- **localhost 仅在开发生效**：`https://localhost:5000`、`https://localhost:5001` 只在 `Development` 环境加入 `Origins`，生产不要依赖它们验证。
- **真实设备冒烟**：部署后在真实手机/电脑上完成一次通行密钥注册和一次无密码登录，再尝试删除凭据。仅用 curl 或桌面浏览器无痕窗口不足以确认平台验证器可用。

---

## 架构说明

### 注册流程

```
用户点击"设置通行密钥"
  → POST /api/auth/webauthn/register-begin（获取 CredentialCreateOptions）
  → 浏览器弹出指纹/面容/PIN 验证（navigator.credentials.create）
  → POST /api/auth/webauthn/register-complete（提交 attestation + X-Device-Name）
  → 账号被标记为已配置 WebAuthn（`TwoFactorEnabled=true`），但密码登录不会因此进入 2FA 挑战
  → 返回 8 个 10 位恢复码
  → 注册完成区域展示恢复码（仅显示一次，可一键复制）
```

### 无密码登录流程（Passkey）

> **注意：** 通行密钥仅用于无密码登录，不作为密码登录后的 2FA 第二因素验证。触发条件与已知限制见 [安全模型与已知限制](#安全模型与已知限制)。

```
用户点击"使用通行密钥登录"
  → POST /api/auth/webauthn/login-begin（获取 AssertionOptions，匿名端点）
  → 浏览器弹出指纹/面容/PIN 验证（navigator.credentials.get）
  → POST /api/auth/webauthn/login-complete（提交 assertion，签发 Cookie）
  → 登录成功
```

### 凭据管理

```
GET  /api/auth/webauthn/credentials       → 已注册凭据列表
DELETE /api/auth/webauthn/credentials/{id} → 删除指定凭据
```

### 技术栈

- **后端**：`Fido2NetLib` NuGet 包
- **前端**：浏览器原生 `WebAuthn API`（通过 `webauthn.js` 封装）
- **Session**：服务器端内存 Session（5 分钟超时），存储注册/登录中间状态
- **速率限制**：Passkey 登录端点使用 `passkey-login` 策略（30次/5分钟/IP）

---

## 安全模型与已知限制

密码登录是否进入 2FA 挑战由 `SignInManager.IsTwoFactorEnabledAsync` 决定：

```
TwoFactorEnabled && GetValidTwoFactorProvidersAsync(user).Count > 0
```

有效 provider 来自 Identity authenticator key（通过 Identity `EnableAuthenticator` 设置 TOTP）、已确认邮箱（Email provider）或已确认电话（本应用未使用）。**通行密钥不是 Identity provider**：注册通行密钥只写 `ConfiguredMethods |= WebAuthn` 并置 `TwoFactorEnabled=true`，不会让密码登录进入 2FA 挑战。

因此存在两个真实组合：

- **仅通行密钥 + 邮箱未确认**：密码登录不触发 2FA，通行密钥不保护密码登录。
- **仅通行密钥 + 邮箱已确认**：Identity 会发起 2FA 挑战，但 `LoginWith2fa` 只接受验证器码，用户无法完成；只能用恢复码关闭双重验证。这是当前已知限制。

仅通行密钥的账户不要只依赖密码登录来保护账户；在相关问题修复前，也不要通过确认邮箱来为这类账户启用密码找回。

---

## 常见问题

### Q: "当前环境不支持通行密钥" 提示

**原因：** 以下任一条件未满足：
1. 非 HTTPS 连接（生产环境）或 非 localhost
2. 浏览器不支持 WebAuthn API
3. `WebAuthn:Origin` 配置与浏览器地址栏不一致

**排查：**
1. 确认使用 `https://` 访问
2. 在浏览器控制台执行 `window.webauthn.isAvailable()`，应返回 `true`
3. 检查 `appsettings.Production.json` 中 `WebAuthn:Origin` 与浏览器地址栏完全匹配（Docker 则检查 `WebAuthn__Origin` 环境变量）

### Q: iOS Safari 上点击注册无反映

iOS Safari 要求 `navigator.credentials.create()` 必须由用户手势直接触发。如果 API 调用耗时过长（如慢网络导致 Begin 请求延迟），手势上下文可能丢失。

**解决：** 确保网络环境良好，刷新页面后重试。

### Q: 注册成功后登录时无法使用通行密钥

**排查：**
1. 确认注册和登录使用**同一个 origin**（协议 + 域名 + 端口一致）
2. 检查凭据是否未被删除（设置页 → 凭据列表）
3. 如果更换了设备，通行密钥不会自动同步（取决于平台密钥同步策略）

### Q: 通行密钥注册失败"已达凭证数量上限"

每个用户最多注册 10 个通行密钥。在设置页的凭据列表中删除不再使用的密钥后重试。

### Q: 生产环境 Caddy 反代后 WebAuthn 不可用

确认 Caddy 转发了原始 Host 头：

```
boxwise.example.com {
    reverse_proxy localhost:5000 {
        header_up Host {host}
    }
}
```

### Q: 恢复码在哪里查看？

通行密钥注册成功后，完成区域会显示 8 个恢复码，并提供"复制全部恢复码"按钮。恢复码**仅显示一次**，请在关闭对话框前离线保存。

如果已错过展示：仍能登录账户时（例如用密码登录，或密码登录会进入 2FA 时用恢复码打开一次），注册一个新的通行密钥会重新生成恢复码（旧码全部失效）；也可以在 TOTP 管理页重新生成恢复码。

### Q: 丢失通行密钥设备，也没有 TOTP 验证器怎么办？

当密码登录会进入 2FA 页面时（已配置验证器应用，或邮箱已确认）：使用任意一个已保存的恢复码 → 密码登录 → 2FA 页面点击"使用恢复码关闭双重验证" → 输入恢复码。验证成功会关闭该账户的双重验证并作废全部恢复码（自定义与 Identity 两套），随后用密码重新登录，在设置页重新注册通行密钥。恢复码不是登录凭据，不会直接建立登录会话。

如果密码登录不触发 2FA（仅通行密钥且邮箱未确认），直接密码登录后在设置页重新注册即可，不需要恢复码。

---

## 安全建议

1. **凭据数量限制**：建议用户仅注册常用设备，定期清理不再使用的凭据
2. **恢复码保管**：恢复码应离线保存（打印或密码管理器），不要存储在设备明文文件中；每个码只能使用一次，使用任意一个码会作废全部恢复码
3. **设备丢失**：如通行密钥设备丢失，使用恢复码关闭双重验证后，立即用密码重新登录、删除失效凭据并重新注册
4. **生产环境**：仅允许 HTTPS origin；`localhost` 只在开发环境自动加入 `Origins`，不要在生产配置中保留
