## Deferred from: code review of 3-5-item-detail + 4-2-thumbnail-grid-browse (2026-05-26)

- SearchItemsAsync 每次请求全量加载位置表用于路径解析，数据量增长后需优化
- 重复的路径解析逻辑（LocationRepository + ItemEndpoints），两个方法服务不同场景
- 已删除位置的 ID 会降级显示在 UI 路径中，需要级联删除功能
- GET /api/items/{id} 响应缺少标签字段 — Story 3.5 AC-1 预已有问题
- ResolvePathNames (static) 不可单独测试，路径解析逻辑在端点类中

## Deferred from: code review of 3-3-ai-recognition (2026-05-26)

- **空 base64 创建 0 字节 PhotoCapture** [ImageUploader.razor:101] — 既有问题。若 dataUrl 为 `data:image/jpeg;base64,` 则 base64 为空，创建 0 字节 PhotoCapture，AI 请求静默失败
- **BaseUrl 为 null 时 NRE** [LlmClient.cs:57] — 既有问题。LlmOptions.BaseUrl 无 [Required] 注解，配置为 null 时 ValidateOnStart 不拦截
- **MemoryStream 双重释放** [ItemEntry.razor:148] — 既有问题。using stream + using streamContent 双重释放同一 MemoryStream，目前幂等安全
