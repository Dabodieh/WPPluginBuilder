# Milestone 12 — Credit System recovery

Scope: finish credits only; preserve all pre-existing/uncommitted work and the two historical migrations. No payments or Milestone 13.

Starting verification: `dotnet build` passed (0 warnings/errors); `dotnet test` passed 157/157. Snapshot contains credits but no credit migration exists.

Reuse: AppDbContext, Identity UserManager, concrete CreditService, project build controller, filesystem artifact store, existing InMemory web factories and fake validator.

Implementation: restore the snapshot from AddPluginProjectsAndVersions target model; generate credit-only schema and one-time existing-user backfill. Add Identity FKs, options validation, bounded token retries, refund verification, transactional registration, and a single build compensation boundary. Render authoritative credits in existing UI.

Data/API/UI: CreditAccounts and immutable CreditTransactions only; GET /api/credits returns own balance and configured costs; project builds charge server-selected costs. Dashboard and builder fetch balances, never calculate them locally.

Security/privacy/audit: keep ledger references internal; no mutation endpoint; prevent duplicate grants/refunds; use request-independent refund token; clear failed pending project changes before refund. Preserve legacy anonymous deterministic harness route, keep it out of UI.

Research: Microsoft EF Core official concurrency and transaction docs confirm application-managed tokens with reload/retry and atomic relational SaveChanges. Registration needs an explicit transaction across Identity and grant saves; InMemory tests use compensation because they have no transactions.
Sources: https://learn.microsoft.com/en-us/ef/core/saving/concurrency and https://learn.microsoft.com/en-us/ef/core/saving/transactions.

Verification: focused InMemory tests including overlapping separate contexts, complete build/test suite, inspect migration SQL, apply to existing local PostgreSQL without reset, verify tables/history/backfill, exercise real OpenAI/Docker/account lifecycle where available. Review cancellation, failure and authorization boundaries.

Risks/rollback: InMemory does not provide relational transactions, so live schema checks supplement tests. Do not roll back a populated ledger destructively; back up database and prefer a forward fix. Refund exhaustion/database outage must surface failure rather than falsely claiming a refund. Historical migrations remain untouched.


Outcome: implemented Milestone 12 only. Final build 0 warnings/errors; tests 178/178; JavaScript syntax and whitespace clean; EF model matches snapshot. Existing PostgreSQL migrated preserving 4 users/2 projects/2 versions; 4 backfill grants verified. New live HTTP account reached 100 -> 99 -> 97, controlled artifact refund kept 97, login retained 97. Real Docker validation and both downloads passed. OpenAI planning returned unconfigured provider; browser runtime module missing. See MILESTONE12-COMPLETION.md for exact files and evidence. Final review completed; no commit/push/tag.


# Milestone 13 - Pre-payments security hardening

Scope: production build bypass closure, standard antiforgery, built-in rate limiting, body limits, cookie/header/configuration safety. No Stripe, schema changes, generation features or redesign.

Starting state: clean working tree at cff9daa; build 0 warnings/errors; 178/178 tests pass. Both legacy plugin build actions bypass credits. Customer UI already calls projects/build. Static pages contain inline scripts/styles.

Reuse: existing controllers, Identity cookies, CreditService, ownership-checked downloads, options and InMemory web factories. Remove both legacy actions from non-Development route discovery; preserve local script. Use ASP.NET antiforgery header obtained from a no-store account endpoint and a shared fetch helper; extract inline scripts for self-only script CSP. Set production Secure cookies and generic exception responses. Validate selected provider/database/artifact configuration without secret output.

Rate limits: built-in fixed windows, per-user planning/build and a tighter validated-build lease after model binding; IP partition for login/register. No distributed store. RequestSizeLimit on JSON actions and server body ceiling. No static rate limiting.

Research: official ASP.NET Core antiforgery, rate-limit and Kestrel documentation confirms GetAndStoreTokens/header + MVC validation, authentication-before-rate-limiter ordering, and RequestSizeLimit. Version: .NET/EF 8. No added packages.
https://learn.microsoft.com/en-us/aspnet/core/security/anti-request-forgery?view=aspnetcore-8.0
https://learn.microsoft.com/en-us/aspnet/core/performance/rate-limit?view=aspnetcore-8.0
https://learn.microsoft.com/en-us/aspnet/core/fundamentals/servers/kestrel/options?view=aspnetcore-8.0

Data/API/UI/security impact: no migrations; new GET antiforgery bootstrap, required CSRF header for browser POST actions, production legacy routes 404, bounded requests/rates, secure production cookie and self-only script policy. Existing page appearance preserved. No claims/ledger changes.

Verification: update existing HTTP tests to use real antiforgery exchange; focused production/CSRF/rate/size/header/secret tests; full build/test; local development validation harness; local Production HTTPS account/build/download with isolated safe configuration and existing DB. No external dependency in normal tests. Document unavailable manual steps precisely.

Risks/rollback: clients must fetch/send CSRF header; HTTPS required for production cookies; proxy deployments must supply trusted scheme/client IP separately. Per-process rate limits reset on restart. Rollback security changes only with production legacy paths still blocked. Update README and dedicated completion report with route matrix and limitations.

Milestone 13 outcome: complete within scope. Final build 0 warnings/errors; 201/201 tests; real Development harness passed. Live Production HTTPS legacy routes 404, anonymous SaaS 401, missing CSRF 400, secure cookies confirmed, build 100 to 99 with attachment download, limits 429 and oversized bodies 413. Planning used loopback fixture because OpenAI key is absent; browser runtime module missing. Full inventory and evidence: MILESTONE13-COMPLETION.md. No schema changes, commits, payments or next milestone.


# Public SaaS homepage

Scope: replace the authentication gate at `/` with a static public marketing page.
Reuse existing auth status and destination pages; isolated native CSS and the
existing index.js. No backend, schema, permission, credit, or generation changes.
The frontend design skill informed restrained typography, spacing and responsive
layout; explicit user requirements take precedence over framework suggestions.
No framework API or security configuration changed, so no new framework research
was needed. Current CSP inspected; all scripts/styles stay local and external.

Verification: build/test; focused homepage/navigation/secret assertions; actual
Edge browser checks at 1440, 1024, 768, 390 and 320 pixels; real local registration,
logout/login and homepage auth-aware links; JS errors and CSP inspection.
Rollback: restore only homepage HTML/JS and remove its isolated stylesheet.
Preserve all prior Milestone 13 work. Outcome and evidence: HOMEPAGE-COMPLETION.md.


# Milestone 16 — Stripe credit purchases, billing UI, revenue/financial analytics

Scope: Stripe Checkout credit purchases, customer billing UI, admin revenue,
AI-cost-vs-revenue, per-user economics, operational analytics with date
filters. No Stripe subscriptions, no automatic customer-initiated refunds.

Business decision gate: no credit pack names/prices/currency/refund policy
existed anywhere in the repo before this session, so implementation was
correctly blocked and the user was asked explicitly rather than inventing
values. Approved: packs starter (25cr/£4.99), builder (75cr/£9.99), pro
(200cr/£19.99); GBP only; signup grant changed from 100 (test-era) to 5
(configuration only, no historical CreditTransaction/migration touched);
refunds tracked as status only, no automatic credit clawback, any credit
correction is a separate explicit audited admin adjustment; idempotency via a
client-generated key the server folds into its own reference (client supplies
entropy only, never a meaningful value).

Reuse: existing CreditService ledger architecture (new AdjustCreditsAsync-
style GrantPurchaseCreditsAsync, same dedup-in-every-catch-branch pattern
proven necessary last session for EF InMemory's no-rollback quirk), existing
AdminAuditLog, existing AiUsageEvent for AI cost, existing options-validation
pattern for CreditPackOptions/StripeOptions, existing admin.html/js tab
architecture, existing apiFetch/nav.js/CSRF conventions.

New: Purchase and ProcessedPaymentEvent models; IPaymentGateway seam
(StripePaymentGateway wraps the Stripe.net SDK - the only file that
references it directly; FakePaymentGateway drives all tests) so `dotnet test`
never needs live Stripe/network; PaymentsController (checkout/webhook/
history/packs); AdminFinanceController (revenue/contribution/operational,
separate from AdminController to keep each controller's concern focused).

Data/API/UI: browser sends only PackId to checkout - price/currency/credits
always server-resolved from CreditPacks:Packs config, verified by test that a
forged amountMinor/credits in the request body has zero effect. Webhook
requires a verified Stripe-Signature; ProcessedPaymentEvents makes retries
idempotent at the event-ID level, Purchase.Status=Pending-only-once-completed
guards at the business level too (double defence, both proven by forced-
overlap and duplicate-delivery tests). Money is integer minor units
throughout (GBP pence) - no float/double anywhere in the payment or revenue
path, confirmed by an exact-decimal average-purchase assertion. Billing UI
(/billing.html) shows real packs/prices, redirects to Stripe-hosted Checkout,
never grants credits itself, re-reads the authoritative server balance after
return. Admin Revenue tab: real Purchase-sourced aggregates, Today/7-days/
30-days/this-month/all-time filters, a clearly-labelled "approximate
contribution estimate" (revenue vs. AI spend, kept in separate native units,
explicitly never called profit).

Research: Stripe.net 52.4.2 (latest stable) - Stripe Checkout Sessions API
and EventUtility.ConstructEvent for webhook signature verification, per
Stripe's own documented pattern. No other new dependency.

Verification: 266/266 automated tests (47 new: checkout/webhook/accounting/
admin-finance/authorization), full build/release-build/publish/Docker build,
git diff --check clean, JS syntax-checked. Real Stripe TEST MODE verification
performed against the user's own connected Stripe account (sk_test_ key,
`stripe listen` for real webhook forwarding): genuine Checkout Session
created via the real API, real webhook signature verification confirmed
(both a real event accepted and a tampered signature correctly rejected with
400), a real session expired via the Stripe API produced a real
checkout.session.expired webhook that correctly cancelled the Purchase with
zero credits granted. Full completion-to-credit-grant was not exercised live
(requires a browser filling Stripe's hosted test-card page, unavailable in
this environment) - covered instead by the automated FakePaymentGateway
suite exercising the identical PaymentsController/PurchaseService/
CreditService code path. All test-mode secrets removed from user-secrets
after verification; dev database/server left in a clean stopped state.

Risks/rollback: Stripe is not startup-required - checkout returns 503 until
Stripe:SecretKey/PublicBaseUrl are configured, so existing deployments are
unaffected until an operator opts in. Migration is additive-only (Purchases,
ProcessedPaymentEvents) - no existing table touched. Outcome and evidence:
STRIPE-FINANCIAL-COMPLETION.md.


# Milestone 17 - New-customer free builds + promotions

Scope: every new registration gets 2 free plugin builds (Promotions:SignupFreeBuilds)
alongside the existing 5 signup credits - a distinct, ledger-backed
entitlement, never a generic credit. A small promotions system (FreeBuilds,
BonusCredits, PackPriceDiscount) layers on top for limited-time deals and
promo codes. No product redesign, no weakened payment/credit accounting.

Free-build model: BuildEntitlementAccount/BuildEntitlementTransaction, an
exact structural mirror of CreditAccount/CreditTransaction - same
application-managed optimistic-concurrency pattern (load/check/update/
increment Version/SaveChanges/bounded retry), same immutable-ledger
guarantee. Existing accounts default to 0 free builds; no backfill
migration, no runtime fallback grants the current configured amount to an
old account. Build charging: server decides free-build-vs-credit, one
buildReference correlates the credit charge and free-build consumption for
refund-on-failure, standard-build-covered-by-free-build charges 0 credits,
validated-build-covered-by-free-build still charges 1 credit for validation.

Promotions: Promotion/PromotionRedemption models, server-computed state
(Draft/Scheduled/Active/Expired, never stored), at most one promotion per
purchase (explicit code beats automatic; highest Priority breaks automatic
ties, then CreatedAtUtc/Id), PackPriceDiscount computed in decimal (never
float/double, .5 rounds away from zero), BonusCredits granted as a separate
PromotionBonus ledger entry never merged into CreditPurchase. Purchase gained
a checkout-time commercial-terms snapshot (BaseAmountMinor/BonusCredits/
PromotionId/PromotionCodeSnapshot/PromotionNameSnapshot) - authoritative even
if the promotion later changes/expires; redemption limits are re-checked
(not the agreed price/credits) at webhook-completion time. FreeBuilds-type
promotions always require a code (no automatic trigger exists for that type)
and redeem immediately via a dedicated endpoint, independent of Checkout.

Admin: /admin -> Promotions (create/edit/enable/disable/duplicate - no hard
delete, ever; per-promotion reporting), audited via the existing
AdminAuditLog. Overview gained active-promotion/promotional-credit/
free-build-redemption counts.

Research: no new external API or package - reuses the existing
CreditService/PurchaseService/Stripe.net integration exactly. Rounding
verified against the milestone's own worked example (Builder £9.99, 25% off
-> £7.49) both in a unit test and live against real PostgreSQL.

Verification: 66 new targeted tests (Entitlements, Promotions, AdminPromotions)
plus the existing Accounts/Credits/Projects/Payments/Admin/Security suites
re-run and passing (177/177 combined targeted total) - full suite
deliberately not run, per this milestone's own instruction. Live-verified
against real PostgreSQL: new registration -> 5 credits + 2 free builds;
standard free build -> 0 credits charged, 1 free build remaining; admin-
created automatic 25% Builder discount -> GET /api/payments/packs correctly
showed 749p (£7.49) for Builder only, unaffected Starter/Pro - then disabled
via the same admin API. Server log reviewed for the whole run - no secret.

Risks/rollback: MaxRedemptions/MaxRedemptionsPerUser are a best-effort,
documented limitation (counted, not a database constraint - a true N-per-
user limit isn't a plain unique index); the free-build concurrency guarantee
itself remains airtight. Migration is additive-only plus one backfilled
column (CreditAccounts.CreatedAtUtc, defaulted to DateTime.MinValue for
existing rows - an intentional "never new" sentinel) and one backfilled
Purchases column (BaseAmountMinor = AmountMinor for historical rows). No
existing table rewritten. Outcome and evidence:
PROMOTIONS-FREE-BUILDS-COMPLETION.md.


# Milestone 18 - ModuleMint branding + account recovery + legal/support launch blockers

Scope: customer-facing rebrand to ModuleMint (no technical rename), forgot/
reset password via ASP.NET Core Identity's own token provider, a small
Resend-backed transactional-email seam, and the four legal/support pages
(Privacy, Terms, Refunds, Support). No product features, no Stripe/credit/
promotion accounting changes.

Branding: text-only across every customer-facing page/title/email - internal
namespaces, project files, assemblies, table/migration names untouched.
Brand-mark glyph changed from "w+" to "M" (same box, same position, no
layout redesign).

Password recovery: no custom token crypto, no reset-token table, no raw
token persisted - UserManager.GeneratePasswordResetTokenAsync/
ResetPasswordAsync throughout, DataProtectionTokenProviderOptions.TokenLifespan
explicitly set to 1 hour. Reset URL built server-side from App:PublicBaseUrl
only - a spoofed Host header cannot redirect a real reset link. Forgot-
password always returns the identical generic response regardless of
account existence; only a real match triggers a token+email. Dedicated
IP-based passwordRecovery rate-limit policy, reusing the existing limiter
architecture. CSRF unchanged (existing AutoValidateAntiforgeryToken).

Transactional email: ITransactionalEmailSender seam (mirrors IPaymentGateway/
IPlanningProvider) - ResendTransactionalEmailSender is the only file
referencing the Resend SDK, selected in DI only when Resend:ApiKey is
configured; NoOpTransactionalEmailSender otherwise (never logs the token/
URL). Outside Development, App:PublicBaseUrl/Support:Email/Email:FromAddress/
Email:FromName/Resend:ApiKey are now hard startup requirements alongside the
existing database/provider/artifacts check - password reset is core account
security, not optional like Stripe. Tests use their own
FakeTransactionalEmailSender - no Resend/internet required anywhere.

Legal/support pages: Privacy, Terms, Refunds, Support describe only real,
current mechanics (credits/free-builds/promotions/Stripe/refunds already
documented in prior milestones) - no invented guarantees, no fabricated
company/legal details. Operator identity is the literal placeholder
[LEGAL OPERATOR NAME]; governing law is a flagged working assumption
(Scotland); production domain/support email/business address/company
number/VAT remain owner-supplied blockers, visibly marked as such on every
affected page and in LAUNCH-CHECKLIST.md.

Research: Resend .NET SDK confirmed via its own NuGet/docs pages this
session - package `Resend` 0.19.0, namespace `Resend`, `IResend`/
`ResendClient`/`ResendClientOptions.ApiToken`/`EmailMessage`
(From/To/Subject/TextBody/HtmlBody)/`EmailSendAsync` - verified for real by
a successful `dotnet build` against the actual package, not assumed.

Verification: 26 new targeted tests (PasswordRecoveryTests, WebAppTests
route/branding/link additions, AdminApiTests email-status) plus every
pre-existing targeted suite this milestone touched (Accounts, Security,
Admin) re-run and passing. `ProductionSecurityTests`'s shared Production()
helper and its missing-config theory both updated to cover the five new
required production keys - a real, necessary fix caught by running the
existing suite, not a workaround. Full suite deliberately not run, per this
milestone's own instruction.

Risks/rollback: no migration - password reset uses only Identity's existing
token-provider tables, no schema change at all. Owner-supplied legal/company
details, production domain, and governing-law confirmation remain explicit,
undischarged launch blockers - none invented. Outcome and evidence:
LAUNCH-BLOCKERS-COMPLETION.md.
