# Milestone 13 completion report

Implementation complete. Starting state: clean tree, build 0 warnings/errors, 178 passing tests. Final build: 0 warnings/errors; tests: 201 passed, 0 failed, 0 skipped. No payments, generation features, migrations, dependencies, commits or next milestone.

## Production route matrix

| Route | Access | CSRF | Default rate |
| --- | --- | --- | --- |
| POST /api/plugins/build | Unmapped, 404 | N/A | N/A |
| POST /api/plugins/build-validated | Unmapped, 404 | N/A | N/A |
| POST /api/plugins/plan | Authenticated | Required | 10/user/minute |
| POST /api/projects/build | Authenticated; credits enforced | Required | 6/user/minute; validated additionally 2/user/minute |
| GET /api/projects, /api/projects/{id} | Authenticated, own records | Safe GET | None |
| GET /api/projects/{id}/versions/{id}/download | Authenticated, own artifact | Safe GET | None |
| GET /api/credits | Authenticated, own balance/costs | Safe GET | None |
| POST /api/account/register, /login | Public | Required | Combined 10/IP/5 minutes |
| POST /api/account/logout | Caller session only | Required | None |
| GET /api/account/me | Authenticated | Safe GET | None |
| GET /api/account/csrf | Public, no-store bootstrap | Safe GET | None |
| Static UI | Public | N/A | None |

Swagger is Development-only. No credit mutation/grant/refund/history endpoint exists. Generation audit found only ProjectsController.Build and the two legacy methods; the latter are absent from all non-Development route discovery. Production customer generation has one path through authentication, server cost, credits, generation, optional validation, persistence and ownership-checked download.

## Legacy tooling

Development retains anonymous deterministic /api/plugins/build for scripts/Validate-GeneratedPlugin.ps1, exempt from antiforgery because it is not a cookie-authenticated SaaS action. Development legacy build-validated retains authentication, antiforgery and the build rate limit. Neither appears in customer UI.

Production and other non-Development environments do not map either legacy action. Anonymous and authenticated calls, including casing/trailing-slash variations, return 404. Never deploy customer instances with the Development environment.

## Rate limiting and sizes

Built-in fixed windows configured under Security: PlanningPerMinute=10, BuildsPerMinute=6, ValidatedBuildsPerMinute=2, AccountRequestsPerFiveMinutes=10. No queue, custom limiter algorithm or distributed store. Authenticated Identity IDs partition planning/build limits; connection IP partitions account abuse protection. Validated builds acquire an additional built-in PartitionedRateLimiter permit after model binding and before charging. Rejections return 429 with Retry-After. Static files are not rate-limited.

Account bodies: 16 KiB; planning: 64 KiB; builds: 1 MiB; Kestrel ceiling: 1 MiB. Existing 2,000-character planning limit remains. Live oversized account/build requests return 413 without charging.

## CSRF, cookies and headers

Standard ASP.NET IAntiforgery.GetAndStoreTokens supplies the token through GET /api/account/csrf. Shared api.js fetches a fresh token before writes and sends X-CSRF-TOKEN. MVC validates registration/login/logout/planning/builds. Fresh tokens handle identity transitions; no localStorage or custom algorithm. Existing HTTP tests perform the real token exchange.

Identity cookies: HttpOnly, SameSite=Lax, Secure=Always outside Development. Antiforgery cookies: HttpOnly, SameSite=Strict, same Secure policy. Local HTTP Development uses SameAsRequest. HTTPS redirection retained; HSTS outside Development. No JWT or CORS relaxation.

Headers: nosniff; strict-origin-when-cross-origin Referrer-Policy; X-Frame-Options DENY. CSP: default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; connect-src 'self'; object-src 'none'; base-uri 'none'; frame-ancestors 'none'; form-action 'self'. Inline page scripts were extracted; existing inline CSS retained without redesign. Only Development Swagger permits inline script bootstrap. Headers apply at response start; API responses are no-store.

## Configuration, artifacts and errors

Non-Development startup requires nonempty database connection, selected OpenAI/Anthropic key and artifact root. Only the selected provider is required. Validation messages omit values. Artifact roots under wwwroot are rejected. Downloads retain attachment Content-Disposition and ownership checks; no ArtifactKey, UserId or filesystem path is added to responses.

Unexpected Production errors return generic 500 responses; body-limit exceptions retain 413. Provider failures return generic messages. Our provider logs record categories rather than raw exceptions; framework exception-message logging is suppressed at the Production boundary, which records a generic category instead.

## Verification results

- Final dotnet build: 0 warnings/errors. Final dotnet test: 201/201, including all original 178. No external dependencies for normal tests.
- Every frontend JS file passes node --check. Frontend scan found no legacy build calls, provider keys, localhost URLs, inline script blocks or inline event handlers. Existing credit and ownership tests remain green.
- Development validation script passed with real Docker/WordPress: ZIP, PHP lint, WordPress install, plugin install, activation and active check. Disposable resources cleaned up. A nonfatal WordPress CLI HTTP_HOST warning did not affect success.
- Live Production HTTPS: anonymous legacy build and build-validated 404; planning and projects/build 401; authenticated legacy build 404; missing-CSRF registration 400.
- New local account: registration/logout/login 200; Secure/HttpOnly/SameSite=Lax verified. Planning via loopback provider fixture 200, balance still 100. Standard build with creditCost=0 returned 200, balance 99. My Plugins listed it; ZIP download returned attachment and 200. Direct artifact path returned 404.
- Process-local planning limit of 2 produced 429 on the third request. Oversized auth/build requests returned 413. Final balance remained 99. Known provider marker absent from served static files/customer responses. Normal test configuration was not changed by the manual fixture.
- Temporary Production app/provider fixture stopped. Existing development database retains test accounts/artifacts; balances were not edited directly. No migration was necessary.

## Manual limitations and deferred work

No real OpenAI key is configured in environment/user-secrets. Manual authenticated planning used the application's provider adapter with a loopback HTTP fixture, not the real OpenAI service. The local HTTPS client bypassed validation only for the development certificate; server Secure cookies remained enabled.

Browser/DevTools interaction was unavailable: the installed browser tool references a missing browser-service.mjs. Cookie/network/static verification used direct HTTP and integration tests. No claim of graphical browser testing is made.

Deferred: Stripe, subscriptions, payments, generation features, distributed limiting, trusted reverse-proxy forwarding setup, removal of inline CSS, broader authentication features and durable recovery from interrupted refunds. Limits are per process and reset on restart. Proxy deployments must configure trusted forwarding/HTTPS deliberately; no trust in arbitrary forwarded headers was added.

Next safe step: configure the existing provider key and repair browser tooling, then repeat the unavailable manual checks. Do not start another milestone automatically.

## Research and documentation

Reused Identity, MVC, CreditService, artifact storage and test factories. Official ASP.NET Core [antiforgery](https://learn.microsoft.com/en-us/aspnet/core/security/anti-request-forgery?view=aspnetcore-8.0), [rate limiting](https://learn.microsoft.com/en-us/aspnet/core/performance/rate-limit?view=aspnetcore-8.0) and [Kestrel limits](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/servers/kestrel/options?view=aspnetcore-8.0) informed token exchange, middleware order and size handling. MVC view services enable the standard antiforgery filters; UI remains static. README and PLANS updated; no standalone changelog exists.

## Exact file inventory

Existing files modified:
- PLANS.md
- README.md
- scripts/Validate-GeneratedPlugin.ps1
- src/WPAIPlugin.Api/Controllers/AccountController.cs
- src/WPAIPlugin.Api/Controllers/PluginsController.cs
- src/WPAIPlugin.Api/Controllers/ProjectsController.cs
- src/WPAIPlugin.Api/Program.cs
- src/WPAIPlugin.Api/appsettings.json
- src/WPAIPlugin.Api/wwwroot/app.js
- src/WPAIPlugin.Api/wwwroot/builder.html
- src/WPAIPlugin.Api/wwwroot/dashboard.html
- src/WPAIPlugin.Api/wwwroot/index.html
- src/WPAIPlugin.Api/wwwroot/login.html
- src/WPAIPlugin.Api/wwwroot/myplugins.html
- src/WPAIPlugin.Api/wwwroot/plugin.html
- src/WPAIPlugin.Api/wwwroot/register.html
- src/WPAIPlugin.Planning/Providers/Anthropic/AnthropicPlanningProvider.cs
- src/WPAIPlugin.Planning/Providers/OpenAI/OpenAIPlanningProvider.cs
- tests/WPAIPlugin.Generator.Tests/Accounts/AccountControllerTests.cs
- tests/WPAIPlugin.Generator.Tests/Accounts/AccountTestFactory.cs
- tests/WPAIPlugin.Generator.Tests/Credits/CreditApiTests.cs
- tests/WPAIPlugin.Generator.Tests/Projects/ProjectsControllerTests.cs
- tests/WPAIPlugin.Generator.Tests/Projects/ProjectsTestFactory.cs

Files added:
- MILESTONE13-COMPLETION.md
- src/WPAIPlugin.Api/Security/LegacyBuildConvention.cs
- src/WPAIPlugin.Api/Security/SecurityOptions.cs
- src/WPAIPlugin.Api/wwwroot/api.js
- src/WPAIPlugin.Api/wwwroot/dashboard.js
- src/WPAIPlugin.Api/wwwroot/index.js
- src/WPAIPlugin.Api/wwwroot/login.js
- src/WPAIPlugin.Api/wwwroot/myplugins.js
- src/WPAIPlugin.Api/wwwroot/plugin.js
- src/WPAIPlugin.Api/wwwroot/register.js
- tests/WPAIPlugin.Generator.Tests/Security/CsrfClient.cs
- tests/WPAIPlugin.Generator.Tests/Security/ProductionSecurityTests.cs
