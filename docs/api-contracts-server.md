# Server API 合约

> BoxWise.Server — ASP.NET Core Minimal API 端点文档

## 概述

- **API 风格:** Minimal API + RouteGroupBuilder 静态扩展方法
- **认证:** Cookie 认证（`SameSite=None`, `Secure`, `HttpOnly`）
- **授权:** 全局 `FallbackPolicy = RequireAuthenticatedUser`，匿名端点显式标记 `.AllowAnonymous()`
- **错误返回:** `TypedResults.Problem()` + `ProblemDetails`
- **所有端点:** 均需要 `.ProducesProblem(401)` 注解

---

## 1. 认证 (Auth) — `/api/auth`

```
POST   /api/auth/login   → 200 Ok<AuthUserDto> | 401 | 400
POST   /api/auth/logout  → 200 Ok
GET    /api/auth/me      → 200 Ok<AuthUserDto>
```

### POST `/api/auth/login` [匿名]

**请求:**
```json
{ "username": "string", "password": "string" }
```

**响应 (200):**
```json
{ "userName": "string", "isAdmin": false }
```

**错误:** 401 (凭证错误), 400 (验证失败)

### POST `/api/auth/logout` [认证]

**响应:** 200 Ok（清除 Cookie 会话）

### GET `/api/auth/me` [认证]

**响应:** `AuthUserDto` — 用于客户端 Cookie 恢复和状态同步

---

## 2. 位置 (Location) — `/api/locations`

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

## 3. 物品 (Item) — `/api/items`

```
POST   /api/items       → 201 Created<ItemDto> | 400
GET    /api/items/{id}  → 200 Ok<ItemDto> | 404
GET    /api/items       → 200 Ok<ItemSummaryDto[]>, X-Total-Count header
DELETE /api/items/{id}  → 204 NoContent | 404
```

### GET `/api/items` (搜索/筛选)

**查询参数:**
| 参数 | 类型 | 说明 |
|------|------|------|
| `q` | `string?` | 搜索关键词（名称模糊匹配） |
| `locationId` | `int?` | 位置筛选 |
| `tagId` | `int?` | 标签筛选（可多个） |

**响应头:** `X-Total-Count: N`

### POST `/api/items`

**请求:** `{ "name": "string", "locationId": int, "tagIds": [int], "note": "string?" }`

**约束:** 名称 1-200 字符，Trim()

### DELETE `/api/items/{id}`

删除物品及其关联图片文件

---

## 4. 图片 (Image) — `/api/images`

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

## 5. 标签 (Tag) — `/api/tags`

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

## 6. AI 识别 — `/api/ai`

```
POST /api/ai/recognize → 200 Ok<RecognitionResultDto> | 400 | 422
```

### POST `/api/ai/recognize` [认证]

**请求:** `multipart/form-data` (图片文件)

**验证:** Magic-byte 检测
- JPEG: `FF D8 FF`
- PNG: `89 50 4E 47`
- WebP: RIFF + WEBP

**限制:** 10MB

**响应 (200):**
```json
{ "name": "string", "note": "string" }
```

**错误:** 422 (AI 服务不可用/超时，静默降级)

---

## Client 端 API 消费层

| Service | 端点 | 请求类型 |
|---------|------|---------|
| `AuthService.LoginAsync` | POST `api/auth/login` | LoginRequest |
| `AuthService.LogoutAsync` | POST `api/auth/logout` | - |
| `CookieAuthenticationStateProvider` | GET `api/auth/me` | - |
| `ItemService.GetAllAsync` | GET `api/items` | - |
| `ItemService.GetFilteredAsync` | GET `api/items?locationId=&tagId=&q=` | - |
| `ItemService.SearchAsync` | GET `api/items?q=` | - |
| `ItemService.GetByIdAsync` | GET `api/items/{id}` | - |
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
| `AiService.RecognizeAsync` | POST `api/ai/recognize` | MultipartFormData |
| Image Upload (ItemEntry) | POST `api/images/upload` | MultipartFormData |
| Image Serve (ItemCard/ItemDetail) | GET `api/images/{id}?type=thumb\|medium` | - |
