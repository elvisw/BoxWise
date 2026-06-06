# Server API 合约

> BoxWise.Server — ASP.NET Core Minimal API 端点文档

## 概述

- **API 风格:** Minimal API + RouteGroupBuilder 静态扩展方法
- **认证:** Cookie 认证（`SameSite=None`, `Secure`, `HttpOnly`）
- **授权:** 全局 `FallbackPolicy = RequireAuthenticatedUser`，匿名端点显式标记 `.AllowAnonymous()`
- **错误返回:** `TypedResults.Problem()` + `ProblemDetails`
- **所有端点:** 均需要 `.ProducesProblem(401)` 注解
- **端点注册顺序（Program.cs）:** Auth → Locations → Images → Items → Tags → WebAuthn → Admin2FA

---

## 1. 认证 (Auth) — `/api/auth`

```
GET    /api/auth/me      → 200 Ok<AuthUserDto>
```

### GET `/api/auth/me` [认证]

**响应:** `AuthUserDto` — 用于客户端 Cookie 恢复和状态同步

```json
{ "userName": "string", "isAdmin": false, "passwordManagedByEnv": false, "passwordRequiresChange": false, "email": null }
```

**说明:** 登录/登出由 Identity 脚手架 Razor Pages 处理（`Areas/Identity/Pages/Account/Login.cshtml` 等），不再有 `/api/auth/login` 和 `/api/auth/logout` 端点。

---

## 2. 通行密钥 (WebAuthn) — `/api/auth/webauthn`

```
GET    /api/auth/webauthn/available               → 200 Ok<WebAuthnAvailableResponse>
POST   /api/auth/webauthn/register-begin           → 200 Ok<object> | 400 | 401
POST   /api/auth/webauthn/register-complete         → 200 Ok<RecoveryCodesResponse> | 400 | 401 | 500
GET    /api/auth/webauthn/credentials               → 200 Ok<List<WebAuthnCredentialDto>>
DELETE /api/auth/webauthn/credentials/{id}          → 200 Ok | 404 | 401
POST   /api/auth/webauthn/login-begin               → 200 Ok<object> [匿名, passkey-login 限流]
POST   /api/auth/webauthn/login-complete             → 200 Ok<AuthUserDto> | 400 | 404 [匿名, passkey-login 限流]
```

### GET `/api/auth/webauthn/available` [认证]

检查当前来源是否支持 WebAuthn（基于 Origin 判断）。返回可用性和用户 handle。

**响应 (200):**
```json
{ "available": true, "origin": "https://localhost:5001", "userHandle": "base64..." }
```

### POST `/api/auth/webauthn/register-begin` [认证]

开始 WebAuthn 凭证注册。生成 `CredentialCreateOptions` 存入 Session。

**响应 (200):** `CredentialCreateOptions` JSON 对象（Fido2NetLib 格式）

### POST `/api/auth/webauthn/register-complete` [认证, CSRF]

完成 WebAuthn 凭证注册。验证 attestation 响应，持久化凭证。

**请求:** `AuthenticatorAttestationRawResponse` JSON（Fido2NetLib 格式）
**请求头:** `X-Device-Name` — 设备名称

**响应 (200):**
```json
{ "codes": ["ABCD-EFGH", "IJKL-MNOP", ...] }
```
首次注册时自动启用 2FA，返回恢复码列表。

### GET `/api/auth/webauthn/credentials` [认证]

列出当前用户的所有已注册 WebAuthn 凭证。

**响应 (200):**
```json
[{ "id": 1, "deviceName": "iPhone 上的 iCloud 钥匙串", "createdAt": "2026-01-15T10:30:00Z", "credentialId": "base64..." }]
```

### DELETE `/api/auth/webauthn/credentials/{id}` [认证, CSRF]

删除指定的 WebAuthn 凭证。删除最后一个凭证后自动清除 `ConfiguredMethods` 中的 WebAuthn 标志。如果不再有其他 2FA 方法，关闭 `TwoFactorEnabled`。

**响应:** 200 Ok | 404（凭证不存在）

### POST `/api/auth/webauthn/login-begin` [匿名]

开始通行密钥无密码登录。生成 `AssertionOptions` 存入 Session。

**限流策略:** `passkey-login`（30次/5分钟），宽松于密码登录

### POST `/api/auth/webauthn/login-complete` [匿名]

完成通行密钥登录。验证 assertion 响应，签发 Identity Cookie。

**请求:** `AuthenticatorAssertionRawResponse` JSON（Fido2NetLib 格式）

**响应 (200):** `AuthUserDto`
**错误:** 404（通行密钥未绑定），400（验证失败）

---

## 3. 位置 (Location) — `/api/locations`

```
GET    /api/locations              → 200 List<LocationDto>
POST   /api/locations              → 201 Created<LocationDto> | 400
PUT    /api/locations/{id}         → 200 Ok<LocationDto> | 400 | 404
DELETE /api/locations/{id}         → 204 NoContent | 400 | 404
GET    /api/locations/{id}/children → 200 List<LocationDto> | 404
```

### POST `/api/locations`

**请求:** `{ "name": "string", "parentId": int?, "sortOrder": 0 }`

**约束:** 名称 1-100 字符，Trim() 处理

### PUT `/api/locations/{id}`

**请求:** `{ "name": "string" }`

### DELETE `/api/locations/{id}`

**约束:** 有子节点或有关联物品时返回 400

---

## 4. 物品 (Item) — `/api/items`

```
POST   /api/items       → 201 Created<ItemDto> | 400
GET    /api/items/{id}  → 200 Ok<ItemDto> | 404
GET    /api/items       → 200 Ok<ItemSummaryDto[]>, X-Total-Count header
PUT    /api/items/{id}  → 200 Ok<ItemDto> | 400 | 404 | 409
DELETE /api/items/{id}  → 204 NoContent | 404
```

### GET `/api/items` (搜索/筛选)

**查询参数:**
| 参数 | 类型 | 说明 |
|------|------|------|
| `q` | `string?` | 搜索关键词（名称模糊匹配） |
| `locationId` | `int?` | 位置筛选 |
| `tagId` | `int?` | 标签筛选（可多个重复） |

**响应头:** `X-Total-Count: N`

### POST `/api/items`

**请求:** `{ "name": "string", "locationId": int, "tagIds": [int], "note": "string?" }`

**约束:** 名称 1-200 字符，Trim()

### PUT `/api/items/{id}`

**请求:** `{ "name": "string", "locationId": int, "tagIds": [int], "note": "string?" }`

**说明:** 编辑物品名称/位置/标签/备注。记录 `UpdatedByUserId` 和 `UpdatedAt`。

**错误:** 409 Conflict（`DbUpdateConcurrencyException` — 物品已被他人修改，通过 `Version` 字段检测）

### DELETE `/api/items/{id}`

删除物品及其关联图片文件

---

## 5. 图片 (Image) — `/api/images`

```
POST /api/images/upload    → 202 Accepted<UploadResultDto> | 400
GET  /api/images/{itemId}  → 200 PhysicalFile (image) | 404
```

### POST `/api/images/upload` [认证]

**请求:** `multipart/form-data`
| 字段 | 类型 | 说明 |
|------|------|------|
| `file` | `IFormFile` | 图片文件 |
| `itemId` | `int` | 关联物品 ID |

**限制:**
- 最大 10MB
- MIME: `image/jpeg`, `image/png`, `image/webp`
- 后台异步生成 300px + 1200px 缩略图 (SkiaSharp)

**响应 (202):**
```json
{ "itemId": int, "originalUrl": "string" }
```

### GET `/api/images/{itemId}` [认证]

**查询参数:**
| 参数 | 选项 | 说明 |
|------|------|------|
| `type` | `thumb` / `medium` / `original` | 图片尺寸 |

---

## 6. 标签 (Tag) — `/api/tags`

```
GET    /api/tags       → 200 List<TagDto>
POST   /api/tags       → 201 Created<TagDto> | 400
PUT    /api/tags/{id}  → 200 Ok<TagDto> | 400 | 404
DELETE /api/tags/{id}  → 204 NoContent | 404
```

### POST `/api/tags`

**请求:** `{ "name": "string" }`

**约束:** 名称 1-50 字符，不区分大小写唯一

### PUT `/api/tags/{id}`

**请求:** `{ "name": "string" }`

---

## 7. AI 识别 — 已退役

> AI 识别已迁移至客户端浏览器直调火山 ARK API。详见 Story 12.1。

---

## 8. Admin 2FA 管理 — `/api/admin/users/{userId}/two-factor`

```
GET  /api/admin/users/{userId}/two-factor/status  → 200 Ok<AdminTwoFactorStatusResponse> | 401 | 403 | 404
POST /api/admin/users/{userId}/two-factor/reset    → 200 Ok | 401 | 403 | 404 | 500
```

### GET `/api/admin/users/{userId}/two-factor/status` [Admin]

获取目标用户的 2FA 状态详情。

**响应 (200):**
```json
{ "userName": "string", "status": { "twoFactorEnabled": false, "twoFactorMethod": null, "availableMethods": ["TOTP", "Email"], "configuredMethods": [], "hasRecoveryCodes": false, "gracePeriodEnd": null, "setupCompletedAt": null } }
```

### POST `/api/admin/users/{userId}/two-factor/reset` [Admin, CSRF, 限流]

重置目标用户的 2FA。清除所有：
- 2FA 设置（`TotpSecretKey`, `ConfiguredMethods`, `TwoFactorEnabled` 等）
- 恢复码（`RecoveryCodes` 表）
- WebAuthn 凭证（`WebAuthnCredentials` 表）
- 更新安全戳记（使已登录会话失效）

**速率限制:** `login-per-account` 策略

---

## DTO 参考

### AuthUserDto

| 字段 | 类型 | 说明 |
|------|------|------|
| `userName` | `string` | 用户名 |
| `isAdmin` | `bool` | 是否为管理员 |
| `passwordManagedByEnv` | `bool` | 密码是否由环境变量管理（管理员账号） |
| `passwordRequiresChange` | `bool` | 是否需要更改密码 |
| `email` | `string?` | 用户邮箱 |

### ItemDto

| 字段 | 类型 | 说明 |
|------|------|------|
| `id` | `int` | |
| `name` | `string` | |
| `note` | `string?` | |
| `photoPath` | `string?` | |
| `thumbPath` | `string?` | |
| `mediumPath` | `string?` | |
| `locationId` | `int?` | |
| `locationName` | `string?` | |
| `locationPath` | `string?` | 从根到位置的名称路径（如 "客厅 / 电视柜"） |
| `tagNames` | `IReadOnlyList<string>` | |
| `createdByUserName` | `string` | |
| `createdAt` | `DateTime` | |
| `updatedByUserName` | `string?` | |
| `updatedAt` | `DateTime?` | |

### ItemSummaryDto

| 字段 | 类型 | 说明 |
|------|------|------|
| `id` | `int` | |
| `name` | `string` | |
| `thumbPath` | `string?` | 缩略图路径 |
| `locationPath` | `string?` | 位置名称路径 |
| `tagNames` | `IReadOnlyList<string>` | |
| `createdAt` | `DateTime` | |

### LocationDto

| 字段 | 类型 | 说明 |
|------|------|------|
| `id` | `int` | |
| `name` | `string` | |
| `path` | `string` | |
| `parentId` | `int?` | |
| `sortOrder` | `int` | |

### TagDto

| 字段 | 类型 | 说明 |
|------|------|------|
| `id` | `int` | |
| `name` | `string` | |
| `itemCount` | `int` | 使用此标签的物品数量 |

### TwoFactorStatusDto

| 字段 | 类型 | 说明 |
|------|------|------|
| `twoFactorEnabled` | `bool` | 2FA 是否启用 |
| `twoFactorMethod` | `string?` | 主要 2FA 方法（向后兼容） |
| `availableMethods` | `List<string>` | 可用于设置的方法 |
| `configuredMethods` | `List<string>` | 已配置的方法列表 |
| `hasRecoveryCodes` | `bool` | 是否有恢复码 |
| `gracePeriodEnd` | `DateTime?` | 宽限期截止时间 |
| `setupCompletedAt` | `DateTime?` | 设置完成时间 |

---

## Client 端 API 消费层

| Service | 端点 | 请求类型 |
|---------|------|---------|
| `CookieAuthenticationStateProvider.GetAuthenticationStateAsync` | GET `api/auth/me` | - |
| `AuthService.GetWebAuthnAvailableInfoAsync` | GET `api/auth/webauthn/available` | - |
| `AuthService.GetWebAuthnAvailableAsync` | GET `api/auth/webauthn/available` | - |
| `AuthService.GetWebAuthnCredentialsAsync` | GET `api/auth/webauthn/credentials` | - |
| `AuthService.DeleteWebAuthnCredentialAsync` | DELETE `api/auth/webauthn/credentials/{id}` | - |
| `AuthService.StartWebAuthnRegistrationAsync` | POST `api/auth/webauthn/register-begin` | - |
| `AuthService.CompleteWebAuthnRegistrationAsync` | POST `api/auth/webauthn/register-complete` | Attestation JSON |
| `AuthService.StartWebAuthnLoginAsync` | POST `api/auth/webauthn/login-begin` | - |
| `AuthService.CompleteWebAuthnLoginAsync` | POST `api/auth/webauthn/login-complete` | Assertion JSON |
| `ItemService.GetAllAsync` | GET `api/items` | - |
| `ItemService.GetFilteredAsync` | GET `api/items?locationId=&tagId=&q=` | - |
| `ItemService.SearchAsync` | GET `api/items?q=` | - |
| `ItemService.GetByIdAsync` | GET `api/items/{id}` | - |
| `ItemService.UpdateAsync` | PUT `api/items/{id}` | UpdateItemRequest |
| `ItemService.DeleteAsync` | DELETE `api/items/{id}` | - |
| `ItemEntryService.CreateItemAsync` | POST `api/items` | CreateItemRequest |
| `LocationService.GetAllAsync` | GET `api/locations` | - |
| `LocationService.CreateAsync` | POST `api/locations` | CreateLocationRequest |
| `LocationService.RenameAsync` | PUT `api/locations/{id}` | RenameLocationRequest |
| `LocationService.DeleteAsync` | DELETE `api/locations/{id}` | - |
| `TagService.GetAllAsync` | GET `api/tags` | - |
| `TagService.CreateAsync` | POST `api/tags` | CreateTagRequest |
| `TagService.RenameAsync` | PUT `api/tags/{id}` | RenameTagRequest |
| `TagService.DeleteAsync` | DELETE `api/tags/{id}` | - |
| Image Upload (ItemEntry) | POST `api/images/upload` | MultipartFormData |
| Image Serve (ItemCard/ItemDetail) | GET `api/images/{id}?type=thumb\|medium` | - |
