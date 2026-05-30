---
title: 'Admin 后台 SMTP 配置管理 + 测试发送 + 文档'
type: 'feature'
created: '2026-05-30'
status: 'done'
specLoopIteration: 2
baseline_commit: 'abbe26d'
context: ['_bmad-output/project-context.md']
---

## Spec Change Log

### Loopback 1 (2026-05-30)
**触发：** 代码审查发现 5 个 bad_spec 问题
**修正内容：**
1. SaveAsync 并发安全 — Always 约束追加"SaveAsync 使用 SemaphoreSlim(1,1) 保护整个临界区（含文件 I/O），不使用分离的 lock 块"
2. FromAddress/FromName 默认值 — Always 约束追加"EmailTwoFactorService.SendVerificationEmailAsync 对 FromAddress/FromName 保留默认值回退：FromAddress ?? "noreply@boxwise.app", FromName ?? "BoxWise""
3. TryWriteInitialFile 原子写入 — Always 约束追加"TryWriteInitialFile 也使用原子写入模式（.tmp → File.Move），与 SaveAsync 一致"
4. 异常捕获粒度 — Always 约束"所有文件 I/O"条目强化为"捕获特定异常（JsonException/IOException/UnauthorizedAccessException），禁止裸 catch(Exception)"
5. FromAddress 保存校验 — I/O 矩阵追加"保存时 FromAddress 为空 | 显示'发件人地址不能为空' | ModelState 校验"
**避免的坏状态：** SaveAsync TOCTOU 竞态导致并发写入数据丢失、FromAddress 为空时 2FA 邮件静默失败、TryWriteInitialFile 崩溃致首次配置损坏
**KEEP：** SmtpConfigDto 设计、ISmtpConfigurationService 接口、Data Protection 加密、GetSnapshot 快照模式、Admin 页面 UI、14 个测试、README 文档、超时常量（15s/30s）、FromName 换行剥离

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** SMTP 邮件服务器配置目前只能通过 `appsettings.json` 或环境变量设置，管理员每次修改都需要编辑文件并重启服务。此外，README 缺少 SMTP 配置文档，且配置后没有直观的方式验证是否生效。

**Approach:** 在 Admin 后台新增 "SMTP 设置" 页面，管理员可通过 Web 界面直接编辑 Host/Port/Username/Password/FromAddress/FromName 六个配置项，保存到持久化加密 JSON 文件（`{DataDirectory}/smtp-config.json`）即时生效无需重启。同一页面提供 "发送测试邮件" 按钮即时验证配置。README 新增 SMTP 配置章节说明 Admin 界面操作方式。

## Boundaries & Constraints

**Always:**
- Admin 页面使用 `[Authorize(Policy = "AdminOnly")]` 保护，表单内嵌 `@Html.AntiForgeryToken()`
- SMTP 密码使用 ASP.NET Core Data Protection API 加密存储（与 TOTP 密钥保护一致），明文永不出现在文件系统
- JSON 文件采用原子写入（先写 .tmp → `File.Move` 覆盖），防止崩溃损坏配置
- 首次启动时若 smtp-config.json 不存在，从 `IConfiguration["Smtp:*"]` 回退读取作为初始值（保证旧部署升级平滑）
- JSON 文件损坏/格式错误时优雅降级（使用空配置 + 日志警告），不阻断应用启动
- `EmailTwoFactorService` 改为从 `SmtpConfigurationService` 的快照方法读取配置（避免发送中途属性变化）
- 页面 UI 密码字段 `type="password"` + `autocomplete="off"`，GET 时永远不填充密码值（仅显示"已设置/未设置"状态）
- POST 保存时密码留空 = 保持旧值，非空 = 更新为新值
- FromName 保存前剥离 `\r`、`\n` 字符，防止邮件头注入
- `SaveAsync` 使用 `SemaphoreSlim(1,1)` 保护整个临界区（从密码加密到文件写入到内存更新），不分离 lock 块
- `TryWriteInitialFile` 也使用原子写入模式（先写 .tmp → `File.Move` 覆盖）
- `EmailTwoFactorService.SendVerificationEmailAsync` 对 `FromAddress`/`FromName` 保留默认值：`config.FromAddress.NullOrWhiteSpaceTo("noreply@boxwise.app")`、`config.FromName.NullOrWhiteSpaceTo("BoxWise")`
- 新建 `ISmtpConfigurationService` 接口便于测试 Mock
- `IsConfigured()` 仅检查 Host 非空 + Port 在有效范围，Password/Username 可选（支持无认证 SMTP 中继）
- 所有文件 I/O 操作使用 try/catch 保护，捕获特定异常（JsonException/IOException/UnauthorizedAccessException），禁止裸 catch(Exception)
- `SmtpConfigDto` 必须 `override ToString()` 将 Password 输出为 `***`，防止 record 自动 ToString 泄露密码
- `GetSnapshot()` 在 lock 内创建 `_config with { }` 浅拷贝后立即释放锁，网络请求全程在锁外执行
- 页面风格与现有 Admin 页面一致（纯 HTML/CSS，`_Layout.cshtml` 布局）
- README 文档格式与现有 "AI 识别（可选）" 章节一致

**Ask First:**
- 无

**Never:**
- 不在 Blazor WASM 客户端添加此功能
- 不使用 DB 存储 SMTP 配置
- 不添加 SMTP 配置的 API 端点（Admin Razor Page 的 OnPost 直接处理）
- 不修改 `appsettings.json` 的 Smtp 默认节（仅作为升级回退源）
- SMTP 密码不记录到任何日志
- 不提供"清空已设置密码"的独立操作（v1 限制：密码一旦设置，只能修改不能清空，需清空时手动删除 smtp-config.json）

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| 首次访问（无 JSON，有 appsettings） | smtp-config.json 不存在 | 从 IConfiguration["Smtp:*"] 回退加载初始值，表单显示已有配置 | N/A |
| 首次访问（完全无配置） | 无 JSON + appsettings Smtp:Host 为空 | 表单各字段为空，显示"尚未配置"提示 | 自动创建空 JSON |
| 编辑保存（含新密码） | 填写所有字段 → 保存 | JSON 原子写入，密码 Data Protection 加密，显示绿色成功提示 | 磁盘满/权限不足返回错误 |
| 密码保持原值 | 编辑其他字段，密码留空 → 保存 | 其他字段更新，密码保持旧加密值 | N/A |
| JSON 文件损坏 | 启动时 JSON 解析失败 | 捕获 JsonException，使用空配置启动，日志警告，Admin 页面可重新配置 | 不阻断启动 |
| 原子写入崩溃恢复 | 保存中途进程崩溃 | .tmp 文件残留但不影响（下次保存覆盖），原 .json 文件保持旧配置 | N/A |
| 测试邮件-成功 | 有效邮箱 + SMTP 配置完整 → 发送 | 显示绿色"测试邮件已发送到 xxx" | N/A |
| 测试邮件-SMTP 未配置 | Host 为空 → 点击发送 | 显示警告"请先配置 SMTP 服务器" | 不调用 Send |
| 测试邮件-连接失败 | 错误的主机/端口 | 显示红色错误含异常消息，密码不出现在日志 | catch → 用户友好错误 |
| 测试邮件-认证失败 | 错误的用户名/密码 | 显示红色"SMTP 认证失败，请检查用户名和密码" | catch → 区分认证错误 |
| 测试邮件-无效邮箱 | 空或格式错误 | 显示"请输入有效的邮箱地址" | ModelState 校验 |
| 端口校验 | 输入非数字/超出 1-65535 | 显示"端口号必须为 1-65535 之间的数字" | ModelState + Range 校验 |
| 非管理员访问 | 普通用户访问 `/admin/smtp-settings` | 返回 403 | `[Authorize]` 自动处理 |
| 旧部署升级 | 无 smtp-config.json，appsettings 有配置 | 首次访问自动从 IConfiguration 迁移到 JSON 文件 | 透明迁移 |
| RTL/多语言发件人名称 | FromName 含非 ASCII 字符 | 正常显示，MIME 编码为 UTF-8 | N/A |
| 密钥环丢失导致密码无法解密 | data/keys/ 目录丢失（Docker 重建未持久化） | GetSnapshot() catch CryptographicException，Password 置 null，UI 显示"解密失败，请重新输入密码" | 日志警告，不阻断启动 |
| 并发保存丢失更新 | 两管理员同时打开页面并先后保存 | 后保存者覆盖前保存者（最后写入胜出），无乐观锁 | 接受风险：SMTP 配置极少变更，概率极低 |
| 保存失败内存回滚 | SaveAsync 中途密码加密或文件写入失败 | 内存 _config 保持不变，返回错误给用户 | 文件写入成功后才更新内存 _config |
| 保存时 FromAddress 为空 | FromAddress 为空或空白 → 保存 | 显示"发件人地址不能为空" | ModelState 校验 |
| JSON 反序列化前向兼容 | 未来版本新增字段 | 反序列化使用 `JsonUnmappedMemberHandling.Skip`，旧 JSON 可被新版本正常加载 | 忽略未知字段 |

</frozen-after-approval>

## Code Map

- `src/BoxWise.Shared/Dtos/SmtpConfigDto.cs` -- **新建**：positional record，字段 Host/Port/Username/Password/FromAddress/FromName，Password 字段加 `[JsonIgnore]`，`override ToString()` 遮蔽密码
- `src/BoxWise.Shared/Dtos/SmtpTestResult.cs` -- **新建**：`record SmtpTestResult(bool Success, string? ErrorMessage)`
- `src/BoxWise.Server/Services/ISmtpConfigurationService.cs` -- **新建**：接口，含 `IsConfigured()`、`GetSnapshot()`、`SaveAsync(SmtpConfigDto)` 返回 `(bool, string?)`、`SendTestEmailAsync(string)` 返回 `SmtpTestResult`
- `src/BoxWise.Server/Services/SmtpConfigurationService.cs` -- **新建** Singleton：构造时加载 JSON（不存在则从 IConfiguration 回退），Data Protection 加解密密码，原子写入，JSON 损坏时优雅降级
- `src/BoxWise.Server/Services/EmailTwoFactorService.cs` -- 构造函数改为注入 `ISmtpConfigurationService` 替代 `IConfiguration`，发送前调用 `GetSnapshot()` 快照配置
- `src/BoxWise.Server/Program.cs` -- 注册 `ISmtpConfigurationService`/`SmtpConfigurationService` 为 Singleton（在 EmailTwoFactorService 之前）
- `src/BoxWise.Server/Pages/Admin/SmtpSettings.cshtml` -- **新建**：配置表单 + 测试邮件区
- `src/BoxWise.Server/Pages/Admin/SmtpSettings.cshtml.cs` -- **新建** PageModel：OnGet 加载/OnPostSave 保存/OnPostTest 发送测试
- `src/BoxWise.Server/Pages/Admin/Index.cshtml` -- 在 actions 区域添加 "SMTP 设置" 入口链接
- `src/BoxWise.Server.Tests/Services/SmtpConfigurationServiceTests.cs` -- **新建**：覆盖 Load/Save/IsConfigured/文件损坏/文件不存在/密码加密往返
- `src/BoxWise.Server.Tests/Endpoints/TwoFactorEndpointsTests.cs` -- 适配 EmailTwoFactorService 构造函数签名变更
- `README.md` -- 新增 SMTP 邮件配置章节

## Tasks & Acceptance

**Execution:**
- [x] `src/BoxWise.Shared/Dtos/SmtpConfigDto.cs` + `SmtpTestResult.cs` -- 新建 positional record：SmtpConfigDto(Host/Port/Username/Password/FromAddress/FromName，Password 加 `[JsonIgnore]` + `override ToString()` 遮蔽)，SmtpTestResult(Success, ErrorMessage) -- 供 Service 和 PageModel 共享
- [x] `src/BoxWise.Server/Services/ISmtpConfigurationService.cs` -- 新建接口：`IsConfigured()`、`GetSnapshot()` 返回 `SmtpConfigDto`、`SaveAsync(SmtpConfigDto)` 返回 `(bool Success, string? Error)`、`SendTestEmailAsync(string toEmail)` 返回 `SmtpTestResult` -- 便于 EmailTwoFactorService 和测试 Mock
- [x] `src/BoxWise.Server/Services/SmtpConfigurationService.cs` -- 新建 Singleton 实现：构造时加载 JSON（不存在则回退 IConfiguration 迁移），Data Protection 加解密，原子写入（.tmp→Move），JSON 损坏降级，FromName 换行剥离、控制字符清理，密码不入日志，SemaphoreSlim(1,1) 保护 SaveAsync 临界区 -- 实现 ISmtpConfigurationService
- [x] `src/BoxWise.Server/Program.cs` -- 注册 `ISmtpConfigurationService`/`SmtpConfigurationService` 为 Singleton（`services.AddSingleton<ISmtpConfigurationService, SmtpConfigurationService>()`，在 EmailTwoFactorService 注册之前）
- [x] `src/BoxWise.Server/Services/EmailTwoFactorService.cs` -- 构造函数 `IConfiguration` → `ISmtpConfigurationService`，`SendVerificationEmailAsync` 入口调用 `GetSnapshot()` 快照配置，`IsSmtpConfigured()` 委托给 service，FromAddress/FromName 默认值回退，DisconnectAsync 独立 try/catch -- 适配运行时可变配置
- [x] `src/BoxWise.Server/Pages/Admin/SmtpSettings.cshtml` + `.cshtml.cs` -- 新建：表单（Host/Port/Username/Password/FromAddress/FromName）+ 保存按钮 + 测试邮件区 + 结果反馈 + 配置状态指示 + `autocomplete="off"` + PRG 模式防重复发送 + HTML required + 前端防双击 -- AdminOnly 保护
- [x] `src/BoxWise.Server/Pages/Admin/Index.cshtml` -- 在 actions 区域添加 `<a href="/admin/smtp-settings">` 按钮（btn-outline 样式）
- [x] `src/BoxWise.Server.Tests/Services/SmtpConfigurationServiceTests.cs` -- 新建测试：Load（正常/空/损坏JSON）、Save（正常/磁盘满模拟/长度校验/控制字符清理/非DP密码重加密）、IsConfigured、密码加密往返、文件不存在回退 IConfiguration、并发 SemaphoreSlim
- [x] `src/BoxWise.Server.Tests/Endpoints/TwoFactorEndpointsTests.cs` -- 适配：将 `_config` 替换为 `Mock<ISmtpConfigurationService>`，确保 6 个测试通过
- [x] `README.md` -- 在 "AI 识别（可选）" 后新增 "SMTP 邮件配置（可选）" 章节：说明进入 Admin → SMTP 设置即可配置，含配置项表格、端口选择指南（587 STARTTLS / 465 SSL）、常见提供商示例（Gmail/QQ邮箱/163/Outlook）

**Acceptance Criteria:**
- Given 管理员登录 Admin 后台，when 点击 "SMTP 设置"，then 进入配置页面看到 6 个配置字段 + 配置状态 + 测试邮件区
- Given SMTP 配置页面，when 管理员填写所有字段并保存，then 配置加密持久化到 JSON 文件，即时生效无需重启
- Given 已保存 SMTP 配置（含密码），when 重启 Server 后再次访问配置页，then 密码字段显示"已设置"状态（不显示明文），其他字段值正确
- Given 密码字段留空，when 保存其他字段，then 旧密码不被清空
- Given 已保存 SMTP 配置，when 在测试区输入邮箱并点击发送，then 邮件到达且页面显示绿色成功提示
- Given SMTP 未配置 (Host 为空)，when 点击发送测试，then 显示 "请先配置 SMTP 服务器"
- Given 普通用户，when 访问 `/admin/smtp-settings`，then 返回 403
- Given 旧部署升级（无 smtp-config.json 但 appsettings 有 Smtp 节），when 管理员首次访问 SMTP 设置页，then 自动显示 appsettings 中的已有配置
- Given 新用户部署 BoxWise，when 阅读 README，then 能找到 SMTP 配置指南含端口选择和常见提供商示例
- Given EmailTwoFactorService 正在发送验证邮件，when 管理员同时保存新 SMTP 配置，then 进行中的发送不受影响（使用快照）

## Design Notes

**为什么用 JSON 文件而非数据库：** SMTP 配置是单行设置（非集合数据），JSON 文件方案无需 EF 迁移、无新实体/表、与 Docker 持久化卷兼容。`SmtpConfigurationService` 在构造时加载，保存时写回文件 + 内存刷新。

**为什么提取 ISmtpConfigurationService 接口：** EmailTwoFactorService 和 TwoFactorEndpointsTests 需要 Mock 配置服务。接口也让未来切换存储方式（如迁移到 DB）成为可能。

**密码加密策略：** 使用项目已有的 Data Protection API（与 TOTP 密钥保护一致）。加密后存储格式：`DP:{base64-protected-data}`，便于未来识别加密版本。

**配置快照模式：** `GetSnapshot()` 返回配置的不可变副本，EmailTwoFactorService 在发送邮件入口处快照，防止发送过程中另一管理员修改配置导致连接参数中途变化。

**旧部署升级平滑迁移：** SmtpConfigurationService 构造时流程：
1. 尝试读 `smtp-config.json` → 成功则加载（JSON 反序列化使用 `JsonUnmappedMemberHandling.Skip` 保证前向兼容）
2. JSON 不存在 → 从 `IConfiguration["Smtp:*"]` 读取 → 写入 JSON 文件 → 加载
3. JSON 存在但损坏 → 日志警告 → 使用空配置（不丢 appsettings 值，下次 Save 覆盖损坏文件）

**密码加密容错：** Data Protection 密钥环必须持久化（Docker 部署需确保 `data/keys/` 目录在卷内）。若密钥丢失导致解密失败，`GetSnapshot()` catch `CryptographicException` 后 Password 置 null，UI 显示"解密失败，请重新输入密码"，日志警告，不阻断启动。

**并发安全策略：** SMTP 配置是极少变更的管理操作，多管理员同时编辑概率极低。v1 采用"最后写入胜出"策略（不引入乐观锁），接受低概率的丢失更新风险。`SaveAsync` 保证操作顺序：加密密码 → 序列化 → 写 .tmp → File.Move → 成功后才更新内存 `_config`，任何步骤失败都保持旧状态。

**SendTestEmailAsync 放在配置服务中的理由：** 该方法本质是"验证当前配置是否可用"，与配置管理紧密耦合。虽然形式上增加了邮件发送职责，但它仅供 Admin 测试使用，不属于核心邮件发送路径（2FA 验证码由 EmailTwoFactorService 负责）。将其放在同一服务中避免了额外的服务注册和接口抽象。未来如果测试邮件逻辑变复杂，可提取为独立接口。

**SMTP 超时设置：** `SendTestEmailAsync` 中 SmtpClient 设置 `Timeout = 15000`（15 秒，管理员等待的合理上限，与项目 `LlmClient` 超时一致）。`EmailTwoFactorService.SendVerificationEmailAsync` 设置 `Timeout = 30000`（30 秒，用户登录流程可容忍稍长的等待）。

## Verification

**Commands:**
- `dotnet build BoxWise.slnx` -- expected: 零错误零警告
- `dotnet test BoxWise.slnx` -- expected: 全部通过（含新增 SmtpConfigurationServiceTests + 适配后的 TwoFactorEndpointsTests）

**Manual checks (if no CLI):**
- 启动 Server → 管理员登录 → `/admin/smtp-settings` → 填写真实 SMTP 配置 → 保存 → 发送测试邮件 → 邮件到达
- 重启 Server → 再次访问配置页 → 确认配置仍在且密码显示"已设置"
- 密码字段留空保存 → 测试邮件仍能发送（密码未变）→ 重启后确认
- 模拟：手动损坏 smtp-config.json → 重启 → 确认应用正常启动（日志有警告）→ Admin 页面可重新配置

## Suggested Review Order

**Core Architecture — 新增服务与并发安全**

- 入口点：SemaphoreSlim 保护全临界区（加密→写入→内存更新），Data Protection 密码加密，JSON 原子写入
  [`SmtpConfigurationService.cs:33`](../../src/BoxWise.Server/Services/SmtpConfigurationService.cs#L33)

- 服务接口定义：IsConfigured/GetSnapshot/SaveAsync/SendTestEmailAsync 四个契约
  [`ISmtpConfigurationService.cs:8`](../../src/BoxWise.Server/Services/ISmtpConfigurationService.cs#L8)

- DTO 设计：Password [JsonIgnore] + ToString() 遮蔽，JsonUnmappedMemberHandling.Skip 前向兼容
  [`SmtpConfigDto.cs:1`](../../src/BoxWise.Shared/Dtos/SmtpConfigDto.cs#L1)

- 测试结果 record
  [`SmtpTestResult.cs:1`](../../src/BoxWise.Shared/Dtos/SmtpTestResult.cs#L1)

**Integration — EmailTwoFactorService 重构适配**

- 构造函数 IConfiguration → ISmtpConfigurationService，入口 GetSnapshot() 快照，FromAddress/FromName 默认值回退
  [`EmailTwoFactorService.cs:10`](../../src/BoxWise.Server/Services/EmailTwoFactorService.cs#L10)

- DI 注册：ISmtpConfigurationService Singleton 先于 EmailTwoFactorService Scoped
  [`Program.cs:109`](../../src/BoxWise.Server/Program.cs#L109)

**UI — Admin SMTP 配置页面**

- PageModel：OnGet 加载配置 + OnPostSave 保存（长度校验/原子写入）+ OnPostTest 发送测试（PRG 模式）
  [`SmtpSettings.cshtml.cs:1`](../../src/BoxWise.Server/Pages/Admin/SmtpSettings.cshtml.cs#L1)

- 表单视图：6 配置字段 + 测试邮件区 + 配置状态徽章 + 防双击 + HTML required
  [`SmtpSettings.cshtml:1`](../../src/BoxWise.Server/Pages/Admin/SmtpSettings.cshtml#L1)

- Admin 首页入口链接
  [`Index.cshtml:21`](../../src/BoxWise.Server/Pages/Admin/Index.cshtml#L21)

**Tests — 新增 + 适配**

- SmtpConfigurationService 单元测试：18 个覆盖 Load/Save/IsConfigured/密码加密往返/文件损坏/FromName 清理
  [`SmtpConfigurationServiceTests.cs:1`](../../src/BoxWise.Server.Tests/Services/SmtpConfigurationServiceTests.cs#L1)

- TwoFactorEndpointsTests 适配：_config → Mock.Of<ISmtpConfigurationService>()
  [`TwoFactorEndpointsTests.cs:25`](../../src/BoxWise.Server.Tests/Endpoints/TwoFactorEndpointsTests.cs#L25)

**Docs — SMTP 配置指南**

- README 新增章节：配置项表格 + 端口选择指南 + 常见提供商示例 + 安全说明
  [`README.md:77`](../../README.md#L77)

**Spec — 本次变更的完整规格**

- 含 frozen intent + 21 个 I/O 场景 + Spec Change Log（Loopback 1 记录）
  [`spec-smtp-config-test-email.md:1`](spec-smtp-config-test-email.md#L1)
