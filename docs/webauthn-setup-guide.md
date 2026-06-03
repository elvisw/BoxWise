# WebAuthn 通行密钥配置指南

## 概述

BoxWise 支持 WebAuthn（通行密钥）作为双因素认证（2FA）方式。用户可以使用设备的指纹、面容或硬件密钥（如 YubiKey）进行登录验证，无需输入验证码。

WebAuthn 基于 [FIDO2 标准](https://fidoalliance.org/fido2/)，凭证与注册时的 **origin（协议 + 域名 + 端口）** 绑定。

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
var fido2Config = new Fido2Configuration
{
    ServerDomain = builder.Configuration["WebAuthn:ServerDomain"]
        ?? new Uri(webAuthnOrigin).Host,
    ServerName = "BoxWise",
    Origins = new HashSet<string>
    {
        webAuthnOrigin,
        // 开发环境同时允许两个 localhost 端口
        "https://localhost:5000",
        "https://localhost:5001"
    }
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

**注意：** `Origins` 集合始终包含 `https://localhost:5000` 和 `https://localhost:5001` 两个端口，确保开发环境下无论用户使用哪个端口访问，通行密钥认证都能正常工作。`WebAuthn:Origin` 配置仅控制 `ServerDomain` 的 fallback 解析，不影响 `Origins` 的多端口支持。

---

## 生产环境配置

### 1. HTTPS 域名

生产环境必须使用 HTTPS 域名。WebAuthn 在非 `localhost` 环境下强制要求 TLS。

使用 Caddy 的反向代理配置示例（`Caddyfile`）：

```
boxwise.example.com {
    reverse_proxy localhost:5000
}
```

### 2. WebAuthn 配置

在 `appsettings.Production.json` 中配置：

```json
{
  "WebAuthn": {
    "Origin": "https://boxwise.example.com",
    "ServerDomain": "boxwise.example.com"
  }
}
```

| 配置项 | 说明 | 示例 |
|--------|------|------|
| `WebAuthn:Origin` | 用户访问网站的完整 origin（协议 + 域名 + 端口） | `https://boxwise.example.com` |
| `WebAuthn:ServerDomain` | RP ID 的域名部分（不含协议和端口） | `boxwise.example.com` |

### 3. Docker 部署

Docker 环境下通过环境变量注入配置：

```yaml
# docker-compose.yml
environment:
  - WebAuthn__Origin=https://boxwise.example.com
  - WebAuthn__ServerDomain=boxwise.example.com
```

或通过 `appsettings.Production.json` 挂载。

### 4. 反向代理注意事项

- **不要修改 Host 头**：确保反向代理将原始请求的 Host 头传递给后端
- **TLS 终止**：WebAuthn 的 origin 检查依赖浏览器感知的协议。如果使用 Cloudflare 等 CDN 做 TLS 终止，确保 `Origin` 配置与浏览器地址栏一致
- **端口**：如果使用非标准端口（如 8443），origin 必须包含端口号

---

## 架构说明

### 注册流程

```
用户点击"设置通行密钥"
  → POST /api/auth/webauthn/register-begin（获取 CredentialCreateOptions）
  → 浏览器弹出指纹/面容/PIN 验证（navigator.credentials.create）
  → POST /api/auth/webauthn/register-complete（提交 attestation + X-Device-Name）
  → 2FA 启用，返回恢复码
```

### 无密码登录流程（Passkey）

> **注意：** 通行密钥仅用于首次无密码登录，不再作为密码后的 2FA 第二因素验证。

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

## 常见问题

### Q: "当前环境不支持通行密钥" 提示

**原因：** 以下任一条件未满足：
1. 非 HTTPS 连接（生产环境）或 非 localhost
2. 浏览器不支持 WebAuthn API
3. `WebAuthn:Origin` 配置与浏览器地址栏不一致

**排查：**
1. 确认使用 `https://` 访问
2. 在浏览器控制台执行 `window.webauthn.isAvailable()`，应返回 `true`
3. 检查 `appsettings.json` 中 `WebAuthn:Origin` 与浏览器地址栏完全匹配

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

### Q: 恢复码未显示

WebAuthn 注册成功后，恢复码会在完成对话框中显示。如果作为第二个 2FA 方法注册（已有 TOTP 或 Email），恢复码会重新生成。请务必在关闭对话框前保存恢复码。

---

## 安全建议

1. **凭据数量限制**：建议用户仅注册常用设备，定期清理不再使用的凭据
2. **恢复码保管**：恢复码应离线保存（打印或密码管理器），不要存储在设备明文文件中
3. **设备丢失**：如通行密钥设备丢失，使用恢复码登录后立即删除该设备的凭据
4. **生产环境**：仅允许 HTTPS origin，不要将 `localhost` 加入生产环境的 Origins 白名单
