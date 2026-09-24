# Launch checklist

Practical go-live checklist for WPAIPlugin (public brand: **ModuleMint**).
Nothing here is pre-checked — checking a box is an operator action, not a
claim this audit made for you. See
[GO-LIVE-READINESS-COMPLETION.md](GO-LIVE-READINESS-COMPLETION.md) and
[FINAL-LAUNCH-READINESS.md](FINAL-LAUNCH-READINESS.md) for the full audits
this checklist was derived from.

**Classification legend** (added this pass, per item — nothing was marked
complete merely because code for it exists):

- **[OWNER]** — a decision, credential, or real-money action only the
  business owner can make. An automated agent must not perform this.
- **[DEPLOYMENT]** — a step performed while deploying, using values the
  owner has already supplied. Mechanical, not a business decision.
- **[EXTERNAL]** — requires action inside a third-party service's own
  console (Stripe, Resend, DNS/domain registrar).
- **[VERIFY]** — a check to run after deployment to confirm things actually
  work, not a one-time setup step.

Everything in this file is genuinely outstanding — the application code and
targeted tests behind it are complete, but none of these operator/owner
actions have been performed.

## BEFORE DEPLOYMENT

- [ ] **[DEPLOYMENT]** Provision a PostgreSQL 17 (or compatible) server reachable from the app.
- [ ] **[OWNER]** Choose and configure exactly one AI planning provider (OpenAI or
      Anthropic) and obtain its production API key.
- [ ] **[DEPLOYMENT]** Decide the production artifact storage path/volume (`Artifacts:RootPath`)
      and the Data Protection key path (`DataProtection:KeyRingPath`) — both
      must be on volumes that survive restarts/redeploys.
- [ ] **[OWNER]** Set the production domain and confirm HTTPS termination (direct Kestrel
      TLS or a reverse proxy).
- [ ] **[DEPLOYMENT]** If behind a reverse proxy, list its IP(s)/CIDR under
      `ForwardedHeaders:KnownProxies`/`KnownNetworks`.
- [ ] **[OWNER]** Register the intended owner account on the deployed instance (or plan
      to do so immediately after first deploy) — which email is a business decision.
- [ ] **[DEPLOYMENT]** Set `Admin:BootstrapEmail` to that exact email.
- [ ] **[VERIFY]** Confirm `Credits:SignupGrant` is `5` (already the configured default;
      verify no environment override changes it before go-live).
- [ ] **[VERIFY]** Confirm `Promotions:SignupFreeBuilds` is `2` (already the configured
      default) — the "Generate your first 2 plugins free" offer.
- [ ] **[OWNER]** Confirm `CreditPacks:Packs` pricing (`starter` £4.99/25,
      `builder` £9.99/75, `pro` £19.99/200) is what the owner wants to charge
      for real money.
- [ ] **[OWNER]** Review any active/scheduled promotions in `/admin` → Promotions before
      launch — a live discount or bonus-credit promotion left enabled from
      testing will apply to real customers/real money.
- [ ] **[OWNER]** Decide whether `Build & Validate` (Docker-in-production) will be
      offered; if yes, plan to mount the host Docker socket (accepted
      root-equivalent tradeoff).
- [ ] **[OWNER]** Supply the real legal operator identity to replace the
      `[LEGAL OPERATOR NAME]` placeholder on `/privacy.html` and `/terms.html`.
- [ ] **[OWNER]** Confirm Scotland is the correct governing jurisdiction for
      `/terms.html`/`/refunds.html`, or supply the correct one.
- [ ] **[OWNER]** Supply real business/registered address, company registration number,
      and VAT details if legally applicable — none are currently present.
- [ ] **[OWNER]** Set `Support__Email` to a real, monitored support address (used across
      Support/Privacy/Terms/Refunds and for billing/refund/account-access
      enquiries).
- [ ] **[OWNER]** Have the real legal operator/counsel review the final wording on
      `/privacy.html`, `/terms.html`, and `/refunds.html` before launch — the
      current text accurately describes real application behaviour but is
      not a substitute for legal review.

## DEPLOYMENT

- [ ] **[DEPLOYMENT]** Set all required environment variables (see `.env.example` and
      README → "Required environment variables"): `ASPNETCORE_ENVIRONMENT=Production`,
      `ConnectionStrings__DefaultConnection`, `Planning__DefaultProvider`,
      the selected provider's API key, `Artifacts__RootPath`,
      `App__PublicBaseUrl`, `Support__Email`, `Email__FromAddress`,
      `Email__FromName`, `Resend__ApiKey`.
- [ ] **[EXTERNAL]** Create a Resend account and API key for the production sending
      domain, and verify that domain in Resend.
- [ ] **[DEPLOYMENT]** Apply database migrations before starting the new version:
      `dotnet ef database update --project src/WPAIPlugin.Api --startup-project src/WPAIPlugin.Api --connection "<production connection string>"`.
- [ ] **[DEPLOYMENT]** Deploy the image/build (`docker build -t wpaiplugin-api .` or
      `dotnet publish` + `dotnet WPAIPlugin.Api.dll`).
- [ ] **[DEPLOYMENT]** Mount persistent volumes for `Artifacts:RootPath` and
      `DataProtection:KeyRingPath`.
- [ ] **[VERIFY]** Confirm `GET /health/live` returns `200`.
- [ ] **[VERIFY]** Confirm `GET /health/ready` returns `200` (database reachable).
- [ ] **[DEPLOYMENT]** Restart the app once after the owner's account has registered, so
      `AdminBootstrapper` promotes `Admin:BootstrapEmail` (see README →
      "Admin bootstrap").
- [ ] **[VERIFY]** Log in as that account and confirm `/admin` loads and
      `GET /api/account/me` reports `"isAdmin": true`.
- [ ] **[VERIFY]** Confirm `/admin` → System tab shows the expected AI provider
      configured, Docker validation availability, artifact storage writable,
      Transactional email configured = Yes, and (once Stripe is configured)
      Stripe configured = Yes.
- [ ] **[VERIFY]** Confirm a real forgot-password request delivers a working reset email
      via Resend, and that the reset link uses the real production domain
      (`App__PublicBaseUrl`), not `localhost` or any other host.
- [ ] **[VERIFY]** Confirm `/privacy.html`, `/terms.html`, `/refunds.html`, `/support.html`
      all load and show the real configured support email.

## STRIPE LIVE ACTIVATION

Stripe runs in test mode until every step below is completed. See README →
"Stripe live-mode activation (owner-only)" and
[FINAL-LAUNCH-READINESS.md](FINAL-LAUNCH-READINESS.md) → "Stripe LIVE
activation runbook" for the full runbook.

- [ ] **[EXTERNAL]** Complete Stripe account activation (business details,
      payout bank account).
- [ ] **[EXTERNAL]** Obtain the live `sk_live_...` secret key.
- [ ] **[DEPLOYMENT]** Set `Stripe__SecretKey` in production configuration only.
- [ ] **[EXTERNAL]** Create the production webhook endpoint in the Stripe
      Dashboard pointing at `https://<domain>/api/payments/webhook`.
- [ ] **[EXTERNAL]** Obtain the live webhook signing secret.
- [ ] **[DEPLOYMENT]** Set `Stripe__WebhookSecret` in production configuration only.
- [ ] **[DEPLOYMENT]** Set `Stripe__PublicBaseUrl` to the real production `https://` domain.
- [ ] **[VERIFY]** Confirm the reverse proxy passes the webhook route's raw request body
      through unmodified (required for signature verification).
- [ ] **[OWNER]** Verify the configured GBP pack prices one more time before real money
      is at stake.
- [ ] **[OWNER, exactly once]** Perform one controlled live purchase
      with a real card; confirm the webhook is received, credits are granted
      exactly once, and `/admin` → Revenue reflects it correctly. **This
      audit did not perform this step — it is owner-executed only. See
      FINAL-LAUNCH-READINESS.md → "First live purchase checklist" for the
      exact verification steps.**

## POST-DEPLOYMENT

- [ ] **[VERIFY]** Confirm the homepage, registration, login, dashboard, builder, My
      Plugins, and billing pages all load correctly over the real domain.
- [ ] **[VERIFY]** Confirm a real signup grants exactly 5 credits and 2 free builds.
- [ ] **[VERIFY]** Confirm `/admin` → Overview, Users, Plugins, AI Usage, Credits,
      Promotions, Audit Log, Revenue, and System tabs all load with real data.
- [ ] **[VERIFY]** Monitor `Artifacts:RootPath` and PostgreSQL disk usage — both grow
      without automatic pruning.
- [ ] **[VERIFY]** Confirm logs contain no secrets (API keys, Stripe keys, Resend keys,
      connection string passwords, password hashes, reset tokens/URLs) —
      spot-check after first real traffic.

## BACKUP

- [ ] **[DEPLOYMENT]** Schedule regular `pg_dump` backups of the PostgreSQL database.
- [ ] **[DEPLOYMENT]** Schedule regular backups of the `Artifacts:RootPath` directory (plain
      recursive file copy is sufficient).
- [ ] **[DEPLOYMENT]** Include the `DataProtection:KeyRingPath` directory in the backup plan
      (session-continuity only, not a security requirement if lost).
- [ ] **[DEPLOYMENT]** Take database and artifact backups close together in time, to avoid a
      restored project/version row referencing a ZIP the restore doesn't
      have.
- [ ] **[VERIFY]** Verify a `pg_restore` has actually been tested at least once in a
      non-production environment before relying on it. **Not performed
      during this or the prior audit — no PostgreSQL/Docker was reachable in
      this session's environment; see FINAL-LAUNCH-READINESS.md.**

## ROLLBACK

- [ ] **[DEPLOYMENT]** Keep the previous deployed image/build available for immediate
      redeploy.
- [ ] **[VERIFY]** Only roll back a migration if it was written to be reversible and the
      data implications are understood — treat all current migrations as
      forward-only (matches existing project convention).
- [ ] **[DEPLOYMENT]** If a bad deploy needs reverting: redeploy the previous image/build,
      confirm `GET /health/ready` returns `200`, and confirm no migration
      needs to be rolled back first.
- [ ] **[OWNER]** If Stripe live mode was just activated and something is wrong,
      disable the Stripe webhook endpoint in the Stripe Dashboard
      immediately (stops further credit grants) rather than only reverting
      the app.
