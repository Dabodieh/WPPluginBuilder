# Production readiness / deployment hardening completion

Prepares the existing application for a real production deployment: safe
config/secret handling, startup validation, Data Protection persistence,
reverse-proxy awareness, a minimal health endpoint, and a Docker image/
compose example. No Stripe, payments, new product features, UI redesign,
or generation-behavior changes.

## Files added

- `Dockerfile` (repo root) — multi-stage `.NET 8` build, non-root runtime.
- `.dockerignore` — excludes `bin/`, `obj/`, `.git`, secrets, local artifacts.
- `.env.example` — safe placeholder environment template (no real secrets).
- `docker/docker-compose.prod.example.yml` — production-oriented compose
  example (app + PostgreSQL), env-var placeholders only.
- `src/WPAIPlugin.Api/Health/DatabaseHealthCheck.cs` — readiness check
  (`Database.CanConnectAsync()`), used by `GET /health/ready`.
- `PRODUCTION-READINESS-COMPLETION.md` — this report.

## Files changed

- `src/WPAIPlugin.Api/Program.cs` — see "Configuration changes" below for
  the substantive additions (ForwardedHeaders, Data Protection persistence,
  Permissions-Policy header, health-check registration/mapping).
- `src/WPAIPlugin.Api/appsettings.json` — added `DataProtection:KeyRingPath`
  (empty default → falls back to `App_Data/dataprotection-keys`) and
  `ForwardedHeaders:KnownProxies`/`KnownNetworks` (both empty arrays by
  default → nothing is trusted from outside the container, matching
  existing direct-Kestrel behavior).
- `.gitignore` — added `!.env.example` (so the safe template stays tracked
  despite the existing broad `.env.*` ignore) and
  `docker/docker-compose.prod.yml` (a real, filled-in copy of the example
  stays untracked).
- `tests/WPAIPlugin.Generator.Tests/Security/ProductionSecurityTests.cs` —
  added a `Permissions-Policy` assertion to the existing header test, and
  one new test for the two health endpoints (see "Tests" below).
- `README.md` — new `# Production Deployment` section (see
  "Documentation" implicitly covered throughout this report; not
  duplicated here).

No files unrelated to this milestone were touched. All prior milestones'
uncommitted work (UI, security, credits) remains exactly as it was.

## Configuration changes

All new configuration has a safe default that preserves existing
Development/test behavior with zero required changes:

| Setting | Default | Purpose |
| --- | --- | --- |
| `DataProtection:KeyRingPath` | `""` (→ `App_Data/dataprotection-keys`) | Where Identity/antiforgery encryption keys persist |
| `ForwardedHeaders:KnownProxies` | `[]` | IP addresses of trusted reverse proxies |
| `ForwardedHeaders:KnownNetworks` | `[]` | CIDR ranges of trusted reverse proxies |

Nothing was hard-coded and nothing was added to `appsettings.json` that
resembles a secret — both new sections are structural/path configuration,
not credentials.

## Required production environment variables

Unchanged from Milestone 13, still enforced by the existing startup check
in `Program.cs` (now slightly earlier in the file but logically identical):
`ConnectionStrings:DefaultConnection`, the selected provider's API key
(`Planning:OpenAI:ApiKey` or `Planning:Anthropic:ApiKey` matching
`Planning:DefaultProvider`), and `Artifacts:RootPath`. See
`README.md → Production Deployment → Required environment variables` for
the full table and `.env.example` for every optional variable with its
default. No new hard-required variable was introduced — Data Protection
and Forwarded Headers both have working defaults.

## Migration deployment strategy

Confirmed unchanged: `Program.cs` never calls `Database.Migrate()` or
applies migrations automatically. Verified live against the existing local
PostgreSQL (`docker-compose.saas.yml`) that all four migrations
(`InitialIdentity`, `AddPluginProjectsAndVersions`,
`AddCreditAccountsAndTransactions`, `BackfillCreditsForExistingUsers`) are
already applied and the history is a clean incremental sequence — nothing
regenerated, nothing skipped. Documented the explicit deployment command
(`dotnet ef database update ...`) in the README; no migration files were
added or edited this pass.

## Data Protection persistence strategy

**Real gap found and fixed.** Before this pass, `Program.cs` never
configured Data Protection at all, so ASP.NET Core used its own ephemeral
default key ring — ASP.NET Identity auth cookies and antiforgery (CSRF)
tokens would all be silently invalidated on every process/container
restart, forcibly logging out every signed-in user. Fixed by persisting
keys to a configurable filesystem directory
(`DataProtection:KeyRingPath`, default `App_Data/dataprotection-keys`,
same tier as artifact storage) via `PersistKeysToFileSystem` +
`SetApplicationName("WPAIPlugin")`. That directory must sit on a volume
that survives restarts — the Dockerfile creates it under `/app/App_Data/`
and the compose example mounts a named volume over it. No external key
vault/infrastructure was introduced; a multi-instance deployment behind a
load balancer needs to share this directory (documented in the README).
Verified live: the Docker container logs ASP.NET Core's own warning
("Storing keys in a directory that may not be persisted outside of the
container") when the volume isn't mounted, confirming the code path is
active and exactly matches the documented risk.

## Reverse proxy / HTTPS handling

**Real gap found and fixed.** Before this pass, no `ForwardedHeaders`
middleware existed, so behind any reverse proxy every request would appear
to originate from the proxy's own IP (breaking the per-client-IP account
rate limiter — every user sharing one proxy would share one rate-limit
bucket) and the app could never see the original HTTPS scheme (breaking
Secure-cookie logic and HTTPS redirection behind TLS-terminating proxies).
Fixed by registering `ForwardedHeadersOptions` for
`XForwardedFor|XForwardedProto|XForwardedHost`, but **only trusting
proxies explicitly listed** in `ForwardedHeaders:KnownProxies` (IPs) or
`ForwardedHeaders:KnownNetworks` (CIDR ranges) — with both empty (the
default), `KnownProxies`/`KnownNetworks` are cleared and ASP.NET Core's
own default same-loopback-only trust applies, identical to current
direct-Kestrel behavior. `app.UseForwardedHeaders()` runs first in the
pipeline, before `UseHttpsRedirection`/cookie/rate-limiter logic that
depends on scheme or remote IP. `UseHttpsRedirection`, `UseHsts` (outside
Development), and the existing Secure/HttpOnly/SameSite cookie policy are
all unchanged and now correctly proxy-aware when configured.

## Cookie/security-header result

Unchanged cookie policy (Identity: `HttpOnly`, `SameSite=Lax`,
`Secure=Always` outside Development; antiforgery: `HttpOnly`,
`SameSite=Strict`, same Secure policy). CSP unchanged — no
`unsafe-inline`/`unsafe-eval` added, no weakening. Added one new header:
`Permissions-Policy: camera=(), microphone=(), geolocation=(), payment=()`
(the app uses none of these; explicitly disabling them is a no-risk
hardening step, and it does not touch CSP). No duplicate/conflicting
headers — verified live (`X-Content-Type-Options`, `X-Frame-Options`,
`Permissions-Policy`, `Content-Security-Policy` each appear exactly once).
Confirmed unchanged CSRF/authentication/rate-limiting behavior for every
production-sensitive route (login, register, logout, planning, build,
validated build, downloads, project access, credits) — all still enforced
exactly as documented in the existing Milestone 13 README section; nothing
in that route matrix was touched.

## Artifact persistence strategy

Confirmed unchanged and audited: `PluginArtifactStore` only ever addresses
files by three server-generated GUID segments (rejects anything else in
`ResolvePath`), so there is no path-traversal surface. Startup already
refused (pre-existing check, unchanged) to let the artifact root resolve
inside `wwwroot`. Documented explicitly in the README that
`Artifacts:RootPath` (`/app/App_Data/artifacts` in the Docker image) must
be a persistent volume, and traced what happens on disk-full/unwritable:
`SaveAsync` throws `IOException`, which the existing
`ProjectsController.Build` catch-all treats as a failed build and refunds
the charge — verified by reading the controller, no code change was
needed there (behavior was already correct).

## Docker validation production requirements

Audited `DockerPluginValidator` in full. **Standard `Build Plugin` never
touches Docker.** `Build & Validate` requires the `docker` CLI and a
reachable Docker daemon from wherever the app process runs. Documented
precisely in the README: production containers wanting this feature must
mount the host Docker socket (a real root-equivalent privilege escalation
tradeoff, called out explicitly, not silently enabled) and have the
`docker` CLI on `PATH` — neither is included in the provided Dockerfile by
default. If Docker is unavailable, validation already fails cleanly with
`PluginValidationFailureReason.ValidationUnavailable` and the credit
charge is refunded (existing behavior, re-verified by reading the code,
not changed). No redesign: cleanup (`finally` block tearing down
containers/volumes/temp files even on timeout/cancellation), GUID-suffixed
compose project names for concurrent-run isolation, and the 120-second
default timeout were all already correct.

## Temporary file cleanup result

Reviewed `DockerPluginValidator.ValidateAsync` line by line: the `finally`
block always runs `docker compose down -v --remove-orphans` (if containers
were started) and always deletes the temp directory, wrapped in try/catch
so a teardown failure never masks the real result. This covers success,
every failure branch, timeout (`OperationCanceledException` without
`cancellationToken.IsCancellationRequested`), and explicit cancellation.
**No cleanup bug was found**, so no code change or new test was added here
— the task instructions explicitly say to add tests only if a real defect
is discovered.

## Logging/security result

No third-party logging platform added — `ILogger` throughout, unchanged.
Re-confirmed the existing Milestone 13 production log filter (suppressing
`Microsoft.AspNetCore.Diagnostics.ExceptionHandlerMiddleware`'s own
exception-message logging outside Development) is still in place and still
correct: our own `app.Logger.LogError("Request failed with an unhandled
server error.")` on that path logs a fixed string only, verified live by
reproducing a real 500 and reading the server's own log output — it never
printed the underlying exception. `DockerPluginValidator` already
truncates command output before logging. No new logging statements were
added that interpolate secrets, prompts, or connection strings.

## Health-check behaviour

Added `GET /health/live` (anonymous, no database access — a fixed `200
Healthy` whenever the process is up, using `Predicate = _ => false` so it
runs zero registered checks) and `GET /health/ready` (anonymous,
additionally runs the new `DatabaseHealthCheck`, tagged `"ready"`, which
only calls `Database.CanConnectAsync()` — never returns a connection
string, database name, or exception detail, verified both by reading the
code and by a new test, `HealthEndpointsAreAnonymousAndExposeNoInternalDetails`).
Both endpoints use only the shared-framework
`Microsoft.Extensions.Diagnostics.HealthChecks` API — no new NuGet
package. Verified live against a real, reachable PostgreSQL: `/health/ready`
returned `200 Healthy`.

## Error-handling result

Confirmed unchanged and re-verified live: production unhandled exceptions
return the existing generic `{"error": "..."}` 500 (or the correct status
for a known `BadHttpRequestException`, e.g. 413) — reproduced a real
exception in a running Production instance and confirmed the HTTP response
body contained no stack trace, SQL, EF details, Docker internals, or
provider secrets. Existing safe, specific error responses (402
insufficient credits, 429 rate limited, 401/403, 404 for unmapped legacy
routes) are untouched.

## Docker image/deployment result

`docker build` succeeded from the repository root
(`docker build -t wpaiplugin-api:verify .`). Ran the resulting image
(`docker run`) against the existing local PostgreSQL via
`host.docker.internal` with a fake provider key: container started
cleanly, `GET /health/live`, `GET /health/ready`, `GET /`, `GET
/login.html`, and `GET /site.css` all returned `200` from the host. The
container correctly logged ASP.NET Core's own "keys may not be persisted"
warning because no volume was mounted in this ad hoc test run (expected —
confirms the Data Protection code path is live and the documented risk is
real, not hypothetical). Image and container were removed after
verification (`docker rm`/`docker rmi`); nothing was left running beyond
what this session started with.

## Backup/restore strategy

Documented in the README with concrete `pg_dump`/`pg_restore` commands,
the artifact directory as a second backup target, and the Data Protection
key directory as a third (session-continuity, not data-loss) concern —
with an explicit note that database and artifact backups should be taken
close together in time to avoid a project/version row referencing a ZIP
that a restore doesn't have (or vice versa). No backup automation/
framework was built — this is operational documentation only, as
instructed.

## Secret scan result — REDACTED

Searched all tracked and untracked source, config, script, and markdown
files (excluding `bin/`/`obj/`) for `sk-`, `OPENAI_API_KEY`,
`ANTHROPIC_API_KEY`, AWS/GCP/Slack-style key patterns, PEM private-key
headers, bearer tokens, and `password=`/`Password=` connection-string
fragments.

**No real secrets found.** Every match was one of:
- Test-fixture fake keys clearly named as such (e.g.
  `tests/.../OpenAIPlanningProviderTests.cs`: `sk-test-marker-should-never-appear`,
  `sk-test-authz-key`; `tests/.../PluginBuilderTests.cs`:
  `sk-ant-test-marker-should-never-appear`; `tests/.../ProductionSecurityTests.cs`:
  `sk-private-regression-marker` — all const strings used only to assert
  they never leak into a response).
- The already-documented, already-gitignored-pattern-adjacent throwaway
  development password `local-dev-only` in
  `src/WPAIPlugin.Api/appsettings.Development.json` (line 9) and
  `docker/docker-compose.saas.yml` — matches the compose file's own
  connection string exactly and is explicitly commented as "Development-
  only, throwaway local credentials" in that file. Not a production
  secret; `appsettings.Development.json` is a config file, not a `.env`,
  and this is the established pattern from Milestone 10 onward.
- Similar throwaway WordPress-validation passwords (`validate-admin-pw`,
  `validate-wp-pw`, `validate-root-pw`, `dev-root-pw`, `dev-wp-pw`) in
  `scripts/Validate-GeneratedPlugin.ps1` and the `docker/docker-compose.*.yml`
  files, all scoped to disposable, non-production Docker containers with
  no real data.
- Code comments naming environment-variable conventions
  (`Planning__OpenAI__ApiKey or OPENAI_API_KEY`) in
  `src/WPAIPlugin.Planning/PlanningOptions.cs` — documentation of a
  variable *name*, not a value.

No `.pfx`/`.pem`/`id_rsa`-style private key files exist anywhere in the
repository. `appsettings.json`'s own `Planning:*:ApiKey` fields are empty
strings (the existing, correct pattern — populated only via user-secrets
or environment variables, never committed).

## Development compatibility result

`dotnet run` continues to work exactly as before — no code path added
this milestone runs unless it's already guarded the same way as existing
production-only logic, or has a working, non-blocking default
(`ForwardedHeaders` with empty trust lists is a documented no-op;
`PersistKeysToFileSystem` works identically in Development, just persisting
to a local folder instead of the ephemeral default, which is a strict
improvement — Development sessions now also survive restarts without
re-registering). `dotnet test` still requires no Docker, PostgreSQL, or
OpenAI — confirmed by running the full suite, which uses only the existing
InMemory-database/fake-provider/fake-validator test factories.

## Debug build result

`dotnet build`: **0 warnings, 0 errors.**

## Release build result

`dotnet build -c Release`: **0 warnings, 0 errors.**

## Publish result

`dotnet publish src/WPAIPlugin.Api/WPAIPlugin.Api.csproj -c Release -o <dir>`:
succeeded, produced a working `WPAIPlugin.Api.dll`. Ran the published
output directly (not just built) against the real local PostgreSQL in both
Development and Production (HTTP and HTTPS) modes — see "Docker
image/deployment result" and the HTTPS finding below for what that
exercised.

## Test count

**205/205 passing** (204 existing + 1 new: `HealthEndpointsAreAnonymousAndExposeNoInternalDetails`).
The existing `HeadersScriptsAndProviderFailuresDoNotExposeSecrets` test
was extended with one additional assertion (Permissions-Policy header) —
not counted as a new test, same test.

## Docker build result

Executed (Docker Desktop was available):
`docker build -t wpaiplugin-api:verify .` — **succeeded**, multi-stage,
final image built from `mcr.microsoft.com/dotnet/aspnet:8.0`. Then ran the
image and verified real HTTP behavior — see "Docker image/deployment
result" above. Both image and container were cleaned up afterward.

## Anything not manually verifiable

- **A real multi-instance/load-balanced deployment sharing the Data
  Protection key directory** was not set up — documented as a requirement,
  not exercised, since it needs infrastructure (a second host or shared
  network volume) beyond this session's scope.
- **A real reverse proxy (nginx/Caddy/Cloudflare) forwarding
  X-Forwarded-* headers** was not stood up; `ForwardedHeaders` was verified
  by code review and by confirming its default (empty trust list) doesn't
  change existing behavior, but the "trusted proxy correctly rewrites
  scheme/IP" path itself wasn't exercised end-to-end against a real proxy.
- **HTTPS with a real (non-development) TLS certificate** was not
  exercised — the HTTPS verification below used the local ASP.NET Core
  developer certificate.
- Browser-based visual verification was not attempted this pass — this
  milestone is backend/deployment-only and touches no HTML/CSS/JS, so no
  UI rendering could plausibly regress; only HTTP-level checks apply here.

## Anything deliberately deferred

- Moving artifact storage to S3/Azure Blob/etc — explicitly out of scope;
  local persistent-volume storage remains the strategy, now documented.
- A full orchestration platform (Kubernetes manifests, autoscaling) —
  explicitly out of scope; only a Dockerfile and a compose example were
  requested.
- Bundling a reverse proxy (nginx/Traefik/Caddy) into the deployment —
  explicitly deferred to documentation per the task instructions; the
  current architecture doesn't require one to be bundled.
- An automated backup framework/scheduler — explicitly out of scope; only
  the manual `pg_dump`/`pg_restore` commands and the three things to back
  up were documented.
- Encrypting Data Protection keys at rest (the "No XML encryptor
  configured" warning seen in every run, Development and Production
  alike) — this is standard, expected ASP.NET Core behavior on a host
  without Windows DPAPI or a cloud key-vault integration (e.g. any Linux
  container); adding one (Azure Key Vault, AWS KMS, etc.) would be new
  external infrastructure, explicitly out of scope for this milestone, and
  is noted in the README as a known, accepted limitation rather than
  silently left unmentioned.

## A note on what the manual production-like test actually found

Testing the published binary directly in `Production` mode over **plain
HTTP** produced a generic 500 on registration. Investigating (by
reproducing the same request over HTTPS with the real local dev
certificate, which succeeded, and by comparing against `Development` mode
over the same plain-HTTP connection, which also succeeded) showed this is
**correct, pre-existing behavior, not a defect**: Production's
`CookieSecurePolicy.Always` (set in Milestone 13, unchanged here) means
the antiforgery/Identity cookie system genuinely cannot operate over a
connection it believes is plain HTTP — and with no trusted reverse proxy
configured (the default), the app correctly does *not* believe an
unproxied HTTP connection is HTTPS. This is Production correctly refusing
to degrade security silently, exactly as designed; it reinforces why HTTPS
(direct or via a properly configured, trusted reverse proxy) is
documented as a hard production requirement, not an optional nicety. No
code change was made in response to this finding — it is included here
because it was a real, initially-surprising result from a real manual
test, not because it indicated a bug.

## git status --short

```
 M .gitignore
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
?? .dockerignore
?? .env.example
?? AUTHENTICATED-UI-COMPLETION.md
?? Dockerfile
?? HOMEPAGE-COMPLETION.md
?? MILESTONE13-COMPLETION.md
?? UI-POLISH-COMPLETION.md
?? docker/docker-compose.prod.example.yml
?? src/WPAIPlugin.Api/Health/
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

Everything from `src/WPAIPlugin.Api/Security/` through
`tests/.../ProductionSecurityTests.cs` in the untracked list (other than
this milestone's own new files, called out above) is unrelated prior-
milestone work, preserved exactly as found. Note: this session's manual
verification registered a few additional test accounts against the shared
local development PostgreSQL (`docker-compose.saas.yml`), consistent with
what every prior milestone's manual testing has also done — no balances
were manually edited, and the database was left running as it was found.

No commit, push, or tag was performed.
