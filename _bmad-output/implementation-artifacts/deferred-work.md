## Deferred from: code review of 3-3-ai-recognition (2026-05-26)

- **空 base64 创建 0 字节 PhotoCapture** [ImageUploader.razor:101] — 既有问题。若 dataUrl 为 `data:image/jpeg;base64,` 则 base64 为空，创建 0 字节 PhotoCapture，AI 请求静默失败
- **BaseUrl 为 null 时 NRE** [LlmClient.cs:57] — 既有问题。LlmOptions.BaseUrl 无 [Required] 注解，配置为 null 时 ValidateOnStart 不拦截
- **MemoryStream 双重释放** [ItemEntry.razor:148] — 既有问题。using stream + using streamContent 双重释放同一 MemoryStream，目前幂等安全
