# ModuleMint branding + account recovery + legal/support launch blockers — completion

Customer-facing rebrand to **ModuleMint** (text-only, no technical rename),
forgot/reset password via ASP.NET Core Identity's own token provider, a
small Resend-backed transactional-email seam, and the four legal/support
pages (Privacy, Terms, Refunds, Support). No product features added, no
Stripe/credit/free-build/promotion accounting changed.

## 1. Exact files added

**Backend**
- `src/WPAIPlugin.Api/Configuration/AppOptions.cs`, `SupportOptions.cs`, `EmailOptions.cs`
- `src/WPAIPlugin.Api/Email/ITransactionalEmailSender.cs`, `ResendTransactionalEmailSender.cs`, `NoOpTransactionalEmailSender.cs`
- `src/WPAIPlugin.Api/Controllers/PublicConfigController.cs` (`GET /api/config/public` — the one non-secret value the legal pages need: the configured support email)

**Frontend**
- `src/WPAIPlugin.Api/wwwroot/forgot-password.html`, `forgot-password.js`
- `src/WPAIPlugin.Api/wwwroot/reset-password.html`, `reset-password.js`
- `src/WPAIPlugin.Api/wwwroot/privacy.html`, `terms.html`, `refunds.html`, `support.html`
- `src/WPAIPlugin.Api/wwwroot/legal.css`, `legal.js`

**Tests**
- `tests/WPAIPlugin.Generator.Tests/Accounts/FakeTransactionalEmailSender.cs`
- `tests/WPAIPlugin.Generator.Tests/Accounts/PasswordRecoveryTests.cs`

**Documentation**
- `LAUNCH-BLOCKERS-COMPLETION.md` — this report.

## 2. Exact files changed

- `src/WPAIPlugin.Api/Controllers/AccountController.cs` — `POST /api/account/forgot-password`, `POST /api/account/reset-password`.
- `src/WPAIPlugin.Api/Controllers/AdminController.cs`, `AdminDtos.cs` — `GET /api/admin/system` gained `TransactionalEmailConfigured` (boolean only, reflects which `ITransactionalEmailSender` implementation DI actually resolved).
- `src/WPAIPlugin.Api/Security/SecurityOptions.cs` — `PasswordRecoveryPerFiveMinutes`.
- `src/WPAIPlugin.Api/Program.cs` — App/Support/Email options binding, conditional Resend DI wiring, `DataProtectionTokenProviderOptions.TokenLifespan` (1 hour), new `passwordRecovery` rate-limit policy, extended production-required-config check.
- `src/WPAIPlugin.Api/appsettings.json` — `Security:PasswordRecoveryPerFiveMinutes`, `App`, `Support`, `Email`, `Resend` sections (all blank/safe defaults).
- `src/WPAIPlugin.Api/WPAIPlugin.Api.csproj` — `Resend` NuGet package (0.19.0).
- `.env.example` — matching placeholders.
- `src/WPAIPlugin.Api/wwwroot/{index,login,register,builder,dashboard,billing,admin,myplugins,plugin}.html`, `nav.js` — ModuleMint branding (title/wordmark/brand-mark/copy).
- `src/WPAIPlugin.Api/wwwroot/login.html` — "Forgot password?" link.
- `src/WPAIPlugin.Api/wwwroot/billing.html`, `billing.css` — Terms/Refund Policy links.
- `src/WPAIPlugin.Api/wwwroot/admin.js` — System tab shows "Transactional email configured".
- `tests/WPAIPlugin.Generator.Tests/Accounts/AccountTestFactory.cs` — registers `FakeTransactionalEmailSender`, pins `App:PublicBaseUrl`/`PasswordRecoveryPerFiveMinutes` for the shared fixture.
- `tests/WPAIPlugin.Generator.Tests/Security/ProductionSecurityTests.cs` — shared `Production()` helper and its missing-config theory extended to cover the five new required production keys (a real, necessary fix — without it, every existing Production-mode test would have started failing against the new startup check; see item 20).
- `tests/WPAIPlugin.Generator.Tests/Admin/AdminApiTests.cs` — one new test for the System-endpoint email-status flag.
- `tests/WPAIPlugin.Generator.Tests/WebAppTests.cs` — route/branding/footer-link tests.
- `README.md`, `PLANS.md`, `LAUNCH-CHECKLIST.md` — see item 30's own section below.

No other file was touched. All prior uncommitted milestone work (production
readiness, admin panel, AI usage, credits audit, Stripe/financial, go-live
readiness, promotions/free-builds, the promotion-snapshot corrective pass)
remains exactly as it was — confirmed by the `git status --short` at the
end of this report matching the pre-existing set plus this session's own
additions.

## 3. ModuleMint branding changes

Text-only, everywhere a customer sees it: page `<title>`s, wordmark link
text, the `brand-mark` glyph (changed from `w+` to `M` — same box/position/
CSS, no layout change), homepage meta description and hero/step copy, the
admin shell's page-lead, and the public footer's wordmark. Internal
technical identity was **not** touched: .NET namespaces, the solution/
project files, assemblies, database table names, migration history, and
this repository's own name (`WPAIPlugin`) are unchanged — confirmed by
`git diff --stat` showing only `wwwroot/*.html`/`nav.js` text edits, no
`.cs`/`.csproj`/migration renames. Grepped all of `wwwroot/*.html`/`*.js`
after the pass for a bare customer-facing `WPAIPlugin` string — none found
outside this report/PLANS.md/README's own explanatory prose (which
correctly still refers to the technical project by its real name).

## 4. Forgot-password implementation

`POST /api/account/forgot-password { email }` (rate-limited, CSRF-protected
via the controller's existing `[AutoValidateAntiforgeryToken]`) always
returns the identical `{"message":"If an account exists for that email
address, a password reset link has been sent."}`, regardless of whether the
email belongs to a registered account. Only for a real match does anything
happen: `UserManager.GeneratePasswordResetTokenAsync` issues a token, a
reset URL is built (see item 8), and `ITransactionalEmailSender.SendPasswordResetEmailAsync`
is called inside a `try/catch` — a send failure is swallowed at the
controller level (after being logged safely inside the sender itself) so it
can never change the response shape, which would otherwise reveal, during
an email-service outage, which submitted addresses are real accounts. No
artificial timing-equalization was added — the unknown-email path is
naturally fast (a single lookup, no token generation, no send call), which
this milestone's own instructions call "reasonable" rather than requiring
exact-timing defenses.

## 5. Reset-password implementation

`POST /api/account/reset-password { email, token, newPassword, confirmPassword }`
(same rate limit + CSRF). Validates `newPassword == confirmPassword`
server-side (never trusts the client-side check alone), looks up the user,
and calls `UserManager.ResetPasswordAsync(user, token, newPassword)` — the
existing Identity password policy applies unchanged, no separate/
conflicting JavaScript-only policy was invented. An unknown email and a
genuinely invalid/expired token both return the identical
`"This reset link is invalid or has expired. Please request a new one."` —
neither response distinguishes the two. Any other password-policy failure
returns Identity's own safe, descriptive error text (the same pattern
already used at registration) — never an internal Identity/security detail.
A successful reset immediately invalidates the old password and every
existing session's security stamp (standard Identity behaviour via
`ResetPasswordAsync`) — proven by `ResetPassword_ValidToken_ResetsPassword_OldFailsNewSucceeds`.

## 6. Token lifetime

`DataProtectionTokenProviderOptions.TokenLifespan` explicitly configured to
**1 hour** in `Program.cs` (ASP.NET Core Identity's own default is 1 day -
deliberately shortened here, since a password-reset link is materially more
sensitive than the confirmation-email tokens that default was originally
designed for). No custom reset-token crypto, no reset-token database table,
no raw token ever persisted — the token is generated and validated entirely
by Identity's existing `DataProtectorTokenProvider` (already registered via
`AddDefaultTokenProviders()`).

## 7. Resend integration

`ResendTransactionalEmailSender` (`src/WPAIPlugin.Api/Email/`) is the only
file in the app that references the Resend SDK directly — mirrors the
existing `IPaymentGateway`/`IPlanningProvider` seam pattern exactly.
Registered in DI (`AddHttpClient<ResendClient>` + `Configure<ResendClientOptions>`
+ `AddTransient<IResend, ResendClient>`) only when `Resend:ApiKey` is
non-blank at startup. The email itself (subject "Reset your ModuleMint
password", both `TextBody` and `HtmlBody`) contains the reset link, a note
to ignore it if unrequested, and a note that it expires — no current
password, no raw token separate from the link, no Identity/provider
internals. Package: NuGet `Resend` 0.19.0 (confirmed the exact package id,
namespace, `IResend`/`ResendClient`/`ResendClientOptions.ApiToken`/
`EmailMessage` shape, and `EmailSendAsync` method against the package's own
NuGet/GitHub pages this session, then verified for real via a successful
`dotnet build` against the actual installed package — not assumed from
memory).

## 8. Development/test email seam

`NoOpTransactionalEmailSender` is selected in DI whenever `Resend:ApiKey` is
blank (the Development default) — it never sends anything and logs only a
fixed, safe line (`"...no email was sent."`), never the token or URL.
Verified live this session: with no Resend key configured, a real
forgot-password request against a real registered account produced exactly
that one safe log line and nothing else — grepped the full server log for
`token=`/`reset-password.html?`/any exception, found none. Tests use their
own `FakeTransactionalEmailSender` (records `(toEmail, resetUrl)` pairs for
inspection) via `AccountTestFactory`'s DI override — no Resend or internet
access required anywhere in the test suite, confirmed by every
`PasswordRecoveryTests` test passing with zero network access.

## 9. Account-enumeration protection

Verified by `ForgotPassword_KnownAccount_ReturnsGenericResultAndSendsEmail`,
`ForgotPassword_UnknownAccount_ReturnsSameGenericResultAndSendsNoEmail`, and
`ForgotPassword_KnownAndUnknown_ProduceIdenticalResponseBody` (byte-for-byte
identical response body for a known vs. unknown email) — and live, this
session, against the real database (see item 22). `reset-password` returns
the same "invalid or expired" message for an unknown email and a genuinely
invalid token (`ResetPassword_UnknownEmail_ReturnsSameInvalidLinkMessageAsInvalidToken`).
No artificial exact-timing equivalence was built, per this milestone's own
instruction that reasonable application-level protection is sufficient.

## 10. Rate limiting

New `passwordRecovery` policy in the existing built-in `AddRateLimiter`
infrastructure (no second rate-limit system) — IP-partitioned (the
submitted email may not belong to a real, authenticated account, so there
is no user identity to partition on), default 5 requests per 5 minutes
(`Security:PasswordRecoveryPerFiveMinutes`), applied to both
`forgot-password` and `reset-password`. Verified by
`ForgotPassword_RateLimitEnforced` (a derived factory overriding the limit
to 1, proving the 2nd request within the window returns `429` with a
`Retry-After` header) — the exact same pattern already proven for `account`/
`checkout`/`promoCode` in this codebase.

## 11. CSP/CSRF result

**CSP unchanged** — no `unsafe-inline`, `unsafe-eval`, or new remote script
source was added or is needed; Resend is called server-to-server only, and
none of the four new legal pages or two new recovery pages load anything
beyond the existing `app.css`/`api.js` pattern already in place for every
other authenticated-style page. **CSRF unchanged** — both new POST actions
sit inside `AccountController`, which already carries
`[AutoValidateAntiforgeryToken]` at the class level; no CSRF exception was
added for convenience.

## 12. Privacy page

`/privacy.html` — covers operator identity (flagged placeholder), account
information, plugin/project information, AI processing, AI usage metadata,
payment/purchase information, credit ledger, free-build entitlement ledger,
promotion/redemption information, admin audit records, technical/security
logs, authentication/cookies, third-party processors (OpenAI/Anthropic,
Stripe, Resend — named explicitly, nothing else claimed), retention,
security, customer rights, contact, and policy changes. Does not claim no
third-party processing, absolute security, unheld certifications, or a
retention period the app doesn't actually implement.

## 13. Terms page

`/terms.html` — covers the service, accounts, acceptable use, AI planning,
plugin generation, Build & Validate's real limitations, free-build
entitlements, credits, promotions, paid packs, Stripe payments, downloads,
generated-code responsibility, user content, IP, availability, account
restrictions, a Refund Policy link, cautious limitation wording, service/
policy changes, a flagged governing-law assumption, and contact. Explicitly
does **not** claim generated plugins are guaranteed secure, bug-free,
permanently WordPress-compatible, automatically legally compliant, or
guaranteed to pass any marketplace review — and explicitly frames Build &
Validate as a functional smoke test, not a security audit or a production-
readiness guarantee.

## 14. Refund Policy page

`/refunds.html` — describes the actual current mechanics exactly as
specified: no self-service monetary refund workflow; requests go through
support and are assessed manually; approved refunds go back through Stripe/
admin; failed payments and cancelled Checkouts grant zero credits; a
successful verified payment grants its benefits exactly once; a monetary
refund never automatically forces the credit balance negative; credit
corrections are separate, explicit, audited admin adjustments; statutory
consumer rights are not overridden. Does **not** say "all sales are final"
and does **not** invent a 7/14/30-day guarantee.

## 15. Support page

`/support.html` — shows the configured support email (via `data-support-mailto`,
filled by `legal.js` from `GET /api/config/public`), explains support
covers account access, password recovery, plugin/build issues, billing,
purchases, refunds, and general technical issues, and points to
`forgot-password.html` as the fastest self-service path. No ticketing
database, live chat, or contact-form backend was built — a `mailto:` link
is the entire mechanism, exactly as instructed.

## 16. Navigation/footer changes

Public homepage footer (`index.html`) gained Privacy/Terms/Refunds/Support
links alongside the existing Log in/Create account links, in the one
existing `<nav>` element (no new footer row/layout, no CSS change needed).
Authenticated app navigation (`nav.js`, shown on every dashboard/builder/
billing/admin/myplugins page) was deliberately **not** touched — adding
legal links there would clutter every authenticated page, which this
milestone's own instruction explicitly warns against; those pages are one
click away via the homepage/billing footer instead. Login page gained a
"Forgot password?" link next to the password field.

## 17. Billing legal links

`/billing.html` gained a small "Terms of Service · Refund Policy" line
after the purchase-history section (`billing.css` gained a matching
`.billing-legal-links` rule, styled consistently with the existing muted-
link pattern). No VAT-included/VAT-excluded statement was added anywhere —
tax handling is not yet defined, so nothing was invented.

## 18. Production configuration additions

Outside Development, `Program.cs`'s existing startup check (database,
planning provider, artifact root) was extended to also require
`App:PublicBaseUrl`, `Support:Email`, `Email:FromAddress`, `Email:FromName`,
and `Resend:ApiKey` — all together, since password reset is a core account-
security feature, not an optional one like Stripe (which is allowed to
return `503` until configured). A production deployment missing any of
these now fails to start with the same generic
`InvalidOperationException` pattern already used for the pre-existing
required keys — never the missing value itself. Development and the test
suite are unaffected: all five have safe blank defaults in
`appsettings.json`, and Development never enters this check at all.

## 19. Exact targeted tests run

- `dotnet test --filter "FullyQualifiedName~PasswordRecovery"` → 9/9.
- `dotnet test --filter "FullyQualifiedName~Accounts"` → 17/17 (includes the 9 above).
- `dotnet test --filter "FullyQualifiedName~Security"` → 39/39 (includes 5 new `InlineData` cases on `ProductionMissingCriticalConfigurationFailsWithoutSecrets` proving each new required key is individually enforced).
- `dotnet test --filter "FullyQualifiedName~Admin"` → 48/48 (includes 1 new email-status test).
- `dotnet test --filter "FullyQualifiedName~WebAppTests"` → 16/16 (includes 4 new route/branding/link tests).
- `dotnet test --filter "FullyQualifiedName~Accounts|FullyQualifiedName~Security|FullyQualifiedName~Admin|FullyQualifiedName~WebApp"` → **119/119, final combined confirmation run.**

No unfiltered `dotnet test` was run this session, per this milestone's
explicit instruction.

## 20. Targeted test results

**119/119 passing.** One necessary, intentional fix (not a workaround) was
required to keep the pre-existing suite green: `ProductionSecurityTests`'s
shared `Production()` helper builds a Production-mode host used by roughly
ten existing tests, and none of them previously configured the five new
required keys — without updating it, every one of those tests would have
started failing the instant the new startup check was added. Fixed by
adding the five keys (safe, non-secret test values) to that helper's
existing in-memory configuration injection, exactly the same way it already
supplies `ConnectionStrings:DefaultConnection`/`Planning:*`. This is a real,
correct fix caught by actually running the affected suite, not a shortcut
around a failure.

## 21. Build result

`dotnet build`: **0 warnings, 0 errors** (run repeatedly through
implementation; final run after all changes confirmed clean, including the
new `Resend` NuGet package resolving and compiling correctly).

## 22. Manual verification

Performed live against the real local PostgreSQL (`docker-compose.saas.yml`,
restarted this session after a prior session's container had stopped) — no
migration was needed (this milestone makes no schema change):

1. Homepage (`GET /`) — confirmed "ModuleMint" appears in the response body.
2. `GET /login.html` — confirmed a `forgot-password.html` link is present.
3. Registered a real test account, then called
   `POST /api/account/forgot-password` for that real email — confirmed the
   generic response.
9–10. Called the same endpoint for a nonexistent email — confirmed the
   **identical** response body to step 3.
11. `GET /privacy.html`, `/terms.html`, `/refunds.html`, `/support.html` —
   all confirmed `200`.
12. `GET /` — confirmed `href="privacy.html"`/`"terms.html"`/`"refunds.html"`/
   `"support.html"` all present in the footer.
13. `GET /billing.html` — confirmed `href="terms.html"`/`"refunds.html"`
   present.
- Server log reviewed for the whole run — confirmed no token, complete
  reset URL, password, or other secret ever appeared; only the two expected
  safe log lines (`App:PublicBaseUrl is not configured...`,
  `...no email was sent.`), since this local Development environment has no
  Resend key or PublicBaseUrl configured (neither is required outside
  Production).
- Server stopped cleanly afterward (`Stop-Process` on the exact
  `WPAIPlugin.Api.exe` PID, identified via `Get-CimInstance Win32_Process`
  to avoid touching unrelated processes).

**Steps 4–8 (capture the reset email, open the real reset link, set a new
password, confirm old-password-fails/new-password-succeeds) were not
performed as a live browser walkthrough** — this environment has no
configured Resend key (correctly: real credentials should never be added
here), and the `NoOpTransactionalEmailSender` deliberately never logs the
token or reset URL anywhere retrievable, by design (see item 8) — there is
no safe capture mechanism to complete this specific end-to-end walkthrough
live in this environment. The identical code path (`ForgotPassword` →
token generation → `BuildResetUrl` → `ResetPassword` → old password fails →
new password succeeds) is fully exercised instead by the automated
`PasswordRecoveryTests.ResetPassword_ValidToken_ResetsPassword_OldFailsNewSucceeds`
test, using `FakeTransactionalEmailSender` to capture the real, unmodified
reset URL the same code path produces. No claim is made here that a browser
walkthrough of steps 4–8 was performed when it was not.

## 23. Remaining owner-supplied fields

Explicitly preserved as launch blockers, visible on the affected pages and
in `LAUNCH-CHECKLIST.md`:

- `[LEGAL OPERATOR NAME]` — real trading/company name.
- Production domain (`App:PublicBaseUrl`).
- Real support email/domain (`Support:Email`).
- Confirmation that Scotland is the correct governing jurisdiction (or the
  correct one, if not).
- Business/registered address, if legally applicable.
- Company registration number, if applicable.
- VAT registration/details, if applicable.
- A real Resend account/API key and a verified sending domain.

None of these were fabricated or guessed.

## 24. Remaining launch blockers

Everything in item 23, plus (carried forward, unchanged, from
`GO-LIVE-READINESS-COMPLETION.md`): Stripe live-mode activation, a real
production domain/reverse-proxy/HTTPS deployment exercised end-to-end, and
a rehearsed backup/restore drill. This milestone did not attempt any of
those — they remain exactly as previously documented.

## 25. git status --short

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
?? LAUNCH-BLOCKERS-COMPLETION.md
?? LAUNCH-CHECKLIST.md
?? MILESTONE13-COMPLETION.md
?? PRODUCT-SPEC.md
?? PRODUCTION-READINESS-COMPLETION.md
?? PROMOTIONS-FREE-BUILDS-COMPLETION.md
?? STRIPE-FINANCIAL-COMPLETION.md
?? UI-POLISH-COMPLETION.md
?? docker/docker-compose.prod.example.yml
?? src/WPAIPlugin.Api/AiUsage/
?? src/WPAIPlugin.Api/Configuration/
?? src/WPAIPlugin.Api/Controllers/AdminController.cs
?? src/WPAIPlugin.Api/Controllers/AdminDtos.cs
?? src/WPAIPlugin.Api/Controllers/AdminFinanceController.cs
?? src/WPAIPlugin.Api/Controllers/AdminFinanceDtos.cs
?? src/WPAIPlugin.Api/Controllers/AdminPromotionsController.cs
?? src/WPAIPlugin.Api/Controllers/AdminPromotionsDtos.cs
?? src/WPAIPlugin.Api/Controllers/PaymentsController.cs
?? src/WPAIPlugin.Api/Controllers/PromotionsController.cs
?? src/WPAIPlugin.Api/Controllers/PublicConfigController.cs
?? src/WPAIPlugin.Api/Data/AdminAuditLog.cs
?? src/WPAIPlugin.Api/Data/AiUsageEvent.cs
?? src/WPAIPlugin.Api/Data/BuildEntitlementAccount.cs
?? src/WPAIPlugin.Api/Data/BuildEntitlementTransaction.cs
?? src/WPAIPlugin.Api/Data/ProcessedPaymentEvent.cs
?? src/WPAIPlugin.Api/Data/Promotion.cs
?? src/WPAIPlugin.Api/Data/PromotionRedemption.cs
?? src/WPAIPlugin.Api/Data/Purchase.cs
?? src/WPAIPlugin.Api/Email/
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
?? src/WPAIPlugin.Api/wwwroot/forgot-password.html
?? src/WPAIPlugin.Api/wwwroot/forgot-password.js
?? src/WPAIPlugin.Api/wwwroot/index.js
?? src/WPAIPlugin.Api/wwwroot/legal.css
?? src/WPAIPlugin.Api/wwwroot/legal.js
?? src/WPAIPlugin.Api/wwwroot/login.js
?? src/WPAIPlugin.Api/wwwroot/myplugins.js
?? src/WPAIPlugin.Api/wwwroot/nav.js
?? src/WPAIPlugin.Api/wwwroot/plugin.js
?? src/WPAIPlugin.Api/wwwroot/privacy.html
?? src/WPAIPlugin.Api/wwwroot/refunds.html
?? src/WPAIPlugin.Api/wwwroot/register.js
?? src/WPAIPlugin.Api/wwwroot/reset-password.html
?? src/WPAIPlugin.Api/wwwroot/reset-password.js
?? src/WPAIPlugin.Api/wwwroot/site.css
?? src/WPAIPlugin.Api/wwwroot/support.html
?? src/WPAIPlugin.Api/wwwroot/terms.html
?? src/WPAIPlugin.Planning/PlanningUsage.cs
?? tests/WPAIPlugin.Generator.Tests/Accounts/FakeTransactionalEmailSender.cs
?? tests/WPAIPlugin.Generator.Tests/Accounts/PasswordRecoveryTests.cs
?? tests/WPAIPlugin.Generator.Tests/Admin/
?? tests/WPAIPlugin.Generator.Tests/Entitlements/
?? tests/WPAIPlugin.Generator.Tests/Payments/
?? tests/WPAIPlugin.Generator.Tests/Promotions/
?? tests/WPAIPlugin.Generator.Tests/Security/CsrfClient.cs
?? tests/WPAIPlugin.Generator.Tests/Security/ProductionSecurityTests.cs
```

Everything outside the files named in items 1–2 is unrelated prior-
milestone work, preserved exactly as found.

No commit, push, or tag was performed. No product feature was added, no
Stripe/payment accounting changed, no promotion/credit/free-build rule
changed, no broad internal rename performed.
