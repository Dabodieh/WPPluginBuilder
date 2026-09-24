# Stripe credit purchases + financial analytics completion

Adds Stripe Checkout credit purchases, a customer billing UI, admin revenue
reporting, combined AI-cost-vs-revenue analytics, per-user economics, and
operational analytics with date filters. No Stripe subscriptions, no
automatic customer-initiated refunds.

## 0. Business decision gate

Checked first, per the task's explicit instruction: no credit pack names,
credits-per-pack, GBP prices, currency policy, refund policy, or Stripe test
configuration existed anywhere in the repository (grepped `README.md`,
`PLANS.md`, all `*-COMPLETION.md` reports, `appsettings.json`). Correctly
stopped and asked rather than inventing values. Approved answers:

- **Packs**: `starter` (25 credits, £4.99 / 499p), `builder` (75 credits,
  £9.99 / 999p), `pro` (200 credits, £19.99 / 1999p).
- **Currency**: GBP only.
- **Signup grant**: changed from the prior test-era value of 100 to **5**
  (configuration only - see "Files changed" below; no historical
  `CreditTransaction` row or migration was touched).
- **Refund policy**: no automated customer-initiated refunds. A Stripe
  monetary refund is recorded on the local `Purchase` record
  (`RefundedAmountMinor`) for admin visibility only. It never automatically
  reduces a customer's credit balance. Any credit correction tied to a
  refund must be a separate, explicit, audited admin credit adjustment
  (reusing the existing `AdjustCreditsAsync`/audit-log machinery from the
  prior milestone).
- **Idempotency**: client-generated key, server folds it into its own
  server-controlled reference format - the client supplies entropy only,
  never a meaningful reference value.

## 1. Files added

- `src/WPAIPlugin.Api/Data/Purchase.cs` — the authoritative purchase model.
- `src/WPAIPlugin.Api/Data/ProcessedPaymentEvent.cs` — webhook idempotency table.
- `src/WPAIPlugin.Api/Payments/CreditPackOptions.cs` — server-side credit-pack catalog.
- `src/WPAIPlugin.Api/Payments/StripeOptions.cs` — Stripe secret/webhook-secret/base-URL configuration.
- `src/WPAIPlugin.Api/Payments/IPaymentGateway.cs` — vendor-neutral seam (mirrors the `IPlanningProvider` pattern).
- `src/WPAIPlugin.Api/Payments/StripePaymentGateway.cs` — the only file that references the Stripe.net SDK directly.
- `src/WPAIPlugin.Api/Payments/PurchaseService.cs` — pending/complete/cancel purchase lifecycle + webhook-event idempotency.
- `src/WPAIPlugin.Api/Controllers/PaymentsController.cs` — `/api/payments/{packs,checkout,webhook,history}`.
- `src/WPAIPlugin.Api/Controllers/AdminFinanceController.cs` — `/api/admin/finance/{revenue,contribution,operational}`.
- `src/WPAIPlugin.Api/Controllers/AdminFinanceDtos.cs` — response DTOs for the above.
- `src/WPAIPlugin.Api/Migrations/20260922233011_AddPurchasesAndProcessedPaymentEvents.{cs,Designer.cs}`.
- `src/WPAIPlugin.Api/wwwroot/billing.html`, `billing.js`, `billing.css` — customer Buy Credits / purchase history UI.
- `tests/WPAIPlugin.Generator.Tests/Payments/FakePaymentGateway.cs` — test double for `IPaymentGateway`.
- `tests/WPAIPlugin.Generator.Tests/Payments/PaymentsApiTests.cs` — checkout/webhook/accounting/history tests (18).
- `tests/WPAIPlugin.Generator.Tests/Payments/AdminFinanceApiTests.cs` — revenue/contribution/operational/per-user-economics tests (7).
- `STRIPE-FINANCIAL-COMPLETION.md` — this report.

(`src/WPAIPlugin.Api/Controllers/AdminController.cs`, `AdminDtos.cs`, and the `Admin`/`AiUsage`/`Security`/`Health` folders and their migrations are prior-milestone work, extended rather than re-added this session.)

## 2. Files changed

- `src/WPAIPlugin.Api/WPAIPlugin.Api.csproj` — added `Stripe.net 52.4.2` (latest stable; the only new dependency this session).
- `src/WPAIPlugin.Api/Credits/CreditService.cs` — added `GrantPurchaseCreditsAsync`, reusing `AdjustCreditsAsync`'s dedup-in-every-catch-branch pattern (see "Concurrency behaviour").
- `src/WPAIPlugin.Api/Data/CreditTransaction.cs` — added `CreditTransactionType.CreditPurchase`.
- `src/WPAIPlugin.Api/Data/AppDbContext.cs` — `Purchases`/`ProcessedPaymentEvents` DbSets, indexes, FK.
- `src/WPAIPlugin.Api/Migrations/AppDbContextModelSnapshot.cs` — regenerated (additive only).
- `src/WPAIPlugin.Api/Program.cs` — registers `StripeOptions`/`CreditPackOptions`/`IPaymentGateway`/`PurchaseService`.
- `src/WPAIPlugin.Api/appsettings.json` — `Credits:SignupGrant` 100→5, new `Stripe:*` (blank placeholders) and `CreditPacks:Packs` (the three approved packs) sections.
- `src/WPAIPlugin.Api/Controllers/AdminController.cs` — `UserDetail`/`CreditAnalytics` now report real per-user/aggregate purchase economics instead of hardcoded 0; class doc comment updated.
- `src/WPAIPlugin.Api/Controllers/AdminDtos.cs` — `AdminUserDetailResponse` gained lifetime purchase/revenue/refund/credits-purchased/credits-consumed/contribution-estimate fields.
- `src/WPAIPlugin.Api/wwwroot/nav.js` — adds a "Billing" nav link.
- `src/WPAIPlugin.Api/wwwroot/admin.html`, `admin.css`, `admin.js` — Revenue tab activated with real data, date-range filter buttons, Contribution and Operational sections; user-detail's Payments/Credits sections show real figures.
- `README.md` — new "Credit Purchases / Stripe" section; environment-variable table updated.
- `PLANS.md` — Milestone 16 entry appended.
- `.env.example` — Stripe/credit-pack placeholders added.
- `tests/WPAIPlugin.Generator.Tests/Projects/ProjectsTestFactory.cs` — pins `SignupGrant=100` for all existing tests (decoupling them from the new production default of 5), wires `FakePaymentGateway` and test `CreditPacks`/`Stripe` configuration into the shared DI pipeline. Other test files needed no signup-grant-related changes, since they either use explicit literal grant amounts or already go through this factory.

No files unrelated to this milestone were touched. All prior uncommitted milestone work remains exactly as it was.

## 3. Purchase model

`Purchase`: `Id`, `UserId` (FK, `Restrict`), `Provider` (`"Stripe"`),
`PackId`, `ProviderCheckoutSessionId` (unique), `ProviderPaymentIntentId`
(nullable, populated on completion), `Currency`, `AmountMinor`,
`CreditsPurchased`, `Status` (`Pending`/`Completed`/`Failed`/`Cancelled`),
`CreatedAtUtc`, `CompletedAtUtc` (nullable), `RefundedAmountMinor`. Created
`Pending` the instant a Checkout Session is created; only a verified webhook
can advance its status. `ProcessedPaymentEvent` (`ProviderEventId` primary
key, `EventType`, `ProcessedAtUtc`) makes webhook delivery idempotent at the
Stripe-event level, independent of the Purchase-status-level guard.

## 4. Money representation

Every money value in this milestone is an integer minor-currency-unit
(`int AmountMinor`, GBP pence — `499` = £4.99). No `float`/`double` appears
anywhere in the payment, revenue, or credit-pack code path. Where an average
or per-unit figure is computed (e.g. average purchase, revenue per customer),
the division is performed in `decimal` over the summed integer totals -
verified by a test asserting the exact rational `decimal` value rather than
an approximate float comparison (`Revenue_AggregatesCorrectly_NoFloatingPointErrors`).

## 5. Credit-pack configuration

`CreditPackOptions` (`CreditPacks:Packs`), a `Dictionary<string, CreditPack>`
keyed by the approved `PackId`s (`starter`/`builder`/`pro`), each with
`DisplayName`, `Credits`, `AmountMinor`, `Currency`. `PaymentsController.Checkout`
accepts only `{ PackId }` from the browser; price/currency/credits are always
looked up from this configuration by `PackId` alone. A request body
containing forged `amountMinor`/`price`/`credits` fields has zero effect,
since `CheckoutRequest` has no such properties and ASP.NET Core model binding
silently ignores unknown JSON fields - verified by test
(`Checkout_FakeBrowserPrice_Ignored`).

## 6. Checkout flow

`POST /api/payments/checkout` (authenticated, CSRF-protected): resolves the
pack, calls `IPaymentGateway.CreateCheckoutSessionAsync` (Stripe Checkout,
`mode: payment`, one line item built from server-side price data - never a
pre-created Stripe Price object, so pack changes need no Stripe-side sync),
then records a `Pending` `Purchase` via `PurchaseService.CreatePendingAsync`.
Returns only a `CheckoutUrl` for the browser to redirect to - Stripe hosts
the entire payment page; this app never handles a raw card number, stores
card details, or builds a custom card form. If `Stripe:SecretKey` or
`Stripe:PublicBaseUrl` is unconfigured, returns `503` cleanly (same pattern
as the existing `Build & Validate`-unavailable path) rather than crashing at
startup - Stripe is an optional feature, not a hard production requirement.
The success/cancel redirect URLs point to `/billing.html?checkout=success|cancelled`,
which only displays a notice and re-fetches the authoritative server
balance/history - it never grants credits itself.

## 7. Webhook verification

`POST /api/payments/webhook` (anonymous - Stripe cannot present a session
cookie or CSRF token - but every event is rejected unless its signature
verifies): reads the raw request body, calls
`IPaymentGateway.ParseWebhookEvent(payload, signatureHeader)`, which in the
real `StripePaymentGateway` calls `Stripe.EventUtility.ConstructEvent`
against the server-side `Stripe:WebhookSecret`. A missing or invalid
signature throws `PaymentGatewayException`, caught and turned into a bare
`400` with no payload/signature echoed back. **Verified against the real
Stripe SDK, live** (see Section 21): a genuine signed event was accepted, and
a deliberately tampered signature was rejected with `400` by the actual
`Stripe.net` verification code, not a mock.

## 8. Webhook idempotency

Two independent layers, both verified under forced/simulated concurrency:

1. **Event-ID level** — `PurchaseService.TryRecordEventAsync` inserts the
   Stripe event's own globally-unique ID into `ProcessedPaymentEvents`
   *before* acting on it; a duplicate delivery of the same event ID is
   recorded as already-processed and short-circuits to a plain `200` with no
   side effects (`Webhook_DuplicateEvent_GrantsOnce`).
2. **Purchase-status level** — `CompletePurchaseAsync` only grants credits
   for a `Pending` purchase; a second `checkout.session.completed` for an
   already-`Completed` purchase (e.g. under a *different* event ID, the
   out-of-order/at-least-once-delivery case Part 5 calls out) is a no-op
   (`Webhook_DifferentEventIdSameSession_StillGrantsOnce`).

`CreditService.GrantPurchaseCreditsAsync` reuses the exact retry-with-dedup
pattern the prior milestone's `AdjustCreditsAsync` needed after a real bug
was found there (EF Core InMemory commits a new ledger row before its
account-row concurrency check can fail, so a bare retry could double-insert)
- every catch branch re-checks for an existing ledger row with this
purchase's reference before retrying. Verified live under two truly
concurrent webhook deliveries for the same session
(`Accounting_ConcurrentIdenticalWebhookDeliveries_GrantOnce`).

## 9. Credit grant behaviour

`GrantPurchaseCreditsAsync(userId, amount, reference)` where
`reference = "purchase:<Purchase.Id>"` (server-generated, never derived from
user input) writes one `CreditTransaction` (`Type = CreditPurchase`,
positive `Amount`) and increases `CreditAccount.Balance` in the same
`SaveChangesAsync` call - there is no direct `Balance = X` write anywhere in
this codebase, in this milestone or any prior one. A successful purchase
grants credits at most once, enforced by both idempotency layers above.

## 10. Failed/cancelled payment behaviour

`checkout.session.expired` → `PurchaseService.MarkFailedOrCancelledAsync`
sets `Status = Cancelled` on the matching `Pending` purchase; no credits, no
ledger entry. `payment_intent.payment_failed` is recorded as a processed
event but leaves the `Pending` purchase untouched (Stripe does not include a
Checkout Session ID on a `payment_intent` event; the purchase is later
marked `Cancelled` if/when Stripe also sends `checkout.session.expired` for
the same session, its normal behavior for an abandoned Checkout). Neither
path ever grants a fake/optimistic balance - verified by test
(`Webhook_FailedPayment_GrantsZeroCredits`, `Webhook_CancelledCheckout_GrantsZeroCredits`)
and live (Section 21: a real session was expired via the Stripe API and the
buyer's balance was confirmed unchanged).

## 11. Refund behaviour

Exactly the approved policy: `Purchase.RefundedAmountMinor` is a plain
integer field an admin (or a future Stripe-refund-webhook handler, not built
this session since none was requested) can set to record a monetary refund.
`AdminFinanceController.Revenue` subtracts it from gross to compute net
revenue. **Nothing in this codebase automatically adjusts
`CreditAccount.Balance` in response to a refund** - verified by test
(`Revenue_RefundCalculation_NeverAutomaticallyNegative`: a purchase is
marked refunded directly, and the buyer's credit balance is asserted
unchanged). Any credit correction tied to a refund is a separate, explicit,
audited action via the existing admin credit-adjustment endpoint from the
prior milestone - deliberately not automated, per the approved policy.

## 12. Customer billing UI

`/billing.html` (new): current credit balance (always fetched fresh, never
cached/optimistic), the three configured packs with their real
server-provided price/credits, a "Buy now" button per pack that POSTs to
`/api/payments/checkout` and redirects the full page to the returned
Stripe-hosted URL, and a purchase-history table (date, pack, amount,
credits, status). After returning from Stripe (`?checkout=success` or
`?checkout=cancelled`), the page shows a plain notice and re-fetches balance
and history from the server - it never adds credits client-side. Added to
the shared nav (`nav.js`) as "Billing", same conditional-link pattern already
used for the Admin link.

## 13. Purchase history

`GET /api/payments/history` (authenticated): the caller's own purchases
only, ordered newest-first, capped at 100 rows. Verified that a second
user's purchase is never returned to the first (`History_OnlyShowsOwnPurchases`)
and that the endpoint requires authentication (`History_RequiresAuthentication`).

## 14. Revenue admin

`GET /api/admin/finance/revenue?from=&to=`: gross/refunded/net revenue
(GBP pence), successful vs. failed/cancelled purchase counts, credits sold,
distinct purchasing customers, average purchase (exact `decimal`), a
day-by-day breakdown, and up to 50 recent purchases with the buyer's email.
**Every figure is sourced from `Purchase` rows, never from
`CreditTransaction`/`CreditAccount`** - a purchased credit and cash revenue
are kept as genuinely separate concepts throughout (verified structurally:
`AdminFinanceController` never queries `CreditTransactions` for revenue, only
for the separate, pre-existing `AdminController.CreditAnalytics` endpoint).
`/admin` → **Revenue** tab renders this with Today/7-days/30-days/
this-month/all-time filter buttons (server-side date filtering via
`from`/`to` query parameters, aggregated in SQL where possible).

## 15. AI-cost-vs-revenue reporting

`GET /api/admin/finance/contribution`: combines `Purchase`-derived revenue
(GBP pence) with `AiUsageEvent`-derived estimated AI spend (USD micro-dollars,
reusing the exact same field the prior milestone already computes - no
redundant AI-cost system was built). The two currencies/units are always
shown **separately**, never combined into one number or silently converted
at an assumed exchange rate. Revenue/customer, AI-cost/customer,
revenue/build, AI-cost/build are each computed independently. The response
carries an explicit `Note` field stating the figures exclude Stripe fees
(genuinely unknown - not invented), hosting, and all other business costs,
and that **the result must never be called profit** - verified by a test
that scans every response property name for the substring "profit" (the
disclaimer text itself is allowed to use the word to explain why it isn't
one).

## 16. Per-user economics

`AdminUserDetailResponse` (existing endpoint, extended) now includes:
`LifetimePurchases`, `LifetimeRevenueMinor`, `LifetimeRefundedMinor`,
`PurchaseCurrency`, `CreditsPurchased`, `CreditsConsumed`, and
`ApproximateContributionMinor` (explicitly nullable/labelled, `revenue - refunded`
only, never called profit). The existing `RecentCreditActivity` ledger table
already showed `CreditPurchase` rows alongside every other transaction type
with no code change needed there. Verified live and by test
(`UserDetail_IncludesLifetimeEconomics`).

## 17. Operational analytics

`GET /api/admin/finance/operational?from=&to=`: plan generations/failures
(reusing `AiUsageEvent` with `OperationType = Plan` - no redundant tracking
system, per the task's explicit instruction), total/standard/validated
builds, refunded builds, credits consumed, AI requests/failures, purchases,
revenue, and `PaymentWebhookFailures` (honestly reported as always `0` - see
the field's own XML doc and the note below - never fabricated). All figures
are server-side date-filtered and aggregated. `Registrations` is `0`,
consistent with the prior milestone's already-documented, unchanged
limitation: ASP.NET Core Identity's own user table has no `CreatedAtUtc`
column to filter by.

**On `PaymentWebhookFailures = 0`**: a rejected webhook (bad/missing
signature, malformed payload) returns `400` immediately with no database
write - by design, since writing a row for an unauthenticated, unverified
request would itself be a minor DoS/storage vector. This session did not add
a persisted failure-counter table for that path, since doing so was not
explicitly requested and would be new infrastructure beyond this milestone's
scope; the metric is reported as a real, honestly-known `0` rather than an
invented non-zero figure.

## 18. Migration details

One new migration, `AddPurchasesAndProcessedPaymentEvents`: creates only
`Purchases` (with a unique index on `ProviderCheckoutSessionId` for
provider-payment lookup, an index on `UserId` for purchase-history queries,
and an index on `CreatedAtUtc` for date-range reporting) and
`ProcessedPaymentEvents` (primary-keyed directly on `ProviderEventId` - no
separate surrogate key or extra index needed for webhook-idempotency lookups,
since every query against that table is by that exact key). Inspected
manually before applying; touches no existing table, column, or index.
Applied live to the existing local PostgreSQL development database on top of
all six prior migrations; all existing data was preserved (confirmed - see
Section 21).

## 19. Security result

- `SecretKey`, `WebhookSecret` never appear in any controller response,
  client-side JavaScript, or log statement - verified by code review (the
  only place `StripeOptions` is read is inside `StripePaymentGateway` and
  `PaymentsController`'s `IsNullOrWhiteSpace` checks) and by test
  (`Webhook_SecretNeverAppearsInResponse`).
- No client-controlled pricing/credits: verified by test
  (`Checkout_FakeBrowserPrice_Ignored`, `Checkout_ServerDeterminesPriceFromPackId_NotFromBrowser`).
- No unauthenticated checkout: `[Authorize]` on `Checkout`/`History`/`Packs`,
  verified by test.
- No cross-user purchases: `Purchase.UserId` always comes from the
  authenticated principal, never the request body; verified by test
  (`Checkout_PurchaseOwnedByCorrectUser`, `History_OnlyShowsOwnPurchases`).
- CSRF preserved: `[ValidateAntiForgeryToken]` on `Checkout`
  (state-changing, authenticated); the webhook endpoint is deliberately
  `[AllowAnonymous]` with no CSRF check, since Stripe cannot present a CSRF
  token - its protection is the signature check instead, which is strictly
  stronger for this threat model.
- `AdminOnly` protection preserved on all new `AdminFinanceController`
  actions, verified live and by test (401 anonymous, 403 normal user).
- No sensitive webhook payload logging: the only log statement on the
  webhook path logs a failure *category* (`ex.GetType().Name`), never the
  payload, signature, or secret - matching the existing pattern already
  established for AI provider failures.
- No payment data beyond what is necessary: `Purchase` never stores a card
  number, cardholder name, or any other card-network data - Stripe Checkout
  never sends it back to this app at all.

## 20. Automated test total

**266/266 passing** (241 baseline this session + 25 new: 18 in
`PaymentsApiTests` covering checkout authentication/server-owned-pricing/
webhook-signature/webhook-idempotency/failed-cancelled-payments/accounting/
purchase-history, and 7 in `AdminFinanceApiTests` covering
authorization/revenue-aggregation/refund-calculation/no-floating-point-errors/
contribution/operational-date-filtering/per-user-economics). `dotnet test`
still requires no live Stripe, internet, PostgreSQL, Docker, or OpenAI - the
entire suite runs against `ProjectsTestFactory`'s InMemory database and the
new `FakePaymentGateway`.

One pre-existing, unrelated test (`PluginBuilderTests.Build_CleansUpTemporaryFiles`)
failed once during a full-suite run immediately after manual live-server
verification and passed cleanly both in isolation and on a subsequent full
run - the same pre-existing environmental flake already documented in the
two prior sessions' completion reports, not a regression from this session.

## 21. Stripe test-mode verification

Performed live against the user's own connected Stripe account (test mode
only - confirmed via `stripe config --list` showing `test_mode_api_key`;
live-mode keys were never read or used):

1. **Real Checkout Session created** via the actual Stripe API
   (`POST /api/payments/checkout` → a genuine `https://checkout.stripe.com/...`
   URL with a real `cs_test_...` session ID), confirmed by inspecting the
   resulting `Pending` `Purchase` row through the admin revenue API (correct
   £4.99/25 credits for the `starter` pack, correctly excluded from revenue
   totals while `Pending`).
2. **Real webhook signature verification confirmed both ways**: a tampered
   `Stripe-Signature` header was rejected with `400` by the actual
   `Stripe.net` `EventUtility.ConstructEvent` call (not a mock); a
   genuinely-signed event (via `stripe trigger checkout.session.completed`,
   forwarded through a real `stripe listen` process) was accepted, recorded
   in `ProcessedPaymentEvents`, and handled without error even though it
   referenced a different (Stripe-fixture-generated) session ID than my
   pending purchase - confirming the "unknown session" path degrades safely
   rather than crashing.
3. **Real cancelled-checkout flow, end to end**: the pending Checkout Session
   was expired via the real Stripe API (`stripe checkout sessions expire`),
   which caused Stripe to deliver a genuine, correctly-signed
   `checkout.session.expired` webhook through `stripe listen` to the running
   local server. The server correctly verified it, marked the `Purchase`
   `Cancelled`, and the buyer's credit balance was confirmed unchanged
   (`5` - only the signup grant) both before and after.
4. **Customer purchase history**: confirmed live, showing exactly the one
   `Pending`→`Cancelled` purchase for the test buyer and no other user's
   data.
5. **Admin revenue display**: confirmed live at each stage (`Pending`
   purchase excluded from revenue; after cancellation, still `0` successful
   purchases, `0` revenue - correct, since it was never completed).
6. **Logout/login balance persistence**: not re-verified this session (the
   underlying mechanism - server-authoritative `GET /api/credits`, no
   client-side balance storage - is unchanged from the prior milestone,
   where it was already verified live).
7. Migration applied live to the real local PostgreSQL database, preserving
   all prior data (users, projects, credits, AI usage, admin audit log).

All test-mode secrets (`Stripe:SecretKey`, `Stripe:WebhookSecret`,
`Stripe:PublicBaseUrl`) were removed from user-secrets immediately after
verification (`dotnet user-secrets remove` for each, confirmed empty via
`dotnet user-secrets list`); the dev server and `stripe listen` process were
both stopped. No value was ever written to a repository file.

## 22. Anything not manually verifiable

- **Full completion-to-credit-grant, end to end** (a real browser filling in
  Stripe's hosted test-card page, e.g. `4242 4242 4242 4242`, actually
  completing the Checkout Session, and Stripe delivering the corresponding
  real `checkout.session.completed` webhook for *that exact session*) was
  **not** exercised live - it requires browser automation, which is
  unavailable in this environment, and Stripe's Checkout Sessions API has no
  server-side "complete this session" call by design (payment completion is
  deliberately only possible through the hosted page or a real card token).
  This exact code path (webhook → `CompletePurchaseAsync` → credit grant) is
  instead fully covered by the automated `FakePaymentGateway` test suite,
  which exercises the identical `PaymentsController`/`PurchaseService`/
  `CreditService` methods the real Stripe webhook would invoke - the only
  difference is the source of the (already separately, live-verified)
  signature check.
- A real production HTTPS deployment behind a reverse proxy, with a
  Stripe-Dashboard-configured (rather than CLI-forwarded) webhook endpoint,
  was not stood up - same class of limitation already noted in the
  Production Readiness completion report for the rest of the application.
- Duplicate-webhook-delivery behavior was verified with the automated fake
  (two identical/near-identical deliveries) and reasoned about from the real,
  live-verified signature/idempotency-table mechanics, but a genuine
  duplicate delivery of the *same* real Stripe event ID (which Stripe itself
  only does on retry, not on demand) was not triggered live.

## 23. Anything deliberately deferred

- Stripe subscriptions - not requested; one-time credit-pack purchases only.
- Automatic customer-initiated refunds - explicitly deferred per the
  approved policy; refunds are admin/Stripe-Dashboard-initiated and recorded
  locally for visibility only.
- A persisted webhook-failure-counter table (for a non-zero
  `PaymentWebhookFailures` metric) - not built this session; see Section 17
  for the reasoning.
- Stripe fee data in revenue/contribution reporting - genuinely unavailable
  without either Stripe's fee-reporting API or a manual fee schedule, so
  omitted rather than invented, per the task's explicit instruction.
- Multi-currency support - GBP only, per the approved decision.
- A Stripe-hosted receipt/invoice link in purchase history - Stripe Checkout
  in `payment` mode does generate a receipt URL on the underlying charge,
  but surfacing it would need an additional API call per purchase (or
  storing it at webhook time); not built this session since it was marked
  conditional ("if safely available") and the core purchase/revenue flow
  already covers the explicitly-required fields.

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
?? HOMEPAGE-COMPLETION.md
?? MILESTONE13-COMPLETION.md
?? PRODUCTION-READINESS-COMPLETION.md
?? UI-POLISH-COMPLETION.md
?? docker/docker-compose.prod.example.yml
?? src/WPAIPlugin.Api/AiUsage/
?? src/WPAIPlugin.Api/Controllers/AdminController.cs
?? src/WPAIPlugin.Api/Controllers/AdminDtos.cs
?? src/WPAIPlugin.Api/Controllers/AdminFinanceController.cs
?? src/WPAIPlugin.Api/Controllers/AdminFinanceDtos.cs
?? src/WPAIPlugin.Api/Controllers/PaymentsController.cs
?? src/WPAIPlugin.Api/Data/AdminAuditLog.cs
?? src/WPAIPlugin.Api/Data/AiUsageEvent.cs
?? src/WPAIPlugin.Api/Data/ProcessedPaymentEvent.cs
?? src/WPAIPlugin.Api/Data/Purchase.cs
?? src/WPAIPlugin.Api/Health/
?? src/WPAIPlugin.Api/Migrations/20260922224407_AddAiUsageEvents.Designer.cs
?? src/WPAIPlugin.Api/Migrations/20260922224407_AddAiUsageEvents.cs
?? src/WPAIPlugin.Api/Migrations/20260922231425_AddAdminAuditLogAndAdminAdjustment.Designer.cs
?? src/WPAIPlugin.Api/Migrations/20260922231425_AddAdminAuditLogAndAdminAdjustment.cs
?? src/WPAIPlugin.Api/Migrations/20260922233011_AddPurchasesAndProcessedPaymentEvents.Designer.cs
?? src/WPAIPlugin.Api/Migrations/20260922233011_AddPurchasesAndProcessedPaymentEvents.cs
?? src/WPAIPlugin.Api/Payments/
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
?? src/WPAIPlugin.Api/wwwroot/index.js
?? src/WPAIPlugin.Api/wwwroot/login.js
?? src/WPAIPlugin.Api/wwwroot/myplugins.js
?? src/WPAIPlugin.Api/wwwroot/nav.js
?? src/WPAIPlugin.Api/wwwroot/plugin.js
?? src/WPAIPlugin.Api/wwwroot/register.js
?? src/WPAIPlugin.Api/wwwroot/site.css
?? src/WPAIPlugin.Planning/PlanningUsage.cs
?? tests/WPAIPlugin.Generator.Tests/Admin/
?? tests/WPAIPlugin.Generator.Tests/Payments/
?? tests/WPAIPlugin.Generator.Tests/Security/CsrfClient.cs
?? tests/WPAIPlugin.Generator.Tests/Security/ProductionSecurityTests.cs
```

Everything under `src/WPAIPlugin.Api/Security/`, `AiUsage/`, `Health/`,
`Controllers/AdminController.cs`/`AdminDtos.cs`, and the top-level untracked
completion reports/Docker files other than this session's own new additions
(called out above) is unrelated prior-milestone work, preserved exactly as
found.

No commit, push, or tag was performed.
