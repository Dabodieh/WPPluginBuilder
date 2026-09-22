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
