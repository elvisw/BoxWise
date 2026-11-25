---
title: 'Fix configurable Chat Completions endpoints for image recognition'
type: bugfix
ticket: ''
created: '2026-11-25'
status: built
baseline_revision: '56ee2ae88229650a22ec7bc4871166eab54fbe89'
route: full
route_source: auto
risk: medium
review: quick
review_source: pinned
lenses_ran: [quick]
review_loop_iteration: 0
context: ['{project-root}/AGENTS.md']
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** Browser image recognition always POSTs to `/api/v3/chat/completions`, discarding the configured URL path and sending requests to the wrong path for DeepSeek and OpenAI-compatible gateways.

**Approach:** Accept an API root or full Chat Completions endpoint in `BaseUrl`, keep documented Ark URLs working, and require a fresh Key when changing origin. Remove BoxWise's fixed `max_tokens: 200` without replacing it. Omit the output budget and thinking controls; provider defaults still apply.

## Boundaries & Constraints

**Always:** Send Bearer auth and Base64 `image_url` from the browser; keep the prompt, model and timeout defaults, configuration cache and manual fallback. Item photo storage is unchanged.

**Never:** Add a proxy, migration, auth scheme, guessed-URL retry, or credential/photo logging. Keep DTO, route and seed contracts; the Key guard is the only save-rule change. Do not use business data or paid API calls in automated verification.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected behavior | Error handling |
|---|---|---|---|
| M1 Ark | Documented root `/api/v3[/]`; or bare `https://ark.cn-beijing.volces.com` on its default port | POST `/api/v3/chat/completions`; bare-origin exception is exact, not for lookalikes or custom paths | No guessed retry |
| M2 DeepSeek | `https://api.deepseek.com` or full `/chat/completions` | POST `/chat/completions` | No guessed retry |
| M3 Gateway | `/openai/v1`, or `/openai/v1/chat/completions/` with `?profile=vision` | Trim trailing path slashes, append only if suffix absent, preserve prefix/query | No guessed retry |
| M4 Request | Any valid configuration | Bearer and image payload retained; no `max_tokens`, `max_completion_tokens` or thinking override | Provider defaults apply |
| M5 Invalid | Missing Key/URL, relative URL, non-HTTP(S), userinfo or fragment | No outbound recognition request | Manual entry |
| M6 Reply | Non-2xx, network/timeout/cancellation, unusable JSON, or trimmed name >200 / note >2000 characters | Do not prefill invalid output | Existing manual fallback |
| M7 Switch | Existing Key, origin changes (scheme, host, effective port) and submitted Key is blank | Admin form error / API 400; no settings change | Require a Key for the destination |
| M8 Save | Same origin (path/query may change) or origin changes with a supplied Key | Keep stored Key if blank on same origin; otherwise save the supplied Key with the URL | Unchanged invalid legacy URL may be retained; on URL change without a provable same origin, do not carry the old Key |

</frozen-after-approval>

## Code Map

- `src/BoxWise.Client/Services/AiService.cs` / `src/BoxWise.Client.Tests/Services/AiServiceTests.cs` — `RecognizeAsync` and mocked HTTP; tests currently miss final URI. `ItemEntry.razor` prefills blank fields; `src/BoxWise.Server/Repositories/ItemRepository.cs` enforces save lengths.
- `src/BoxWise.Server/Pages/Admin/LlmConfig.cshtml.cs` / `src/BoxWise.Server/Endpoints/LlmConfigEndpoints.cs` — both preserve blank Key today; API has an insert-conflict retry. `LlmConfig.cshtml` provides field help.

## Tasks & Acceptance

**Execution (tests and fixes first, then help/docs):**
- [x] `src/BoxWise.Client.Tests/Services/AiServiceTests.cs` and `src/BoxWise.Client/Services/AiService.cs` — reproduce and fix M1–M6. Send an absolute URI, validate output before prefill on both parse paths, and test exact-length/over-limit outputs and Ark lookalikes.
- [x] `src/BoxWise.Server.Tests/Endpoints/LlmConfigEndpointsTests.cs` and `src/BoxWise.Server.Tests/Pages/LlmConfigModelTests.cs` (new), `src/BoxWise.Server/Endpoints/LlmConfigEndpoints.cs` and `src/BoxWise.Server/Pages/Admin/LlmConfig.cshtml.cs` — test and implement M7–M8 with one origin rule. Check before any save, including retry; do not pair a concurrent update's Key with a stale URL. Test unchanged persisted state after rejection and retained admin authorization.
- [x] `src/BoxWise.Server/Pages/Admin/LlmConfig.cshtml`, `README.md`, `docs/deployment-guide.md`, `docs/integration-architecture.md`, `docs/api-contracts-server.md` — explain root/full URI, Bearer-only, HTTPS/mixed content, CORS for the deployment origin, Key replacement and refresh. Show DeepSeek's official URL and `deepseek-flash` (not `deepseek-v4-pro`); explain provider defaults may affect latency/cost.

**Acceptance Criteria:**
- Given the documented Ark configuration or the DeepSeek example with a test Key, when a mock vision endpoint returns valid name/note, then the browser recognition flow returns those values from one direct POST, without a BoxWise inference proxy.
- Given an existing configured Key, when an admin changes the provider origin without submitting a Key, then both write paths reject the change and leave the old URL and Key intact.

## Implementation Notes

- Client (`src/BoxWise.Client/Services/AiService.cs`): `BaseUrl` is resolved to an absolute URI before the request. Root paths get `/chat/completions`; an existing `/chat/completions` suffix is not duplicated; trailing path slashes are trimmed and query strings preserved. Only the exact HTTPS bare origin `ark.cn-beijing.volces.com` (default port, no query) maps to `/api/v3/chat/completions`. Relative, non-HTTP(S), userinfo and fragment URLs return `null` before `IHttpClientFactory` is called. The fixed `max_tokens` was removed; no output-budget or thinking override is sent. Both JSON and regex-fallback outputs are trimmed, require a non-empty name, and reject name >200 or note >2000 characters.
- Server: `src/BoxWise.Server/Services/LlmConfigKeyPolicy.cs` is the single origin rule; `src/BoxWise.Server/Services/LlmConfigWriter.cs` is the single writer used by both `PUT /api/llm/config` and the Admin Razor Page. A supplied Key always wins; a blank Key is retained only when the URL text is unchanged (legacy invalid values allowed) or both URLs prove the same scheme, host and effective port. Existing rows are changed by one conditional `UPDATE` whose predicate pins the `BaseUrl` and `ApiKey` snapshot used for the origin check; URL and Key are set in the same statement. If either changed after the snapshot, the update affects zero rows, the writer re-reads and re-checks instead of writing a stale pair. Duplicate inserts are detected the same way. Rejections/mutations are atomic per statement; no schema migration or transaction isolation dependency was added.
- Conflict handling: the writer retries bounded times (4) and then returns `Conflict`; the API returns 409 with a generic message and the Admin page shows the same generic message. No Key, URL or model value is included in conflict responses/messages.
- Docs/help updated in `LlmConfig.cshtml`, `README.md`, `docs/deployment-guide.md`, `docs/integration-architecture.md`, `docs/api-contracts-server.md`: root vs full URI, Bearer-only auth, HTTPS/mixed-content and CORS requirements (Origin, POST, `authorization`, `content-type`), Key replacement/rotation, DeepSeek official URL + `deepseek-flash`, and provider defaults affecting latency/cost. Also corrected stale ARK-only wording in `docs/architecture-server.md`, `docs/component-inventory-client.md` and `docs/source-tree-analysis.md`.
- Verification: `dotnet build BoxWise.slnx -c Release` = 0 warnings/0 errors. `dotnet test BoxWise.slnx -c Release` = Client 120 passed / Server 362 passed, 0 failed, 0 skipped.
- Matrix coverage: M1 Ark root/bare/lookalikes and M2 DeepSeek root/full are in `AiServiceTests.RecognizeAsync_ResolvesConfiguredEndpoint` / `RecognizeAsync_DocumentedConfigurations_ReturnResultFromSinglePost`; M3 gateway prefix/trailing slash/query in the same theory; M4 payload assertions in `RecognizeAsync_SendsCorrectPayload`; M5 invalid URLs in `RecognizeAsync_InvalidBaseUrl_ReturnsNullWithoutOutboundRequest`; M6 output lengths/regex fallback and the existing HTTP/network/timeout/empty-content tests; M7–M8 in `LlmConfigKeyPolicyTests`, `LlmConfigEndpointsTests` and `LlmConfigModelTests`.
- Concurrency regression: `src/BoxWise.Server.Tests/SqliteTestDatabase.cs` creates an isolated temporary SQLite database; `ConcurrentUpdateInterceptor` runs an independent-context write immediately before the writer's first UPDATE, and `ConcurrentInsertInterceptor` does the same before the first insert. `LlmConfigWriterTests` proves a stale snapshot after a concurrent origin+Key change either rejects the blank-Key write (concurrent pair preserved) or, with a supplied Key, persists the request URL+Key as one coherent pair. Persistent conflicts return the generic `Conflict` status. Equivalent interceptor tests run through the API endpoint and the Admin page model, demonstrating both entry points use the shared writer. All inputs are fake; no real data or credentials.
- CORS preflight evidence: `OPTIONS https://api.deepseek.com/chat/completions` with `Origin: https://boxwise.example.com`, requested method `POST`, requested headers `authorization,content-type` returned `200 OK`, `access-control-allow-origin: https://boxwise.example.com`, `access-control-allow-methods: POST`, `access-control-allow-headers: authorization,content-type`. This is preflight evidence only; no paid inference call was made.
- Account-based recognition remains unverified: no live API Key / paid API call was used, per plan boundaries.

## Plan Change Log

## Review Triage Log

- `false / reject` (Quick, `LlmConfigWriter.Matches`): an identical request is a read-only no-op, complete at the snapshot read. Its response is coherent at that point; a later overlapping save can supersede any past response, but the no-op cannot write a stale URL or Key. Only writes need the conditional update.
- `low / patch` (Quick, Key-required message and help): legacy URLs or changed URLs whose origin cannot be proved also fail the Key guard; unchanged URL (including a model/timeout-only edit) retains the Key. Align the message and help with both cases.
- `low / patch` (Quick, `docs/source-tree-analysis.md`): the LLM inventory omits the new writer, key policy and regression files; add those entries and avoid an outdated service-file count.

- `high / patch`：M7–M8 审计指出，普通已有行更新只对读取快照做校验，随后分别写入字段，没有在保存时证明快照仍为当前值；重复插入重试不足以覆盖。已由 `LlmConfigWriter` 统一两条写入路径，改为在单条 SQLite `UPDATE` 中同时以 `BaseUrl`+`ApiKey` 旧值为谓词并写入 URL+Key，冲突时重新读取重校验；新增独立上下文 + 临时 SQLite 的确定性并发回归，覆盖 API、Admin Page 和共享 writer；持续冲突返回 409/页面通用提示，不泄露 Key、URL 或模型值。冻结 Intent/边界/矩阵未修改。

## Verification

**Commands (.NET 10, project audit source, isolated test data):**
- `dotnet build BoxWise.slnx -c Release` — zero warnings/errors.
- `dotnet test BoxWise.slnx -c Release --no-build` — zero failures; record M1–M8 coverage.

**Results:**
- Build: 0 warnings, 0 errors.
- Tests: BoxWise.Client.Tests 120 passed; BoxWise.Server.Tests 362 passed; 0 failed, 0 skipped.
- Independent post-review run (.NET SDK 10.0.404, isolated test data): NuGet audit restore passed; Release build had zero warnings/errors; all 482 tests passed with none skipped. Real-account recognition was not run.
- M1–M8 coverage recorded in Implementation Notes.

**Other checks (record in Implementation Notes):**
- Check Admin help/docs against the matrix. DeepSeek OPTIONS must allow the tested Origin, POST, `authorization` and `content-type`; an OPTIONS response is preflight evidence only. Record account-based recognition as unverified unless separately authorized and exercised.
