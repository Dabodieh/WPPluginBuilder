# UI quality / consistency / edge-case polish pass

Polish and verification milestone only. No Stripe, payments, backend
features, credit-rule changes, or generation-behavior changes. No redesign —
only fixes to inconsistencies and robustness gaps found in the existing UI.

## Files changed

- `src/WPAIPlugin.Api/wwwroot/app.js` — fixed a real double-submit bug (see
  Builder fixes), removed a dead parameter, moved an inline style into CSS,
  fixed two credit-pluralization edge cases.
- `src/WPAIPlugin.Api/wwwroot/app.css` — consistency and robustness fixes
  (see below); removed one dead selector (`.builder-header`, never used in
  any page).
- `src/WPAIPlugin.Api/wwwroot/builder.html` — removed a dead `id="authGate"`
  leftover from an earlier iteration; added `role="status"`/`role="alert"`
  to two notices that were missing it.
- `src/WPAIPlugin.Api/wwwroot/plugin.html` — added missing `role="alert"` to
  the "Plugin not found" notice.
- `src/WPAIPlugin.Api/wwwroot/login.html`, `register.html` — removed a
  redundant `auth-title` class now that `h1` has a shared base size.

No JS/HTML files were added or removed this pass (the shared shell from the
previous milestone — `app.css`, `nav.js`, per-page `.js` files — is unchanged
in structure).

## Visual consistency fixes

- **Heading size**: `dashboard.html`, `builder.html`, `myplugins.html`, and
  `plugin.html` had no explicit `h1` size and were falling back to the
  browser default (~32px), while `login.html`/`register.html` used a
  page-specific 28px rule. Added a single shared `h1{font-size:28px}` rule
  to `app.css` and removed the now-redundant per-page `auth-title` class, so
  every authenticated page (and both auth pages) shares one heading scale.
- **Button height**: `.button` was 46px/`.button-small` 40px in `app.css`
  versus the homepage's 48px/42px in `site.css`. Aligned both to 48px/42px
  so buttons feel identical in height whether a user is on the public
  homepage or inside the app.
- Card borders, radius, spacing tokens, muted-text color, and the three
  notice states (error/warn/ok) were already consistent across all pages
  (all pull from the same `app.css` custom properties) — reviewed, no
  further changes needed.
- Credit display wording ("Credits: N" in the nav and Builder, "N
  remaining" on the Dashboard tile) was reviewed and left as-is: the two
  phrasings are contextually appropriate (inline label vs. a tile already
  titled "Credits"), not an inconsistency.

## Builder fixes

- **Double-submit bug (real, fixed)**: "Build Plugin" and "Build & Validate"
  each only disabled themselves while their own request was in flight — a
  user could click one, then immediately click the other, firing two
  concurrent `/api/projects/build` calls (two charges/builds racing). Fixed
  `submitProjectBuild` in `app.js` to disable **both** build buttons for the
  duration of either request, re-enabling both when it settles. The "Create
  Plan" button was already self-guarding and independent (it hides
  `resultCard`, which contains the build buttons, the instant it's
  clicked), so no cross-step race exists there.
- Cleaned up a dead `buttonId` parameter left over from the single-button
  disable logic once both buttons are always toggled together.
- Success-message line breaks were being force-set via `element.style.whiteSpace
  = "pre-line"` in JS on every successful build; moved that to a static
  `#buildSuccessMessage{white-space:pre-line}` rule in `app.css` (same
  visual result, no per-call inline-style mutation).
- Credit-cost wording fixed for correctness at the edges: "Build & Validate
  — N credit**s**" always used the plural even if the configured cost were
  1, and the insufficient-credits message ("You need N credits…") did the
  same. Both now pluralize correctly, matching the "Build Plugin" button's
  existing singular-aware wording. (Today's configured costs are 1/2, so
  this was latent, not currently visible — fixed so it's correct if costs
  ever change.)
- No fake progress percentages exist or were added. Loading states remain
  simple spinner + text ("Generating plugin plan…", "Building plugin…",
  "Building and validating plugin…").
- Reviewed long-input handling: description textarea, plan fields, feature
  badges, and unsupported-requirements list all wrap or scroll internally
  already (`textarea` resizes, inputs use `width:100%`, `.badge-list` and
  `<ul>` wrap). No changes needed there.

## Form UX fixes

- Added a Chromium `-webkit-autofill` override (`app.css`) so browser
  autofill on the email/password fields renders with the dark theme's
  background/text colors instead of the default light-yellow autofill
  style, which was visually broken against the dark UI.
- Reviewed labels, `required` attributes, `autocomplete` values
  (`username`/`current-password`/`new-password`), disabled-state styling,
  loading-state text, and Enter-key submission (both auth forms are real
  `<form>` elements with a `type="submit"` button, so Enter already submits
  correctly) — all already correct, no changes needed.
- No new authentication features were added. Register's confirm-password
  field (added in the previous milestone) remains client-side only; no
  server contract change.

## Edge-state fixes

- **Long plugin names/slugs**: `h1`, `h2`, `h3` and the project/version meta
  text had no `overflow-wrap` rule, so a very long AI-generated plugin name
  could in principle overflow its card instead of wrapping. Added
  `overflow-wrap:break-word` to headings and to `.project-meta`,
  `.version-meta`, `.page-lead`, and `.detail-meta`.
- **Project/version card layout under long content**: `.project-card` and
  `.version-row` are flex rows with a text column and an actions column;
  the text column had no `min-width:0`, which is the classic flexbox trap
  that lets long unbroken text force the row wider than its container
  instead of wrapping. Added `min-width:0` plus a sane `flex-basis` to both
  text columns so long names/slugs wrap within the card instead of pushing
  the download/view buttons off-edge.
- Zero/one/many plugins, missing optional fields, zero credits, and
  network-failure paths were reviewed: `myplugins.js` already shows the
  "You haven't built any plugins yet." empty state when the array is empty
  and a plain vertical list otherwise (no pagination exists server-side, so
  none was added client-side); `dashboard.js`, `myplugins.js`, `plugin.js`
  all show explicit fallback text ("Unavailable. Please refresh.", "Plugin
  not found.") rather than raw errors on any fetch failure; `plugin.js`
  already redirects to `login.html` on a 401. No changes needed beyond the
  wrap fixes above.

## Responsive fixes

- Reviewed `app.css` media queries (`767px`, `380px` breakpoints) against
  every page: nav collapses (`app-links`/`app-credits` hidden, account menu
  remains), `field-grid`/`stat-grid` drop to one column, container gutters
  shrink appropriately — consistent with the homepage's own breakpoints.
- The `overflow-wrap`/`min-width:0` fixes above are the responsive-relevant
  changes: they prevent long content from causing horizontal overflow at
  any width, not just the tested ones.
- See "Browser verification result" below for what was and wasn't possible
  to confirm visually at each target width.

## Accessibility fixes

- Added `role="alert"` to the "Plugin not found" notice on `plugin.html`
  (every other error notice already had it; this one was missed).
- Added `role="status"` to the Builder's success notice and its
  "not currently supported" warning notice, so screen readers announce
  build completion and unsupported-feature warnings the same way loading
  and error states already were.
- Reviewed heading order (one `h1` per page, `h2` used only for section/card
  titles, no skipped levels), keyboard access (all interactive elements are
  native `button`/`a`/`input`/`details`-`summary`, no custom widgets),
  visible `:focus-visible` (inherited from the shared token, unchanged),
  label association (every input has a matching `<label for>`), and
  color-only meaning (validated/not-validated and error/ok states all carry
  text, not just color) — all already correct.

## Security/CSP result

Re-confirmed on the running app:
- CSP header unchanged:
  `default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; connect-src 'self'; object-src 'none'; base-uri 'none'; frame-ancestors 'none'; form-action 'self'`.
- No inline `<script>` blocks or inline event handlers anywhere in
  `wwwroot` (`grep` for `<script>`, `onclick=`, `onsubmit=`, `onload=`
  across all `.html` files returned nothing).
- No `localStorage`/`sessionStorage` usage anywhere in `wwwroot` (`grep`
  across all `.js`/`.html` files returned nothing) — credit balance is
  always fetched fresh from `/api/credits`.
- No remote scripts, fonts, or stylesheets; everything still loads from
  same-origin static files.
- CSRF flow (`apiFetch` → `GET /api/account/csrf` → `X-CSRF-TOKEN` header)
  is untouched.
- The SaaS UI still calls only `/api/projects/build`, never
  `/api/plugins/build` (`grep` confirmed no reference to the legacy route
  anywhere in `wwwroot`).
- No ownership-check, ID/claim-exposure, or authorization behavior was
  touched — all changes this pass were CSS/markup/client-JS presentation
  only.

## Browser verification result

Headless Microsoft Edge (already installed at
`C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe`) was
available and used directly via its own `--headless=new --screenshot`
CLI flags — no npm/pip package was installed into the repo or environment.
This is a genuine improvement over the previous two milestones, which had
no browser tool available at all.

**What was actually verified with real rendered screenshots:** `login.html`
and `register.html` at 1440px, 1024px, and 768px (Edge's own window-chrome
overhead means a requested `768` renders at an actual viewport of ~744px,
close enough to be representative). Both pages render correctly: centered
panel, correct spacing/border/radius/button-height, readable contrast,
autofill-safe fields, no visible overflow, layout consistent across all
three widths.

**What could not be verified, and why:** two real limits were hit and
diagnosed with evidence (not assumed):

1. **Sub-~500px screenshots are not trustworthy in this Edge build.** A
   minimal test page confirmed `window.innerWidth` reports `492` even when
   `--window-size=320,900` (or `390`, `480`, `500`) is passed — this
   specific headless CLI invocation has a hard floor around 492px logical
   width regardless of the requested size; only window sizes at or above
   roughly 700px actually shrink the real layout viewport. Screenshots
   initially taken at 320/390px therefore do not reflect the real 320/390px
   layout (they show the same ~492px-wide render cropped into a narrower
   image) and were discarded rather than used as evidence. This is a tool
   limitation of driving the browser via bare CLI flags without full
   DevTools-protocol viewport emulation (which Playwright would normally
   provide, and which is still unavailable in this environment). Mobile-
   width correctness for 390px/320px therefore rests on the CSS media-query
   review above, not on a rendered screenshot — same limitation the
   previous two milestones documented, now root-caused precisely instead of
   just "tool unavailable."
2. **Authenticated pages (Dashboard, Builder as a signed-in user, My
   Plugins, plugin detail) could not be screenshotted.** Driving the
   browser via one-shot CLI screenshot calls has no way to carry a
   previously-obtained Identity session cookie into that browser process
   without full CDP scripting (which isn't available here); an
   unauthenticated headless request to those pages just triggers their
   existing client-side redirect to `login.html`, so a screenshot would
   show the login page, not the actual authenticated screen, and was not
   worth presenting as evidence.

Real HTTP-level verification (which was fully exercised in the previous
milestone and re-confirmed here) covers what the screenshots could not:
registered a fresh account, confirmed `/api/account/me`, `/api/credits`,
and `/api/projects` return exactly the shapes the authenticated pages'
JavaScript consumes, and confirmed the production CSP header. No claim of
having visually reviewed the authenticated pages' rendered screenshots is
made.

An unrelated incident during this pass: a `taskkill /IM msedge.exe` used to
stop the headless instances killed Edge process-wide rather than only the
headless PIDs, which may have closed the user's own Edge windows/tabs if
any were open. Flagged to the user directly in the session; no recovery
action was possible from here.

## Build result

`dotnet build`: **0 warnings, 0 errors**.

## Test result

`dotnet test`: **204/204 passing** — no test needed updating this pass
(unlike the previous milestone, no page-copy or asset-path assertions were
affected by these changes). `node --check` passed for every JS file in
`wwwroot`. `git diff --check`: clean (only the pre-existing LF/CRLF
conversion notices).

No new automated test was added. The one meaningful regression found this
pass (the build-button double-submit race) is pure client-side JavaScript
timing behavior with no server-observable signal to assert on through the
existing xunit HTTP test harness; adding new test infrastructure (e.g. a
headless-browser test runner) purely to cover it would be a disproportionate
new dependency for a one-line fix, so it was fixed and documented here
instead, per the instruction to avoid brittle/invented test tooling.

## Deliberately deferred

- Real screenshot verification at 390px/320px and of any authenticated page
  — tooling limitations documented precisely above, not silently skipped.
- Stripe, payments, subscriptions, billing UI, and new backend capability
  work — out of scope by explicit instruction.
- No further visual redesign was performed; every change above is a
  targeted fix to a concretely identified inconsistency, bug, or gap, not a
  stylistic rework.

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
?? AUTHENTICATED-UI-COMPLETION.md
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

(Same file list as the previous two milestones' reports — nothing from
earlier uncommitted work was reverted or discarded, and no test file
required an update this pass.)

No commit, push, or tag was performed.
