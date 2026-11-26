---
title: 'Make 2FA cleanup atomic'
type: bugfix
ticket: ''
created: '2026-11-26'
status: built
baseline_revision: '6371266ad9daf7c91476fd45f2940b7800cfa02f'
route: full
route_source: auto
risk: high
review: quick
review_source: pinned
lenses_ran: []
review_loop_iteration: 0
context: ['AGENTS.md', 'docs/identity-scaffold-modifications.md']
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** Admin reset and self-service disable can leave old recovery codes or authenticator secrets behind. Recovery-code disable can consume a code or persist part of the cleanup before a later write fails.

**Approach:** Run each cleanup in one SQLite transaction using the scoped `AppDbContext` shared with Identity. Inside it, reload the target, validate or redeem the code as applicable, clean up credentials and state, and rotate `SecurityStamp`.

## Boundaries & Constraints

**Always:** Disable 2FA; clear method flags, legacy active/pending TOTP, 2FA email/setup fields (not the account email) and both recovery stores. Preserve each entry's authorization, throttling and interface. Application and remembered-browser cookies follow existing stamp validation intervals.

**Never:** Regenerate Identity pages, change password login factors, passkey login, in-flight passkey enrollment or last-passkey deletion, add migrations, or log codes/secrets.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|---|---|---|---|
| A1 | Admin reset via API, page or CLI | Revoke passkeys present in the transaction; rotate the Identity authenticator key only if present. Clear grace period. | CLI: success JSON and exit `0` only after commit; failure exits nonzero. |
| A2 | Signed-in user disables 2FA | Preserve Identity authenticator key, passkeys and existing grace-period value. Refresh current sign-in only after commit. | No cookie refresh on failure. |
| A3 | Valid code from either store | Preserve Identity key and passkeys; set existing recovery grace period (+24h). After commit, clear pending 2FA cookie and redirect to password login without signing in. | Check Identity only if the custom code does not match. |
| A4 | Invalid code, conflict, or store failure | Distinguish invalid code, concurrent/SQLite write conflict and storage failure. Do not fall back on write failure or report success before commit. | Confirm rollback before saying the code was not used; show an unconfirmed outcome if commit status is unknown. |

</frozen-after-approval>

## Code Map

- `src/BoxWise.Server/Services/RecoveryCodeService.cs` — custom hash lookup and disable; `src/BoxWise.Server/Areas/Identity/Pages/Account/LoginWithRecoveryCode.cshtml.cs` currently redeems the Identity code outside cleanup.
- `src/BoxWise.Server/Program.cs` — scoped Identity EF registration; `ResetTwoFactorCli` duplicates admin reset. `EnableAuthenticator.cshtml.cs` creates 10 Identity codes after TOTP verification when count is zero.
- `src/BoxWise.Server.Tests/Endpoints/TwoFactorLoginE2ETests.cs` — real login/recovery routes; `src/BoxWise.Server.Tests/SqliteTestDatabase.cs` — isolated SQLite.

## Tasks & Acceptance

**Execution (in order):**
- [x] `src/BoxWise.Server/Services/RecoveryCodeService.cs` — implement A1–A4 with explicit outcomes and one encompassing transaction; Identity may save multiple times within it. Check every Identity write result; use `ResetAuthenticatorKeyAsync` for rotation and `GenerateNewTwoFactorRecoveryCodesAsync(user, 0)` to clear codes. Discard failed tracked changes.
- [x] `src/BoxWise.Server/Endpoints/AdminTwoFactorEndpoints.cs`, `src/BoxWise.Server/Pages/Admin/ResetTwoFactor.cshtml.cs`, `src/BoxWise.Server/Program.cs` — delegate A1. State in `src/BoxWise.Server/Pages/Admin/ResetTwoFactor.cshtml` that passwordless passkeys are revoked.
- [x] `src/BoxWise.Server/Areas/Identity/Pages/Account/Manage/Disable2fa.cshtml.cs` and `.cshtml` — delegate A2 and explain what stays or expires.
- [x] `src/BoxWise.Server/Areas/Identity/Pages/Account/LoginWithRecoveryCode.cshtml.cs` — use A3–A4 outcomes; preserve normalization, throttling and password relogin.
- [x] `src/BoxWise.Server.Tests/Endpoints/TwoFactorCleanupE2ETests.cs` and `src/BoxWise.Server.Tests/Endpoints/TwoFactorLoginE2ETests.cs` — verify A1–A4 and permissions on SQLite. Use a test-only `SaveChangesInterceptor` to fail after an earlier save, including after Identity redemption; reread both code stores, credential rows, user state and stamp through a new connection. Adapt affected service tests to SQLite.
- [x] `docs/identity-scaffold-modifications.md`, `docs/auth-security.md`, `docs/api-contracts-server.md`, `_bmad-output/initiative-boxwise/deferred-work.md` — document outcomes and close the admin reset, self-disable and partial-save entries only.

**Acceptance Criteria:**
- Given A1–A3 succeeded, when the user sets up TOTP again, then they receive new recovery codes and cannot use old ones; after A1 the previous authenticator secret is unusable.
- Given concurrent cleanups or an injected write failure, when requests conclude, then outcomes match persisted state and a confirmed rollback leaves the original recovery code usable.

## Implementation Notes

- The implementation agent left the changes uncommitted for independent verification.
- .NET 10.0.404: audited restore passed, Release build had 0 warnings and 0 errors, and all 494 tests passed. TRX confirmed A1–A4, including successful use of the original Identity code after rollback and new codes after TOTP re-enrollment.
- Isolated CLI: injected token-store failure exited `1` with the database unchanged; success exited `0` with both recovery-code stores and passkeys cleared and the security stamp and authenticator key rotated.

## Plan Change Log

## Review Triage Log

Main-agent diff review only, with no additional review agents. The revision removed the duplicate state snapshot, classified Identity redemption and SQLite transaction-start failures correctly, and checked the return value of clearing the Identity store.

## Verification

- Use .NET SDK 10.0.404 and the temp `boxwise-dependencies/NuGet.audit.config` under `%LOCALAPPDATA%/Temp/opencode` (packages `api.nuget.org`, audit `data.nuget.org`). Run `dotnet restore BoxWise.slnx --configfile <audit-config> -p:NuGetAudit=true -p:NuGetAuditMode=all`, `dotnet build BoxWise.slnx -c Release --no-restore`, and `dotnet test BoxWise.slnx -c Release --no-build --logger trx`. Require zero build warnings/errors and all tests passing.
- Verify CLI success and nonzero failure against temporary SQLite and an isolated data directory; check exit code, output and durable state.
