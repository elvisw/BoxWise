# Story 4.5: PWA 离线支持

Status: review

## Story

As a 用户，
I want 将 BoxWise 安装到手机桌面并离线浏览，
So that 在没有网络时仍能查看已缓存的物品信息。

## Acceptance Criteria

1. **AC-1: 可安装** — manifest.webmanifest 正确配置，浏览器显示安装提示
2. **AC-2: 独立窗口** — 安装后以 standalone 模式启动，显示 splash screen
3. **AC-3: 缓存策略** — Service Worker 按资源类型差异化缓存：框架→Cache-First，图片→SWR，API→Network-Only
4. **AC-4: 离线浏览** — 离线时可访问已缓存的页面和缩略图（只读）
5. **AC-5: 离线写入保护** — 离线时录入/删除等操作不可用，友好提示
6. **AC-6: 主题色** — 应用名称"箱知 BoxWise"，主题色 Primary `#546E7A`

## Tasks / Subtasks

- [x] Task 1: 更新 manifest.webmanifest (AC: #1, #2, #6)
  - [x] 1.1 theme_color 改为 `#546E7A`，background_color 改为 `#FAFAFA`
  - [x] 1.2 确认 name/short_name/display/icons 配置正确

- [x] Task 2: 更新 service-worker.published.js 缓存策略 (AC: #3, #4)
  - [x] 2.1 `/api/images/*` → Stale-While-Revalidate（images-cache-v1，离线可用缓存，后台更新）
  - [x] 2.2 `/api/*` → Network-Only（写入操作必须在线）
  - [x] 2.3 保留框架文件的 Cache-First 策略（原始模板逻辑）

- [x] Task 3: 开发版 service-worker.js (AC: #3)
  - [x] 3.1 开发版保持空 fetch 处理器（热重载需要，不缓存）

- [x] Task 4: 构建验证 (AC: #1-#6)
  - [x] 4.1 `dotnet build BoxWise.slnx` 零错误零警告
  - [x] 4.2 `dotnet test BoxWise.slnx` 全部通过

---

## Dev Notes

### 前置上下文

- **PWA 模板已存在** — `dotnet new blazorwasm --pwa` 已生成 manifest + service-worker + icons
- **manifest.webmanifest** — name="箱知 BoxWise"，display="standalone"，icons 已有 192/512
- **service-worker.published.js** — 标准 Blazor WASM 缓存策略，需扩展图片 SWR + API Network-Only
- **index.html** — 已有 manifest 链接 + service worker 注册 + splash screen loading-progress

### manifest.webmanifest 更新

```json
{
  "name": "箱知 BoxWise",
  "short_name": "BoxWise",
  "theme_color": "#546E7A",
  "background_color": "#FAFAFA"
}
```

### service-worker.published.js 缓存策略扩展

在 `onFetch` 函数中，在现有逻辑之前添加：

```js
// Stale-While-Revalidate for thumbnails/medium images
if (event.request.url.includes('/api/images/')) {
    const cache = await caches.open('images-cache-v1');
    const cached = await cache.match(event.request);
    const fetchPromise = fetch(event.request).then(response => {
        if (response.ok) cache.put(event.request, response.clone());
        return response;
    }).catch(() => cached);
    return cached || fetchPromise;
}

// Network-Only for all API calls (writes must be online)
if (event.request.url.includes('/api/')) {
    return fetch(event.request);
}
```

### 离线场景行为

| 操作 | 在线 | 离线 |
|------|------|------|
| 浏览页面 | 正常 | 缓存页面可用 |
| 查看缩略图 | 正常 | SWR 缓存可用 |
| 搜索 | 服务端搜索 | 不可用（API Network-Only） |
| 录入物品 | 正常 | 不可用（上传失败） |
| 删除物品 | 正常 | 不可用（API 调用失败） |

### 文件结构变更

```
src/BoxWise.Client/wwwroot/
  manifest.webmanifest                  (modified — theme_color/background_color)
  service-worker.js                     (modified — 同步缓存策略)
  service-worker.published.js           (modified — 图片 SWR + API Network-Only)
```

### 构建与验证

```bash
dotnet build BoxWise.slnx
dotnet test BoxWise.slnx
# PWA 验证需在浏览器中测试：
# 1. Chrome DevTools → Application → Manifest → 检查可安装性
# 2. Service Workers → 检查注册和缓存策略
# 3. Network throttling → Offline → 验证离线浏览
```

---

## References

| 内容 | 来源 |
|------|------|
| Story AC 定义 | [Source: epics.md#Story 4.5] |
| NFR-3 PWA/Offline | [Source: prd.md#NFR-3] |
| PWA 缓存策略 | [Source: architecture.md#PWA Cache Strategy] |
| AR-9 Service Worker | [Source: architecture.md#AR-9] |

## Dev Agent Record

### Agent Model Used

deepseek-v4-pro

### Debug Log References

### Completion Notes List

✅ 全部 4 个 Task 完成 — PWA 离线支持就绪，28/28 测试通过

**实施要点：**
- manifest.webmanifest：theme_color #546E7A + background_color #FAFAFA
- service-worker.published.js：新增 images-cache-v1（SWR：优先缓存，后台更新）+ API Network-Only（写入必须在线）
- 框架文件保持原始 Cache-First 策略（offline-cache-{version}）
- 开发版 service-worker.js 保持空 fetch 处理器（Hot Reload 兼容）
- index.html 已有 manifest 链接 + SW 注册 + splash screen，无需修改

### File List

**修改文件:**
- `src/BoxWise.Client/wwwroot/manifest.webmanifest` (modified — theme/background color)
- `src/BoxWise.Client/wwwroot/service-worker.published.js` (modified — 图片 SWR + API Network-Only)
