# Public SaaS homepage completion

Completed the homepage milestone only. Earlier uncommitted Milestone 13 changes
are preserved. No backend, schema, credit, or authenticated-page behavior changed.

## Files in this milestone

Added:
- `src/WPAIPlugin.Api/wwwroot/site.css`
- `HOMEPAGE-COMPLETION.md`

Changed:
- `src/WPAIPlugin.Api/wwwroot/index.html`
- `src/WPAIPlugin.Api/wwwroot/index.js` (already untracked from Milestone 13)
- `tests/WPAIPlugin.Generator.Tests/WebAppTests.cs`
- `tests/WPAIPlugin.Generator.Tests/Security/ProviderKeyExposureTests.cs`
- `README.md`
- `PLANS.md`

## Page and implementation

Public header, hero, labelled static plan preview, three-step workflow,
deterministic-template/optional-validation explanation, five current capabilities,
saved-plugin example, final CTA and minimal footer are implemented. No fake
pricing, testimonials, customer logos, purchasing or legal links.

Native CSS uses a dark neutral palette, mint accent, system fonts, subtle borders,
rounded controls and responsive grids. Only the homepage loads this stylesheet.
There are no dependencies, remote assets, frontend build tools or animations.

The small external `index.js` calls only existing `GET /api/account/me`, checking
success without reading/rendering account data. Public defaults link to registration
and login. Successful authentication changes every build CTA to Builder and account
links to Dashboard, hiding Create account. Failed checks preserve public links.
No localStorage, credit manipulation, AI calls or new endpoints.

Hero, workflow and project layouts stack on mobile; navigation reduces to brand
and account/build actions. Feature columns reduce with viewport width. Semantic
headings, named navigation, skip link, visible keyboard focus, readable contrast
and non-interactive example figures support accessibility. Reduced motion needs
no override because the page has no animation or smooth scrolling.

## Verification

- `dotnet build`: succeeded, 0 warnings, 0 errors.
- `dotnet test`: **204 passed, 0 failed, 0 skipped**.
- `node --check` for homepage JavaScript: passed.
- `git diff --check`: passed (Git reports the existing LF/CRLF conversion notice).
- HTTP tests verify public marketing content, login/register CTA destinations,
  builder/dashboard availability and absence of provider key configuration in
  homepage HTML/JS/CSS. Existing security and backend tests remain green.
- Started the actual Development application against the existing local database.
- Reviewed full-page desktop (1440px) and mobile (390px) screenshots in headless Edge.
  Checked horizontal overflow at 1440, 1024, 768, 390 and 320px: none, including
  authenticated navigation.
- Registered a fresh local test account through the UI, logged out, logged in,
  and visited the homepage: all CTAs switched to Builder/Dashboard correctly.
  Logging out restored register/login destinations. Test registrations remain
  in the local development database; no balances were manually changed.
- Homepage network capture contains only `/api/account/me` beyond static assets.
- Authenticated homepage: zero console/JavaScript errors. Signed-out homepage:
  expected 401 resource diagnostic from the existing protected auth-status endpoint;
  zero JavaScript exceptions or CSP violations. No CSP/header changes were needed.
- Keyboard skip link, section anchor navigation and failed-auth network fallback
  passed; reduced-motion rendering remains static.

The in-app browser runtime could not initialize because its installed service
module is missing. Preinstalled Python Playwright and local Edge provided the
actual browser verification; no tooling was added to the repository.

## Deliberately deferred

Authenticated-page visual changes, real legal pages, payments/subscriptions,
new capabilities and future milestones. No commit, push or tag performed.
