---
id: SPEC-tech-debt-cleanup
companions:
  - debt-inventory.md
sources:
  - _bmad-output/implementation-artifacts/deferred-work.md
---

> **Canonical contract.** This SPEC and the files in `companions:` are the complete, preservation-validated contract for what to build, test, and validate. Source documents listed in frontmatter are for traceability only — consult them only if you need narrative rationale or prose color this contract intentionally omits.

# 技术债务清偿 — Epic 10/11 回顾发现项

## Why

Epic 10（Identity 脚手架迁移）和 Epic 11（前端适配 + 退役）的代码审查与手动验证阶段发现了 16 条预存技术债务，其中 12 条可行动（5 条已转为 GitHub Issues）。这些债务集中在 Cookie 安全配置不一致、API 错误响应缺失、Identity 脚手架页面健壮性不足、UI 资产依赖外部 CDN 四个领域。清偿它们可以消除已知的 500 崩溃风险、提升生产环境安全性、并让 Identity 管理页面的 UI 在浏览器隐私保护开启时正常渲染。不处理则每次 Epic 回顾都需重复记录、反复讨论。

## Capabilities

- id: CAP-1
  intent: 运维者部署到反向代理后，Cookie 安全属性和转发头配置一致且无重复代码，避免 SameSite/Secure 策略因环境差异导致登录失败。
  success: Program.cs 中 SameSite/SecurePolicy 三元表达式提取为单一 helper；TwoFactorRememberMeScheme 显式配置 SecurePolicy；UseForwardedHeaders 在生产环境启用；AccessDeniedPath 指向存在的路径。

- id: CAP-2
  intent: API 消费者（Blazor WASM 客户端）在未认证时收到与项目标准一致的 ProblemDetails JSON 错误体，而非空响应体。
  success: 未认证 API 请求返回 `application/problem+json`，包含 status=401、title、detail 字段，与项目中所有其他错误响应格式一致。

- id: CAP-3
  intent: 用户直接导航到 Identity 2FA 页面或使用空 returnUrl 参数时不会遇到 500 错误，邮箱确认页面在未登录时可正常访问。
  success: LoginWith2fa/RecoveryCode 的 OnGetAsync 处理 null User 场景（重定向到登录页）；空 returnUrl 参数触发默认回退路径；ConfirmEmail 页面正确标注 [AllowAnonymous]。

- id: CAP-4
  intent: Identity 管理页面在浏览器隐私追踪保护开启时侧边栏正常渲染，通行密钥管理对话框的按钮和文字边距与项目其余 UI 一致。
  success: Bootstrap CSS 从本地静态文件提供（非 CDN）；通行密钥管理对话框的 MudBlazor 组件间距与其他设置区域一致。

- id: CAP-5
  intent: 中文用户在 Identity 脚手架页面（登录、2FA、账户管理、邮箱确认等）看到中文本地化文本，而非英文默认文案。
  success: 17 个 Identity 脚手架页面（.cshtml）的静态文本（标题、标签、按钮文字、提示信息）替换为中文。验证错误消息由 ASP.NET Core Identity 框架资源文件生成，不在本 CAP 范围。

## Constraints

- 不修改 Identity 脚手架的核心认证逻辑（仅修复健壮性和本地化）
- Bootstrap CSS 文件放入 Server 的 `wwwroot/lib/bootstrap/`，通过 Server 端 `UseStaticFiles` 中间件提供（Identity 布局页由 Server 渲染，Client `wwwroot/` 不可达）
- 中文汉化范围限于 `.cshtml` 静态文本，不覆盖 Identity 框架内置的错误消息字符串（框架资源文件不在本 spec 范围）
- Cookie 配置修改后须在开发环境（跨端口 5000↔5001）和生产环境（Caddy 反向代理）两种场景下验证

## Non-goals

- Email 发送适配器的 CancellationToken / null 校验 / 端口校验改进 — IEmailSender 接口本身不暴露 CancellationToken，null 校验与接口契约冲突，Port 校验由 Admin SMTP Settings 页面负责
- 开发环境跨端口 Identity 页面 404 问题（#18）— Client dev server 5001 无法服务 Razor Pages，已知限制
- ConfiguredMethods 与 Identity 页面同步 — Epic 10 回顾已记录，不在 deferred-work.md 范围
- Identity 页面架构重构 — 不对脚手架做结构性修改
- 生产环境未发现的未知配置问题 — 仅覆盖 deferred-work.md 和关联 GitHub Issues 中已记录的条目

## Success signal

`deferred-work.md` 中 12 条可行动条目全部勾选为 `[x]`；Program.cs 中 SameSite/SecurePolicy 三元表达式不再重复出现；API 401 响应体包含 ProblemDetails JSON；任意浏览器隐私保护开启时 Identity 管理页面侧边栏正常渲染。

## Assumptions

- Identity 脚手架页面（`Areas/Identity/Pages/Account/`）的现有结构和命名不会在近期发生大规模变更
- 项目当前不启用 `RequireConfirmedAccount`，ConfirmEmail 的 [AllowAnonymous] 为防御性修复
- Bootstrap 5.3.3 的 CSS 文件可直接放入 `wwwroot/` 通过 UseStaticFiles 提供
- 中文化翻译质量以用户（Elvis）审查为准，不引入第三方翻译服务

## Open Questions

<!-- 全部已解决。无待定项。 -->
