# WPAIPlugin — Product & Feature Spec

Reflects the application as it currently exists in this repository. Written
from the shipped code, not aspirational roadmap items — see "Out of scope /
known gaps" at the end for what's deliberately not built.

## 1. Product overview

**What it is:** a SaaS tool that turns a plain-English description into a
real, installable WordPress plugin. A user describes what they want, an AI
provider turns that into a structured plan, and a deterministic builder
turns the approved plan into PHP source from fixed templates — never AI-
generated code. The output is a downloadable ZIP, optionally validated by
actually installing and activating it in a disposable WordPress instance.

**Who it's for:** WordPress site owners, agencies, and developers who need
a small, well-defined custom plugin (a shortcode, a custom post type, a
settings page, custom fields, a scheduled task) without hand-writing
boilerplate PHP or hiring a developer for a simple job.

**Core value proposition:**
- *Plan before you build* — the AI's job is planning, never writing plugin
  code, so the user reviews a structured spec before anything is generated.
- *Deterministic, inspectable output* — every plugin comes from the same
  fixed PHP templates (`WPAIPlugin.Templates`), so behavior is predictable
  and auditable, not "whatever the model felt like generating."
- *Provable, not just claimed* — "Build & Validate" actually installs and
  activates the generated ZIP in a real, disposable WordPress + MariaDB
  environment via Docker before the user trusts it.
- *Pay only for what you use* — a credit ledger, not a subscription; a
  small free signup grant, then GBP credit packs via Stripe.

## 2. How it works (user flow)

1. **Describe** — the user writes a plain-English description of the
   plugin they want (max 2,000 characters).
2. **Plan** — an AI provider (OpenAI or Anthropic, server-selected) turns
   that description into a structured `PluginSpec` (shortcode / custom post
   type / settings page / custom fields / scheduled task definitions). The
   AI never writes PHP or any plugin source — only structured planning
   data.
3. **Review** — the user sees the plan (what will be generated) before
   committing anything.
4. **Build** — the deterministic `PluginBuilder` renders the approved spec
   into real PHP files from fixed templates and zips them. Costs 1 credit.
5. **Validate (optional)** — "Build & Validate" additionally lints every
   generated PHP file, then spins up a disposable WordPress + MariaDB
   Docker environment, installs WordPress, installs and activates the
   plugin, and confirms it stayed active. Costs 2 credits total.
6. **Download** — the ZIP is saved to the user's account (My Plugins) and
   downloadable at any time through an authenticated, ownership-checked
   endpoint.

## 3. Feature spec

### 3.1 Public marketing site (`/`)

Static homepage (no auth required): explains the workflow above, shows two
labelled, non-interactive example previews (a plan preview and a saved-
plugin preview), and lists the five build capabilities (below). Signed-out
visitors are routed to register/login; signed-in visitors see
Builder/Dashboard links instead. No external fonts, scripts, or assets.

### 3.2 Accounts

- **Register** (`POST /api/account/register`) — email + password via
  ASP.NET Core Identity. Creates the Identity user, a `CreditAccount`, and a
  signup-grant ledger entry in one database transaction. Signs the user in
  immediately.
- **Login / logout** — standard Identity cookie auth (`HttpOnly`,
  `SameSite=Lax`, `Secure` outside Development).
- **No email verification, MFA, or password reset** exist today (see "Out
  of scope" below).
- **Admin flag** — `GET /api/account/me` reports the caller's own `isAdmin`
  status, derived from a real Identity role, not a hardcoded check.

### 3.3 AI planning

- One request per plan (`POST /api/plugins/plan`), authenticated, rate
  limited (10/user/minute).
- Provider is server-selected (`Planning:DefaultProvider` = `openai` or
  `anthropic`); the browser never chooses or influences the provider.
- Every plan attempt is recorded as an immutable `AiUsageEvent` — provider,
  model, input/output/total tokens, an estimated cost computed once at
  record time from server-side pricing configuration, duration, and
  success/failure. No prompt or response text is ever stored.
- Planning is **free** — it never touches the credit ledger.

### 3.4 Plugin builder — five supported capabilities

Deterministic PHP generation from fixed templates
(`WPAIPlugin.Templates`), never AI-authored source:

| Capability | What it generates |
| --- | --- |
| **Shortcodes** | A registered `[shortcode]` that renders content into any page/post. |
| **Custom Post Types** | A new content type (e.g. staff profiles) with its own admin UI. |
| **Custom Fields** | Extra structured data attached to posts/content. |
| **Settings Pages** | A dedicated wp-admin settings screen for the plugin's options. |
| **Scheduled Tasks** | A recurring background task via WordPress's own cron scheduling. |

A plan can combine multiple capabilities in one plugin (e.g. the homepage's
own example: a staff directory using Custom Post Type + Custom Fields +
Shortcode together).

### 3.5 Build & Validate

- **Standard build** (1 credit) — generates and zips the plugin. Never
  touches Docker.
- **Build & Validate** (2 credits) — additionally: `php -l` lints every
  generated file, then a disposable, GUID-isolated Docker Compose stack
  (WordPress + MariaDB + WP-CLI) installs WordPress non-interactively,
  installs the plugin ZIP, activates it, and confirms it's still active.
  Always torn down in a `finally` block (containers, volumes, temp files),
  even on timeout (`Validation:TimeoutSeconds`, default 120s) or
  cancellation. If Docker is unavailable, fails cleanly
  (`ValidationUnavailable`) and the charge is refunded — never silently
  falls back to an unvalidated build.

### 3.6 Projects & versions ("My Plugins")

- Every successful build is saved as a `Project` + `Version` under the
  owning user.
- `GET /api/projects`, `GET /api/projects/{id}` — the caller's own projects
  only.
- `GET /api/projects/{id}/versions/{id}/download` — authenticated,
  ownership-checked ZIP download. Artifacts are stored outside `wwwroot`,
  addressed only by server-generated GUID path segments (no path-traversal
  surface), and never served by static-file middleware.

### 3.7 Credits

- Server-authoritative balance + an immutable, append-only ledger
  (`CreditTransaction`) — the browser never calculates or trusts a locally-
  computed balance.
- **Signup grant**: 5 credits (`Credits:SignupGrant`), configurable.
- **Costs**: standard build 1 credit, validated build 2 credits total
  (`Credits:StandardBuildCost` / `ValidatedBuildCost`).
- A failed build (including a storage failure) refunds the charge; refund
  logic is idempotent and request-independent.
- Every balance change (signup grant, build charge, refund, admin
  adjustment, purchase grant) is a signed ledger entry plus a matching
  balance update in the same database transaction — there is no direct
  `Balance = X` write anywhere in the codebase.

### 3.8 Billing — Stripe credit purchases

- Three GBP credit packs, priced and defined **server-side only**:

  | Pack | Credits | Price |
  | --- | --- | --- |
  | Starter | 25 | £4.99 |
  | Builder | 75 | £9.99 |
  | Pro | 200 | £19.99 |

- `/billing.html` — current balance, the three packs, "Buy now" (redirects
  to Stripe-hosted Checkout), and purchase history.
- The browser sends only a `PackId`; price/currency/credits are always
  resolved server-side — a forged browser price has zero effect.
- Credits are granted **exclusively** by a signature-verified Stripe
  webhook (`POST /api/payments/webhook`) — never by the browser's
  success/cancel redirect, which only re-reads the authoritative server
  balance.
- Webhook delivery is idempotent at two independent levels (processed-event
  table + purchase-status guard) — retries and duplicate deliveries can
  never double-grant credits.
- **No Stripe subscriptions.** **No automatic customer-initiated refunds** —
  a Stripe monetary refund is recorded for admin visibility only; any
  credit correction is a separate, explicit, audited admin action.
- Stripe is optional infrastructure, not a startup requirement — checkout
  returns a clean `503` until `Stripe:SecretKey`/`PublicBaseUrl` are
  configured.

### 3.9 Admin panel (`/admin`)

Gated by a real Identity `Admin` role (401 anonymous, 403 non-admin), never
a hardcoded email check. Config-driven bootstrap
(`Admin:BootstrapEmail`) — see `README.md → Admin bootstrap` for the exact
operator runbook.

| Tab | What it shows |
| --- | --- |
| **Overview** | Totals: users, credit balance held, credits consumed/refunded, projects, versions, standard vs. validated builds, AI request count/tokens/estimated cost. |
| **Users** | Search by email; per-user detail includes plugin/version counts, AI usage, recent credit ledger activity, and lifetime purchase economics. |
| **Plugins / Builds** | Every project with owner, version count, and validated status; filterable by user/validated. |
| **Credits** | Aggregate credit analytics: signup grants, admin adjustments (net/granted/deducted), gross/net consumption, purchased credits. |
| **AI Usage** | Request/success/failure counts, token totals, estimated cost, breakdowns by provider/model, a daily time series. |
| **Audit Log** | Every admin credit adjustment and account lock/unlock — who, what, target, when, why. Read-only, immutable. |
| **Revenue** | Gross/net revenue, refunds, successful/failed purchases, credits sold, purchasing customers, average purchase, day-by-day breakdown — sourced from `Purchase` records only, never the credit ledger. Includes a clearly-labelled "approximate contribution estimate" (revenue vs. estimated AI spend) — explicitly never called profit. |
| **System** | App version, environment, database health, AI provider configured, Docker validation available, artifact storage writable, Stripe configured — booleans only, never a secret value. |

Admin mutations (credit adjustment, account lock/unlock) go through the
same ledger-backed `CreditService`/`AdminAuditService` as everything else —
no direct balance write, every action produces an immutable audit entry.

### 3.10 Security

- CSRF (antiforgery token required on every state-changing request),
  per-user/per-IP rate limiting on planning, builds, validated builds,
  checkout, and account endpoints, request body size limits.
- Security headers: CSP (self-only scripts/connections), HSTS,
  `X-Content-Type-Options`, `X-Frame-Options: DENY`,
  `Referrer-Policy`, `Permissions-Policy`.
- Identity/antiforgery cookies: `HttpOnly`, `Secure` outside Development,
  `SameSite=Lax`/`Strict`.
- Reverse-proxy aware (`ForwardedHeaders`, explicit trust list only).
- Generic error responses outside Development — no stack trace, SQL,
  Docker internals, or provider secrets ever returned or logged.
- `GET /health/live` / `GET /health/ready` — anonymous, no internal detail
  exposed.

## 4. Tech stack

- **Backend**: ASP.NET Core 8 (C#), Entity Framework Core, PostgreSQL,
  ASP.NET Core Identity.
- **AI providers**: OpenAI or Anthropic (server-selected, planning-only).
- **Payments**: Stripe Checkout (Stripe.net SDK) — redirect-based, this app
  never handles raw card data.
- **Frontend**: static HTML/CSS/vanilla JS (no framework, no build
  pipeline) served from `wwwroot`.
- **Validation environment**: Docker Compose (disposable WordPress +
  MariaDB + WP-CLI).
- **Deployment**: multi-stage Docker image, non-root runtime; PostgreSQL,
  generated ZIPs, and Data Protection keys are the three things that must
  persist across restarts.

## 5. Out of scope / known gaps

Deliberately not built, or not yet ready for a public launch:

- No email verification, password reset, or any outbound email from the
  app itself (Stripe may send its own receipt emails, a Dashboard-level
  Stripe setting unrelated to this codebase).
- No Stripe subscriptions — one-time credit-pack purchases only.
- No automatic customer-initiated refunds.
- No self-service "promote a second admin" — the bootstrap mechanism only
  ever acts while zero admins exist.
- **No Privacy Policy, Terms of Service, Refund Policy, or Contact/Support
  page exists yet** — a real launch blocker once real money is involved
  (see `LAUNCH-CHECKLIST.md`).
- Stripe currently runs in test mode only; going live is an owner-only
  configuration step (see `README.md → Stripe live-mode activation`).
- No automatic pruning of generated ZIP storage — grows without bound.
