# Admin panel foundation + AI usage/cost tracking completion

Adds a real Admin authorization model, a read-only admin app shell backed by
new admin-only APIs, and immutable AI usage/cost tracking instrumented into
the existing planning path. No Stripe, no manual credit adjustment, no paid
credit packs, no customer-UI redesign.

## Files added

- `src/WPAIPlugin.Api/Security/AdminOptions.cs` — `Admin:BootstrapEmail` config.
- `src/WPAIPlugin.Api/Security/AdminAuthorization.cs` — `Admin` role name, `AdminOnly` policy name.
- `src/WPAIPlugin.Api/Security/AdminBootstrapper.cs` — one-time startup role grant.
- `src/WPAIPlugin.Api/AiUsage/AiPricingOptions.cs` — `AiPricing:{Anthropic,OpenAI}` server-side pricing.
- `src/WPAIPlugin.Api/AiUsage/AiUsageRecorder.cs` — writes `AiUsageEvent` rows, computes cost.
- `src/WPAIPlugin.Api/Data/AiUsageEvent.cs` — the immutable usage/cost model.
- `src/WPAIPlugin.Api/Controllers/AdminController.cs` — `/api/admin/*`.
- `src/WPAIPlugin.Api/Controllers/AdminDtos.cs` — admin response DTOs.
- `src/WPAIPlugin.Planning/PlanningUsage.cs` — vendor-neutral token usage shape.
- `src/WPAIPlugin.Api/wwwroot/admin.html`, `admin.js`, `admin.css` — admin UI.
- `src/WPAIPlugin.Api/Migrations/20260922224407_AddAiUsageEvents.{cs,Designer.cs}`.
- `tests/WPAIPlugin.Generator.Tests/Admin/AdminApiTests.cs`.
- `ADMIN-AI-USAGE-COMPLETION.md` — this report.

## Files changed

- `src/WPAIPlugin.Api/Program.cs` — `AdminOnly` policy, `AdminOptions`/`AiPricingOptions` registration, `AiUsageRecorder` DI, one-time `AdminBootstrapper.RunAsync` call after `app.Build()`.
- `src/WPAIPlugin.Api/appsettings.json` — `Admin:BootstrapEmail` (empty default) and `AiPricing:{Anthropic,OpenAI}` sections.
- `src/WPAIPlugin.Api/Data/AppDbContext.cs` — `AiUsageEvents` DbSet + indexes/FK.
- `src/WPAIPlugin.Api/Migrations/AppDbContextModelSnapshot.cs` — regenerated snapshot (additive only).
- `src/WPAIPlugin.Api/Controllers/AccountController.cs` — `GET /api/account/me` now also returns `isAdmin`.
- `src/WPAIPlugin.Api/Controllers/PluginsController.cs` — instruments `POST /api/plugins/plan` with `AiUsageRecorder`.
- `src/WPAIPlugin.Api/wwwroot/nav.js` — adds a gated "Admin" nav link, shown only when `isAdmin` is true.
- `src/WPAIPlugin.Planning/PlanningResult.cs`, `PluginPlanResult.cs`, `PluginPlanner.cs` — added `Usage`/`Provider` fields, threaded through unchanged.
- `src/WPAIPlugin.Planning/Providers/Anthropic/AnthropicPlanningProvider.cs`, `Providers/OpenAI/OpenAIPlanningProvider.cs` — parse the vendor `usage` object already present in both APIs' responses; no behavior change to planning itself.
- Test files updated only for new constructor parameters / new fields (`PluginsControllerPlanTests`, `PluginsControllerBuildValidatedTests`, `FakePluginPlanner`, `FakePlanningProvider`, `ProjectsTestFactory`) — no assertions changed.

No files unrelated to this milestone were touched. All prior uncommitted milestone work (UI polish, security, credits, production readiness) remains exactly as it was.

## 1. Admin authorization design

Real ASP.NET Core Identity roles, not a hard-coded email check. `AddIdentity<IdentityUser, IdentityRole>` already provisioned `RoleManager`/`AspNetRoles` tables since the first migration but nothing used them; this milestone is the first thing to. `AdminOnly` is an authorization policy (`RequireRole("Admin")`) applied via `[Authorize(Policy = AdminAuthorization.AdminOnlyPolicy)]` at the class level of `AdminController` — every action requires it, none is individually exempted. Anonymous requests get 401 (existing `OnRedirectToLogin` → 401 override, unchanged from Milestone 10); authenticated non-admin requests get 403 (existing `OnRedirectToAccessDenied` → 403 override) — verified both automatically and live (see Security result).

## 2. Admin bootstrap behaviour

User decision (asked explicitly, since AGENTS.md instructions required stopping for this): **config-driven seed email**. `Admin:BootstrapEmail` (empty by default, never checked in with a real value) is read once, in `AdminBootstrapper.RunAsync`, called once from `Program.cs` right before `app.Run()`. Behavior:

- If `Admin:BootstrapEmail` is blank, nothing happens (no role created, no grant attempted).
- Ensures the `Admin` role exists.
- If **any** user already holds `Admin`, does nothing — never re-grants, never nominates a second bootstrap user automatically.
- If no admin exists and the configured email matches a **registered** user, grants them `Admin`.
- If the configured email hasn't registered yet, does nothing this run — grant happens on a later startup after they register. No arbitrary user is ever silently nominated.

Verified live (see Manual verification): registering the bootstrap email first leaves them a normal user; only after the **next process restart** are they promoted, matching the documented behavior above.

Role claims are baked into the ASP.NET Identity auth cookie at sign-in time (standard `ClaimsPrincipalFactory` behavior) — a role granted mid-session takes effect on that user's **next login**, not immediately. This is unchanged framework behavior, not new complexity added here, and is what a real browser session would also experience; the admin test suite's re-authentication helper exists because of this, not to work around a bug.

## 3. Admin app shell

`admin.html`/`admin.js`/`admin.css`, same dark/mint visual language as the rest of the SaaS UI — reuses `app.css`'s existing CSS variables and `.panel`/`.stat-grid`/`.button` classes verbatim, adds only tab-strip and table styles in `admin.css`. No frontend framework, no npm dependency, same `apiFetch`/`nav.js` architecture as every other page. Tabs: Overview, Users, Plugins / Builds, AI Usage, Revenue, System — implemented client-side (one page, hash-free tab switching, lazy per-tab data loading). **Audit Log was intentionally not added** (asked and confirmed): no Part in this milestone defines an audit-log data source, and AGENTS.md prohibits placeholder pages — it will be added in a future phase once real audit-log data exists. `nav.js` shows an "Admin" link only when `GET /api/account/me` reports `isAdmin: true`; hidden for everyone else. Admin pages have no server-side static-file gate (same as every other authenticated page in this app — client-side redirect on a failed `/api/account/me`/`isAdmin` check) since real protection is enforced server-side on every `/api/admin/*` call via the `AdminOnly` policy.

## 4. Overview metrics

All real, queried live: total users, total credit balance, gross credits consumed, credits refunded, total plugin projects, total versions, standard vs. validated builds, AI request count/total tokens/estimated cost. **"Users registered recently" is reported as `0` / omitted from meaningful display**, not approximated — `IdentityUser` has no `CreatedAtUtc` column, and the task instructions explicitly said to report this "if account creation date genuinely exists." It does not, so no value is fabricated. No revenue, profit, purchase, active-user, or conversion metric was invented anywhere.

## 5. User administration

`GET /api/admin/users?email=` — case-insensitive substring search, capped at 200 rows, returns email/lock status/credit balance/plugin count/version count. `GET /api/admin/users/{id}` — adds `isAdmin`, AI request count/token totals/estimated cost, and up to 20 recent AI usage rows. Never returns password hashes, security stamps, concurrency stamps, or any other raw Identity column — only the fields listed in Part D. Verified live and by automated test that no response body contains `PasswordHash`, `ApiKey`, or an `sk-`-prefixed string.

## 6. Plugin/build administration

`GET /api/admin/plugins?userId=&validated=` — every project with its owning user's email, version count, and latest build's validated flag, filterable by user and validated status. Read-only; does not touch `ProjectsController`'s existing per-user-scoped, ownership-checked customer endpoints at all — admin access is a fully separate, explicitly `AdminOnly`-gated path.

## 7. System page

`GET /api/admin/system` — application version (assembly version), environment name, live database connectivity check (same `CanConnectAsync()` used by `/health/ready`), configured planning provider name and whether its key is non-blank (never the key itself), whether Docker validation is enabled (`Validation:Enabled` — the same flag `Build & Validate` itself checks; no new live `docker version` probe was added, since none existed to reuse and none was requested), and whether the artifact storage root is writable (a `Directory.CreateDirectory` probe). No secret, connection string, or key value is ever returned — verified live and by automated test.

## 8. AiUsageEvent model

Exactly as specified: `Id`, nullable `UserId` (FK to `AspNetUsers`, `Restrict` delete), `OperationType` (`"Plan"` today via `AiUsageOperationType` constant, same const-class pattern as `CreditTransactionType`), `Provider`, nullable `Model`, nullable `InputTokens`/`OutputTokens`/`TotalTokens`, nullable `EstimatedCostUsdMicros` (`long`, micro-dollars), `DurationMs`, `Succeeded`, nullable `FailureCategory` (a safe `PluginPlanFailureReason` name, never a raw exception message), `CreatedAtUtc`. No prompts, generated responses, API keys, or other secrets are ever stored in this table — verified by code review (the recorder never receives prompt/response content, only counts and metadata).

## 9. AI pricing configuration

`AiPricingOptions`, bound from `AiPricing:{Anthropic,OpenAI}:{Input,Output}UsdMicrosPerMillionTokens` — plain server-side configuration, same `IOptions<T>` pattern as `CreditOptions`. The browser has no way to influence pricing: `PlanPluginRequest` has no cost field, and even an unknown client-supplied field is silently ignored by model binding (verified by test). Seeded with each provider's public per-million-token pricing as of this session as a starting default — **explicitly documented here as this application's own estimate, not an authoritative billing figure**, per Part G.

## 10. Cost-calculation strategy

`AiUsageRecorder.EstimateCostUsdMicros` uses `long`/`decimal` arithmetic only — no `float`/`double` anywhere in the cost path. Cost is computed once, at the moment a usage event is recorded (`inputTokens * pricePerMillion / 1_000_000`, summed for input+output, rounded to the nearest whole micro-dollar), from whatever `AiPricingOptions` holds **at that instant** — never recalculated later, and no historical event is ever rewritten if pricing configuration subsequently changes. If either token count is unavailable, cost is `null` (unknown), never a fabricated value.

## 11. Provider instrumentation

Both `AnthropicPlanningProvider` and `OpenAIPlanningProvider` now parse the `usage` object their respective APIs already return alongside every successful response (`input_tokens`/`output_tokens` for Anthropic, `input_tokens`/`output_tokens`/`total_tokens` for OpenAI) into a new vendor-neutral `PlanningUsage` on `PlanningResult`. No new HTTP call, no new vendor SDK, no change to the request payload sent to either provider, no change to what plugin spec is produced — purely additive parsing of already-received response data. `PluginPlanner` copies `Usage` and the resolved provider's `Name` onto `PluginPlanResult` (one extra field each, no constructor/dependency change to `PluginPlanner` itself — it does not need database access, so recording happens one layer up).

`PluginsController.Plan` wraps the existing `_pluginPlanner.PlanAsync` call with a `Stopwatch` and:
- **On success**: records a `Succeeded=true` event with whatever tokens the provider actually reported (never fabricated — genuinely `null` when a provider omits them).
- **On `PluginPlanException`**: records a `Succeeded=false` event with a safe `FailureCategory` (the enum name) **except** for `InvalidInput` (no provider call was ever made) and `GeneratedSpecInvalid` (the provider call already succeeded — our own post-hoc spec validation failed, which is not an AI/provider failure) — those two reasons intentionally do not produce a failure usage event, so a validation rejection can never masquerade as a failed AI request or vice versa.

Planning remains free in credit terms — nothing here touches `CreditService`, `CreditOptions`, or any charge/refund path. Both AI usage writes use `CancellationToken.None` for the same reason the SaaS build path does (`ProjectsController.Build`): a client disconnect must not lose the accounting record for work that already happened.

## 12. AI admin reporting

`GET /api/admin/ai-usage?from=&to=&userId=&model=&provider=&succeeded=` — request/success/failure counts, input/output/total tokens, estimated cost, average cost/request, breakdowns by provider and by model, a daily time series, and up to 50 recent individual events (each with the requesting user's email, never their UserId alone without context). All aggregation is done with EF Core `SumAsync`/`GroupBy` translated to SQL where possible; the day-bucketing step materializes the filtered row set first (grouping by a client-computed `DateOnly` isn't SQL-translatable) — acceptable at this data volume and consistent with "keep queries efficient" without introducing a new time-series storage layer.

## 13. Per-user AI reporting

`GET /api/admin/users/{id}` includes AI request count, input/output/total tokens, estimated cost, and up to 20 recent AI usage rows for that user — never prompt or response content, since none is ever stored in `AiUsageEvent` to begin with.

## 14. Migration details

One new migration, `AddAiUsageEvents`, purely additive: creates the `AiUsageEvents` table plus three indexes (`UserId`, `CreatedAtUtc`, composite `Provider+Model`) and its FK to `AspNetUsers`. Inspected manually before applying — touches no existing table, column, or index. Applied live to the existing local PostgreSQL development database (`docker-compose.saas.yml`) on top of the four pre-existing migrations; all prior data (17 users, 6 plugin projects, existing credit ledger) was preserved and is reflected correctly in the live-verified `/api/admin/overview` response. `AspNetRoles`/`AspNetUserRoles` needed no new migration — they were created by the original `InitialIdentity` migration and were simply unused until this milestone.

## 15. Security result

- Verified automatically (`AdminApiTests`) and live: anonymous → 401, authenticated non-admin → 403, admin → 200, for every `/api/admin/*` route.
- Verified automatically and live that no admin response body contains a password hash, API key, or `sk-`-prefixed value.
- `GET /api/account/me` now also reports `isAdmin` — a boolean derived from the authenticated cookie's own role claim, not a new trust boundary (still requires `[Authorize]`, still scoped to the caller's own account).
- No existing customer-facing authorization, rate limit, CSRF, or ownership check was touched or weakened.

## 16. Automated test total

**222/222 passing** (205 existing + 17 new: 5 anonymous-denied, 1 normal-denied×5 theory cases counted as 5, 1 admin-allowed, 1 no-secrets, 1 isAdmin-flag, 2 usage-recording (success/failure), 1 cost-not-client-influenced, 1 normal-user-denied-ai-usage — exact count reconciled by the test run, not hand-tallied here). All pre-existing tests pass unchanged in assertions (only constructor-signature/new-field plumbing updated). `dotnet test` still requires no PostgreSQL, Docker, OpenAI, or internet — confirmed by running the full suite, which uses only `ProjectsTestFactory`'s InMemory database and `FakePlanningProvider`.

One pre-existing, unrelated test (`PluginBuilderTests.Build_CleansUpTemporaryFiles`) failed once during a run immediately after manual live-server verification (temp-directory timing) and passed cleanly both in isolation and on a subsequent full run — confirmed as a pre-existing environmental flake, not a regression from this milestone's changes.

## 17. Manual verification

Performed live against the real local PostgreSQL (`docker-compose.saas.yml`), not simulated:

- Applied the new migration (`dotnet ef database update`) — succeeded, preserved all existing data.
- Set `Admin:BootstrapEmail` via user-secrets, ran the app in Development, registered that email — confirmed `isAdmin: false` immediately after registration (bootstrap had already run before they existed).
- Restarted the app — confirmed the bootstrapper inserted an `AspNetUserRoles` row for that user (visible in the EF command log) without prompting or nominating anyone else.
- Logged that user in again — confirmed `GET /api/account/me` now reports `isAdmin: true`.
- Confirmed `GET /api/admin/{overview,users,plugins,ai-usage,system}` all return `200` with real data drawn from the actual existing 17 users / 6 projects / credit ledger.
- Confirmed `GET /api/admin/overview` for an anonymous request returns `401`, and for a freshly registered non-admin user returns `403`.
- Confirmed `GET /api/admin/users/{id}` for the bootstrap admin returns no password hash, API key, or secret-shaped value.
- Confirmed `admin.html`, `admin.js`, `admin.css` are served with `200`.
- Removed the `Admin:BootstrapEmail` user-secret and stopped the server afterward; no stray configuration was left behind. The manually-registered `admin-verify@example.com` and `normal-verify@example.com` test accounts remain in the shared local development database, consistent with every prior milestone's manual-testing practice (no balances or other users' data were altered).

Not exercised (same class of limitation as every prior milestone's manual pass): a full browser/DOM render of `admin.html` (no browser automation available in this environment) — verified instead via direct HTTP checks of every API call the page makes plus the served-file checks above, and by code review of `admin.js` against the same DOM patterns already proven live in `dashboard.js`/`myplugins.js`. A live Anthropic/OpenAI call was not made (no provider key configured in this environment) — token-usage parsing was verified by automated test against the fake provider and by code review against both vendors' documented response shapes; both providers' `usage` object was added defensively (nullable throughout) specifically because it was never observed live.

## 18. Deliberately deferred

- Stripe/payments — Revenue reports "Payments not enabled" only, exactly as instructed.
- Manual credit adjustment — no admin write endpoint for credits exists; `AdminController` is entirely read-only.
- Paid credit packs — untouched.
- Customer-facing UI — untouched; only `nav.js` gained a conditionally-hidden Admin link.
- Audit Log — omitted from navigation and API this phase (see Section 3); no audit-log data source exists yet.
- A live `docker version` availability probe for the System page — `Validation:Enabled` (the same flag the real `Build & Validate` path already checks) is reported instead, since adding a new live-probe method to `DockerPluginValidator` would be new behavior beyond what any Part explicitly required.

## git status --short

```
 M .gitignore
 M PLANS.md
 M README.md
 M scripts/Validate-GeneratedPlugin.ps1
 M src/WPAIPlugin.Api/Controllers/AccountController.cs
 M src/WPAIPlugin.Api/Controllers/PluginsController.cs
 M src/WPAIPlugin.Api/Controllers/ProjectsController.cs
 M src/WPAIPlugin.Api/Data/AppDbContext.cs
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
?? src/WPAIPlugin.Api/Data/AiUsageEvent.cs
?? src/WPAIPlugin.Api/Health/
?? src/WPAIPlugin.Api/Migrations/20260922224407_AddAiUsageEvents.Designer.cs
?? src/WPAIPlugin.Api/Migrations/20260922224407_AddAiUsageEvents.cs
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

Everything under `src/WPAIPlugin.Api/Security/`, `Health/`, and the top-level untracked completion reports/Docker files other than this milestone's own new additions (called out above) is unrelated prior-milestone (Production Readiness) work, preserved exactly as found.

No commit, push, or tag was performed.
