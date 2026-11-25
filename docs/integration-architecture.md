# 集成架构

> BoxWise — Client-Server 集成与数据流

## 架构总览

```
┌─────────── Blazor WASM Client ───────────┐
│  Browser (localhost:5001 / 生产同源)       │
│                                           │
│  ┌─────────────────────────────────────┐  │
│  │  Pages / Components                 │  │
│  │  (MudBlazor 9.x UI)                 │  │
│  └──────────┬──────────────────────────┘  │
│             │                              │
│  ┌──────────▼──────────────────────────┐  │
│  │  Services Layer                     │  │
│  │  ┌──────────┐ ┌──────────────────┐  │  │
│  │  │ItemService│ │LocationService   │  │  │
│  │  ├──────────┤ ├──────────────────┤  │  │
│  │  │TagService │ │ItemEntryService  │  │  │
│  │  ├──────────┤ ├──────────────────┤  │  │
│  │  │AuthService│ │AiService         │  │  │
│  │  └──────────┘ └──────────────────┘  │  │
│  └──────────┬──────────────────────────┘  │
│             │ HttpClient + CookieHandler   │
│             │ (BrowserRequestCredentials    │
│             │  .Include)                   │
│  ┌──────────▼──────────────────────────┐  │
│  │  AppState (Singleton)               │  │
│  │  CookieAuthenticationStateProvider   │  │
│  └─────────────────────────────────────┘  │
└───────────────┬───────────────────────────┘
                │ REST/JSON + Cookie Auth
                │
┌───────────────▼───────────────────────────┐
│  ASP.NET Core Server (localhost:5000)      │
│                                            │
│  ┌──────────────────────────────────────┐ │
│  │  Minimal API Endpoints (8 groups)    │ │
│  │  /api/auth  /api/auth/webauthn      │ │
│  │  /api/locations                     │ │
│  │  /api/items  /api/images            │ │
│  │  /api/tags                           │ │
│  │  /api/admin/users/{userId}/two-factor               │ │
│  └──────────┬───────────────────────────┘ │
│             │                              │
│  ┌──────────▼───────────────────────────┐ │
│  │  Repositories (Scoped)               │ │
│  │  LocationRepo / ItemRepo / TagRepo   │ │
│  └──────────┬───────────────────────────┘ │
│             │                              │
│  ┌──────────▼───────────────────────────┐ │
│  │  EF Core DbContext                   │ │
│  │  AppDbContext : IdentityDbContext     │ │
│  └──────────┬───────────────────────────┘ │
│             │                              │
│  ┌──────────▼───────────────────────────┐ │
│  │  SQLite (data/boxwise.db)            │ │
│  └──────────────────────────────────────┘ │
│                                            │
│  Services:                                 │
│  ┌──────────────────────────────────────┐ │
│  │  Identity + Cookie Auth              │ │
│  │  ImageStorageService (文件系统)       │ │
│  │  ThumbnailService (SkiaSharp 后台)    │ │

 │
│  │  TwoFactorService / EmailTwoFactor    │ │
│  │  WebAuthnService / RecoveryCodeService│ │
│  │  SmtpConfigurationService            │ │
│  │  IdentityEmailSender / CsrfValidate  │ │
│  └──────────────────────────────────────┘ │
│                                            │
│  Admin UI:                                 │
│  ┌──────────────────────────────────────┐ │
│  │  Razor Pages (Pages/Admin/)          │ │
│  └──────────────────────────────────────┘ │
└────────────────────────────────────────────┘
```

## 通信协议

| 方向 | 协议 | 认证 | 格式 |
|------|------|------|------|
| Client → Server | HTTPS | Cookie (自动附带) | JSON |
| Server → Client | HTTPS | Cookie (Set-Cookie) | JSON |
| Image Upload | HTTPS | Cookie | multipart/form-data |
| Image Serve | HTTPS | Cookie | image/jpeg/png/webp |

## Client ↔ Server 数据流

### 1. 认证流程
```
1. 用户提交登录表单
2. Identity Login.cshtml Razor Page 验证密码 → Set-Cookie → 302 重定向
3. AppState.SetUser() 通过 GET /api/auth/me 获取用户状态 → UI 刷新
5. 后续请求自动附带 Cookie (CookieHandler)
6. 页面刷新时 CookieAuthenticationStateProvider → GET /api/auth/me 恢复会话
```

### 2. 物品录入流程
```
1. ItemEntry.razor: 拍照/选图
2. [可选] AiService.RecognizeAsync() → POST 配置的 Chat Completions 端点 (JSON, CORS, Bearer)
3. 用户确认名称/位置/标签
4. ItemEntryService.CreateItemAsync() → POST /api/items (JSON) → 201 itemId
5. HttpClient.PostAsync() → POST /api/images/upload (multipart) → 202
6. Server 后台 SkiaSharp 生成缩略图
7. AppState.SetContinuousLocation() → 导航回首页
```

`AiService` 从 `/api/llm/config` 读取 `BaseUrl`，该值可以是 API 根地址或完整端点：根地址自动追加 `/chat/completions`，已以该后缀结尾则原样使用，查询参数保留；火山 ARK 裸域名 `https://ark.cn-beijing.volces.com` 使用 `/api/v3/chat/completions`。识别阶段浏览器把 Base64 图片和 `Authorization: Bearer <ApiKey>` 直接发送给配置的 provider；保存物品时照片仍通过 `POST /api/images/upload` 上传到 BoxWise，两条链路相互独立。浏览器直连端点，因此端点需支持 HTTPS 与 CORS（允许部署 Origin、`POST`、`authorization`、`content-type`）；HTTPS 页面不能调用 HTTP 端点。服务端只存取配置，不代理推理请求，也不覆盖输出长度或思考模式。

### 3. 浏览/搜索流程
```
1. Browse.razor: LocationTree + TagFilter 选择筛选条件
2. ItemService.GetFilteredAsync(locationId, tagIds, query)
3. → GET /api/items?locationId=N&tagId=N&q=keyword
4. Server ItemRepository 动态查询 → List<ItemSummaryDto>
5. 客户端 ItemCard 渲染: Http.BaseAddress + "api/images/{Id}?type=thumb"
```

## 引用依赖

```
BoxWise.Shared (DTO records, 无外部依赖)
    ↑                    ↑
    │                    │
BoxWise.Client          BoxWise.Server
(Blazor WASM)           (ASP.NET Core)
    ↑                        │
    └────────────────────────┘ (Server 引用 Client: SPA 回退)
```

## 共享类型

所有 DTO 定义在 `BoxWise.Shared/Dtos/`，两端通过项目引用共享，确保类型一致。Server → Client 使用 `record` 进行 JSON 序列化/反序列化。
