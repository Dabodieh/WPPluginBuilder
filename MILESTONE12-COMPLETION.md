# Milestone 12 completion report

Implementation and automated verification are complete. Live PostgreSQL and Docker build checks passed. Real OpenAI planning and browser interaction could not be completed for the reasons below. Milestone 13 was not started; nothing was committed, pushed or tagged.

## Starting state and recovery

Read AGENTS.md and inspected the full working-tree diff, relevant models/controllers/UI/tests and migration target models before editing. Initial build: 0 warnings/errors. Initial tests: 157/157. Existing uncommitted work included earlier milestones as well as partial credits; it was preserved.

The snapshot already contained credit tables but there was no credit migration. Restored the pre-credit snapshot from the AddPluginProjectsAndVersions designer target model, then generated the two credit migrations. Neither historical migration/designer was changed. Manually inspected schema migration: only CreditAccounts and CreditTransactions are created.

## 1. Exact files added in this recovery session (excluding migrations listed in 3)

- PLANS.md
- MILESTONE12-COMPLETION.md
- tests/WPAIPlugin.Generator.Tests/Credits/CreditApiTests.cs
- tests/WPAIPlugin.Generator.Tests/Credits/CreditBuildBoundaryTests.cs
- tests/WPAIPlugin.Generator.Tests/Credits/CreditConcurrencyDatabase.cs
- tests/WPAIPlugin.Generator.Tests/Credits/CreditServiceTests.cs

## 2. Exact existing files changed in this recovery session

- README.md
- src/WPAIPlugin.Api/Data/AppDbContext.cs
- src/WPAIPlugin.Api/Credits/CreditService.cs
- src/WPAIPlugin.Api/Controllers/AccountController.cs
- src/WPAIPlugin.Api/Controllers/ProjectsController.cs
- src/WPAIPlugin.Api/Program.cs
- src/WPAIPlugin.Api/Migrations/AppDbContextModelSnapshot.cs
- src/WPAIPlugin.Api/wwwroot/dashboard.html
- src/WPAIPlugin.Api/wwwroot/builder.html
- src/WPAIPlugin.Api/wwwroot/app.js
- tests/WPAIPlugin.Generator.Tests/Projects/ProjectsTestFactory.cs

Previously implemented credit files retained and verified: Data/CreditAccount.cs, Data/CreditTransaction.cs, Credits/CreditOptions.cs, Controllers/CreditsController.cs, Controllers/ProjectBuildRequest.cs, Controllers/ProjectDtos.cs, and the Credits configuration in appsettings.json (all under src/WPAIPlugin.Api). These were already present at the start, often untracked. Git status also includes preserved earlier-milestone changes; it is not a list of new edits from this session.

## 3. Migration files added

- src/WPAIPlugin.Api/Migrations/20260922204248_AddCreditAccountsAndTransactions.cs
- src/WPAIPlugin.Api/Migrations/20260922204248_AddCreditAccountsAndTransactions.Designer.cs
- src/WPAIPlugin.Api/Migrations/20260922204344_BackfillCreditsForExistingUsers.cs
- src/WPAIPlugin.Api/Migrations/20260922204344_BackfillCreditsForExistingUsers.Designer.cs

Applied to the existing database with the established EF command in Development. Migration history contains InitialIdentity, AddPluginProjectsAndVersions, then the two new migrations. No reset. Before and immediately after migration: 4 Identity users, 2 projects, 2 versions. All four existing users received 100 credits and exactly one grant. EF reports no pending model changes. Backfill Down explicitly refuses destructive reversal of potentially spent grants.

## 4-17. Delivered behavior

| Item | Result |
| --- | --- |
| 4. Tables/constraints | CreditAccounts: UserId PK, nonnegative Balance check, Identity FK with RESTRICT. CreditTransactions: Id PK, UserId index, Identity FK with RESTRICT. No payment tables. |
| 5. Credit model | Account: UserId, Balance, UpdatedAtUtc, Version. Ledger: Id, UserId, signed Amount, Type, Reference, CreatedAtUtc. No credit state in Identity user, projects, versions, claims or browser storage. |
| 6. Concurrency | Application-managed Version token, increment on every change, maximum three attempts with tracker clearing and reload. No application locks, xmin or conditional SQL UPDATE. Relational SaveChanges atomically writes balance and ledger. |
| 7. Signup | Configured 100 once; explicit PostgreSQL transaction includes Identity creation, account and ledger. Failed grant rolls back before sign-in. InMemory failure tests use compensation. Duplicate registration rejected. |
| 8. Existing users | One-time migration inserts only missing accounts and matching +100 SignupGrant entries in one CTE statement. Runtime missing accounts read as zero; no lazy grants. |
| 9. Standard build | 1 credit, PluginBuild ledger type. |
| 10. Validated build | 2 credits, ValidatedBuild ledger type. |
| 11. Planning | 0 credits. |
| 12. Refund | Generation, validation, artifact, persistence and cancellation failure after charge reach one compensation boundary; failed pending project entries are detached first. |
| 13. Cancellation safety | Refund uses CancellationToken.None. Charge and final commit finish independently once entered so their outcomes are known. A successfully persisted build stays charged if the client disconnects afterward. |
| 14. Idempotency | Every retry checks existing refund, reloads account, checks again to close the intervening race, verifies matching user/reference/amount/build charge, then saves. At most one successful refund; exhaustion throws rather than claiming success. |
| 15. Balance API | Authenticated GET /api/credits returns only own balance, standardBuildCost, validatedBuildCost. No public adjustment/grant/history endpoint. |
| 16. UI | Dashboard real balance; builder balance and configured costs; planning marked free; success/402/refund messages; GET refresh after builds; no optimistic arithmetic or fake purchases. |
| 17. Legacy route | Anonymous /api/plugins/build remains uncharged for the deterministic validation harness. SaaS UI uses only authenticated /api/projects/build. Pre-existing authenticated /api/plugins/build-validated remains compatible and is also absent from customer UI. |

## 18-27. Verification evidence

| Item | Actual result |
| --- | --- |
| 18. Automated tests | Final dotnet build: 0 warnings, 0 errors. Final dotnet test: 178 passed, 0 failed, 0 skipped (21 new tests). No external services required by normal tests. |
| 19. Concurrency | Separate contexts paused after loading/modifying the same account; real EF token conflict observed. Two charges against 1 credit: one success, one failure, balance 0, one charge ledger row. Concurrent refunds: one refund, balance 1. Ledger sums match in both tests. |
| 20. Manual registration | Live HTTP, brand-new account: 200, balance 100. |
| 21. Manual planning | Real planning attempt returned 502: provider not configured. Balance remained 100. Successful free planning is covered with fake provider in automated tests, not claimed as real OpenAI success. |
| 22. Manual standard build | 200, balance 99; client creditCost=0 ignored. Used deterministic spec after planning was unavailable. |
| 23. Manual validated build | Real Docker validation succeeded, 200, balance 97. Both builds listed in My Plugins; both ZIP downloads returned 200. |
| 24. Failed build/refund | Isolated second server using the same DB but a blocked temporary artifact root: controlled 500, refund confirmed, balance still 97. No database balance edits. Generation/validation/persistence/cancellation failure tests also pass. |
| 25. Login persistence | Logout 200, anonymous balance 401, login 200, balance 97. |
| 26. Security | Mutation attempts 404/405; client cost ignored; owner isolation and exact three-field credit response tested; existing provider-key tests pass. All frontend HTML/JS searched: no /api/plugins/build reference. Live DB: zero ledger mismatches, zero duplicate refunds. |
| 27. Unexecuted manual steps | Successful real OpenAI planning (not configured); graphical/browser interaction (browser runtime references a missing browser-service.mjs module). Live validation used HTTP, not a browser. |

JavaScript syntax and git diff whitespace checks pass. During final verification the manually started Windows app locked its executable; stopped only that process and reran build/test successfully. PostgreSQL remains running with its existing persistent volume. Temporary failure server was stopped and its temporary directory removed.

The InMemory provider has no transaction rollback and inserts added rows before checking modified account tokens. A small test-only adapter orders the account update before the ledger insert within the same provider save; the actual EF token check, separate contexts and overlapping operations remain real. Production uses unmodified Npgsql transaction semantics. This test limitation is documented rather than claiming InMemory has relational transactions.

## Research and targeted review

Reused the concrete service, shared AppDbContext, existing Identity flow, artifact store and test factories. Official [EF concurrency documentation](https://learn.microsoft.com/en-us/ef/core/saving/concurrency) and [transaction documentation](https://learn.microsoft.com/en-us/ef/core/saving/transactions) informed the retry and registration boundaries. The [EF 8.0.11 InMemory source](https://github.com/dotnet/efcore/blob/v8.0.11/src/EFCore.InMemory/Storage/Internal/InMemoryStore.cs) confirmed the test-provider rollback limitation.

Reviewed races, duplicate refunds, cancellation after charge, failed registrations, negative balances, client-controlled costs, ownership/privacy, provider keys, ledger alignment, migration baseline, duplicate signup grants and purchase placeholders. Fixed refund recheck races, cancelled refund tokens, failed tracked-project persistence and missing grant transaction/FKs/config validation. No unrelated refactor or new dependencies.

## 28. Deferred and remaining limitations

No Milestone 13, Stripe, subscriptions, purchases, orders, payments, invoices, admin adjustments, transaction history UI or new generation functionality. Legacy route hardening remains deferred. A process crash or sustained database outage can still interrupt compensation; no durable recovery worker/payment-idempotency framework was added. Such failures surface as errors and need operational reconciliation. Missing OpenAI configuration and browser tooling are the remaining manual-validation prerequisites.

Next safe step: configure the existing server-side OpenAI provider and repair browser tooling, then rerun the previously blocked planning/UI validation. Do not begin another milestone automatically.
