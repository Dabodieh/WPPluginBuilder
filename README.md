# ModuleMint

Describe a WordPress plugin in plain English. ModuleMint turns that into a
structured plan, generates real PHP from fixed templates (never
AI-generated code), and hands you a downloadable ZIP — optionally validated
by installing and activating it in a real, disposable WordPress instance
first.

> `WPAIPlugin` is the internal/technical project name (namespaces, solution
> file, repo name). **ModuleMint** is the customer-facing product name shown
> throughout the app.

## How it works

1. **Describe** — write what you want the plugin to do.
2. **Plan** — an AI provider (OpenAI or Anthropic) turns that into a
   structured spec: shortcode, custom post type, custom fields, settings
   page, scheduled task. The AI only plans — it never writes PHP.
3. **Build** — a deterministic builder turns the approved spec into plugin
   source from fixed templates, and packages it as a ZIP.
4. **Validate (optional)** — installs and activates the generated plugin in
   a disposable WordPress + MariaDB container via Docker, so "it works" is
   proven, not just claimed.

Every successful build is saved to the user's account (My Plugins), with
full version history.

## Tech stack

- **Backend:** ASP.NET Core 8, EF Core, PostgreSQL, ASP.NET Identity
- **Frontend:** static HTML/CSS/JS — no framework, no build step
- **Payments:** Stripe Checkout (credit packs)
- **Email:** Resend (password reset / verification)
- **Validation:** Docker + WordPress + MariaDB (disposable, per-run)

## Project structure

```
src/
  WPAIPlugin.Api/         ASP.NET Core app: controllers, EF Core, wwwroot (UI)
  WPAIPlugin.Planning/    AI provider integration (OpenAI/Anthropic) → PluginSpec
  WPAIPlugin.Generator/   Deterministic PluginSpec → PHP source
  WPAIPlugin.Templates/   Fixed PHP templates used by the generator
tests/
  WPAIPlugin.Generator.Tests/
docs/                     Deployment reference, product spec, milestone notes
scripts/                  PowerShell helpers (local DB, WordPress dev site, validation)
docker/                   Compose files for local dev, validation, and prod
```

## Quick start (local development)

**Requirements:** .NET 8 SDK, Docker Desktop (for the local database and
optional plugin validation), PowerShell 7+.

```bash
git clone https://github.com/Dabodieh/WPPluginBuilder.git
cd WPPluginBuilder
```

1. **Start the local database:**
   ```
   powershell -ExecutionPolicy Bypass -File scripts/Start-SaaSDatabase.ps1
   ```
2. **Apply migrations:**
   ```
   dotnet ef database update --project src/WPAIPlugin.Api --startup-project src/WPAIPlugin.Api
   ```
3. **Set an AI provider key** (at least one — OpenAI or Anthropic):
   ```
   dotnet user-secrets set "Planning:Anthropic:ApiKey" "<your-key>" --project src/WPAIPlugin.Api
   ```
4. **Run the app:**
   ```
   dotnet run --project src/WPAIPlugin.Api
   ```
5. Open `https://localhost:7092` (or the URL printed on startup) and
   register an account.

## Tests

```
dotnet test
```

Runs fully offline — no PostgreSQL, Docker, or AI provider required.

## Further documentation

- [docs/PRODUCT-SPEC.md](docs/PRODUCT-SPEC.md) — full product/feature spec
- [docs/DEPLOYMENT.md](docs/DEPLOYMENT.md) — configuration reference and
  production deployment guide
- [docs/](docs/) — milestone-by-milestone implementation notes

## Status

This is a work-in-progress SaaS product, not yet launched. Legal pages
(Privacy/Terms/Refunds) exist but carry placeholder operator/company
details pending real business information — see the notices on those pages
and [docs/DEPLOYMENT.md](docs/DEPLOYMENT.md#legal-pages--support).
