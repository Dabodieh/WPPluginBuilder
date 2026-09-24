# Admin credit management + audit log completion

Adds ledger-backed admin credit adjustment, an immutable admin audit log,
account lock/unlock, and real credit analytics. No Stripe, no direct
mutable-balance write anywhere.

## Files added

- `src/WPAIPlugin.Api/Data/AdminAuditLog.cs` — the immutable audit model.
- `src/WPAIPlugin.Api/Security/AdminAuditService.cs` — writes audit rows.
- `src/WPAIPlugin.Api/Migrations/20260922231425_AddAdminAuditLogAndAdminAdjustment.{cs,Designer.cs}`.
- `tests/WPAIPlugin.Generator.Tests/Admin/AdminCreditsAuditTests.cs`.
- `ADMIN-CREDITS-AUDIT-COMPLETION.md` — this report.

(`src/WPAIPlugin.Api/AiUsage/`, `Controllers/AdminController.cs`, `Controllers/AdminDtos.cs`, `Data/AiUsageEvent.cs`, `Security/` (Admin auth files), `wwwroot/admin.{html,js,css}`, and the AI-usage migration are prior-milestone (Admin Panel Foundation) work, extended rather than re-added this session.)

## Files changed

- `src/WPAIPlugin.Api/Data/CreditTransaction.cs` — added `CreditTransactionType.AdminAdjustment`.
- `src/WPAIPlugin.Api/Credits/CreditOptions.cs` — added `MaxAdminAdjustmentMagnitude` (default 10,000).
- `src/WPAIPlugin.Api/Credits/CreditService.cs` — added `AdjustCreditsAsync`, reusing the existing optimistic-concurrency retry architecture; see "Concurrency behaviour" for a real bug found and fixed here.
- `src/WPAIPlugin.Api/Data/AppDbContext.cs` — `AdminAuditLogs` DbSet + indexes/FK.
- `src/WPAIPlugin.Api/Migrations/AppDbContextModelSnapshot.cs` — regenerated (additive only).
- `src/WPAIPlugin.Api/Program.cs` — registers `AdminAuditService`; extends the existing `CreditOptions` validation to also require `MaxAdminAdjustmentMagnitude > 0`.
- `src/WPAIPlugin.Api/appsettings.json` — `Credits:MaxAdminAdjustmentMagnitude`.
- `src/WPAIPlugin.Api/Controllers/AdminController.cs` — adds `POST users/{id}/credits/adjust`, `POST users/{id}/lock`, `POST users/{id}/unlock`, `GET credits` (analytics), `GET audit-log`; extends `UserDetail` with `RecentCreditActivity`; adds CSRF protection and a request-size limit to the whole controller (previously read-only, now needs both for its new mutations).
- `src/WPAIPlugin.Api/Controllers/AdminDtos.cs` — new request/response DTOs for the above.
- `src/WPAIPlugin.Api/wwwroot/admin.html`, `admin.js`, `admin.css` — Credits and Audit Log tabs; user-detail gains a ledger table, an Adjust Credits form, and a Lock/Unlock button.
- `tests/WPAIPlugin.Generator.Tests/Credits/CreditServiceTests.cs` — two new forced-overlap unit tests for `AdjustCreditsAsync` using the existing `CreditConcurrencyDatabase`/`OverlapInterceptor` pattern.

No files unrelated to this milestone were touched. All prior uncommitted milestone work remains exactly as it was.

## 1. Credit-adjustment architecture

`CreditService.AdjustCreditsAsync(userId, amount, idempotencyKey, cancellationToken)` — one signed operation (positive grants, negative deducts), reusing the exact optimistic-concurrency retry loop already proven by `TryChargeAsync`/`RefundAsync` (reload account, apply delta, bump `Version`, `SaveChangesAsync`, retry on conflict up to 3 attempts). There is no `Balance = X` write anywhere in the codebase — `AdjustCreditsAsync` only ever adds a signed delta to the currently-loaded balance and rejects the whole operation (`Success = false`) if that would take it below zero, exactly like every other credit-mutating method in this service.

`AdminController.AdjustCredits` is the only caller: validates amount is non-zero, `|amount| <= Credits:MaxAdminAdjustmentMagnitude`, reason is non-blank, and `idempotencyKey != Guid.Empty`, then delegates entirely to `CreditService` — the controller never touches `CreditAccount.Balance` directly.

## 2. Ledger behaviour

Every adjustment produces one immutable `CreditTransaction` row: `Type = "AdminAdjustment"`, `Amount` = the signed delta, `Reference = "admin:<idempotencyKey>"` (a server-controlled GUID string — never an email, reason text, API key, or path, as required). The reason text lives only in the audit log's `Description`, never in the ledger. `CreditAccount.Balance` remains the fast, concurrency-safe cache of the ledger's running total, same as every other credit-affecting code path in this app.

## 3. Concurrency behaviour

**A real bug was found and fixed here**, not merely theorized. `AdjustCreditsAsync`'s first implementation matched `TryChargeAsync`'s pattern exactly (retry on `DbUpdateConcurrencyException` by clearing the change tracker and looping), but a forced-overlap test (`CreditServiceTests.OverlappingAdjustments_ApplyExactlyOnce`, `Adjustment_SameIdempotencyKey_AppliesExactlyOnceEvenWhenOverlapping`) exposed that EF Core InMemory's lack of transaction rollback (already documented in this codebase's `CreditConcurrencyDatabase` comment) let a losing writer's ledger-row insert survive even though its account-update conflict threw — so a bare retry re-added a second ledger row with the same idempotency-derived reference, producing a duplicate write and an inflated balance. Fixed by checking `HasAdminAdjustmentAsync(reference, ...)` inside **both** the `DbUpdateConcurrencyException` and `DbUpdateException` catch branches before the loop retries — if the reference's row already committed (from this call or a genuinely concurrent duplicate submission), the method returns the already-applied outcome instead of writing again. Verified via two forced-overlap unit tests using the project's own `SaveChangesInterceptor`-based barrier (the same technique `CreditServiceTests.OverlappingChargesOrRefunds_ApplyExactlyOnce` already used for charge/refund), which guarantees genuine overlap rather than merely parallel dispatch — real PostgreSQL's row locking makes this scenario even safer in production (fewer, shorter-lived conflicts), but the fix itself is provider-independent.

Requirements verified: balance never negative (`newBalance < 0` rejected before any write), ledger+balance stay consistent (both written in the same `SaveChangesAsync` unit), adjustment is transactional (Npgsql's own atomic `SaveChanges`, same as every other credit method), references are always server-generated, and concurrent adjustments (including duplicate submissions) remain correct under forced overlap.

## 4. Double-submit / idempotency behaviour

Design decision (asked explicitly, since a real double-submit guarantee needs some notion of "this is the same request retried," while the spec also requires server-generated references): the **client supplies a random idempotency key** (`crypto.randomUUID()`, generated once per Adjust-Credits form instance in `admin.js`), which the server folds into its own reference format (`admin:<key>`) and uses purely to detect a retried/duplicated submission — the client supplies only entropy, never a meaningful reference value, so "server generates references" still holds in substance. A duplicate submission with the same key returns the original outcome (`AlreadyApplied: true`, same balance) instead of adjusting twice. This is intentionally not Stripe-level (no separate idempotency-key table, no response caching/replay) — it is exactly the check needed for this one operation, reusing the ledger itself as the dedup store.

## 5. AdminAuditLog model

`AdminAuditLog`: `Id`, `AdminUserId` (FK to `AspNetUsers`, `Restrict`), `Action` (`AdminAuditAction` constants: `CreditAdjustment`, `AccountLocked`, `AccountUnlocked` — same const-class pattern as `CreditTransactionType`/`AiUsageOperationType`), `TargetType` (`AdminAuditTargetType.User` today — extensible for a future payment-refund action without a schema change, per the task's own forward note), `TargetId`, nullable `Description` (a short reason string, e.g. `"+25: Customer support compensation"` for adjustments, `null` for lock/unlock since no free-text reason was collected there), `CreatedAtUtc`. Never updated or deleted after creation. Answers who/what/which-account/when/why exactly as specified. No secrets, passwords, or API keys are ever written into `Description` — verified by code review (every call site passes either `null` or a short admin-typed reason string).

## 6. Audit UI/API

`GET /api/admin/audit-log?admin=&action=&targetId=&from=&to=` — up to 200 most-recent entries, each resolved to the admin's and (for `User` targets) the target's email for display, filterable by admin, action, target, and date range. `/admin` → **Audit Log** tab: a table (when, administrator, action, target, details) plus a target-user-ID filter. No edit/delete affordance exists anywhere in the UI or API — the log is read-only by construction (no `PUT`/`DELETE` route was ever added).

## 7. Credit analytics

`GET /api/admin/credits` — real aggregates only: total credits currently held, signup credits granted, admin adjustments (net/granted/deducted, split out per the task's request), gross credits consumed, refunds, net credits consumed, standard- vs validated-build usage. `purchasedCredits` is always `0` with an explicit "Stripe not enabled" label in the UI — never fabricated, ready for Stripe to populate later without an API shape change. `/admin` → new **Credits** tab renders this as a stat grid, same visual pattern as Overview/AI Usage.

## 8. User-detail changes

`GET /api/admin/users/{id}` gained `recentCreditActivity` (up to 20 most-recent ledger rows: type, amount, timestamp — no transaction IDs or internal references exposed). The admin user-detail page now shows: a **Lock/Unlock account** button (hidden for admin accounts — an admin cannot lock another admin from this UI, since nothing in the task asked for cross-admin account management and doing so would be a meaningful, unrequested policy decision), a recent-credit-activity table, and the **Adjust Credits** form (amount, reason, Apply adjustment) directly below it. No internal database IDs are shown beyond what was already exposed pre-existing (e.g. the user's own Identity ID, which the URL already requires to reach this page).

## 9. Lock/unlock result

ASP.NET Core Identity already supports this cleanly via `UserManager.SetLockoutEndDateAsync` (lock: `DateTimeOffset.MaxValue`; unlock: `null`) — no redesign needed, confirmed live. `POST /api/admin/users/{id}/lock` also force-enables `LockoutEnabled` if it was off (Identity's own opt-in flag), since an admin-initiated lock should always take effect regardless of the account's self-service lockout setting. Both actions are `AdminOnly`-gated, CSRF-protected, produce an audit entry, and the UI's `toggleLock` prompts a native `confirm()` before calling either endpoint. Neither the API nor the UI exposes a password hash, allows "login as user," or allows viewing/resetting a password — verified by code review (no such endpoint exists) and by the existing secret-scan test extended over this session's new response types.

## 10. Migration details

One new migration, `AddAdminAuditLogAndAdminAdjustment`: creates only the `AdminAuditLogs` table plus three indexes (`AdminUserId`, `CreatedAtUtc`, composite `TargetType+TargetId`) and its FK to `AspNetUsers`. `AdminAdjustment` itself required no schema change — `CreditTransaction.Type` is already a plain `string` column, so a new constant value needed no migration. Inspected manually before applying; touches no existing table. Applied live to the existing local PostgreSQL development database on top of all five prior migrations; all existing data (users, projects, credit ledger, AI usage events) was preserved.

## 11. Security result

- `AdminOnly` policy already covered every new endpoint (added to the existing class-level `[Authorize(Policy = AdminAuthorization.AdminOnlyPolicy)]`) — verified anonymous → 401, normal user → 403 on `credits/adjust`, `lock`, `audit-log`.
- Added `[ValidateAntiForgeryToken]` to the three new POST actions and a `[RequestSizeLimit(16 * 1024)]` to the whole controller — real gaps closed this session, since `AdminController` was read-only (GET-only) before and had neither.
- Confirmed the browser cannot set the ledger transaction type or reference: `AdminCreditAdjustmentRequest` has no such fields, and a request body containing extra `type`/`reference` fields is silently ignored by model binding, verified by test (`ServerGeneratesReference_BrowserCannotSetTransactionType`).
- Confirmed normal users cannot call `POST credits/adjust`, `POST lock`, `GET audit-log` (403), and anonymous requests get 401.
- Confirmed no admin response (including the new credit/audit/lock endpoints) contains a password hash, API key, or `sk-`-prefixed value.

## 12. Automated test total

**241/241 passing** (222 prior + 19 new: 5 adjustment validation cases (positive/negative/zero/missing-reason/over-magnitude), over-deduction blocked, server-owned reference, 2 authorization denials, sequential-accumulation (HTTP-level), 2 forced-overlap concurrency unit tests, credit-analytics reflection, lock+audit, unlock+audit, normal-user-forbidden-from-locking, audit-log filter-by-target, normal-user-forbidden-from-audit-log). `dotnet test` still requires no PostgreSQL, Docker, OpenAI, or internet.

One pre-existing, unrelated test (`PluginBuilderTests.Build_CleansUpTemporaryFiles`) failed once during a full-suite run immediately after manual live-server verification and passed cleanly both in isolation and on a subsequent full run — the same pre-existing environmental flake already noted in the prior session's completion report, not a regression from this session's changes. The concurrency-sensitive `CreditServiceTests`/`AdminCreditsAuditTests` subset was additionally re-run 3 times in a row with zero failures to confirm stability.

## 13. Manual verification

Performed live against the real local PostgreSQL, following the requested script exactly:

1. Logged in as the existing bootstrap admin (`admin-verify@example.com`) — confirmed `isAdmin: true`.
2. Located test customer `normal-verify@example.com` via `GET /api/admin/users?email=`.
3. Noted starting balance: **100**.
4. Granted **+25** with reason "Manual verification test grant" — response `{"amount":25,"balance":125,"alreadyApplied":false}`.
5. Confirmed new balance **125** via `GET /api/admin/users/{id}`.
6. Confirmed the ledger entry: `{"type":"AdminAdjustment","amount":25,...}` in `recentCreditActivity`.
7. Confirmed the audit record: `{"action":"CreditAdjustment",...,"description":"+25: Manual verification test grant"}` in `GET /api/admin/audit-log`.
8. Deducted **-10** with reason "Manual verification test deduction" — response `{"amount":-10,"balance":115,...}`.
9. Confirmed accounting: balance **115**, ledger and audit rows both present.
10. Attempted an over-deduction (**-10000**) — response `409 {"error":"Adjustment would reduce the balance below zero.","balance":115}`.
11. Confirmed it was blocked: balance unchanged at 115, no new ledger/audit row written for the rejected attempt.
12. Verified the normal user (`normal-verify@example.com`) could not access the controls: `POST credits/adjust` → 403, `GET audit-log` → 403.

Additionally verified end-to-end: locked the same test account (200), confirmed its own login then failed with 401 ("Invalid email or password" — Identity's standard lockout response), unlocked it (200), confirmed login then succeeded (200); both actions appeared in the audit log with the correct admin, action, and target. Confirmed `GET /api/admin/credits` reflected the net effect (`adminAdjustmentsNet: 15`, `adminAdjustmentsGranted: 25`, `adminAdjustmentsDeducted: 10`) and `purchasedCredits: 0`. Confirmed `admin.html`, `admin.js`, `admin.css` all serve `200`. No PostgreSQL row was manually edited at any point — every state change went through the running application's own API. Removed the `Admin:BootstrapEmail` user-secret and stopped the server afterward; the manually-adjusted `normal-verify@example.com` account (now balance 115, previously locked/unlocked) remains in the shared local development database, consistent with every prior milestone's manual-testing practice.

Not exercised: a full browser/DOM interaction with the Adjust Credits form or the Lock/Unlock confirm dialog (no browser automation available) — verified instead via direct HTTP calls to every endpoint the UI invokes, plus code review of `admin.js` against the same DOM/event patterns already proven live in the existing admin tabs.

## 14. Deliberately deferred

- Stripe/payments — untouched this session, as instructed.
- Cross-admin account locking — an admin cannot lock another admin's account from this UI/API (the lock button is hidden for admin targets); this is a conservative default, not an explicit task requirement, and can be revisited if a future session needs it.
- A dedicated idempotency-key table with response replay/expiry (Stripe-level) — explicitly out of scope per the task; the ledger-row dedup implemented here is the smallest reliable mechanism for this one operation.
- Bulk/batch credit adjustments — only a single-user, single-adjustment flow was requested.
- Audit log CSV/export — not requested; the API supports the same filters a future export feature would need without a schema change.

## git status --short

```
 M .gitignore
 M PLANS.md
 M README.md
 M scripts/Validate-GeneratedPlugin.ps1
 M src/WPAIPlugin.Api/Controllers/AccountController.cs
 M src/WPAIPlugin.Api/Controllers/PluginsController.cs
 M src/WPAIPlugin.Api/Controllers/ProjectsController.cs
 M src/WPAIPlugin.Api/Credits/CreditOptions.cs
 M src/WPAIPlugin.Api/Credits/CreditService.cs
 M src/WPAIPlugin.Api/Data/AppDbContext.cs
 M src/WPAIPlugin.Api/Data/CreditTransaction.cs
 M src/WPAIPlugin.Api/Migrations/AppDbContextModelSnapshot.cs
 M src/WPAIPlugin.Api/Program.cs
 M src/WPAIPlugin.Api/appsettings.json
 M src/WPAIPlugin.Api/wwwroot/app.js
 D src/WPAIPlugin.Api/wwwroot/auth.css
 M src/WPAIPlugin.Api/wwwroot/builder.html
 M src/WPAIPlugin.Api/wwwroot/dashboard.html
 M src/WPAIPlugin.Api/wwwroot/index.html
 M src/WPAIPlugin.Api/wwwroot/login.html
 M src/WPAIPlugin.Api/wwwroot/myplugins.html
 M src/WPAIPlugin.Api/wwwroot/plugin.html
 M src/WPAIPlugin.Api/wwwroot/register.html
 M src/WPAIPlugin.Planning/PlanningResult.cs
 M src/WPAIPlugin.Planning/PluginPlanResult.cs
 M src/WPAIPlugin.Planning/PluginPlanner.cs
 M src/WPAIPlugin.Planning/Providers/Anthropic/AnthropicPlanningProvider.cs
 M src/WPAIPlugin.Planning/Providers/OpenAI/OpenAIPlanningProvider.cs
 M tests/WPAIPlugin.Generator.Tests/Accounts/AccountControllerTests.cs
 M tests/WPAIPlugin.Generator.Tests/Accounts/AccountTestFactory.cs
 M tests/WPAIPlugin.Generator.Tests/Credits/CreditApiTests.cs
 M tests/WPAIPlugin.Generator.Tests/Credits/CreditServiceTests.cs
 M tests/WPAIPlugin.Generator.Tests/Planning/FakePlanningProvider.cs
 M tests/WPAIPlugin.Generator.Tests/Planning/FakePluginPlanner.cs
 M tests/WPAIPlugin.Generator.Tests/Planning/PluginsControllerPlanTests.cs
 M tests/WPAIPlugin.Generator.Tests/Projects/ProjectsControllerTests.cs
 M tests/WPAIPlugin.Generator.Tests/Projects/ProjectsTestFactory.cs
 M tests/WPAIPlugin.Generator.Tests/Security/ProviderKeyExposureTests.cs
 M tests/WPAIPlugin.Generator.Tests/Validation/PluginsControllerBuildValidatedTests.cs
 M tests/WPAIPlugin.Generator.Tests/WebAppTests.cs
?? .dockerignore
?? .env.example
?? ADMIN-AI-USAGE-COMPLETION.md
?? AUTHENTICATED-UI-COMPLETION.md
?? Dockerfile
?? HOMEPAGE-COMPLETION.md
?? MILESTONE13-COMPLETION.md
?? PRODUCTION-READINESS-COMPLETION.md
?? UI-POLISH-COMPLETION.md
?? docker/docker-compose.prod.example.yml
?? src/WPAIPlugin.Api/AiUsage/
?? src/WPAIPlugin.Api/Controllers/AdminController.cs
?? src/WPAIPlugin.Api/Controllers/AdminDtos.cs
?? src/WPAIPlugin.Api/Data/AdminAuditLog.cs
?? src/WPAIPlugin.Api/Data/AiUsageEvent.cs
?? src/WPAIPlugin.Api/Health/
?? src/WPAIPlugin.Api/Migrations/20260922224407_AddAiUsageEvents.Designer.cs
?? src/WPAIPlugin.Api/Migrations/20260922224407_AddAiUsageEvents.cs
?? src/WPAIPlugin.Api/Migrations/20260922231425_AddAdminAuditLogAndAdminAdjustment.Designer.cs
?? src/WPAIPlugin.Api/Migrations/20260922231425_AddAdminAuditLogAndAdminAdjustment.cs
?? src/WPAIPlugin.Api/Security/
?? src/WPAIPlugin.Api/wwwroot/admin.css
?? src/WPAIPlugin.Api/wwwroot/admin.html
?? src/WPAIPlugin.Api/wwwroot/admin.js
?? src/WPAIPlugin.Api/wwwroot/api.js
?? src/WPAIPlugin.Api/wwwroot/app.css
?? src/WPAIPlugin.Api/wwwroot/dashboard.js
?? src/WPAIPlugin.Api/wwwroot/index.js
?? src/WPAIPlugin.Api/wwwroot/login.js
?? src/WPAIPlugin.Api/wwwroot/myplugins.js
?? src/WPAIPlugin.Api/wwwroot/nav.js
?? src/WPAIPlugin.Api/wwwroot/plugin.js
?? src/WPAIPlugin.Api/wwwroot/register.js
?? src/WPAIPlugin.Api/wwwroot/site.css
?? src/WPAIPlugin.Planning/PlanningUsage.cs
?? tests/WPAIPlugin.Generator.Tests/Admin/
?? tests/WPAIPlugin.Generator.Tests/Security/CsrfClient.cs
?? tests/WPAIPlugin.Generator.Tests/Security/ProductionSecurityTests.cs
```

Everything under `src/WPAIPlugin.Api/Security/`, `AiUsage/`, `Health/`, and the top-level untracked completion reports/Docker files other than this session's own new additions (called out above) is unrelated prior-milestone work, preserved exactly as found.

No commit, push, or tag was performed.
