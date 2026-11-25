# Server API 合约

> BoxWise.Server — ASP.NET Core Minimal API 端点文档

## 概述

- **API 风格:** Minimal API + RouteGroupBuilder 静态扩展方法
- **认证:** Cookie 认证（`SameSite=None`, `Secure`, `HttpOnly`）
- **授权:** 全局 `FallbackPolicy = RequireAuthenticatedUser`，匿名端点显式标记 `.AllowAnonymous()`
- **错误返回:** `TypedResults.Problem()` + `ProblemDetails`
- **所有端点:** 均需要 `.ProducesProblem(401)` 注解
- **端点注册顺序（Program.cs）:** Auth → Locations → Location Photos → Images → Items → Tags → WebAuthn → Admin2FA → LlmConfig

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
{ "codes": ["ABCDEFGHIJ", "KLMNOPQRST", "..."] }
```
注册成功会置 `TwoFactorEnabled=true` 并加入 `ConfiguredMethods`；这不会使密码登录进入 2FA 挑战。详见 [密码登录的 2FA 触发条件](auth-security.md#密码登录的-2fa-触发条件)。响应中的 8 个 10 位 base32 恢复码**仅用于一次性展示**：用户可在密码登录的 2FA 页面输入任意一个码来关闭双重验证（不建立登录会话），验证成功会作废全部恢复码。服务端只保存 SHA-256 哈希，明文不落库。

### GET `/api/auth/webauthn/credentials` [认证]

列出当前用户的所有已注册 WebAuthn 凭证。

**响应 (200):**
```json
[{ "id": 1, "deviceName": "iPhone 上的 iCloud 钥匙串", "createdAt": "2026-01-15T10:30:00Z", "credentialId": "base64..." }]
```

### DELETE `/api/auth/webauthn/credentials/{id}` [认证, CSRF]

删除指定的 WebAuthn 凭证。删除最后一个凭证后自动清除 `ConfiguredMethods` 中的 WebAuthn 标志。如果不再有其他 2FA 方法（`ConfiguredMethods == None`），关闭 `TwoFactorEnabled`、删除该用户全部恢复码并更新 `SecurityStamp`；若仍有 TOTP，则保留恢复码且不更新 `SecurityStamp`。

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
GET    /api/locations                        → 200 List<LocationDto>
GET    /api/locations/{id}                   → 200 LocationDto | 404
POST   /api/locations                        → 201 Created<LocationDto> | 400
PUT    /api/locations/{id}                   → 200 Ok<LocationDto> | 400 | 404
DELETE /api/locations/{id}                   → 204 NoContent | 200 LocationCleanupWarning | 400 | 404
GET    /api/locations/{id}/children          → 200 List<LocationDto> | 404
GET    /api/locations/{id}/photo             → 200 PhysicalFile | 404
PUT    /api/locations/{id}/photo             → 200 Ok<LocationDto> | 400 | 404
DELETE /api/locations/{id}/photo             → 204 NoContent | 200 LocationCleanupWarning | 404
```

所有端点均需要登录；未认证请求返回 401。
位置照片的 `PUT`/`DELETE` 是写操作，复用项目 `CsrfValidationFilter`：请求必须带
`X-Requested-With: XMLHttpRequest`，否则返回 400。

### POST `/api/locations`

**请求:** `{ "name": "string", "parentId": int?, "sortOrder": 0, "description": "string?" }`

**约束:** 名称 1-100 字符，Trim() 处理。描述可选，规则见下。

### GET `/api/locations/{id}`

返回单个位置元数据，包括描述、`hasPhoto` 和展示用完整名称路径 `namePath`（如 `"书房 → 抽屉"`）。
物品详情页用它判断是否显示「查看位置照片」入口，不通过试探图片 URL 判断。

### PUT `/api/locations/{id}`

**请求:** `{ "name": "string", "description": "string?" }`

| 情形 | 行为 |
|------|------|
| `description` 省略 | 保持原描述不变（可用于仅改名） |
| `description` 为 `""` 或 `null` | 清除描述 |
| `description` 为文本 | 去除首尾空白后保存；超过 300 个 Unicode 字素簇返回 400 |

描述换行保留在字符串内部；去除首尾空白后全空白视为无描述。服务端不静默截断。

### DELETE `/api/locations/{id}`

**约束:** 有子节点或有关联物品时返回 400，位置、描述和照片均不变。
服务端使用单条条件 `DELETE ... WHERE Id = @id AND NOT EXISTS (子位置) AND NOT EXISTS (物品)`
在同一 SQLite 语句内完成检查与删除；并发移入物品时语句不匹配任何行，不会删除位置，也不会经由外键
`SetNull` 清空物品的位置。

空位置删除成功后，同时清理该位置的描述和照片文件：

- 正常：`204 NoContent`
- 数据库删除已提交、但照片文件清理失败：`200 OK` + `{ "cleanupWarning": true }`。客户端应显示删除成功与清理警告两条信息，**不要重试删除**。

### GET `/api/locations/{id}/photo`

仅当数据库中的照片指针已提交时读取文件；文件缺失或 MIME 与文件头不一致时返回 404。

**响应头:** `Cache-Control: no-store`（位置照片不进入 PWA 图片缓存，也不使用 `/api/images/`）。

### PUT `/api/locations/{id}/photo`

**请求:** `multipart/form-data`，只允许一个字段名为 `file` 的文件；额外字段或第二个文件返回 400。
**请求头:** `X-Requested-With: XMLHttpRequest`（CSRF）。

**限制:**
- 文件本身严格限制 10 MiB（10,485,760 字节）；端点自行设置请求体边界（10 MiB + multipart 开销），不修改全局 `FormOptions`，物品上传仍保持原有 10 MiB 表单限制。
- 只接受能实际解码的 JPG/PNG/WebP（使用 SkiaSharp 验证像素数据，不仅检查扩展名或声明 MIME）；声明 MIME 不属于白名单时直接拒绝。
- 像素验证策略：不依赖 `SKCodec.GetScaledDimensions`/子区域接口（并非所有 codec 支持）。JPEG 使用逐行全图 scanline 扫描（每块 ≤64 行、单行 ≤256 KiB），不分配整图缓冲；WebP 使用 512px 缩放解码（仍完整校验像素数据）；PNG 等不支持缩放/scanline 的格式使用受像素数与并发限制的完整解码。
- 完整解码仅用于兜底，限制为 32 MP / 128 MiB，并在进程内串行执行（同一时刻最多一块完整像素缓冲）。超过该安全边界的图片（典型为 >32 MP 的 PNG）返回 400 `图片尺寸过大，无法安全处理，请换一张照片。`；常见 12 MP/27 MP PNG、48 MP JPEG 和 WebP 仍可处理。该边界是解码炸弹防护，不改变 10 MiB 文件上限或格式白名单。
- 每个位置最多 1 张；上传成功即替换旧照片，响应为更新后的 `LocationDto`。
- 写入顺序：暂存 → 发布文件 → 提交数据库指针 → 清理旧文件。发布失败或数据库明确未提交时保留旧照片。
- 数据库提交结果无法确认时（例如 `SaveChanges` 在提交后失败），服务端用新的数据库连接重新读取已提交指针：仅当新文件确实未被引用时才清理；否则保留。响应仍为错误，不会伪装成回滚；客户端把 5xx 视为 Unknown，提示重新核对而不是自动重提。
- 请求体超过边界时返回 413；格式/大小/多文件/像素尺寸等验证失败返回 400。

### DELETE `/api/locations/{id}/photo`

**请求头:** `X-Requested-With: XMLHttpRequest`（CSRF）。

移除照片并清空指针；没有照片时仍返回 204（幂等）。

文件清理失败时返回 `200 OK` + `{ "cleanupWarning": true }`，此时指针已清空，照片 GET 返回 404。
数据库提交结果无法确认时，端点同样用新连接重新读取指针：仅确认文件已不再被引用时才清理；
否则保留文件并返回错误，客户端按 Unknown 处理，提示重新核对。

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

## 7. LLM 配置 — `/api/llm/config`

> AI 识别在浏览器端直接调用配置的 Chat Completions 端点，服务端不代理推理请求。

```
GET /api/llm/config  → 200 LlmConfigDto | 401
PUT /api/llm/config  → 200 LlmConfigDto | 400 | 401 | 403 | 409
```

### GET `/api/llm/config`

任意已登录用户可读，返回 `{ BaseUrl, ApiKey, Model, TimeoutSeconds }`。

### PUT `/api/llm/config`

仅 Admin。请求体同 `LlmConfigDto`。

- `BaseUrl` 必填；可填 API 根地址或完整 Chat Completions 地址。客户端对根地址追加 `/chat/completions`，已以该后缀结尾则原样使用，查询参数保留；火山 ARK 裸域名 `https://ark.cn-beijing.volces.com` 特别映射到 `/api/v3/chat/completions`。
- `ApiKey` 为空时保留原 Key，但仅限 URL 文本未变化（可只改模型/超时）或协议、主机、有效端口均可证明相同（可改路径/查询）。更换来源或 URL 无法证明同源（包括无效旧 URL）时必须提供新 Key，否则返回 400 且不修改已存配置。
- 保存使用条件更新：UPDATE 的谓词同时固定读取时的 `BaseUrl` 和 `ApiKey` 快照，URL 与 Key 在同一语句写入。并发修改使谓词失效时重新读取并再次校验；持续冲突返回 409，响应只包含通用提示，不包含任何 Key、URL 或模型值。
- 仅使用 Bearer 认证；响应不回显其他凭据。服务端不限制输出长度，也不覆盖思考/推理模式。

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
| `path` | `string` | 内部 ID 链（如 `/1/5/`），客户端用于构建层级 |
| `parentId` | `int?` | |
| `sortOrder` | `int` | |
| `description` | `string?` | 规范化后的位置描述；无描述为 `null` |
| `hasPhoto` | `bool` | 位置元数据中的照片有无；入口可见性只看此字段 |
| `namePath` | `string?` | 展示用完整名称路径（`A → B → C`） |

### UpdateLocationRequest

| 字段 | 类型 | 说明 |
|------|------|------|
| `name` | `string` | 必填 |
| `description` | `string?` | 省略 = 不变；`""`/`null` = 清除 |

### LocationCleanupWarning

| 字段 | 类型 | 说明 |
|------|------|------|
| `cleanupWarning` | `bool` | 数据库删除/指针清空已提交，但磁盘文件清理失败 |

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
| `LocationService.GetByIdAsync` | GET `api/locations/{id}` | - |
| `LocationService.CreateAsync` | POST `api/locations` | CreateLocationRequest |
| `LocationService.UpdateAsync` | PUT `api/locations/{id}` | UpdateLocationRequest |
| `LocationService.DeleteAsync` | DELETE `api/locations/{id}` | - |
| `LocationService.UploadPhotoAsync` | PUT `api/locations/{id}/photo` | MultipartFormData |
| `LocationService.RemovePhotoAsync` | DELETE `api/locations/{id}/photo` | - |
| `LocationService.GetPhotoAsync` | GET `api/locations/{id}/photo` | -（按需） |
| `TagService.GetAllAsync` | GET `api/tags` | - |
| `TagService.CreateAsync` | POST `api/tags` | CreateTagRequest |
| `TagService.RenameAsync` | PUT `api/tags/{id}` | RenameTagRequest |
| `TagService.DeleteAsync` | DELETE `api/tags/{id}` | - |
| Image Upload (ItemEntry) | POST `api/images/upload` | MultipartFormData |
| Image Serve (ItemCard/ItemDetail) | GET `api/images/{id}?type=thumb\|medium` | - |

**写结果语义:** `2xx` 为成功；`400`（校验/CSRF）、`413`（请求体过大）等确定的业务拒绝为 Rejected；
`404` 为 NotFound；`5xx` 或响应丢失为 Unknown（服务端可能在提交后失败，客户端不自动重提，
提示重新核对）。`LocationSaveCoordinator` 负责“先资料、后照片”的编排：资料明确失败或未知时不发送照片操作，
重试沿用已创建位置的 ID。
