# Promotions + new-customer free builds completion

Every new registration now receives 2 free plugin builds alongside the
existing 5 signup credits — a distinct, ledger-backed entitlement, never a
generic credit. A small promotions system (FreeBuilds, BonusCredits,
PackPriceDiscount) layers on top for limited-time deals and promo codes,
integrated safely with billing, credits, admin reporting, and Stripe.

## 1. Exact files added

**Data models**
- `src/WPAIPlugin.Api/Data/BuildEntitlementAccount.cs`
- `src/WPAIPlugin.Api/Data/BuildEntitlementTransaction.cs`
- `src/WPAIPlugin.Api/Data/Promotion.cs` (+ `PromotionType`, `PromotionEligibility`, `PromotionState`, `PromotionStateResolver`)
- `src/WPAIPlugin.Api/Data/PromotionRedemption.cs` (+ `PromotionBenefitType`)

**Services / config**
- `src/WPAIPlugin.Api/Entitlements/BuildEntitlementService.cs`
- `src/WPAIPlugin.Api/Promotions/PromotionsOptions.cs`
- `src/WPAIPlugin.Api/Promotions/PromotionService.cs`

**Controllers**
- `src/WPAIPlugin.Api/Controllers/PromotionsController.cs` (`POST /api/promotions/redeem`)
- `src/WPAIPlugin.Api/Controllers/AdminPromotionsController.cs`
- `src/WPAIPlugin.Api/Controllers/AdminPromotionsDtos.cs`

**Migration**
- `src/WPAIPlugin.Api/Migrations/20260923233308_AddEntitlementsAndPromotions.cs` (+ `.Designer.cs`)

**Tests**
- `tests/WPAIPlugin.Generator.Tests/Entitlements/BuildEntitlementConcurrencyDatabase.cs`
- `tests/WPAIPlugin.Generator.Tests/Entitlements/BuildEntitlementServiceTests.cs`
- `tests/WPAIPlugin.Generator.Tests/Entitlements/FreeBuildsApiTests.cs`
- `tests/WPAIPlugin.Generator.Tests/Promotions/PromotionsApiTests.cs`
- `tests/WPAIPlugin.Generator.Tests/Admin/AdminPromotionsTests.cs`

**Documentation**
- `PROMOTIONS-FREE-BUILDS-COMPLETION.md` — this report.

(`PRODUCT-SPEC.md` was added in an earlier, separate session request — unrelated to this milestone, listed here only because `git status` shows it untracked.)

## 2. Exact files changed

- `src/WPAIPlugin.Api/Controllers/AccountController.cs` — registration grants the free-build entitlement alongside signup credits; non-relational (test-provider) failure compensation extended to clean up both.
- `src/WPAIPlugin.Api/Controllers/CreditsController.cs` — `GET /api/credits` returns `freeBuildsRemaining`.
- `src/WPAIPlugin.Api/Controllers/ProjectDtos.cs` — `ProjectBuildResponse`/`InsufficientCreditsResponse` gained `FreeBuildUsed`/`FreeBuildsRemaining`.
- `src/WPAIPlugin.Api/Controllers/ProjectsController.cs` — build charging flow: free build first, then reduced/full credit cost; refund logic restores exactly what was consumed; dynamic, accurate failure messages.
- `src/WPAIPlugin.Api/Controllers/PaymentsController.cs` — `POST /api/payments/checkout` accepts `PromoCode`, resolves it server-side, snapshots terms; `GET /api/payments/packs` and new `POST /api/payments/resolve-code` expose resolved automatic/code pricing; `PurchaseHistoryResponse` gained `BonusCredits`/`PromotionNameSnapshot`.
- `src/WPAIPlugin.Api/Controllers/AdminController.cs` — `GET /api/admin/system`/`overview` gained `StripeConfigured` (unrelated prior session's own change, preserved) and `ActivePromotions`/`PromotionalCreditsGranted`/`FreeBuildsRedeemed`.
- `src/WPAIPlugin.Api/Controllers/AdminDtos.cs` — `AdminOverviewResponse` extended accordingly.
- `src/WPAIPlugin.Api/Credits/CreditService.cs` — `GrantPurchaseCreditsAsync`/new `GrantPromotionBonusAsync` refactored onto one shared idempotent-grant implementation; `GrantSignupCreditsAsync` now stamps `CreditAccount.CreatedAtUtc`.
- `src/WPAIPlugin.Api/Data/AppDbContext.cs` — new `DbSet`s + `OnModelCreating` config for all new tables; `Purchase`/`Promotion` FK.
- `src/WPAIPlugin.Api/Data/CreditAccount.cs` — `CreatedAtUtc` (see item 4).
- `src/WPAIPlugin.Api/Data/CreditTransaction.cs` — `PromotionBonus` type constant.
- `src/WPAIPlugin.Api/Payments/PurchaseService.cs` — `CreatePendingAsync` takes a `PromotionResolution` snapshot; `CompletePurchaseAsync` grants base + bonus credits separately and records the redemption (with a late re-eligibility check).
- `src/WPAIPlugin.Api/Security/SecurityOptions.cs` — `PromoCodePerMinute`.
- `src/WPAIPlugin.Api/Data/AdminAuditLog.cs` — `PromotionCreated`/`Updated`/`Enabled`/`Disabled` actions, `Promotion` target type.
- `src/WPAIPlugin.Api/Program.cs` — DI registration for `BuildEntitlementService`/`PromotionService`/`PromotionsOptions`; `promoCode` rate-limit policy.
- `src/WPAIPlugin.Api/appsettings.json` — `Promotions:SignupFreeBuilds`, `Security:PromoCodePerMinute`.
- `.env.example` — matching placeholders/comments.
- `src/WPAIPlugin.Api/wwwroot/{app.js,builder.html,dashboard.html,dashboard.js,billing.html,billing.js,billing.css,admin.html,admin.js,admin.css,index.html}` — see item 10/19.
- `tests/WPAIPlugin.Generator.Tests/Projects/ProjectsTestFactory.cs` — pins `Promotions:SignupFreeBuilds = 0` (mirrors the existing `CreditOptions.SignupGrant` pin) so pre-existing credit-only tests stay decoupled from this milestone's new production default.
- `tests/WPAIPlugin.Generator.Tests/Credits/CreditApiTests.cs` — one test's exact-property-list assertion updated for the new `freeBuildsRemaining` field.
- `tests/WPAIPlugin.Generator.Tests/Security/CsrfClient.cs` — added `PutAsJsonWithCsrfAsync` (needed for the admin promotion-edit test; no existing PUT-based admin endpoint existed before this milestone).
- `README.md`, `PLANS.md`, `LAUNCH-CHECKLIST.md` — see item 40 in the task/ item 21 below.

No other file was touched. All prior uncommitted milestone work (production readiness, admin panel, AI usage, credits audit, Stripe/financial, go-live readiness) remains exactly as it was — confirmed by the `git status --short` at the end of this report matching the pre-existing untracked/modified set plus this session's own additions.

## 3. Free-build account model

`BuildEntitlementAccount` (one row per user): `UserId` (PK), `RemainingBuilds`, `UpdatedAtUtc`, `Version` (application-managed EF concurrency token) — an exact structural mirror of `CreditAccount`, including a `CHECK (RemainingBuilds >= 0)` constraint. A missing row reads as 0 remaining (never created eagerly, never silently backfilled) — the same defensive-read behavior already established for a missing `CreditAccount`.

## 4. Entitlement ledger model

`BuildEntitlementTransaction`: `Id`, `UserId`, `Amount` (signed), `Type` (`SignupFreeBuildGrant`/`FreeBuildConsumed`/`FreeBuildRefund`/`PromotionGrant`), nullable `PromotionId`, `Reference` (server-generated, shared with the correlated `CreditTransaction.Reference` for a build operation), `CreatedAtUtc`. Immutable after creation — never updated or deleted. Every `BuildEntitlementAccount.RemainingBuilds` change has a matching row written in the same `SaveChangesAsync` call, exactly like the credit ledger.

## 5. Signup grant behaviour

`AccountController.Register` now performs, in the same relational transaction as the Identity account:
1. `CreditService.GrantSignupCreditsAsync` (unchanged, 5 credits).
2. `BuildEntitlementService.GrantAsync` (2 free builds), skipped only if `Promotions:SignupFreeBuilds` is configured to 0.

Both are guaranteed exactly-once by the same mechanism that already guaranteed the credit grant was exactly-once (one registration call, one relational transaction). For the non-relational InMemory test provider, the failure-compensation `catch` block was extended to explicitly remove any partially-written `CreditAccount`/`CreditTransaction`/`BuildEntitlementAccount`/`BuildEntitlementTransaction` rows before deleting the Identity user — a real gap found and fixed: each grant is its own already-committed `SaveChangesAsync` call (InMemory has no rollback), so if the *second* grant call failed after the *first* had already succeeded, the old code would have deleted only the Identity user and left an orphaned `CreditAccount`. Verified by a new test (`FailedEntitlementGrant_DoesNotLeaveSuccessfulRegistrationOrOrphanedCredits`) that forces exactly this failure ordering and asserts all four tables end up empty.

## 6. Existing-user behaviour

No backfill migration and no runtime fallback grants an existing account the current configured free-build amount. `GetRemainingAsync` returns 0 for a missing account row, `TryConsumeAsync` returns `false`. Verified live (registered a user, confirmed `freeBuildsRemaining: 0`, confirmed no `BuildEntitlementAccount` row exists) and by test (`ExistingAccountWithNoEntitlementRow_DefaultsToZeroFreeBuilds`). An admin can grant free builds to a specific existing user later only via a `FreeBuilds`-type promotion redemption — never automatically.

## 7. Concurrency strategy

`BuildEntitlementService.TryConsumeAsync`/`GrantAsync`/`RefundAsync` use the identical application-managed pattern as `CreditService`: load account → check → mutate → increment `Version` → write matching ledger row → `SaveChangesAsync` in one unit → catch `DbUpdateConcurrencyException` → clear the change tracker → retry, bounded at 3 attempts. No in-memory locks, no distributed locks, no PostgreSQL-specific `xmin`. Verified with the same forced-overlap technique already proven for `CreditService` (`BuildEntitlementConcurrencyDatabase`, a sibling of the existing `CreditConcurrencyDatabase` test adapter, plus a `SaveChangesInterceptor` barrier): two simultaneous `TryConsumeAsync` calls against an account with exactly one remaining build — exactly one succeeds, the account ends at 0, exactly one `FreeBuildConsumed` row exists. Normal tests require no PostgreSQL (InMemory throughout).

## 8. Build charging rules

`ProjectsController.Build`: `TryConsumeAsync` is attempted first, under one `build:<guid>` reference. Cost is then computed server-side: `0`/`1` credit if a free build was consumed (standard/validated respectively), or the full configured `StandardBuildCost`/`ValidatedBuildCost` otherwise. `TryChargeAsync` is skipped entirely when cost is `0` (it would otherwise throw, since it requires a positive amount) — the balance is read directly instead. If the reduced/full charge still fails (insufficient credits), the just-consumed free build is refunded immediately before returning `402`, so a user is never left with a silently-vanished free build on a failed charge. The browser never supplies or influences any of this — `request.Validated` only selects which server-known cost path applies.

## 9. Failure/refund behaviour

The `finally` block refunds exactly what was actually consumed: the credit charge only if `cost > 0`, the free-build entitlement only if one was consumed — using the same `build:<guid>` reference for both, correlating with the original charge/consumption. Verified for every listed failure case (validation failure, artifact-storage failure, persistence failure) via `FailedStandardBuildRestoresFreeBuild...`/`FailedValidatedBuild_RestoresBothFreeBuildAndCredit`, reusing the exact `FailSave`/`FakeValidator.Handler`/artifact-blocking techniques the pre-existing `CreditApiTests.FailedBuildRefundsOnceAndLeavesNoProject` already established. Both `CreditService.RefundAsync` and `BuildEntitlementService.RefundAsync` remain idempotent (a duplicate refund call is a safe no-op, proven by `RefundRestoresExactlyOnce`). Server-owned, non-request-aborted (`CancellationToken.None`) tokens are used for all compensating work, unchanged from the existing established pattern.

## 10. Customer UI changes

- **Builder** (`app.js`/`builder.html`): a new "Free builds: N" line alongside "Credits: N"; `Build Plugin`/`Build & Validate` button labels switch dynamically between "— Free build" / "— Free build + 1 credit" and "— N credit(s)" based on the server's authoritative `freeBuildsRemaining` — never optimistically decremented, always re-fetched after build/refund. Success messaging is built dynamically from the server's response (`freeBuildUsed`, `creditsCharged`, `freeBuildsRemaining`) so it only ever states what was actually consumed/remains — exactly matching the milestone's own worked examples. Failure messages come from the server's own dynamic `ResourcesReturnedMessage()` (see item 9) — never claim a resource was returned that wasn't.
- **Dashboard** (`dashboard.html`/`.js`): a "Free builds" stat card next to "Credits".
- **Homepage** (`index.html`): the existing hero note now leads with "Generate your first 2 plugins free." — a small, in-place text change, no layout redesign.
- **Nav** (`nav.js`) deliberately unchanged — it already shows credits on every authenticated page; adding free builds there too would clutter every page, per the milestone's own instruction to keep this to Dashboard/Builder.

## 11. Promotion model

`Promotion`: `Id`, `Name`, `Code` (nullable, normalized to uppercase), `Type`, `StartsAtUtc`, `EndsAtUtc` (nullable), `IsEnabled`, `RequiresCode`, `AppliesToPackId` (nullable = all packs), `Value` (int — meaning depends on `Type`), `MaxRedemptions`/`MaxRedemptionsPerUser` (nullable), `Eligibility`, `Priority`, `CreatedAtUtc`, `UpdatedAtUtc`. State (`Draft`/`Scheduled`/`Active`/`Expired`) is never a stored column — `PromotionStateResolver.Resolve` derives it from `IsEnabled`/`StartsAtUtc`/`EndsAtUtc` against the current UTC time on every read, so it can never drift out of sync.

## 12. Promotion types

- **`FreeBuilds`** — grants free-build entitlements. Always requires a code (validated at admin create/edit time) — there is no automatic/codeless trigger for this type, since nothing in this milestone defines one and inventing one risked exactly the "fragile registration reliability" the spec explicitly warned against.
- **`BonusCredits`** — `Value` is an absolute bonus-credit count, granted as a separate `PromotionBonus` ledger entry alongside the real `CreditPurchase` grant.
- **`PackPriceDiscount`** — `Value` is a whole-number percentage (1–100). Discount computed as `Math.Round(pack.AmountMinor * Value / 100m, MidpointRounding.AwayFromZero)`, `decimal` throughout, never `float`/`double`. Verified against the milestone's own worked example: Builder 999p at 25% → 999 × 0.25 = 249.75 → rounds to 250p → **749p (£7.49) paid** — both by a unit-level assertion and live against real PostgreSQL (see item 27).

## 13. Promo-code behaviour

The browser submits only a code string. `PromotionService.ResolveForPackAsync`/`FindValidFreeBuildsCodeAsync` normalize it (trim + uppercase) and check, in order: exists, correct type for the calling context, enabled, active window, pack eligibility, `Eligibility` rule, per-user/global redemption limits. Any failure produces the identical generic `{"error":"Invalid or expired code."}` response — never reveals which specific check failed, so campaign details/limits are not enumerable by probing. Verified by `InvalidCode_RejectedSafely_NoInternalDetailLeaked` asserting the exact response body.

## 14. Automatic-promotion behaviour

`PromotionService.FindBestAutomaticPromotionAsync` considers only `IsEnabled && !RequiresCode` promotions of type `PackPriceDiscount`/`BonusCredits`, ordered by `Priority` descending then `CreatedAtUtc`/`Id` ascending, returning the first one that passes the same eligibility checks as a code. The server determines this — `GET /api/payments/packs` calls it per pack and returns the resolved pricing; no JavaScript ever decides whether a sale is active.

## 15. Promotion precedence/priority

No stacking: at most one promotion applies per purchase. An explicit, valid promo code always wins over any automatic deal (`ResolveForPackAsync` checks the code path first and never falls back to automatic if a supplied code is invalid — it rejects outright, per the spec's explicit instruction not to silently substitute). Among automatic promotions, `Priority` (higher wins) with `CreatedAtUtc` then `Id` as a deterministic tie-break. Verified by `ExplicitCode_WinsOverAutomaticPromotion_NoStacking` and `HigherPriorityAutomaticPromotion_WinsDeterministically`.

## 16. Promotion limits

`MaxRedemptions` (global) and `MaxRedemptionsPerUser` are enforced by counting existing `PromotionRedemption` rows **once, at resolution time** (Checkout creation via `ResolveForPackAsync`, and FreeBuilds redemption via `FindValidFreeBuildsCodeAsync`) — **not re-checked at webhook-completion time.** This is a documented best-effort check, not a database-level constraint — a true N-per-user limit isn't expressible as a plain unique index, and building a distributed/serializable-transaction guarantee for it was explicitly out of scope ("do not use in-memory locks, distributed locks, PostgreSQL-specific xmin" was scoped to free-build *consumption*, which remains airtight; this is a narrower, lower-stakes promo-redemption-counting limitation, called out explicitly rather than silently accepted).

**Corrected in a follow-up pass** (see the note at the end of this section): an earlier version of `CompletePurchaseAsync` re-checked live promotion eligibility (window/enabled/pack/eligibility-rule/limits) before deciding whether to record the redemption, skipping it if the promotion had since expired/been disabled — while still granting the agreed credits. That produced an inconsistent state: a customer who paid the agreed discounted price got no redemption record, understating redemption counts, per-user/global usage, and promotion revenue reporting. The check has been removed entirely from the completion path — once a Purchase legitimately exists with a resolved promotion, a successful payment always grants the snapshotted credits/bonus and always records the redemption, regardless of what has happened to the Promotion row since. If a global/per-user limit is exceeded between Checkout creation and payment completion (a narrow race in the best-effort check above), the already-agreed, already-paid transaction is preserved in full and its redemption is still recorded — the limit is honoured only at the point a *new* Checkout is being created, never retroactively against one that already exists.

## 17. Purchase snapshot behaviour

`Purchase` gained `BaseAmountMinor`, `BonusCredits`, `PromotionId`, `PromotionCodeSnapshot`, `PromotionNameSnapshot` (existing `AmountMinor`/`CreditsPurchased` keep their meaning — "amount actually paid" / "base credits purchased" — no rename, minimizing schema churn). These are written once, at Checkout creation, and never re-resolved later: if the referenced Promotion is edited, disabled, or expires before the customer completes payment, the already-created Purchase's terms are unaffected — Stripe already agreed to charge that exact amount, and honoring it is a legal/business necessity, not just an implementation choice. `CompletePurchaseAsync` never reloads or re-resolves the Promotion row at all — the benefit type/amount recorded on the redemption is derived purely from the Purchase's own snapshot fields (`BonusCredits > 0` ⇒ `BonusCredits` benefit; otherwise `BaseAmountMinor − AmountMinor` ⇒ `PriceDiscount` benefit), so a later edit to the Promotion's `Value`/`Type` cannot retroactively change what gets recorded for an already-created Purchase either.

Verified by four tests, each expiring/disabling/editing the Promotion *after* Checkout creation but *before* webhook completion:
- `ExpiredAfterCheckoutCreation_PaymentCompletes_SnapshotHonoured_RedemptionRecorded` — payment completes at the original 749p, **redemption is recorded** (not skipped).
- `DisabledAfterCheckoutCreation_PaymentCompletes_RedemptionRecorded` — same, for a disabled (not expired) promotion.
- `EditedAfterCheckoutCreation_OriginalSnapshotTermsHonoured_RedemptionRecorded` — the promotion's discount `Value` is changed from 25% to 50% after Checkout creation; the completed Purchase and its redemption both reflect the *original* 25%/749p terms, never the edited 50%.
- `BonusCreditsPromotion_ExpiresAfterCheckoutCreation_BonusGrantedOnce_RedemptionRecordedOnce` — the snapshotted bonus credits are granted exactly once and the redemption recorded exactly once, even though the promotion expired before completion.

## 18. Stripe integration

Unchanged invariants from Milestone 16, re-verified: the browser can never set a discount, bonus, or final price (`ForgedBrowserDiscountOrBonus_HasNoEffect`); Stripe only ever receives the already-resolved `PaidAmountMinor` (built by constructing a `CreditPack` copy with the resolved amount, passed into the existing, unmodified `IPaymentGateway.CreateCheckoutSessionAsync` — no interface change was needed); credits (base and bonus) are granted exclusively by the verified webhook, never the success redirect; webhook idempotency (event-ID + purchase-status) still holds and now additionally guards the bonus-credit grant and redemption record (`DuplicateWebhookDelivery_DoesNotDuplicateBonusOrRedemption`).

## 19. Admin promotions UI

`/admin` → **Promotions** tab: a table (Name, Type, Code, Benefit, Start, End, State, Redemptions, Actions) with Edit/Enable-Disable/Duplicate per row and a "New promotion" form (Name, Type, Value, Code, Requires code, Applies-to-pack, Eligibility, Starts/Ends UTC, Priority, Max redemptions, Max redemptions per user, Enabled) — server-side validated in full regardless of client input. A detail view shows the reporting figures from item 21. No hard delete anywhere in the UI or API — only enable/disable/duplicate, matching the "prefer archive semantics" instruction. Admin's page-lead text ("Read-only in this phase") was also corrected — stale since the Admin Credits Audit milestone added mutations, noticed while touching this page.

## 20. Audit integration

`POST/PUT /api/admin/promotions[/…]` create/update/enable/disable all call the existing `AdminAuditService.RecordAsync` with new `AdminAuditAction` constants (`PromotionCreated`/`Updated`/`Enabled`/`Disabled`) and `AdminAuditTargetType.Promotion`. Description is a short safe string (promotion name + type) — never a secret. Verified by `Admin_CanCreateEditEnableDisableDuplicatePromotion` asserting all four actions (plus a second `PromotionCreated` for the duplicate) appear in `AdminAuditLogs`.

## 21. Promotion reporting

`GET /api/admin/promotions/{id}` returns real data only: redemption count, distinct purchasing customers (redemptions with a `PurchaseId`), credits granted (`BenefitType=BonusCredits` sum), free builds granted (`BenefitType=FreeBuilds` sum), discount granted (`BenefitType=PriceDiscount` sum), and revenue associated (`Purchase.AmountMinor` sum where `PromotionId` matches, `Status=Completed`). No conversion rate or other unsupported metric is fabricated. Overview also gained `ActivePromotions` (server-computed state count), `PromotionalCreditsGranted` (sum of `PromotionBonus` ledger entries), `FreeBuildsRedeemed` (count of `FreeBuildConsumed` transactions) — kept light per the "don't overload overview" instruction.

## 22. Migration details

One migration, `AddEntitlementsAndPromotions`: creates `BuildEntitlementAccounts`, `BuildEntitlementTransactions`, `Promotions`, `PromotionRedemptions`; adds `Purchases.{BaseAmountMinor,BonusCredits,PromotionId,PromotionCodeSnapshot,PromotionNameSnapshot}`; adds `CreditAccounts.CreatedAtUtc`. Touches no existing table's existing columns. Two data-backfill statements were hand-added to the generated migration after inspection (a real gap in the auto-generated defaults, caught before applying):
- `UPDATE "Purchases" SET "BaseAmountMinor" = "AmountMinor"` — the auto-generated default (`0`) would have made every historical purchase look 100%-discounted in reporting; historical purchases had no discount, so their base price equals what was actually charged.
- `CreditAccounts.CreatedAtUtc` for existing rows uses EF's auto-generated `DateTime.MinValue` default (year 0001) deliberately kept as-is rather than "now" — an honest "unknown/never new" sentinel that correctly makes every pre-existing account permanently ineligible for `NewRegistrations`-eligibility promotions (`registeredAtUtc < promotion.StartsAtUtc` always holds for them), which is the semantically correct outcome.

Applied live to the real local PostgreSQL development database on top of the seven pre-existing migrations; all existing data (users, projects, credits, AI usage, admin audit, purchases) was preserved — confirmed live (see item 27).

## 23. Security result

- Customers cannot grant themselves free builds: `TryConsumeAsync`/`GrantAsync` are never reachable from an unauthenticated or unprivileged route; the only customer-facing entry points are the server-decided build-charging flow and the `FreeBuilds`-code-gated `/api/promotions/redeem`.
- Customers cannot create/edit/enable/disable/duplicate promotions: `AdminPromotionsController` carries the same `[Authorize(Policy = AdminAuthorization.AdminOnlyPolicy)]` class-level gate as every other admin controller — verified by `NormalUser_CannotCreateOrListPromotions`/`Anonymous_DeniedAdminPromotions`.
- The client cannot set discount, final price, bonus credits, or free-build count — every one of these is server-resolved from `Promotion`/`CreditPackOptions` by ID/code alone; verified by `ForgedBrowserDiscountOrBonus_HasNoEffect`.
- Redemption limits are enforced server-side once, at resolution time (Checkout creation / FreeBuilds redemption) — never retroactively against an already-created Purchase (item 16/17).
- Promo-code failure responses reveal no internal campaign detail (item 13).
- All admin promotion endpoints remain `AdminOnly`; mutating ones are `[ValidateAntiForgeryToken]`.
- The Stripe webhook remains the sole authority for a successful paid grant — unchanged, re-verified (item 18).
- Existing CSRF/rate limits preserved on every touched endpoint; a new dedicated `promoCode` rate-limit policy was added for the two new customer-facing promo endpoints (item 37 below), reusing the exact same built-in limiter architecture as `checkout`/`build`/`planning` — not a new system.
- Promotion IDs are returned to admins only (safe, internal-operator context); customer-facing responses (`CreditPackResponse`, checkout, resolve-code) expose only `PromotionName`, never an ID or reference.

## 24. Exact targeted tests run

- `dotnet test --filter "FullyQualifiedName~Entitlements"` → 16/16 (7 `BuildEntitlementServiceTests` concurrency/refund/grant unit tests + 9 `FreeBuildsApiTests` HTTP-level tests).
- `dotnet test --filter "FullyQualifiedName~Promotions"` → 22/22 (`PromotionsApiTests`: scheduling/state, code validation, discount math, eligibility, limits, precedence/priority, webhook bonus/redemption/duplicate/expiry-after-checkout/disable-after-checkout/edit-after-checkout, FreeBuilds redemption).
- `dotnet test --filter "FullyQualifiedName~AdminPromotions"` → 5/5 (authorization, full CRUD/enable/disable/duplicate + audit, validation rejections, duplicate-code rejection).
- `dotnet test --filter "FullyQualifiedName~Accounts|FullyQualifiedName~Credits|FullyQualifiedName~Projects|FullyQualifiedName~Payments|FullyQualifiedName~Admin|FullyQualifiedName~Security|FullyQualifiedName~Entitlements|FullyQualifiedName~Promotions"` → **180/180, final combined confirmation run** (every pre-existing suite touched by this milestone's changes, plus all new tests).
- **Corrective pass** (see item 30): `dotnet test --filter "FullyQualifiedName~Promotions|FullyQualifiedName~Payments"` → 53/53.

No unfiltered `dotnet test` was run this session, per this milestone's explicit instruction.

## 25. Targeted test results

**180/180 passing** (53/53 in the corrective pass's own narrower `Promotions|Payments` re-run). Two pre-existing tests needed a real, intentional fix (not a workaround) after the new default free-build grant started covering their first build for free: `ProjectsTestFactory` now pins `Promotions:SignupFreeBuilds = 0` for the shared test factory (mirroring the exact existing `CreditOptions.SignupGrant` pin, with the same rationale — decouple pre-existing credit-charge assertions from this milestone's new production default), and `CreditApiTests.ConfiguredGrantCostsAndResponseAreServerOwned`'s exact-property-list assertion was updated to include the new, permanent `freeBuildsRemaining` field. Both changes were verified necessary and correct by re-running the full previously-passing set after each. In the corrective pass, one existing test's assertion was fixed because it encoded the *bug* being corrected (`ExpiredAfterCheckoutCreation_...` previously asserted `Assert.Empty(db.PromotionRedemptions)`, renamed and corrected to assert the redemption **is** recorded — see item 30), and three new tests were added for the disabled/edited/bonus-expires cases.

## 26. Build result

`dotnet build`: **0 warnings, 0 errors** (run repeatedly through implementation; final run after all changes confirmed clean; re-confirmed clean after the corrective pass in item 30).

## 27. Manual verification

Performed live against the real local PostgreSQL (`docker-compose.saas.yml`), not simulated — Docker Desktop and the dev database from the prior Go-Live Readiness session were still available this session:

1. Registered a new account over real HTTP — confirmed `GET /api/credits` returned `{"balance":5,"freeBuildsRemaining":2,...}`.
2. Submitted a real standard build (`POST /api/projects/build`, `validated:false`) — response showed `"creditsCharged":0,"creditBalance":5,"freeBuildUsed":true,"freeBuildsRemaining":1`; `GET /api/credits` confirmed the same afterward.
3. Logged in as the existing bootstrap admin (`admin-verify@example.com`) and created a real automatic `PackPriceDiscount` promotion (25% off, `builder` pack only) via `POST /api/admin/promotions` — response showed `"state":"Active"`.
4. As a second, freshly-registered customer, called `GET /api/payments/packs` — **Builder correctly showed `discountedAmountMinor: 749`** (exactly the milestone's own worked example, £9.99 → £7.49) with `promotionName: "Smoke Test Sale"`; **Starter and Pro were correctly unaffected** (`discountedAmountMinor: null`) since the promotion was pack-scoped.
5. Disabled the test promotion via `POST /api/admin/promotions/{id}/disable` afterward, leaving no live discount for real customers.
6. Reviewed the full server log for the session — no secret, connection string, or internal exception detail appeared anywhere.
7. Migration applied live, preserving all prior data (confirmed via `dotnet ef database update` output showing only additive `CREATE TABLE`/`CREATE INDEX`/`ALTER TABLE ADD COLUMN` statements, no data loss).
8. Server stopped cleanly afterward (`Stop-Process` on the exact `WPAIPlugin.Api.exe` PID, identified via `Get-CimInstance Win32_Process` to avoid touching unrelated processes).

**Not exercised live**: `Build & Validate` covering a free build (requires Docker WordPress/MariaDB spin-up, which is slow; the identical code path is fully covered by the automated `FailedValidatedBuild_RestoresBothFreeBuildAndCredit`/`ValidatedBuild_UsesFreeBuild_ChargesOnlyOneCredit` tests using `FakeDockerPluginValidator`), and a genuine Stripe TEST-mode Checkout-to-webhook completion with a real `stripe listen` process (same class of limitation already documented in the Stripe/Financial milestone's own completion report — requires browser automation to fill Stripe's hosted card page, unavailable in this environment; covered instead by the automated `FakePaymentGateway` suite exercising the identical `PaymentsController`/`PurchaseService`/`CreditService` code path).

## 28. Anything deliberately deferred

- **Automatic (codeless) `FreeBuilds` promotions** — no trigger point exists for one in this milestone (registration already has its own deterministic, non-promotion-based grant per the spec's own explicit preference); `FreeBuilds` promotions require a code, redeemed via a dedicated endpoint. Documented as an intentional scope limit, not an oversight.
- **A hard database-level guarantee for `MaxRedemptions`/`MaxRedemptionsPerUser`** — best-effort counting only (item 16); building true serializable-transaction or advisory-lock enforcement was explicitly out of scope and disproportionate for a promo-code limit.
- **Bulk/batch promotion operations** — only single-promotion create/edit/enable/disable/duplicate, matching the "smallest maintainable service" instruction.
- **Promotion CSV/export** — not requested.
- **A `NewRegistrations`-eligibility promotion applied automatically at registration time** — deliberately scoped out per the milestone's own explicit warning not to make "basic registration reliability depend on a fragile admin-created promotion record"; `NewRegistrations` eligibility only applies to promotions redeemed later (via a code, at checkout or `/redeem`), evaluated against `CreditAccount.CreatedAtUtc`.
- Password-reset/legal-page work was **not** started, per this milestone's explicit closing instruction.

## Corrective pass — redemption-recording inconsistency fix

A separate, later corrective pass fixed a documented-vs-intended inconsistency: `PurchaseService.CompletePurchaseAsync` re-checked live promotion eligibility (`PromotionService.IsStillEligibleAsync` — window/enabled/pack/eligibility-rule/limits, all re-resolved from the *current* Promotion row) before deciding whether to record the `PromotionRedemption`, even though the Purchase's own snapshot was already correctly authoritative for the *price/credits* granted. A promotion expiring, being disabled, or being edited between Checkout creation and webhook completion caused the redemption to be silently skipped while the customer's paid transaction still completed normally — understating redemption counts, per-user/global usage, and promotion-attributed revenue in every admin report.

**Fixed**: `CompletePurchaseAsync` no longer reloads or re-resolves the Promotion at all. When `Purchase.PromotionId` is set, the redemption is now always recorded, with its `BenefitType`/`BenefitAmount` derived purely from the Purchase's own snapshot fields (`BonusCredits > 0` ⇒ `BonusCredits` benefit amount `BonusCredits`; otherwise ⇒ `PriceDiscount` benefit amount `BaseAmountMinor − AmountMinor`) — never from the current `Promotion.Type`/`Value`. `PromotionService.IsStillEligibleAsync` and the now-unused `GetAsync` were deleted (dead code). Redemption limits (`MaxRedemptions`/`MaxRedemptionsPerUser`) remain enforced exactly once, at resolution time (Checkout creation / FreeBuilds redemption) — never retroactively against a Purchase that already exists; see the corrected item 16 above for the full reasoning, including the accepted best-effort-limit tradeoff this preserves rather than silently worsens.

Sections 16, 17, and the redemption-limits bullet in section 23 above were rewritten in place to describe this corrected behaviour (the original, incorrect versions are not preserved separately — this file describes the current, correct implementation throughout). Four tests prove the fix: a promotion expiring after Checkout creation, one being disabled after Checkout creation, one being edited after Checkout creation (original snapshot terms — not the edit — are what get recorded), and a `BonusCredits` promotion expiring after Checkout creation (bonus granted exactly once, redemption recorded exactly once). A fifth existing test (`DuplicateWebhookDelivery_DoesNotDuplicateBonusOrRedemption`) was re-run unchanged to confirm webhook/redemption idempotency (via the pre-existing unique index on `PromotionRedemption.PurchaseId`) was not affected by removing the eligibility re-check.

Targeted re-run: `dotnet test --filter "FullyQualifiedName~Promotions|FullyQualifiedName~Payments"` → **53/53**. `dotnet build`: 0 warnings/errors. `git diff --check`: clean (only pre-existing, unrelated LF/CRLF line-ending warnings on files this pass didn't touch). No unfiltered `dotnet test` was run.

Files touched in this corrective pass only: `src/WPAIPlugin.Api/Payments/PurchaseService.cs`, `src/WPAIPlugin.Api/Promotions/PromotionService.cs`, `tests/WPAIPlugin.Generator.Tests/Promotions/PromotionsApiTests.cs`, this report. Nothing else.

## 29. git status --short

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
?? GO-LIVE-READINESS-COMPLETION.md
?? HOMEPAGE-COMPLETION.md
?? LAUNCH-CHECKLIST.md
?? MILESTONE13-COMPLETION.md
?? PRODUCT-SPEC.md
?? PRODUCTION-READINESS-COMPLETION.md
?? PROMOTIONS-FREE-BUILDS-COMPLETION.md
?? STRIPE-FINANCIAL-COMPLETION.md
?? UI-POLISH-COMPLETION.md
?? docker/docker-compose.prod.example.yml
?? src/WPAIPlugin.Api/AiUsage/
?? src/WPAIPlugin.Api/Controllers/AdminController.cs
?? src/WPAIPlugin.Api/Controllers/AdminDtos.cs
?? src/WPAIPlugin.Api/Controllers/AdminFinanceController.cs
?? src/WPAIPlugin.Api/Controllers/AdminFinanceDtos.cs
?? src/WPAIPlugin.Api/Controllers/AdminPromotionsController.cs
?? src/WPAIPlugin.Api/Controllers/AdminPromotionsDtos.cs
?? src/WPAIPlugin.Api/Controllers/PaymentsController.cs
?? src/WPAIPlugin.Api/Controllers/PromotionsController.cs
?? src/WPAIPlugin.Api/Data/AdminAuditLog.cs
?? src/WPAIPlugin.Api/Data/AiUsageEvent.cs
?? src/WPAIPlugin.Api/Data/BuildEntitlementAccount.cs
?? src/WPAIPlugin.Api/Data/BuildEntitlementTransaction.cs
?? src/WPAIPlugin.Api/Data/ProcessedPaymentEvent.cs
?? src/WPAIPlugin.Api/Data/Promotion.cs
?? src/WPAIPlugin.Api/Data/PromotionRedemption.cs
?? src/WPAIPlugin.Api/Data/Purchase.cs
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
?? src/WPAIPlugin.Api/wwwroot/index.js
?? src/WPAIPlugin.Api/wwwroot/login.js
?? src/WPAIPlugin.Api/wwwroot/myplugins.js
?? src/WPAIPlugin.Api/wwwroot/nav.js
?? src/WPAIPlugin.Api/wwwroot/plugin.js
?? src/WPAIPlugin.Api/wwwroot/register.js
?? src/WPAIPlugin.Api/wwwroot/site.css
?? src/WPAIPlugin.Planning/PlanningUsage.cs
?? tests/WPAIPlugin.Generator.Tests/Admin/
?? tests/WPAIPlugin.Generator.Tests/Entitlements/
?? tests/WPAIPlugin.Generator.Tests/Payments/
?? tests/WPAIPlugin.Generator.Tests/Promotions/
?? tests/WPAIPlugin.Generator.Tests/Security/CsrfClient.cs
?? tests/WPAIPlugin.Generator.Tests/Security/ProductionSecurityTests.cs
```

Everything outside the files named in items 1–2 is unrelated prior-milestone work, preserved exactly as found.

No commit, push, or tag was performed.
