# Authenticated SaaS UI / app shell completion

Milestone scope only: visual/UX modernization of authenticated pages to match
the homepage design language. No Stripe, payments, subscriptions, credit-rule,
generation-behavior, or API-contract changes.

## Files added

- `src/WPAIPlugin.Api/wwwroot/app.css` — shared design tokens/base/components for authenticated pages (dark theme, mint accent, matches `site.css` tokens).
- `src/WPAIPlugin.Api/wwwroot/nav.js` — shared top-navigation partial for Dashboard/My Plugins/plugin-detail pages: injects nav markup, highlights current page, fills email/credits from existing endpoints, wires logout.
- `AUTHENTICATED-UI-COMPLETION.md` — this report.

## Files changed

- `src/WPAIPlugin.Api/wwwroot/login.html`, `register.html`, `dashboard.html`, `myplugins.html`, `plugin.html`, `builder.html` — redesigned markup using `app.css`.
- `src/WPAIPlugin.Api/wwwroot/register.js` — added client-side confirm-password check (no API change; server contract unchanged).
- `src/WPAIPlugin.Api/wwwroot/dashboard.js` — now reveals content after auth check and independently populates credit balance and real project count (`/api/projects`), no longer duplicates nav rendering (delegated to `nav.js`).
- `src/WPAIPlugin.Api/wwwroot/myplugins.js`, `plugin.js` — updated to render into new markup/classes; same endpoints, same redirect-on-401 behavior.
- `tests/WPAIPlugin.Generator.Tests/WebAppTests.cs` — updated expected builder-page substring from "Plan Plugin" to "Create Plan" (button copy changed).
- `tests/WPAIPlugin.Generator.Tests/Security/ProductionSecurityTests.cs` — static-file-serving assertion now checks `/site.css` instead of the removed `/auth.css`.

## Files removed

- `src/WPAIPlugin.Api/wwwroot/auth.css` — superseded by `app.css`; nothing referenced it after the redesign.

## Shared app-shell implementation

Single top-navigation pattern used everywhere: Dashboard, My Plugins, and the
plugin-detail page mount `<nav id="appNav" data-active="...">` and load
`nav.js`, which injects brand + Dashboard/Builder/My Plugins links (current
page marked with `aria-current="page"`), a credit-balance indicator, and an
account menu (`<details>`/`<summary>`, no JS framework) with email + Log out.
`nav.js` itself gates the page: an unauthenticated `/api/account/me` redirects
to `login.html` before content renders.

The Builder page keeps its own lighter dual-state nav (logged-out vs
logged-in), because `builder.html` is intentionally served to signed-out
visitors too (existing `WebAppTests`/`ProviderKeyExposureTests` assert
anonymous 200). Reusing `nav.js` there would incorrectly redirect anonymous
visitors away from the public builder preview, so `app.js`'s existing
`refreshAuthNav()` toggle was kept and only restyled with the shared classes.

## CSS/design structure

`app.css` mirrors `site.css`'s tokens (dark background, mint accent, borders,
radii) plus shared components: `.panel`, `.button`/`.button-secondary`,
form fields, `.notice` (error/warn/ok), `.app-nav`/`.app-links`/`.app-account`,
`.stat-grid`, `.project-list`/`.project-card`, `.version-row`, and builder-
specific `.step-label`/`.build-actions`/`.success-actions`. No Bootstrap,
Tailwind, npm, build pipeline, remote fonts, or remote CSS. `site.css` is
untouched and still loads only on the homepage.

## Login / Register changes

Both redesigned as a centered auth panel: brand mark, heading, lead line,
email/password fields, submit button, loading state, error notice, and a
link to the other auth page plus back-to-homepage link. No social auth,
forgot-password, or remember-me controls added (none existed before).
Register adds a client-only confirm-password field and a password-rules
hint reflecting ASP.NET Identity's actual default policy (min 6 chars,
upper/lower/digit/symbol) — no invented rule, no server contract change.
The "100 test credits" marketing line was deliberately omitted: the signup
grant is a server config value (`CreditOptions.SignupGrant`) with no public
endpoint exposing it, so hardcoding it in the page would embed a business
rule in the frontend: the task instructions say to omit rather than do that.

## Dashboard changes

Now the authenticated landing page: heading, lead, "New Plugin" CTA, a
two-tile stat grid (Credits balance, real "N saved plugins" count via
`/api/projects`), a "View My Plugins" link, and a static "Plan → Build →
Validate → Download" strip. No invented metrics (no usage %, no fake charts).

## Builder changes (highest priority)

Restructured into the three-step flow: Step 1 describe (textarea + "Create
Plan — free"), Step 2 review plan (existing field-grid + feature summary +
unsupported-requirements notice, unchanged data model), Step 3 build (two
clearly distinguished actions — "Build Plugin" vs "Build & Validate" with a
one-line description each). Every element ID `app.js` depends on was
preserved exactly (`description`, `planBtn`, `f-name`/`f-slug`/etc.,
`featureSummary`, `buildBtn`, `buildValidateBtn`, `creditBalance`,
`buildSuccess`, `downloadZipLink`, `viewPluginLink`, `createAnotherBtn`,
`loggedOutNav`/`loggedInNav`/`logoutLink`) — zero JS logic changes, only
markup/CSS.

## Plan-preview changes

Same field-grid and `<dl>` feature summary as before (name/slug/description/
version/author, features badge list, optional custom-post-type/settings-page/
custom-fields/scheduled-task rows) — no fields invented or removed, matching
the real `PluginSpec` shape `app.js` already renders.

## Build/result/error states

Loading states show a spinner + explicit text ("Generating plugin plan…",
"Building plugin…", "Building and validating plugin…"). Buttons disable
during their own in-flight request (existing `app.js` behavior, untouched).
Success panel shows "Plugin built successfully", the existing success
message (credits charged/remaining, validation checklist when validated),
and three actions (Download, View in My Plugins, Build Another). Error
states render the existing server error text in a `.notice.error`, including
the existing 402 insufficient-credits message with required/balance figures.
No stack traces, paths, or internal details were ever rendered here, and
none were added.

## My Plugins changes

Card-per-project list: name, slug, version/revision, validated indicator,
updated date, View + Download-latest actions. Empty state: "You haven't
built any plugins yet." + a build CTA. No invented fields.

## Project/version UI changes

Plugin-detail page: name, slug, created/updated dates, and a version list
(revision number, plugin version, validated indicator, created date,
Download ZIP). Ownership/401 handling unchanged (redirects to login on 401,
shows "Plugin not found" on any other non-OK/exception).

## Credit-display behaviour

Balance shown in exactly three places: shared nav (Dashboard/My
Plugins/plugin pages), Dashboard stat tile, and Builder's own credit line
(which also shows per-action costs) — all sourced from `GET /api/credits`,
never computed client-side. No Buy/Upgrade/Subscribe/Billing controls were
added (none existed before).

## Mobile/responsive behaviour

`app.css` collapses `.app-links` and `.app-credits` under 767px (account
menu and brand remain, matching the homepage's own nav-collapse pattern),
`.field-grid`/`.stat-grid` drop to one column, and `.container`/
`.container-medium`/`.container-narrow` use the same viewport-based gutters
as `site.css`. Verified via CSS media queries only (no browser tool
available — see Browser/manual verification below).

## Accessibility verification

Semantic headings (`h1`/`h2`) on every page, labeled form fields, native
`<details>/<summary>` for the account menu (no custom ARIA needed), visible
`:focus-visible` outline reused from the homepage token, `role="status"`
kept on live credit/loading text, `aria-current="page"` on the active nav
link, `aria-label` on navigation regions, alert role on error notices.
Colour is never the only state signal (validated/not-validated and
error/ok states also carry text). No inaccessible custom controls were
introduced.

## CSP/security verification

Production CSP unchanged and re-verified live:
`default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; connect-src 'self'; object-src 'none'; base-uri 'none'; frame-ancestors 'none'; form-action 'self'`.
All new/changed JS ships as external files loaded via `<script src>`; no
inline scripts or inline event handlers were added anywhere (`nav.js` uses
`addEventListener`). CSRF flow (`apiFetch` → `/api/account/csrf` →
`X-CSRF-TOKEN`) is untouched and still used by every mutating call. No
provider keys, secrets, or internal identifiers appear in any page/script.

## Automated test result

`dotnet build`: **0 warnings, 0 errors**.
`dotnet test`: **204/204 passing** (one pre-existing assertion updated for
the intentional "Plan Plugin" → "Create Plan" button-copy change; one
static-file assertion repointed from the removed `/auth.css` to the
still-served `/site.css`).
`node --check` passed for every changed/added JS file (`login.js`,
`register.js`, `dashboard.js`, `myplugins.js`, `plugin.js`, `nav.js`,
`app.js`, `api.js`, `index.js`).
`git diff --check`: clean (only the existing LF/CRLF conversion notices).

## Browser/manual verification

Started the app against the existing local development database. Verified
over real HTTP: every authenticated page (`login`, `register`, `dashboard`,
`builder`, `myplugins`, `plugin`) and every new/changed static asset
(`app.css`, `nav.js`, and the page JS files) returns `200`. Registered a
fresh test account through the real `/api/account/register` (with a real
antiforgery exchange), confirmed `/api/account/me`, `/api/credits`
(`balance: 100`, standard/validated costs `1`/`2`), and `/api/projects`
(`[]`, matching the dashboard's "0 saved plugins" / My Plugins empty state)
return exactly the shapes the new JS consumes, then logged out successfully.
Confirmed the Production CSP header is unchanged on `dashboard.html`.

The in-app browser tool and Playwright are both unavailable in this
environment (same limitation noted in the two prior milestone reports), so
visual rendering, computed contrast, and on-page console/DevTools checks at
1440/1024/768/390/320px could not be performed directly. Layout correctness
at those widths is supported by the CSS media queries added to `app.css`
(mirroring the already-verified `site.css` breakpoints) but is not
independently confirmed by a rendered screenshot in this session.

## Backend changes

None. `AccountController`, `ProjectsController`, `CreditsController`,
`PluginsController`, credit rules, CSRF, rate limits, CSP, and all DTOs are
untouched by this milestone. The two test-file edits above are the only
non-`wwwroot` changes, and both are assertions about frontend copy/asset
naming, not backend behavior.

## Deliberately deferred

Direct browser/visual verification (tooling unavailable, as above). Signup
credit amount is not shown on the register page (no authoritative public
endpoint for it; omitted rather than hardcoded, per the milestone's own
fallback instruction). Stripe, payments, subscriptions, billing UI, and any
new backend capability — out of scope by explicit instruction.

## git status --short

```
 M PLANS.md
 M README.md
 M scripts/Validate-GeneratedPlugin.ps1
 M src/WPAIPlugin.Api/Controllers/AccountController.cs
 M src/WPAIPlugin.Api/Controllers/PluginsController.cs
 M src/WPAIPlugin.Api/Controllers/ProjectsController.cs
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
 M src/WPAIPlugin.Planning/Providers/Anthropic/AnthropicPlanningProvider.cs
 M src/WPAIPlugin.Planning/Providers/OpenAI/OpenAIPlanningProvider.cs
 M tests/WPAIPlugin.Generator.Tests/Accounts/AccountControllerTests.cs
 M tests/WPAIPlugin.Generator.Tests/Accounts/AccountTestFactory.cs
 M tests/WPAIPlugin.Generator.Tests/Credits/CreditApiTests.cs
 M tests/WPAIPlugin.Generator.Tests/Projects/ProjectsControllerTests.cs
 M tests/WPAIPlugin.Generator.Tests/Projects/ProjectsTestFactory.cs
 M tests/WPAIPlugin.Generator.Tests/Security/ProviderKeyExposureTests.cs
 M tests/WPAIPlugin.Generator.Tests/WebAppTests.cs
?? HOMEPAGE-COMPLETION.md
?? MILESTONE13-COMPLETION.md
?? src/WPAIPlugin.Api/Security/
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
?? tests/WPAIPlugin.Generator.Tests/Security/CsrfClient.cs
?? tests/WPAIPlugin.Generator.Tests/Security/ProductionSecurityTests.cs
```

(Everything above `app.css`/`nav.js`/the `wwwroot/*.js` and `*.html` edits
and the two test-file edits in the "?? " list was already present/untracked
from earlier milestones and is unrelated to this one; nothing was reverted
or discarded.)

No commit, push, or tag was performed.
