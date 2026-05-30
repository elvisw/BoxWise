---
stepsCompleted: [1, 2, 3, 4]
inputDocuments:
  - _bmad-output/planning-artifacts/prds/prd-BoxWise-2026-05-21/prd.md
  - _bmad-output/planning-artifacts/architecture.md
  - _bmad-output/planning-artifacts/ux-design-specification.md
---

# 箱知 · BoxWise — Epic 拆分

## Overview

本文档基于 PRD（20 FR）、架构设计文档和 UX 设计规格，将需求拆分为可独立实现的 Epic 和 Story。

## Requirements Inventory

### Functional Requirements

| 编号 | 需求 | 来源 |
|------|------|------|
| FR-1 | 拍照采集（可选）：拍照或跳过直接录入 | PRD §4.1 |
| FR-2 | AI 识别预填：OpenAI 兼容 API 识别物品并预填名称/备注 | PRD §4.1 |
| FR-3 | 手动输入兜底：AI 不可用时手动输入 | PRD §4.1 |
| FR-4 | 位置分配：为物品分配层级位置（必填） | PRD §4.1 |
| FR-5 | 标签附加：添加多个自定义标签（可选） | PRD §4.1 |
| FR-6 | 入库保存：确认后保存物品记录 | PRD §4.1 |
| FR-7 | 位置继承：保存后下一件默认继承位置 | PRD §4.2 |
| FR-8 | 连续模式提示：UI 标识当前连续收纳位置 | PRD §4.2 |
| FR-9 | 模糊搜索：按物品名称关键词模糊匹配 | PRD §4.3 |
| FR-10 | 搜索结果展示：列表展示缩略图+名称+位置+标签 | PRD §4.3 |
| FR-11 | 缩略图网格：所有物品以缩略图网格展示 | PRD §4.4 |
| FR-12 | 位置筛选：按位置层级筛选物品 | PRD §4.4 |
| FR-13 | 标签筛选：按标签筛选物品 | PRD §4.4 |
| FR-14 | 层级位置创建：用户自定义深度层级位置 | PRD §4.5 |
| FR-15 | 层级浏览：按位置层级导航浏览物品 | PRD §4.5 |
| FR-16 | 物品删除：详情页删除按钮+确认对话框 | PRD §4.6 |
| FR-17 | 用户注册：管理员通过后台界面创建账户 | PRD §4.7 |
| FR-18 | 用户登录：用户名+密码登录，会话保持 | PRD §4.7 |
| FR-19 | 登录保护：所有功能页面需登录后访问 | PRD §4.7 |
| FR-20 | 录入者标识：物品详情显示录入者用户名 | PRD §4.7 |

### NonFunctional Requirements

| 编号 | 需求 | 类别 |
|------|------|------|
| NFR-1 | ASP.NET Core Identity + Cookie HttpOnly/Secure + HTTPS | Security |
| NFR-2 | 1C1G VPS，≤5用户，首屏<2s，搜索<500ms | Performance |
| NFR-3 | PWA 安装到桌面 + 离线只读 | PWA/Offline |
| NFR-4 | AI API 15s 超时，静默降级为手动输入 | AI Reliability |
| NFR-5 | SQLite + 文件系统图片，Docker 持久化卷 | Data |
| NFR-6 | 密码安全存储，[Authorize] 保护所有端点 | Security |
| NFR-7 | ImageSharp 300px+1200px 两级缩略图，懒加载 | Performance |
| NFR-8 | 物化路径 B-tree 查询位置子树 | Data |

### Additional Requirements (from Architecture)

- **AR-1**: 项目脚手架：blazorwasm --pwa --empty + webapi + classlib + Directory.Build + CPM
- **AR-2**: Minimal API + TypedResults + ProblemDetails
- **AR-3**: 物化路径 Path TEXT 列查询位置子树
- **AR-4**: ImageSharp 两级缩略图（300px+1200px），后台异步生成
- **AR-5**: CookieAuthenticationStateProvider + /api/auth/me
- **AR-6**: Admin Razor Pages 独立区域（Pages/Admin/），IsAdmin 标记
- **AR-7**: Docker + Caddy + Linux VPS (1C1G)
- **AR-8**: IEntityTypeConfiguration<T> + AppState scoped DI
- **AR-9**: Service Worker 缓存策略（框架 Cache-First，图片 SWR，API Network-Only）

### UX Design Requirements

| 编号 | 需求 |
|------|------|
| UX-1 | MudBlazor 设计系统 + 蓝灰 #546E7A 主题 |
| UX-2 | 底部 3 Tab 导航（首页/录入/浏览）+ FAB 拍照按钮 |
| UX-3 | 自定义组件：ItemCard（有/无照片双状态）、ImageUploader、ContinuityBanner、EmptyState |
| UX-4 | 拍照/跳过拍照双入口，保存按钮 disabled 直到名称非空+位置已选 |
| UX-5 | 静默降级反馈（AI 失败不弹错误框），删除用 MudDialog 确认 |

### FR Coverage Map

| FR | Epic | Description |
|----|------|-------------|
| FR-1 | Epic 3 | 拍照采集（可选） |
| FR-2 | Epic 3 | AI 识别预填 |
| FR-3 | Epic 3 | 手动输入兜底 |
| FR-4 | Epic 3 | 位置分配 |
| FR-5 | Epic 2 | 标签附加 |
| FR-6 | Epic 3 | 入库保存 |
| FR-7 | Epic 3 | 位置继承 |
| FR-8 | Epic 3 | 连续模式提示 |
| FR-9 | Epic 4 | 模糊搜索 |
| FR-10 | Epic 4 | 搜索结果展示 |
| FR-11 | Epic 4 | 缩略图网格 |
| FR-12 | Epic 4 | 位置筛选 |
| FR-13 | Epic 4 | 标签筛选 |
| FR-14 | Epic 2 | 层级位置创建 |
| FR-15 | Epic 2 | 层级浏览 |
| FR-16 | Epic 4 | 物品删除 |
| FR-17 | Epic 1 | 用户注册 |
| FR-18 | Epic 1 | 用户登录 |
| FR-19 | Epic 1 | 登录保护 |
| FR-20 | Epic 3 | 录入者标识 |

## Epic List

### Epic 1: 项目搭建与账户认证

项目骨架就绪 + 家庭成员可以创建账户并登录使用。

**FRs covered:** FR-17, FR-18, FR-19
**ARs covered:** AR-1, AR-2, AR-5, AR-6, AR-8
**User outcome:** 项目完整骨架就绪，Identity 认证系统可用，家庭成员可通过登录页进入系统

### Epic 2: 位置体系与标签管理

用户可以创建层级收纳位置体系，通过标签对物品进行分类。

**FRs covered:** FR-5, FR-14, FR-15
**ARs covered:** AR-3
**User outcome:** 可以建立房间→柜子→箱子的层级位置树，标签系统就绪

### Epic 3: 物品录入与智能识别

用户可以拍照（或跳过）录入物品，AI 自动识别物品信息，支持连续收纳。

**FRs covered:** FR-1, FR-2, FR-3, FR-4, FR-6, FR-7, FR-8, FR-20
**ARs covered:** AR-4
**UX:** UX-3, UX-4, UX-5
**User outcome:** 拍照→AI识别→选位置→保存，连续收纳自动继承，物品库开始积累

### Epic 4: 查找浏览与生产部署

用户可以搜索/浏览/筛选物品并找到实物，PWA 安装到桌面，应用上线运行。

**FRs covered:** FR-9, FR-10, FR-11, FR-12, FR-13, FR-16
**ARs covered:** AR-7, AR-9
**UX:** UX-2
**NFRs:** NFR-2, NFR-3, NFR-7
**User outcome:** 可搜索/浏览找到任何物品，PWA 可安装，应用生产环境运行

### Epic 5: 用户管理增强与品牌收尾

用户可以自行修改信息和密码，管理员可编辑/删除用户、分配角色、重置密码，应用品牌完善。

**FRs covered:** 无（增强现有功能）
**User outcome:** 用户可管理自己的账户信息，管理员可完整管理用户，应用有专业品牌形象

### Epic 6: 单元测试补完

将单元测试从 52 个扩展到 ≥ 85 个，覆盖 Repository/Service/Endpoint/PageModel 层的测试缺口，建立回归安全网。

**Source:** SPEC-test-coverage
**User outcome:** 开发者可以放心重构，CI 可自动捕获回归

---

## Epic 1: 项目搭建与账户认证

**Goal:** 项目骨架就绪 + 家庭成员可以创建账户并登录使用。

### Story 1.1: 项目脚手架搭建

As a 开发者，
I want 使用 dotnet CLI 创建完整的解决方案结构，
So that 三个项目 + Directory.Build 基础设施就绪，后续 Story 可在此之上构建。

**Acceptance Criteria:**

**Given** 空的工作目录
**When** 执行 `dotnet new sln` + `blazorwasm --pwa --empty` + `webapi` + `classlib`
**Then** 生成 `BoxWise.sln` 包含三个项目，均目标 `net10.0`

**Given** 三个项目已创建
**When** 添加 Directory.Build 文件和项目引用
**Then** `dotnet build BoxWise.sln` 成功编译

**And** `Directory.Build.props` 启用 Nullable、ImplicitUsings、TreatWarningsAsErrors
**And** `Directory.Packages.props` 启用 Central Package Management
**And** `src/Directory.Build.props` 使用 GetPathOfFileAbove 链式导入
**And** Client → Shared, Server → Shared 引用正确

### Story 1.2: Identity 集成与登录认证

As a 家庭成员，
I want 用用户名和密码登录，
So that 我可以进入系统看到家庭物品库。

**Acceptance Criteria:**

**Given** 未登录
**When** 访问任何功能页面
**Then** 重定向到登录页

**Given** 有有效账户
**When** 输入正确的用户名和密码
**Then** 登录成功，Cookie 持久化，下次打开无需重登

**Given** 密码错误
**When** 提交登录
**Then** 显示错误提示，不重定向

**Given** 已登录
**When** 调用 `/api/auth/me`
**Then** 返回用户名 + IsAdmin 状态

**Given** 未认证
**When** 调用非 auth API
**Then** 返回 401

**And** ASP.NET Core Identity + Cookie 认证，密码哈希存储，HttpOnly + Secure
**And** Minimal API，`Endpoints/AuthEndpoints.cs`
**And** Client 自定义 `CookieAuthenticationStateProvider`

### Story 1.3: 后台管理界面 — 账户管理

As a 管理员，
I want 通过后台界面创建家庭成员账户，
So that 其他家庭成员有自己的账号登录使用。

**Acceptance Criteria:**

**Given** 管理员（IsAdmin=true）已登录
**When** 访问 `/admin`
**Then** 显示账户列表页

**Given** 管理员填写用户名和密码
**When** 提交创建
**Then** 新账户创建成功，用户名唯一

**Given** 非管理员
**When** 访问 `/admin`
**Then** 返回 403

**And** Admin 为独立 Razor Pages（`Pages/Admin/Index.cshtml` + `CreateAccount.cshtml`）
**And** 首个用户手动标记为 IsAdmin，不做自助注册

---

## Epic 2: 位置体系与标签管理

**Goal:** 用户可以创建层级收纳位置体系，通过标签对物品进行分类。

### Story 2.1: Location 实体 + 物化路径 CRUD

As a 用户，
I want 创建层级位置节点，
So that 可以建立收纳体系。

**Acceptance Criteria:**

**Given** 已登录
**When** `POST /api/locations` 传入名称和父节点 ID
**Then** 创建位置，自动生成物化路径，深度不限

**Given** 已有位置
**When** `PUT /api/locations/{id}` 传入新名称
**Then** 重命名成功，已关联物品不受影响

**Given** 空位置
**When** `DELETE /api/locations/{id}`
**Then** 删除成功

**Given** 非空位置
**When** `DELETE`
**Then** 返回错误

**And** `IEntityTypeConfiguration<Location>` + `Path TEXT NOT NULL` + `SortOrder INT`
**And** `Path.StartsWith()` 查询封装在 `LocationRepository`

### Story 2.2: 位置树浏览 API

As a 用户，
I want 按层级浏览位置树，
So that 可以看到完整收纳结构。

**Acceptance Criteria:**

**Given** 已有层级位置
**When** `GET /api/locations`
**Then** 返回扁平带 Path 的位置列表

**Given** 选中节点
**When** `GET /api/locations/{id}/children`
**Then** 返回直接子节点，按 SortOrder 排列

### Story 2.3: 标签系统

As a 用户，
I want 创建和管理标签，
So that 可以跨位置分类物品。

**Acceptance Criteria:**

**Given** 已登录
**When** `GET /api/tags`
**Then** 返回所有标签，含关联物品计数

**Given** 录入物品时输入新标签名
**When** 标签不存在
**Then** 自动创建

**And** Tag 实体 + ItemTag 多对多中间表

### Story 2.4: 前端 — 位置树选择器 + 标签选择器

As a 用户，
I want 在 UI 中浏览位置树和选择标签，
So that 录入时可快速选位置，浏览时可筛选。

**Acceptance Criteria:**

**Given** 在录入页
**When** 使用位置选择器
**Then** `LocationTree.razor`（MudTreeView）展示层级树，选中叶子节点高亮

**Given** 在浏览页
**When** 展开位置树侧栏
**Then** 点击节点过滤物品网格

**Given** 使用标签选择器（MudChipSet）
**Then** 支持多选筛选

**And** 空位置显示空状态提示

---

## Epic 3: 物品录入与智能识别

**Goal:** 用户可以拍照（或跳过）录入物品，AI 自动识别，支持连续收纳。

### Story 3.1: Item 实体 + 图片上传管线

**As a** 用户
**I want** 上传物品照片
**So that** 系统可以保存照片并生成缩略图

**Acceptance Criteria:**

**Given** 已登录
**When** 拍照确认后调用上传 API
**Then** 原图保存至 `{DataDirectory}/images/{itemId}/original.jpg`，返回 202 + imageId

**Given** 上传完成
**When** 后台异步处理
**Then** 生成 300px thumb.jpg + 1200px medium.jpg（ImageSharp），写入 DB 路径字段

**Given** 跳过拍照
**When** 不调用上传 API
**Then** Item 的 PhotoPath/ThumbPath/MediumPath 为 null

**And** `IEntityTypeConfiguration<Item>` 配置 EF Core
**And** Item 表含 `Name`、`PhotoPath`、`ThumbPath`、`MediumPath`、`Note`、`LocationId`、`CreatedByUserId`、`CreatedAt`
**And** 上传使用 multipart/form-data，`Endpoints/ImageEndpoints.cs`

### Story 3.2: 物品录入 API + 位置分配 + 入库保存

**As a** 用户
**I want** 填写物品信息并保存
**So that** 物品记录生成，进入家庭物品库

**Acceptance Criteria:**

**Given** 已上传照片（或跳过）
**When** `POST /api/items` 传入 name、locationId、tagIds、note
**Then** 创建 Item 记录，返回 201 + ItemDto

**Given** name 为空
**When** 调用创建
**Then** 返回 400 ProblemDetails + 校验错误

**Given** locationId 无效或为空
**When** 调用创建
**Then** 返回 400

**Given** 创建成功
**When** 保存
**Then** `CreatedByUserId` 自动设为当前登录用户，`CreatedAt` 设为 UTC 时间

**And** `ItemService.CreateAsync()` 封装业务逻辑
**And** `Endpoints/ItemEndpoints.cs` 处理请求

### Story 3.3: AI 识别集成 + 降级策略

**As a** 用户
**I want** 拍照后 AI 自动识别物品名称
**So that** 不需要手动打字就能完成录入

**Acceptance Criteria:**

**Given** 拍照确认
**When** 调用 AI 识别 API
**Then** 返回物品名称和备注描述，预填至前端表单

**Given** AI API 15s 内无响应
**When** 超时
**Then** 静默切换为空白输入框，不弹错误提示

**Given** AI API 返回错误
**When** 调用失败
**Then** 静默切换，不阻塞录入流程

**And** `LlmClient` 通过配置文件切换 base URL + model name + 自定义字段
**And** API key 在后端，前端不持有
**And** AI 调用在 `ItemService` 中触发，返回结果预填

### Story 3.4: 前端 — 录入页面

**As a** 用户
**I want** 在统一界面中拍照/跳过→填信息→选位置→保存
**So that** 一件物品的录入在一屏内完成

**Acceptance Criteria:**

**Given** 进入录入 Tab 或点击 FAB
**When** 显示录入界面
**Then** 显示"拍照"和"跳过拍照"两个入口

**Given** 选择拍照
**When** 拍照确认
**Then** AI 识别加载中显示 MudProgressCircular，结果预填至名称和备注字段

**Given** AI 成功或跳过拍照
**When** 填写/编辑物品名称 + 选择位置
**Then** 名称非空且位置已选 → 保存按钮 enabled

**Given** 名称空或位置未选
**When** 点击 disabled 保存按钮
**Then** 不执行任何操作

**Given** 点击保存
**When** API 返回成功
**Then** 物品卡片生成，返回首页

**And** 连续收纳模式下，下一件自动继承位置，顶部 ContinuityBanner 绿色提示条
**And** "退出连续模式"可清空继承位置
**And** `ItemEntry.razor` 单页组件 + `ImageUploader.razor` 子组件

### Story 3.5: 物品详情展示 + 录入者标识

**As a** 用户
**I want** 查看物品的完整信息
**So that** 确认物品详情和录入者

**Acceptance Criteria:**

**Given** 在网格/搜索结果中
**When** 点击物品
**Then** 进入详情页，显示：缩略图/原图、名称、位置完整路径、标签、备注、录入者用户名、录入时间

**Given** 无照片物品
**When** 进入详情页
**Then** 显示 MudIcon 占位图标替代照片

**Given** 已登录
**When** 点击"查看原图"
**Then** 加载 1200px medium 图或原图

**And** `ItemDetail.razor` 页面组件
**And** `GET /api/items/{id}` 返回完整 ItemDto 含 CreatedByUserName

---

## Epic 4: 查找浏览与生产部署

**Goal:** 用户可以搜索/浏览/筛选物品并找到实物，PWA 安装到桌面离线访问，应用容器化上线运行。

### Story 4.1: 搜索功能

As a 用户，
I want 用关键词搜索物品，
So that 快速找到目标物品而无需翻遍整个物品库。

**Acceptance Criteria:**

**Given** 已登录且在首页
**When** 在搜索框输入关键词
**Then** 调用 `GET /api/items?q={keyword}`，EF Core LIKE 模糊匹配物品名称、备注和标签

**Given** 搜索有结果
**When** API 返回匹配物品
**Then** 列表展示缩略图 + 名称 + 位置路径 + 标签，按相关度排列

**Given** 搜索无匹配
**When** API 返回空列表
**Then** 显示 EmptyState 空状态提示

**Given** 搜索结果
**When** 点击某个物品
**Then** 跳转至物品详情页 `/items/{id}`

**And** `GET /api/items?q=` 返回 `ItemSummaryDto[]`，含 `X-Total-Count` 响应头
**And** 搜索在 `ItemService.SearchAsync()` 中实现，服务端 EF Core LIKE 查询
**And** `SearchBar.razor` 组件，MudTextField + Adornment 搜索图标
**And** 搜索响应 < 500ms（NFR-2）

### Story 4.2: 缩略图网格浏览

As a 用户，
I want 以缩略图网格浏览所有物品，
So that 视觉化地概览家庭物品库。

**Acceptance Criteria:**

**Given** 已登录
**When** 进入浏览 Tab
**Then** `GET /api/items` 返回所有物品，按创建时间倒序排列

**Given** 物品有照片
**When** 网格渲染
**Then** ItemCard 展示 300px 缩略图 + 物品名称 + 位置概要

**Given** 物品无照片
**When** 网格渲染
**Then** ItemCard 展示 MudIcon 占位图标替代照片

**Given** 网格加载中
**When** API 未返回
**Then** 显示 MudProgressCircular 加载指示器

**And** `Browse.razor` 页面组件，MudGrid + MudItem 响应式布局
**And** 移动端 2 列 / 平板 4 列 / 桌面 6 列
**And** `ItemCard.razor` 组件，Material Design 卡片风格，4dp 圆角，0-1 elevation
**And** 缩略图懒加载，首屏 < 2s（NFR-2）
**And** `ItemSummaryDto` 包含 ThumbUrl、Name、LocationPath、Tags

### Story 4.3: 位置与标签筛选

As a 用户，
I want 按位置和标签筛选物品，
So that 按收纳结构或分类快速缩小查找范围。

**Acceptance Criteria:**

**Given** 在浏览页
**When** 点击位置树节点
**Then** `GET /api/items?locationId={id}` 返回该节点及所有子节点下的物品

**Given** 在浏览页
**When** 选择一个或多个标签
**Then** `GET /api/items?tagId=3&tagId=5` 返回具有这些标签的物品

**Given** 同时设置位置和标签筛选
**When** 组合调用
**Then** `GET /api/items?locationId=3&tagId=5` 返回同时满足两个条件的物品

**Given** 筛选无结果
**When** API 返回空列表
**Then** 网格区域显示 EmptyState 空状态提示

**And** `LocationTree.razor` 使用 MudTreeView，支持展开/折叠，选中节点高亮
**And** `TagFilter.razor` 使用 MudChipSet 多选，显示每标签关联物品数
**And** 位置子树查询使用物化路径 `Path.StartsWith()`（LocationRepository）
**And** `GET /api/tags` 返回所有标签含 `ItemCount`

### Story 4.4: 物品详情与删除

As a 用户，
I want 查看物品完整信息并删除不需要的物品，
So that 管理物品库保持整洁。

**Acceptance Criteria:**

**Given** 从网格/搜索结果点击物品
**When** 进入详情页
**Then** `GET /api/items/{id}` 返回完整 ItemDto，展示：medium 图（1200px）、名称、位置完整路径、标签、备注、录入者用户名、录入时间

**Given** 无照片物品
**When** 进入详情页
**Then** 显示 MudIcon 占位图标替代照片

**Given** 在详情页
**When** 点击删除按钮（Error 色 `#EF5350`）
**Then** 弹出 MudDialog 确认对话框

**Given** 确认删除
**When** 调用 `DELETE /api/items/{id}`
**Then** 返回 204，删除 DB 记录 + 级联删除 original/thumb/medium 三个图片文件

**Given** 删除完成
**When** 返回上一页
**Then** 已删除物品不再出现

**And** `ItemDetail.razor` 页面组件，路由 `/items/{id}`
**And** v1 详情页为只读展示，不做编辑
**And** 任何已认证用户可删除任何物品（v1 无角色区分）

### Story 4.5: PWA 离线支持

As a 用户，
I want 将 BoxWise 安装到手机桌面并离线浏览，
So that 在没有网络时仍能查看已缓存的物品信息。

**Acceptance Criteria:**

**Given** 使用支持的浏览器（Chrome/Edge/Safari）
**When** 访问 BoxWise
**Then** 浏览器显示"安装"提示，可添加到桌面

**Given** PWA 已安装
**When** 从桌面图标打开
**Then** 以独立窗口模式启动，显示 splash screen + 应用图标

**Given** 在线状态
**When** Service Worker 激活
**Then** 按缓存策略处理资源：
  - `_framework/*.dll`、`*.wasm` → Cache-First（不可变资源）
  - `/images/thumb/*`、`/images/medium/*` → Stale-While-Revalidate
  - `/api/*` → Network-Only

**Given** 离线状态
**When** 访问已缓存的页面和缩略图
**Then** 可浏览缓存的物品列表和缩略图（只读）

**Given** 离线状态
**When** 尝试写入/搜索
**Then** 操作不可用，友好提示

**And** `manifest.webmanifest` 在 `wwwroot/`，含 `icon-192.png` 和 `icon-512.png`
**And** `service-worker.js` + `service-worker.published.js` 在 `wwwroot/`
**And** 应用名称 "箱知 BoxWise"，主题色 Primary `#546E7A`

### Story 4.6: Docker 容器化部署

As a 运维者，
I want 用 Docker Compose 一键部署应用，
So that 在 1C1G Linux VPS 上稳定运行。

**Acceptance Criteria:**

**Given** 已配置 `appsettings.Production.json`（含 LLM API Key 等密钥）
**When** 执行 `docker compose up -d`
**Then** 应用启动，可通过 HTTPS 访问

**Given** 容器运行中
**When** 上传物品照片
**Then** 图片和 SQLite 数据库持久化到 Docker 卷 `./data:/app/data`

**Given** 容器重启
**When** 重新启动
**Then** 所有数据完整保留

**And** 多阶段 Dockerfile：build 阶段 `mcr.microsoft.com/dotnet/sdk:10.0`，runtime 阶段 `mcr.microsoft.com/dotnet/aspnet:10.0`
**And** `Caddyfile` 反向代理：`/api/*`、`/admin/*` → ASP.NET，静态文件直出，`/images/*` Cache-Control 24h
**And** Caddy 自动 Let's Encrypt TLS 证书
**And** Gzip 压缩，同源部署消除 CORS
**And** 构建命令：`dotnet publish src/BoxWise.Server -c Release -o /app && docker build -t boxwise:latest .`
**And** `docker-compose.yml`：环境变量注入密钥，端口映射 443

---

Epic 4 共 6 条 Story，覆盖搜索/浏览/筛选/删除/PWA/部署全部流程。

## Epic 5: 用户管理增强与品牌收尾

**Goal:** 管理员可以管理用户账户、角色和密码；用户可自行修改信息和密码；应用品牌完善。

### Story 5.1: Admin 用户管理增强

As a 管理员，
I want 在后台编辑/删除用户、分配角色、重置密码，
So that 用户账户管理完整可控。

**Acceptance Criteria:**

**Given** 管理员在 /admin 页面
**When** 点击编辑用户
**Then** 可修改用户名、分配/取消 Admin 角色

**Given** 管理员操作
**When** 删除其他用户
**Then** 用户被删除，管理员不能删除自己

**Given** 管理员操作
**When** 重置用户密码
**Then** 新密码生效，旧会话失效（SecurityStamp 更新）

**And** 使用 `UserManager<T>` 内置 API（SetUserNameAsync、AddToRoleAsync、UpdateSecurityStampAsync）
**And** NormalizedUserName 通过 SetUserNameAsync 自动维护

### Story 5.2: 用户自助服务

As a 普通用户，
I want 修改自己的用户名和密码，
So that 我可以管理自己的账户信息。

**Acceptance Criteria:**

**Given** 已登录
**When** 修改用户名
**Then** 新用户名生效，不能与已有用户重复

**Given** 已登录
**When** 修改密码（输入正确的旧密码）
**Then** 密码修改成功

**Given** 旧密码错误
**When** 修改密码
**Then** 返回验证错误

**And** `TypedResults.ValidationProblem` 返回结构化错误
**And** MudDialog 用于密码修改 UI

### Story 5.3: 品牌与版权完善

As a 用户，
I want 应用有专业的品牌形象，
So that BoxWise 看起来像一个成熟的产品。

**Acceptance Criteria:**

**Given** 访问应用
**When** 浏览器加载
**Then** 显示 SVG favicon、OG 标签、正确的页面标题

**Given** 页面底部
**When** 滚动到底部
**Then** Footer 显示版权信息和版本号（AssemblyInformationalVersion）

**And** `箱知 BoxWise` 应用名称统一
**And** v1.0.0 版本标记

---

## Epic 6: 单元测试补完

**Goal:** 将单元测试从 52 个扩展到 ≥ 85 个，覆盖 Repository 缺口、Service 层、Endpoint 层和 Admin PageModel 剩余 handler。

**Source:** `_bmad-output/specs/spec-test-coverage/SPEC.md`

### Story 6.1: 测试清理与质量改进

As a 开发者，
I want 删除死代码、将重复边界验证重构为 Theory、建立统一的测试模式，
So that 后续测试补完有干净的基线和可复用的参数化模式。

**Acceptance Criteria:**

**Given** 测试项目存在死代码 UnitTest1.cs
**When** 删除该文件
**Then** 项目编译通过，测试总数减少 1

**Given** 同类边界条件验证（空字符串/超长名）
**When** 重构为 `[Theory]` + `[InlineData]`
**Then** ≥ 3 个 Theory 覆盖 ≥ 10 个数据组合，原有覆盖不变

**Given** 所有现有测试
**When** `dotnet test`
**Then** 52 个测试通过（删除 UnitTest1 后）→ 51 个测试均通过

**And** 删除 `src/BoxWise.Server.Tests/UnitTest1.cs`
**And** TagRepositoryTests 中 CreateAsync/RenameAsync/GetOrCreateAsync 的空名+超长名边界从多个 Fact 合并为 Theory
**And** LocationRepositoryTests 中 CreateAsync 边界验证从多个 Fact 合并为 Theory

### Story 6.2: Repository 层覆盖补完

As a 开发者，
I want 补齐 ItemRepository、TagRepository、LocationRepository 的测试缺口，
So that Repository 层的每个 public 方法都有 happy-path 和关键异常路径测试。

**Acceptance Criteria:**

**Given** ItemRepository 缺失 GetByIdAsync 测试
**When** 添加测试
**Then** 覆盖：存在返回 Item（含 Location+Tags 导航属性）、不存在返回 null

**Given** ItemRepository 缺失 DeleteAsync 测试
**When** 添加测试
**Then** 覆盖：存在删除返回 true、不存在返回 false、含标签级联删除 ItemTag

**Given** LocationRepository 缺失 GetAllAsync 测试
**When** 添加测试
**Then** 覆盖：返回扁平列表按 SortOrder 排序

**Given** LocationRepository 缺失边界条件
**When** 添加测试
**Then** 覆盖：CreateAsync 不存在的父节点抛 ArgumentException、CreateAsync 超过 MaxDepth(10) 抛 InvalidOperationException、DeleteAsync 有 Item 关联抛 InvalidOperationException

**Given** TagRepository 缺失边界条件
**When** 添加测试
**Then** 通过 Theory 覆盖：空名和超长名（>50）在 CreateAsync/GetOrCreateAsync/RenameAsync 中均抛 ArgumentException

**And** 所有新增测试使用 `TestDbContextFactory.Create()` 创建隔离 DbContext
**And** 新增测试 ≥ 13 个

### Story 6.3: Service 层测试建立

As a 开发者，
I want 为 ImageStorageService 和 LlmClient 建立测试，
So that 文件存储逻辑和 AI 调用的解析逻辑有回归保护。

**Acceptance Criteria:**

**Given** ImageStorageService 无测试
**When** 添加测试
**Then** 覆盖：SaveOriginalAsync 保存文件到正确路径、GetItemDirectory 返回路径含 itemId、DeleteItemFiles 清理所有文件、GetOriginalPath/GetThumbPath/GetMediumPath 返回正确路径

**Given** LlmClient 无测试
**When** 添加测试（使用 Moq HttpMessageHandler）
**Then** 覆盖：JSON 正常解析返回 RecognitionResultDto、fallback 正则解析（代码块格式）、空配置返回 null、HTTP 超时静默降级、无效 JSON 返回 null

**And** ImageStorageService 测试使用 `Path.GetTempPath()` + GUID 子目录，测试后清理
**And** LlmClient 测试使用 Moq `HttpMessageHandler` 模拟 HTTP 响应，不发起真实网络请求
**And** ThumbnailService 不在本次范围内（手动验证）
**And** 新增测试 ≥ 9 个

### Story 6.4: Endpoint 层测试建立

As a 开发者，
I want 为 Auth、Item、Tag、Location 四个核心 Endpoint 建立 handler 级别测试，
So that 请求-响应完整路径有回归保护。

**Acceptance Criteria:**

**Given** AuthEndpoints 无 handler 测试
**When** 添加测试
**Then** 覆盖：LoginAsync 成功/失败/锁定、LogoutAsync 成功、GetCurrentUserAsync 返回用户+IsAdmin、UpdateProfileAsync 成功/重复名/空名、ChangePasswordAsync 成功/错误旧密码/太短

**Given** ItemEndpoints 无 handler 测试
**When** 添加测试
**Then** 覆盖：CreateItemAsync 成功/缺名/无效位置、GetItemByIdAsync 存在/404、SearchItemsAsync 无参/关键词/位置/标签、DeleteItemAsync 成功/404

**Given** TagEndpoints 无 handler 测试
**When** 添加测试
**Then** 覆盖：GetAllTagsAsync 返回列表、CreateTagAsync 成功/空名/重复、RenameTagAsync 成功/不存在/重复、DeleteTagAsync 成功/不存在

**Given** LocationEndpoints 无 handler 测试
**When** 添加测试
**Then** 覆盖：GetAllLocationsAsync 返回列表、CreateLocationAsync 根/子/空名、RenameLocationAsync 成功/不存在、DeleteLocationAsync 叶节点/有子节点拒绝、GetChildrenAsync 成功/不存在

**And** 使用 `TestIdentityFactory.CreateAsync()` 获取 UserManager/SignInManager
**And** Repository 通过 `TestDbContextFactory.Create()` 创建，注入 handler 调用
**And** 新增测试 ≥ 30 个

### Story 6.5: Admin PageModel 测试补完

As a 开发者，
I want 补齐 CreateAccountModel 和其余 PageModel 的 OnGetAsync handler 测试，
So that Admin 后台的所有页面 handler 都有回归保护。

**Acceptance Criteria:**

**Given** CreateAccountModel 无测试
**When** 添加测试
**Then** 覆盖：OnPostAsync 成功创建用户、空用户名返回错误、弱密码返回错误、重复用户名返回错误

**Given** 其余 PageModel 的 OnGetAsync 无测试
**When** 添加测试
**Then** 覆盖：EditAccountModel.OnGetAsync 加载用户、IndexModel.OnGetAsync 加载用户列表、ChangeUserPasswordModel.OnGetAsync 加载用户名

**And** 使用 `TestIdentityFactory.CreateAsync()` 获取所需 Identity 服务
**And** 新增测试 ≥ 5 个
**And** `dotnet test` 全部通过，总数 ≥ 85

## Epic 9: 2FA 设置管理

> **Spec:** `_bmad-output/specs/spec-2fa-modify-settings/SPEC.md`
> **动机:** 用户配置 2FA 后无法自行修改邮箱地址、重置 TOTP 密钥、或重新生成恢复码——所有操作需管理员后台介入，耗时且不切实际。
> **范围:** 7 个新 API 端点（修改验证、邮箱修改、TOTP 重置、恢复码管理），1 个新 DB 字段（PendingTotpSecretKey），前端管理对话框。

### Story 9.1: 2FA 设置管理 —— 服务端与客户端完整实现

As a 已配置 2FA 的用户，
I want 通过 2FA 验证后修改邮箱地址、重置 TOTP 应用、重新生成恢复码，
So that 我可以自行管理 2FA 设置，无需管理员介入。

**Acceptance Criteria:**

**Given** 用户已配置 TOTP 2FA
**When** 打开设置 → 双因素认证 → 管理对话框 → 选择重置 TOTP → 2FA 验证通过 → 扫描新 QR 码 → 输入新验证码
**Then** TOTP 密钥已更新，旧密钥失效，2FA 保持启用状态

**Given** 用户已配置 Email 2FA
**When** 打开管理对话框 → 选择修改邮箱 → 2FA 验证通过 → 输入新邮箱 → 发送验证码 → 验证新邮箱
**Then** EmailForTwoFactor 更新为新邮箱，后续登录验证码发送到新邮箱

**Given** 用户已配置 2FA
**When** 打开管理对话框 → 2FA 验证通过 → 点击重新生成恢复码
**Then** 获得 8 个新恢复码，旧恢复码全部失效

**Given** 用户未通过 2FA 验证
**When** 尝试调用任何修改端点
**Then** 返回 401 或 session token 无效错误

**And** 修改任一设置后，`TwoFactorEnabled` 保持 true，已有恢复码（如未重新生成）仍有效
**And** 所有现有 2FA 测试继续通过
**And** 新增测试覆盖：端点正常/错误路径 + TOTP 双密钥窗口 + 恢复码非消耗验证 + session token purpose 校验
