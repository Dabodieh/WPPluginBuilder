# Final launch readiness — ModuleMint (WPAIPlugin)

Closure audit of [LAUNCH-CHECKLIST.md](LAUNCH-CHECKLIST.md). This is not a
development milestone — no product feature was added, nothing was
redesigned, and no code was changed unless a genuine defect is called out
in §14 (none was found this pass). The goal is a precise statement of what
stands between the current repository and a real, safe production launch.

## 1. Current launch status

**The application is code-complete and test-verified for launch.** Every
technical piece required for a real launch exists and has automated
coverage: accounts, AI planning, deterministic plugin generation and
optional Docker validation, credits, free-build entitlements, promotions,
Stripe Checkout/webhook payments, admin panel (including Promotions and
Revenue), password recovery, transactional email, and the four legal/
support pages.

**Nothing has actually been launched.** Every remaining item is an owner
decision, an external-service action, or a deployment-time step — never a
missing line of code. No production domain exists, no live Stripe account
is connected, no real legal identity has been supplied, and no production
infrastructure has been provisioned. This audit found **zero code defects**
(§14) and changed no source file.

## 2. Completed technical work

- Production hardening: startup config validation, Data Protection
  persistence, reverse-proxy-aware forwarded headers, security headers/CSP,
  health endpoints, generic production error handling, Docker image.
- Public homepage, authenticated SaaS UI (dashboard/builder/My Plugins/
  plugin detail), UI polish.
- Admin panel: Overview, Users, Credits, Plugins/Builds, AI Usage, Revenue,
  Promotions, Audit Log, System.
- AI usage/cost tracking (server-side pricing, never client-influenced).
- Admin credit adjustment + immutable audit log.
- Stripe Checkout credit purchases, webhook-only credit grants, idempotent
  at both the event-ID and Purchase-status level.
- Revenue/financial analytics (Purchase-sourced, never derived from the
  credit ledger).
- Free-build entitlements (5 signup credits + 2 free standard builds,
  ledger-backed exactly like credits) and the promotions system
  (FreeBuilds/BonusCredits/PackPriceDiscount, checkout-time snapshot
  authoritative even if the promotion later changes).
- ModuleMint customer-facing branding (text/glyph only — no technical
  rename).
- Forgot/reset password (Identity's own token provider, 1-hour lifespan,
  generic response regardless of account existence, reset URL built
  server-side only).
- Resend transactional email (`ITransactionalEmailSender` seam, no-op
  fallback outside Development).
- Privacy, Terms, Refund Policy, and Support pages describing only real,
  current behaviour.

Evidence for each: `PRODUCTION-READINESS-COMPLETION.md`,
`ADMIN-AI-USAGE-COMPLETION.md`, `ADMIN-CREDITS-AUDIT-COMPLETION.md`,
`STRIPE-FINANCIAL-COMPLETION.md`, `GO-LIVE-READINESS-COMPLETION.md`,
`PROMOTIONS-FREE-BUILDS-COMPLETION.md`, `LAUNCH-BLOCKERS-COMPLETION.md`.

## 3. Remaining owner actions

None of these were invented or guessed — every one is a literal placeholder
or an explicitly-flagged assumption in the current codebase, found by
grepping the repository this session:

| Field | Current placeholder | Where it appears |
| --- | --- | --- |
| Legal operator identity | `[LEGAL OPERATOR NAME]` | `/privacy.html`, `/terms.html` |
| Governing jurisdiction | "Scotland", explicitly flagged as a working assumption | `/terms.html`, `/refunds.html` |
| Production domain | `https://example.com` | `.env.example` → `App__PublicBaseUrl` |
| Support email | `support@example.com` | `.env.example` → `Support__Email` |
| Sending email identity | `no-reply@example.com` / `ModuleMint` | `.env.example` → `Email__FromAddress`/`Email__FromName` |
| Business/registered address | Not present anywhere | Required only if legally applicable |
| Company registration number | Not present anywhere | Required only if legally applicable |
| VAT registration/details | Not present anywhere | Required only if legally applicable; billing UI deliberately states neither "VAT included" nor "VAT excluded" until this is defined |

Owner decisions needed, not placeholders: which real email becomes the
`Admin:BootstrapEmail` owner account; final approval of the £4.99/£9.99/
£19.99 GBP pack pricing (unchanged since Milestone 16, re-confirmed
correct this session); whether `Build & Validate` (which needs the host
Docker socket mounted — a real root-equivalent privilege tradeoff) is
offered in production.

## 4. Remaining external-service actions

- **Resend**: create an account, verify the real sending domain, generate a
  production API key.
- **Stripe**: activate the live account, obtain the live secret key,
  configure the production webhook endpoint and obtain its live signing
  secret (see §9).
- **DNS/domain registrar**: point the real production domain at the
  deployed infrastructure; obtain/renew the TLS certificate (direct Kestrel
  or reverse proxy, per README → "Reverse proxy / HTTPS").
- **PostgreSQL hosting**: provision a reachable production instance (self-
  hosted or managed).

## 5. Required production environment variables

All bind via ASP.NET Core's `Section__Key` convention. Never print or
commit a real secret value — the table below states *purpose* only.

### Application

| Variable | Required | Secret | Purpose |
| --- | --- | --- | --- |
| `ASPNETCORE_ENVIRONMENT` | Yes | No | Must be `Production` (or any non-Development value) to enable all hardening below |
| `App__PublicBaseUrl` | Yes | No | Only trusted source for building the password-reset URL — never a browser Host header |
| `Support__Email` | Yes | No | Shown on Support/Privacy/Terms/Refunds; used for billing/refund/account-access enquiries |
| `AllowedHosts` | No (default `*`) | No | ASP.NET Core host-header allow-list |

### Database

| Variable | Required | Secret | Purpose |
| --- | --- | --- | --- |
| `ConnectionStrings__DefaultConnection` | Yes | Yes (contains password) | PostgreSQL connection string |

### Identity / security

| Variable | Required | Secret | Purpose |
| --- | --- | --- | --- |
| `DataProtection__KeyRingPath` | No (default `App_Data/dataprotection-keys`) | No (but the key files themselves are sensitive) | Where Identity auth-cookie/CSRF encryption keys persist across restarts — must be a durable volume |
| `ForwardedHeaders__KnownProxies` | No (default empty = trust nothing) | No | IP addresses of trusted reverse proxies |
| `ForwardedHeaders__KnownNetworks` | No (default empty) | No | CIDR ranges of trusted reverse proxies |

### Storage

| Variable | Required | Secret | Purpose |
| --- | --- | --- | --- |
| `Artifacts__RootPath` | Yes | No | Where generated plugin ZIPs live — must be a durable volume outside `wwwroot` (startup enforces this) |
| `Validation__Enabled` | No (default `true`) | No | Whether `Build & Validate` is offered at all |
| `Validation__TimeoutSeconds` | No (default `120`) | No | Docker validation run timeout |

### AI provider (OpenAI / Anthropic)

| Variable | Required | Secret | Purpose |
| --- | --- | --- | --- |
| `Planning__DefaultProvider` | Yes | No | `openai` or `anthropic` |
| `Planning__OpenAI__ApiKey` | Yes if provider = openai | Yes | Provider API key |
| `Planning__Anthropic__ApiKey` | Yes if provider = anthropic | Yes | Provider API key |
| `Planning__OpenAI__Model` / `Planning__Anthropic__Model` | No (has defaults) | No | Model override |
| `AiPricing__*` | No (has defaults) | No | Server-side USD-per-million-token pricing used only for cost *estimates* in admin reporting |

### Email / Resend

| Variable | Required | Secret | Purpose |
| --- | --- | --- | --- |
| `Email__FromAddress` | Yes | No | From address on the password-reset email |
| `Email__FromName` | Yes | No | From display name (`ModuleMint`) |
| `Resend__ApiKey` | Yes | Yes | Sends the password-reset email; blank = safe no-op sender (Development only — required outside Development) |

### Stripe

| Variable | Required | Secret | Purpose |
| --- | --- | --- | --- |
| `Stripe__SecretKey` | No at startup (feature returns `503` until set) | Yes | Stripe API secret key |
| `Stripe__WebhookSecret` | No at startup | Yes | Verifies the `Stripe-Signature` header — the only path that ever grants purchase credits |
| `Stripe__PublicBaseUrl` | No at startup | No | Builds Checkout success/cancel redirect URLs |
| `CreditPacks__Packs__*` | No (has the approved GBP defaults) | No | Pack catalog — override only to change pricing |

### Admin bootstrap

| Variable | Required | Secret | Purpose |
| --- | --- | --- | --- |
| `Admin__BootstrapEmail` | No (default blank = no auto-promotion) | No | The one account auto-promoted to Admin on first startup after it registers |

### Credits

| Variable | Required | Secret | Purpose |
| --- | --- | --- | --- |
| `Credits__SignupGrant` | No (default `5`) | No | New-account signup credits |
| `Credits__StandardBuildCost` | No (default `1`) | No | Standard build cost |
| `Credits__ValidatedBuildCost` | No (default `2`) | No | Build & Validate cost |
| `Credits__MaxAdminAdjustmentMagnitude` | No (default `10000`) | No | Largest single admin credit adjustment |

### Promotions

| Variable | Required | Secret | Purpose |
| --- | --- | --- | --- |
| `Promotions__SignupFreeBuilds` | No (default `2`) | No | New-account free-build entitlements |

### Rate limiting

| Variable | Required | Secret | Purpose |
| --- | --- | --- | --- |
| `Security__PlanningPerMinute` | No (default `10`) | No | Per-user AI planning requests/min |
| `Security__BuildsPerMinute` | No (default `6`) | No | Per-user build requests/min |
| `Security__ValidatedBuildsPerMinute` | No (default `2`) | No | Per-user validated-build requests/min |
| `Security__AccountRequestsPerFiveMinutes` | No (default `10`) | No | Per-IP register/login attempts |
| `Security__CheckoutPerMinute` | No (default `6`) | No | Per-user checkout-session creation |
| `Security__PromoCodePerMinute` | No (default `10`) | No | Per-user promo-code resolve/redeem |
| `Security__PasswordRecoveryPerFiveMinutes` | No (default `5`) | No | Per-IP forgot/reset-password attempts |

**Hard production startup requirement** (`Program.cs` throws a generic
`InvalidOperationException` — never the missing value — if any are blank
outside Development): `ConnectionStrings__DefaultConnection`,
`Planning__DefaultProvider` + the selected provider's key,
`Artifacts__RootPath`, `App__PublicBaseUrl`, `Support__Email`,
`Email__FromAddress`, `Email__FromName`, `Resend__ApiKey`. Everything else
above has a safe default.

## 6. Production deployment order

1. Provision PostgreSQL, reachable from the app host.
2. Provision/mount persistent volumes for `Artifacts:RootPath` and
   `DataProtection:KeyRingPath`.
3. Configure the production environment (§5's required variables, at
   minimum).
4. Apply EF migrations: `dotnet ef database update --project src/WPAIPlugin.Api --startup-project src/WPAIPlugin.Api --connection "<production connection string>"`.
5. Deploy the application (Docker image or `dotnet publish` output).
6. Confirm `GET /health/live` then `GET /health/ready` both return `200`.
7. Register the intended owner account through the real, deployed app.
8. Restart the app once (so `AdminBootstrapper` promotes
   `Admin:BootstrapEmail` — see §7). Confirm `/admin` loads for that
   account.
9. Confirm `/admin` → System shows artifact storage writable, AI provider
   configured, and Transactional email configured = Yes.
10. Configure Resend (§8) and send a real test password-reset email to
    confirm delivery end-to-end.
11. When ready to accept real money: configure the Stripe LIVE webhook and
    secrets (§9) — Stripe stays in test mode until this step.
12. Perform exactly one controlled live transaction (§10), owner-executed.

## 7. Admin bootstrap runbook

Confirmed against the current `AdminBootstrapper`/`AdminOptions`/
`AdminAuthorization` implementation (unchanged this session — no flaw
found, nothing modified):

1. **Configure** `Admin:BootstrapEmail` to the intended owner's real email
   (before or after step 2 — order doesn't matter, see step 3).
2. **Register** that exact email through the normal public
   `/register.html` flow on the deployed instance.
3. **Restart** the application once. `AdminBootstrapper.RunAsync` runs once
   per startup: if `Admin:BootstrapEmail` is set, no admin currently exists,
   and that email matches a registered user, it is granted the `Admin`
   Identity role. It never nominates any other user and never re-grants
   once any admin exists.
4. **Verify**: log in as that account (role claims are baked into the
   Identity cookie at sign-in — a role granted mid-session only takes
   effect on the *next* login). Confirm `GET /api/account/me` reports
   `"isAdmin": true` and `/admin` loads.
5. **Verify admin routes**: confirm `/api/admin/overview`,
   `/api/admin/users`, `/api/admin/promotions`, `/api/admin/finance/revenue`
   all return `200` for this account and `401`/`403` for anonymous/normal
   users respectively (already covered by automated tests; spot-check live
   once).
6. **Afterward**: leaving `Admin:BootstrapEmail` configured is safe — the
   bootstrapper's own "do nothing once an admin exists" rule means it has
   no further effect. There is still no in-app way to promote a *second*
   admin (by design — no self-service privilege-escalation surface); doing
   so requires a direct, operator-performed database insert into
   `AspNetUserRoles`, documented in README → "Admin bootstrap".

## 8. Resend activation runbook

1. **[EXTERNAL, owner]** Create/sign in to a Resend account.
2. **[EXTERNAL, owner]** Choose the real sending domain (e.g.
   `mail.yourdomain.com` or the apex domain) and add/verify it in Resend
   (DNS records — SPF/DKIM — Resend's own dashboard walks through this).
3. **[EXTERNAL, owner]** Create a production API key in Resend.
4. **[DEPLOYMENT]** Set `Email__FromAddress` to a real address on the
   verified domain and `Email__FromName` (e.g. `ModuleMint`).
5. **[DEPLOYMENT]** Set `Resend__ApiKey` in production configuration only —
   never commit it, never place it in a static file or JavaScript.
6. **[DEPLOYMENT]** Restart/redeploy so the app resolves
   `ResendTransactionalEmailSender` in place of the no-op sender (confirm
   via `/admin` → System → "Transactional email configured" = Yes).
7. **[VERIFY]** Request a password reset for a real test account through
   `/forgot-password.html`.
8. **[VERIFY]** Receive the real email (subject "Reset your ModuleMint
   password") and open the reset link — confirm it points at the real
   production domain, not `localhost`.
9. **[VERIFY]** Set a new password through `/reset-password.html`.
10. **[VERIFY]** Confirm the old password no longer authenticates.
11. **[VERIFY]** Confirm the new password authenticates.

No production secret was used, requested, or placed in any source file
this session.

## 9. Stripe LIVE activation runbook

Stripe stays in test mode until every step below is completed by the
owner. **No live Stripe action was performed this session.**

1. **[EXTERNAL, owner]** Switch to (or fully configure) the Stripe live
   account — business details, payout bank account.
2. **[EXTERNAL, owner]** Obtain the live `sk_live_...` secret key.
3. **[DEPLOYMENT]** Set `Stripe__SecretKey` in production configuration
   only.
4. **[EXTERNAL, owner]** In the Stripe Dashboard, create the production
   webhook endpoint: `https://<real-domain>/api/payments/webhook`, events
   at minimum `checkout.session.completed`, `checkout.session.expired`,
   `payment_intent.payment_failed`.
5. **[EXTERNAL, owner]** Obtain that endpoint's live signing secret.
6. **[DEPLOYMENT]** Set `Stripe__WebhookSecret` in production configuration
   only.
7. **[DEPLOYMENT]** Set `Stripe__PublicBaseUrl` to the real production
   `https://` domain.
8. **[VERIFY]** Ensure the current packs are correct: `starter` 25 credits/
   £4.99, `builder` 75 credits/£9.99, `pro` 200 credits/£19.99, GBP only —
   confirmed unchanged and correct in `appsettings.json` this session.
9. **[VERIFY]** Confirm the reverse proxy (if any) forwards the webhook
   route's raw request body unmodified — Stripe's signature is computed
   over the exact bytes it sent; any proxy rewriting breaks verification.
10. **[OWNER, exactly once]** Perform one controlled Starter purchase with
    a real card — see §10 for the exact verification checklist.

## 10. Controlled first live purchase checklist

Use the **Starter** pack: £4.99 / 25 credits.

**Before purchase** (a freshly registered test account):
- [ ] Balance = 5 credits, 2 free builds.

**Immediately after** a successful Starter purchase (assuming no build
consumed in between):
- [ ] Balance = **30 credits** (5 + 25), free builds still **2**
  (purchases never touch free-build entitlements).

**Verify in the database / admin UI**:
- [ ] Exactly one `Purchase` row, `Status = Completed`.
- [ ] `Purchase.AmountMinor = 499`.
- [ ] `Purchase.Currency = "GBP"`.
- [ ] `Purchase.CreditsPurchased = 25`.
- [ ] Exactly one `CreditTransaction` with `Type = CreditPurchase`,
  `Amount = 25` (the purchase ledger grant) — not two.
- [ ] Re-deliver the same webhook event (Stripe's own retry, or manually
  from the Dashboard's webhook logs) and confirm **no duplicate** credit
  grant occurs (both idempotency layers — event-ID and Purchase-status —
  already covered by automated tests; this is the one live confirmation
  worth doing before trusting real traffic).
- [ ] `GET /api/payments/history` for that customer shows the purchase.
- [ ] `/admin` → Revenue shows gross revenue £4.99, 1 successful purchase,
  25 credits sold, for the correct date.

## 11. Backup / restore

The documented procedure (README → "Backup / restore") already covers all
three required targets — confirmed unchanged and still accurate this
session:

- **PostgreSQL** — `pg_dump`/`pg_restore`.
- **Generated plugin artifacts** (`Artifacts:RootPath`) — plain recursive
  file copy.
- **Data Protection keys** (`DataProtection:KeyRingPath`) — session-
  continuity only, not a security requirement if lost.

**Launch-day backup checklist**:
- [ ] Take a database + artifact-directory backup immediately before
  cutting over to production traffic (a known-good rollback point).
- [ ] Confirm the backup schedule (cron / platform snapshot feature) is
  actually active, not just documented.
- [ ] Database and artifact backups should be taken close together in time
  — a restore where one is newer than the other can leave a project/
  version row referencing a ZIP the restore doesn't have.

**Not performed this session** (same as the prior audit): a live
`pg_restore` rehearsal — no PostgreSQL/Docker was exercised for this
specific purpose this session, and the current local development database
was **not** destructively restored over to demonstrate it. Recommend a real
restore drill in a non-production environment before go-live.

## 12. Monitoring

Confirmed exact, unchanged health endpoints:

- `GET /health/live` — anonymous, no database access, confirms the process
  is alive.
- `GET /health/ready` — anonymous, additionally confirms PostgreSQL is
  reachable (`Database.CanConnectAsync()`), for orchestrators that gate
  traffic on readiness.

**What to watch after launch** (using existing logs/endpoints — no new
external monitoring platform was added or is recommended by this audit):

- Liveness/readiness — point your orchestrator's health checks at the two
  endpoints above.
- HTTP 5xx rate — production returns a generic `{"error": "..."}` body for
  any unhandled exception; watch your reverse proxy/hosting platform's own
  status-code metrics.
- Stripe webhook failures — a rejected webhook (bad/missing signature)
  returns `400` immediately with no persisted record (by design, to avoid
  a write-on-every-unauthenticated-request vector); watch Stripe's own
  Dashboard webhook-delivery log for repeated failures, which would
  indicate a misconfigured `Stripe__WebhookSecret` or a proxy mangling the
  raw body.
- AI provider failures — `/admin` → AI Usage shows failed-request counts/
  failure categories already.
- Artifact/storage failures — a disk-full or unwritable `Artifacts:RootPath`
  surfaces as a failed build with an automatic credit/free-build refund
  (existing behaviour); watch disk usage directly (§ "Storage" below) since
  nothing currently alerts proactively.
- Credit/refund failures — `/admin` → Audit Log and Credits tab show every
  admin adjustment and the aggregate refund total; a spike in `Refund`
  ledger entries is worth investigating.
- Disk usage — `Artifacts:RootPath` and the PostgreSQL data volume both
  grow without automatic pruning (documented, unchanged limitation);
  monitor at the infrastructure level.

## 13. Remaining legal placeholders

Restated from §3 for a single, scannable list — none of these were
invented:

- `[LEGAL OPERATOR NAME]` (`/privacy.html`, `/terms.html`)
- Governing jurisdiction "Scotland" — flagged working assumption, not
  confirmed (`/terms.html`, `/refunds.html`)
- `App__PublicBaseUrl` = `https://example.com` placeholder
- `Support__Email` = `support@example.com` placeholder
- `Email__FromAddress` = `no-reply@example.com` placeholder
- Business/registered address — absent, needed only if legally applicable
- Company registration number — absent, needed only if legally applicable
- VAT registration/details — absent; billing UI intentionally states
  neither "VAT included" nor "VAT excluded" until this is resolved

## 14. Actual defects discovered/fixed

**None.** This pass performed a targeted review of: production secret
handling (`Program.cs`'s startup validation and `appsettings.json`
defaults), admin authorization (`AdminAuthorization`/`AdminBootstrapper`/
`AdminOptions` — role-based, never a hard-coded email, single-use bootstrap
confirmed correct), password recovery (generic response, server-only reset-
URL construction, no token/URL ever logged — re-confirmed in
`AccountController`), the Stripe webhook (signature-required, idempotent,
never echoes payload/signature), checkout (server-resolved pricing/
promotion, ownership from the authenticated principal only), customer
purchase ownership (`PaymentsController.History` filters by the
authenticated `userId`), credit grants (`CreditService` — no direct
`Balance = X` write anywhere), free-build grants
(`BuildEntitlementService` — same ledger/concurrency pattern), promotion
grants (`PurchaseService.CompletePurchaseAsync` honours the Purchase
snapshot, per the already-completed corrective pass), reset URL generation
(`App:PublicBaseUrl` only, never a request header), CSP/CSRF (unchanged —
no `unsafe-inline`/`unsafe-eval`, `AutoValidateAntiforgeryToken` still
covers every mutating account/admin/payment route), and production
exception handling (generic bodies, no stack traces/secrets). Also grepped
the full repository for stray real-looking secrets (`sk_live`, `sk_test_`,
`whsec_`, `re_`) and for `TODO`/`FIXME`/`HACK`/`console.log` in the newer
`wwwroot` files — none found. No source file was changed as a result of
this review.

## 15. Exact targeted tests run, if any

**None.** Per this milestone's own instruction: "If no code defect is
found, run no automated test suite merely for ceremony." No source or
configuration file was modified this pass (only the two documentation
deliverables — this file and `LAUNCH-CHECKLIST.md`), so no `dotnet build`
was run either, per the same instruction ("a single `dotnet build` may be
used only if code/configuration files are modified"). `git diff --check`
was run against the two changed documentation files and is clean (aside
from this repository's existing, pre-existing LF/CRLF line-ending
warnings, unrelated to this pass).

The most recent genuine targeted-test evidence remains what the prior two
completion reports already recorded: 119/119
(`Accounts|Security|Admin|WebApp` — `LAUNCH-BLOCKERS-COMPLETION.md`) and
53/53 (`Promotions|Payments` — the promotion-snapshot corrective pass).
Nothing in this pass invalidates either result.

## 16. git status --short

```
 M .gitignore
 M PLANS.md
 M README.md
 M scripts/Validate-GeneratedPlugin.ps1
 M src/WPAIPlugin.Api/Controllers/AccountController.cs
 M src/WPAIPlugin.Api/Controllers/CreditsController.cs
 M src/WPAIPlugin.Api/Controllers/PluginsController.cs
 M src/WPAIPlugin.Api/Controllers/ProjectDtos.cs
 M src/WPAIPlugin.Api/Controllers/ProjectsController.cs
 M src/WPAIPlugin.Api/Credits/CreditOptions.cs
 M src/WPAIPlugin.Api/Credits/CreditService.cs
 M src/WPAIPlugin.Api/Data/AppDbContext.cs
 M src/WPAIPlugin.Api/Data/CreditAccount.cs
 M src/WPAIPlugin.Api/Data/CreditTransaction.cs
 M src/WPAIPlugin.Api/Migrations/AppDbContextModelSnapshot.cs
 M src/WPAIPlugin.Api/Program.cs
 M src/WPAIPlugin.Api/WPAIPlugin.Api.csproj
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
?? ADMIN-CREDITS-AUDIT-COMPLETION.md
?? AUTHENTICATED-UI-COMPLETION.md
?? Dockerfile
?? FINAL-LAUNCH-READINESS.md
?? GO-LIVE-READINESS-COMPLETION.md
?? HOMEPAGE-COMPLETION.md
?? LAUNCH-BLOCKERS-COMPLETION.md
?? LAUNCH-CHECKLIST.md
?? MILESTONE13-COMPLETION.md
?? PRODUCT-SPEC.md
?? PRODUCTION-READINESS-COMPLETION.md
?? PROMOTIONS-FREE-BUILDS-COMPLETION.md
?? STRIPE-FINANCIAL-COMPLETION.md
?? UI-POLISH-COMPLETION.md
?? docker/docker-compose.prod.example.yml
?? src/WPAIPlugin.Api/AiUsage/
?? src/WPAIPlugin.Api/Configuration/
?? src/WPAIPlugin.Api/Controllers/AdminController.cs
?? src/WPAIPlugin.Api/Controllers/AdminDtos.cs
?? src/WPAIPlugin.Api/Controllers/AdminFinanceController.cs
?? src/WPAIPlugin.Api/Controllers/AdminFinanceDtos.cs
?? src/WPAIPlugin.Api/Controllers/AdminPromotionsController.cs
?? src/WPAIPlugin.Api/Controllers/AdminPromotionsDtos.cs
?? src/WPAIPlugin.Api/Controllers/PaymentsController.cs
?? src/WPAIPlugin.Api/Controllers/PromotionsController.cs
?? src/WPAIPlugin.Api/Controllers/PublicConfigController.cs
?? src/WPAIPlugin.Api/Data/AdminAuditLog.cs
?? src/WPAIPlugin.Api/Data/AiUsageEvent.cs
?? src/WPAIPlugin.Api/Data/BuildEntitlementAccount.cs
?? src/WPAIPlugin.Api/Data/BuildEntitlementTransaction.cs
?? src/WPAIPlugin.Api/Data/ProcessedPaymentEvent.cs
?? src/WPAIPlugin.Api/Data/Promotion.cs
?? src/WPAIPlugin.Api/Data/PromotionRedemption.cs
?? src/WPAIPlugin.Api/Data/Purchase.cs
?? src/WPAIPlugin.Api/Email/
?? src/WPAIPlugin.Api/Entitlements/
?? src/WPAIPlugin.Api/Health/
?? src/WPAIPlugin.Api/Migrations/20260922224407_AddAiUsageEvents.Designer.cs
?? src/WPAIPlugin.Api/Migrations/20260922224407_AddAiUsageEvents.cs
?? src/WPAIPlugin.Api/Migrations/20260922231425_AddAdminAuditLogAndAdminAdjustment.Designer.cs
?? src/WPAIPlugin.Api/Migrations/20260922231425_AddAdminAuditLogAndAdminAdjustment.cs
?? src/WPAIPlugin.Api/Migrations/20260922233011_AddPurchasesAndProcessedPaymentEvents.Designer.cs
?? src/WPAIPlugin.Api/Migrations/20260922233011_AddPurchasesAndProcessedPaymentEvents.cs
?? src/WPAIPlugin.Api/Migrations/20260923233308_AddEntitlementsAndPromotions.Designer.cs
?? src/WPAIPlugin.Api/Migrations/20260923233308_AddEntitlementsAndPromotions.cs
?? src/WPAIPlugin.Api/Payments/
?? src/WPAIPlugin.Api/Promotions/
?? src/WPAIPlugin.Api/Security/
?? src/WPAIPlugin.Api/wwwroot/admin.css
?? src/WPAIPlugin.Api/wwwroot/admin.html
?? src/WPAIPlugin.Api/wwwroot/admin.js
?? src/WPAIPlugin.Api/wwwroot/api.js
?? src/WPAIPlugin.Api/wwwroot/app.css
?? src/WPAIPlugin.Api/wwwroot/billing.css
?? src/WPAIPlugin.Api/wwwroot/billing.html
?? src/WPAIPlugin.Api/wwwroot/billing.js
?? src/WPAIPlugin.Api/wwwroot/dashboard.js
?? src/WPAIPlugin.Api/wwwroot/forgot-password.html
?? src/WPAIPlugin.Api/wwwroot/forgot-password.js
?? src/WPAIPlugin.Api/wwwroot/index.js
?? src/WPAIPlugin.Api/wwwroot/legal.css
?? src/WPAIPlugin.Api/wwwroot/legal.js
?? src/WPAIPlugin.Api/wwwroot/login.js
?? src/WPAIPlugin.Api/wwwroot/myplugins.js
?? src/WPAIPlugin.Api/wwwroot/nav.js
?? src/WPAIPlugin.Api/wwwroot/plugin.js
?? src/WPAIPlugin.Api/wwwroot/privacy.html
?? src/WPAIPlugin.Api/wwwroot/refunds.html
?? src/WPAIPlugin.Api/wwwroot/register.js
?? src/WPAIPlugin.Api/wwwroot/reset-password.html
?? src/WPAIPlugin.Api/wwwroot/reset-password.js
?? src/WPAIPlugin.Api/wwwroot/site.css
?? src/WPAIPlugin.Api/wwwroot/support.html
?? src/WPAIPlugin.Api/wwwroot/terms.html
?? src/WPAIPlugin.Planning/PlanningUsage.cs
?? tests/WPAIPlugin.Generator.Tests/Accounts/FakeTransactionalEmailSender.cs
?? tests/WPAIPlugin.Generator.Tests/Accounts/PasswordRecoveryTests.cs
?? tests/WPAIPlugin.Generator.Tests/Admin/
?? tests/WPAIPlugin.Generator.Tests/Entitlements/
?? tests/WPAIPlugin.Generator.Tests/Payments/
?? tests/WPAIPlugin.Generator.Tests/Promotions/
?? tests/WPAIPlugin.Generator.Tests/Security/CsrfClient.cs
?? tests/WPAIPlugin.Generator.Tests/Security/ProductionSecurityTests.cs
```

No commit, push, or tag was performed.

---

## BLOCKING LAUNCH

1. Legal operator identity (`[LEGAL OPERATOR NAME]`).
2. Governing-jurisdiction confirmation (Scotland, or the correct one).
3. Legal/counsel review of Privacy/Terms/Refund Policy wording.
4. Real production domain + TLS.
5. Real, monitored support email (`Support__Email`).
6. Real Resend account, verified sending domain, production API key.
7. Business/registered address, company number, VAT details — only if
   legally applicable, but must be *decided* either way before launch.
8. Stripe live account activation, live secret key, live webhook +
   signing secret.
9. One owner-executed controlled live purchase, verified per §10.
10. A rehearsed `pg_restore` (not yet performed in any session).

## NON-BLOCKING AFTER LAUNCH

- A second admin account has no in-app self-service path (documented,
  intentional — a direct database operation when actually needed).
- No external monitoring platform beyond the existing health endpoints/
  admin reporting (deliberately out of scope per this and prior audits).
- `PaymentWebhookFailures` in admin operational analytics is honestly
  reported as always `0` (no persisted failure-counter table exists —
  documented limitation, not a defect).
- Generated-ZIP and PostgreSQL storage growth have no automatic pruning
  (documented, monitor manually).
- A load-balanced multi-instance deployment sharing the Data Protection
  key directory has not been set up or exercised (single-instance
  deployment is the documented, supported baseline).
