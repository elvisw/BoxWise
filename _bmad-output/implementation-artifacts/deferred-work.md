# Deferred Work

## Deferred from: code review of tech-debt-epic6 (2026-05-27)

- SKBitmap.Resize() 返回 null 时触发 NRE — `ThumbnailService.cs:55` 缺少 null 检查 [EC-1]
- 构造函数 `.GetAwaiter().GetResult()` 死锁风险 — `AuthEndpointsTests.cs:22`, `ItemEndpointsTests.cs:30` [EC-2]
- 并发 GenerateInBackground 对同一 itemId 数据竞争 — `ThumbnailService.cs:33-35` [EC-3]
- MemoryStream 在 TestIdentityFactory 中未释放 — `TestIdentityFactory.cs:56` [EC-4]
- ServiceProvider 在 InvokeAsync 中未释放 — `ItemEndpointsTests.cs:47,63` [EC-5]
- ThumbnailService 宽高验证可拆分以提供更清晰的错误信息 — `ThumbnailService.cs:52-53` [EC-6]
- File.Create 在目标目录不存在时抛异常 — `ThumbnailService.cs:59` [EC-7]
- SearchItemsAsync 的 string?[]? 模型绑定在不同框架版本行为不一致 [EC-8]
- X-Total-Count 标头与实际返回数量语义不一致 [EC-9]
- Login 成功后 TOCTOU 竞态窗口 — `AuthEndpoints.cs:58` [EC-10]
- SearchItemsAsync tagId 参数无端点层测试 — `ItemEndpointsTests.cs:80-83` [EC-12]
- Login 测试仅断言状态码不验证响应体 — `AuthEndpointsTests.cs` 新增测试 [BH-5]
- ItemEndpointsTests 硬编码 LocationId=1 隐式依赖自增 ID — `ItemEndpointsTests.cs:88` [BH-4]

## Deferred from: code review of 6-1-test-cleanup-quality (2026-05-27)

- Null 输入未测试 — `IsNullOrWhiteSpace` 守卫也捕获 null，但当前测试仅覆盖 `""` 和超长字符串 [TagRepositoryTests/LocationRepositoryTests]
- 纯空白输入未测试 — `"   "`、`"\t"` 同样触发 `IsNullOrWhiteSpace`，未覆盖 [TagRepositoryTests/LocationRepositoryTests]
- 失败后状态未验证 — RenameAsync 抛异常后未断言 DB 中实体名称未变 [LocationRepositoryTests]
