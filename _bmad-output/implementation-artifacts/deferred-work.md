## Deferred from: code review of 3-5-item-detail + 4-2-thumbnail-grid-browse (2026-05-26)

- SearchItemsAsync 每次请求全量加载位置表用于路径解析，数据量增长后需优化
- 重复的路径解析逻辑（LocationRepository + ItemEndpoints），两个方法服务不同场景
- 已删除位置的 ID 会降级显示在 UI 路径中，需要级联删除功能
- ResolvePathNames (static) 不可单独测试，路径解析逻辑在端点类中
