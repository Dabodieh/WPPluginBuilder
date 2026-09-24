using System.Net;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using WPAIPlugin.Api.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using WPAIPlugin.Api.Data;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllersWithViews(options =>
    options.Conventions.Add(new LegacyBuildConvention(builder.Environment.IsDevelopment())));
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 1024 * 1024);

// X-Forwarded-* is only honored from explicitly configured trusted proxies -
// never from arbitrary clients, which would let anyone spoof scheme/host/IP
// (e.g. bypass HTTPS-only cookie logic, or poison the account-abuse IP
// partition above). Deployments behind a reverse proxy must list it here;
// with none configured (the default, matching direct-Kestrel Development),
// ASP.NET Core's default same-loopback-only trust applies and nothing is
// forwarded from the public internet.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
    options.KnownProxies.Clear();
    options.KnownNetworks.Clear();
    foreach (var proxy in builder.Configuration.GetSection("ForwardedHeaders:KnownProxies").Get<string[]>() ?? [])
    {
        if (IPAddress.TryParse(proxy, out var address)) options.KnownProxies.Add(address);
    }
    foreach (var network in builder.Configuration.GetSection("ForwardedHeaders:KnownNetworks").Get<string[]>() ?? [])
    {
        var parts = network.Split('/');
        if (parts.Length == 2 && IPAddress.TryParse(parts[0], out var prefix) && int.TryParse(parts[1], out var length))
            options.KnownNetworks.Add(new Microsoft.AspNetCore.HttpOverrides.IPNetwork(prefix, length));
    }
});
builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "X-CSRF-TOKEN";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
        ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
});
builder.Services.AddOptions<SecurityOptions>().BindConfiguration("Security")
    .Validate(o => o.PlanningPerMinute > 0 && o.PlanningPerHour > 0 && o.PlanningPerDay > 0
        && o.BuildsPerMinute > 0
        && o.ValidatedBuildsPerMinute > 0 && o.AccountRequestsPerFiveMinutes > 0
        && o.CheckoutPerMinute > 0 && o.PromoCodePerMinute > 0 && o.PasswordRecoveryPerFiveMinutes > 0
        && o.EmailVerificationResendPerFiveMinutes > 0 && o.AccountSecurityPerFiveMinutes > 0,
        "Security request limits must be positive.").ValidateOnStart();
builder.Services.AddOptions<WPAIPlugin.Api.Security.AbuseOptions>().BindConfiguration("Abuse")
    .Validate(o => o.MaxAiCostUsdMicrosPerUserPerDay > 0, "Abuse cost ceilings must be positive.")
    .ValidateOnStart();
builder.Services.AddRateLimiter(_ => { });
builder.Services.AddOptions<RateLimiterOptions>().Configure<IOptions<SecurityOptions>>((options, settings) =>
{
    var limits = settings.Value;
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, token) =>
    {
        context.HttpContext.Response.Headers.RetryAfter = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter)
            ? Math.Ceiling(retryAfter.TotalSeconds).ToString(System.Globalization.CultureInfo.InvariantCulture) : "60";
        await context.HttpContext.Response.WriteAsJsonAsync(new { error = "Too many requests. Please try again later." }, token);
    };
    options.AddPolicy("planning", context => RateLimitPartition.GetFixedWindowLimiter(
        context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "anonymous",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = limits.PlanningPerMinute, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    options.AddPolicy("build", context => RateLimitPartition.GetFixedWindowLimiter(
        context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "anonymous",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = limits.BuildsPerMinute, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    options.AddPolicy("account", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = limits.AccountRequestsPerFiveMinutes, Window = TimeSpan.FromMinutes(5), QueueLimit = 0 }));
    options.AddPolicy("checkout", context => RateLimitPartition.GetFixedWindowLimiter(
        context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "anonymous",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = limits.CheckoutPerMinute, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    options.AddPolicy("promoCode", context => RateLimitPartition.GetFixedWindowLimiter(
        context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "anonymous",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = limits.PromoCodePerMinute, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    // IP-based, like "account" - the email may not belong to a real,
    // authenticated account, so there is no user identity to partition on.
    options.AddPolicy("passwordRecovery", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = limits.PasswordRecoveryPerFiveMinutes, Window = TimeSpan.FromMinutes(5), QueueLimit = 0 }));
    // Authenticated-only endpoint (current user only, never a public
    // arbitrary-email path) - per-account, protects against email bombing.
    options.AddPolicy("emailVerificationResend", context => RateLimitPartition.GetFixedWindowLimiter(
        context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "anonymous",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = limits.EmailVerificationResendPerFiveMinutes, Window = TimeSpan.FromMinutes(5), QueueLimit = 0 }));
    // Authenticated-only endpoints (change-password, change-email) - per-
    // account, protects against credential-stuffing/automation on the
    // account-security surface.
    options.AddPolicy("accountSecurity", context => RateLimitPartition.GetFixedWindowLimiter(
        context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "anonymous",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = limits.AccountSecurityPerFiveMinutes, Window = TimeSpan.FromMinutes(5), QueueLimit = 0 }));
});
// The validated flag is known only after model binding. Use the built-in
// partitioned limiter there, in addition to the common build route limiter.
builder.Services.AddSingleton<PartitionedRateLimiter<string>>(sp =>
    PartitionedRateLimiter.Create<string, string>(userId => RateLimitPartition.GetFixedWindowLimiter(userId,
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = sp.GetRequiredService<IOptions<SecurityOptions>>().Value.ValidatedBuildsPerMinute,
            Window = TimeSpan.FromMinutes(1), QueueLimit = 0,
        })));

// Planning-specific hourly/daily per-account ceilings, on top of the
// per-minute "planning" policy above - free planning has no credit cost, so
// it is the cheapest AI surface to abuse. Checked manually in
// PluginsController (like the validated-build limiter above) so a rejection
// can also be counted/logged, and so it never calls the AI provider.
builder.Services.AddKeyedSingleton<PartitionedRateLimiter<string>>("planningHourly", (sp, _) =>
    PartitionedRateLimiter.Create<string, string>(userId => RateLimitPartition.GetFixedWindowLimiter(userId,
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = sp.GetRequiredService<IOptions<SecurityOptions>>().Value.PlanningPerHour,
            Window = TimeSpan.FromHours(1), QueueLimit = 0,
        })));
builder.Services.AddKeyedSingleton<PartitionedRateLimiter<string>>("planningDaily", (sp, _) =>
    PartitionedRateLimiter.Create<string, string>(userId => RateLimitPartition.GetFixedWindowLimiter(userId,
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = sp.GetRequiredService<IOptions<SecurityOptions>>().Value.PlanningPerDay,
            Window = TimeSpan.FromDays(1), QueueLimit = 0,
        })));

// SaaS persistence + accounts (Milestone 10).
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services
    .AddIdentity<IdentityUser, IdentityRole>(options =>
    {
        // Defaults are ASP.NET Core Identity's own password/lockout rules.
    })
    .AddEntityFrameworkStores<AppDbContext>()
    .AddDefaultTokenProviders();

// Password-reset tokens use Identity's own DataProtectorTokenProvider (via
// AddDefaultTokenProviders above) - never a custom token store, never a raw
// token persisted to the database. One hour is deliberately shorter than the
// framework's own 1-day default, since a password-reset link is more
// sensitive than the confirmation-email tokens this provider was originally
// designed for (see README "Password recovery" for the documented value).
builder.Services.Configure<DataProtectionTokenProviderOptions>(options =>
    options.TokenLifespan = TimeSpan.FromHours(1));

// Admin authorization (Milestone 14): a real Identity role/policy, never a
// hard-coded email check. AdminOnly requires the "Admin" role.
builder.Services.AddAuthorization(options =>
    options.AddPolicy(WPAIPlugin.Api.Security.AdminAuthorization.AdminOnlyPolicy,
        policy => policy.RequireRole(WPAIPlugin.Api.Security.AdminAuthorization.AdminRole)));
builder.Services.AddOptions<WPAIPlugin.Api.Security.AdminOptions>()
    .BindConfiguration(WPAIPlugin.Api.Security.AdminOptions.SectionName);

// Data Protection keys must survive process/container restarts, or every
// restart invalidates all Identity auth cookies and antiforgery tokens
// (silent forced logout for every signed-in user). Persisted to a
// configurable filesystem path - defaults alongside artifact storage in
// App_Data, which is already documented as requiring a persistent volume in
// production. No external key-storage infrastructure introduced.
var dataProtectionKeyPath = builder.Configuration["DataProtection:KeyRingPath"];
var dataProtectionDirectory = new DirectoryInfo(string.IsNullOrWhiteSpace(dataProtectionKeyPath)
    ? Path.Combine(builder.Environment.ContentRootPath, "App_Data", "dataprotection-keys")
    : Path.IsPathRooted(dataProtectionKeyPath)
        ? dataProtectionKeyPath
        : Path.Combine(builder.Environment.ContentRootPath, dataProtectionKeyPath));
builder.Services.AddDataProtection()
    .SetApplicationName("WPAIPlugin")
    .PersistKeysToFileSystem(dataProtectionDirectory);

builder.Services.ConfigureApplicationCookie(options =>
{
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
        ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
    options.Events.OnRedirectToLogin = context =>
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return Task.CompletedTask;
    };
    options.Events.OnRedirectToAccessDenied = context =>
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        return Task.CompletedTask;
    };
});
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddSingleton<WPAIPlugin.Generator.PluginBuilder>();

// AI planning layer (Milestone 2): provider-agnostic. OpenAI is the default
// runtime provider; Anthropic remains supported as an explicit fallback.
builder.Services.Configure<WPAIPlugin.Planning.PlanningOptions>(
    builder.Configuration.GetSection(WPAIPlugin.Planning.PlanningOptions.SectionName));
builder.Services.AddHttpClient<WPAIPlugin.Planning.Providers.Anthropic.AnthropicPlanningProvider>();
builder.Services.AddHttpClient<WPAIPlugin.Planning.Providers.OpenAI.OpenAIPlanningProvider>();
builder.Services.AddSingleton<WPAIPlugin.Planning.IPlanningProvider>(sp =>
    sp.GetRequiredService<WPAIPlugin.Planning.Providers.Anthropic.AnthropicPlanningProvider>());
builder.Services.AddSingleton<WPAIPlugin.Planning.IPlanningProvider>(sp =>
    sp.GetRequiredService<WPAIPlugin.Planning.Providers.OpenAI.OpenAIPlanningProvider>());
builder.Services.AddSingleton<WPAIPlugin.Planning.IPlanningProviderResolver>(sp =>
{
    var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<WPAIPlugin.Planning.PlanningOptions>>().Value;
    var providers = sp.GetServices<WPAIPlugin.Planning.IPlanningProvider>();
    return new WPAIPlugin.Planning.PlanningProviderResolver(providers, options.DefaultProvider);
});
builder.Services.AddSingleton<WPAIPlugin.Planning.IPluginPlanner, WPAIPlugin.Planning.PluginPlanner>();

// Optional Docker-based build validation (Milestone 9): reuses the existing
// PluginBuilder ZIP and docker/docker-compose.validate.yml. Never involves AI.
builder.Services.Configure<WPAIPlugin.Api.Validation.ValidationOptions>(
    builder.Configuration.GetSection(WPAIPlugin.Api.Validation.ValidationOptions.SectionName));
builder.Services.AddSingleton(sp =>
{
    var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<WPAIPlugin.Api.Validation.ValidationOptions>>().Value;
    // AppContext.BaseDirectory (the built DLL's own folder) resolves the same
    // way whether the API is launched via `dotnet run` or `dotnet <dll>`
    // directly, unlike ContentRootPath which differs between the two.
    var composeFilePath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "docker", "docker-compose.validate.yml");
    var logger = sp.GetRequiredService<ILogger<WPAIPlugin.Api.Validation.DockerPluginValidator>>();
    return new WPAIPlugin.Api.Validation.DockerPluginValidator(
        Path.GetFullPath(composeFilePath),
        TimeSpan.FromSeconds(options.TimeoutSeconds),
        logger);
});

// Authenticated SaaS plugin projects + saved builds (Milestone 11). Local
// filesystem artifact storage, outside wwwroot, never served by UseStaticFiles.
builder.Services.Configure<WPAIPlugin.Api.Storage.ArtifactStorageOptions>(
    builder.Configuration.GetSection(WPAIPlugin.Api.Storage.ArtifactStorageOptions.SectionName));
builder.Services.AddSingleton<WPAIPlugin.Api.Storage.PluginArtifactStore>();

// Test credit system (Milestone 12): server-authoritative balance/ledger.
// No Stripe/payments yet.
builder.Services.AddOptions<WPAIPlugin.Api.Credits.CreditOptions>()
    .Bind(builder.Configuration.GetSection(WPAIPlugin.Api.Credits.CreditOptions.SectionName))
    .Validate(o => o.SignupGrant >= 0 && o.StandardBuildCost > 0 && o.ValidatedBuildCost > 0 && o.MaxAdminAdjustmentMagnitude > 0,
        "Credit grant must be nonnegative, build costs must be positive, and the admin adjustment maximum must be positive.")
    .ValidateOnStart();
builder.Services.AddScoped<WPAIPlugin.Api.Credits.CreditService>();
builder.Services.AddScoped<WPAIPlugin.Api.Security.AdminAuditService>();

// Free-build entitlements + promotions (Promotions + Free Builds milestone):
// a distinct entitlement from credits, ledger-backed exactly like
// CreditService. The launch-critical "2 free builds on signup" offer is a
// plain config value (Promotions:SignupFreeBuilds), never a Promotion record
// - see PromotionsOptions's own doc comment.
builder.Services.AddOptions<WPAIPlugin.Api.Promotions.PromotionsOptions>()
    .BindConfiguration(WPAIPlugin.Api.Promotions.PromotionsOptions.SectionName)
    .Validate(o => o.SignupFreeBuilds >= 0, "Promotions:SignupFreeBuilds must be nonnegative.")
    .ValidateOnStart();
builder.Services.AddScoped<WPAIPlugin.Api.Entitlements.BuildEntitlementService>();
builder.Services.AddScoped<WPAIPlugin.Api.Promotions.PromotionService>();

// AI usage/cost tracking (Milestone 14): server-side pricing configuration
// only - the browser never supplies or influences token pricing.
builder.Services.AddOptions<WPAIPlugin.Api.AiUsage.AiPricingOptions>()
    .BindConfiguration(WPAIPlugin.Api.AiUsage.AiPricingOptions.SectionName);
builder.Services.AddScoped<WPAIPlugin.Api.AiUsage.AiUsageRecorder>();

// Stripe credit purchases (Milestone 16): Stripe Checkout only - no raw card
// handling. Pack price/currency/credits are always server-side configuration;
// the browser may only ever send a PackId. Credits are granted exclusively
// from a verified Stripe webhook (see PaymentsController), never the browser
// success redirect.
builder.Services.AddOptions<WPAIPlugin.Api.Payments.StripeOptions>()
    .BindConfiguration(WPAIPlugin.Api.Payments.StripeOptions.SectionName);
builder.Services.AddOptions<WPAIPlugin.Api.Payments.CreditPackOptions>()
    .BindConfiguration(WPAIPlugin.Api.Payments.CreditPackOptions.SectionName)
    .Validate(o => o.Packs.Values.All(p => p.Credits > 0 && p.AmountMinor > 0 && !string.IsNullOrWhiteSpace(p.Currency)),
        "Every configured credit pack must have positive credits, a positive price, and a currency.")
    .ValidateOnStart();
builder.Services.AddSingleton<WPAIPlugin.Api.Payments.IPaymentGateway, WPAIPlugin.Api.Payments.StripePaymentGateway>();
builder.Services.AddScoped<WPAIPlugin.Api.Payments.PurchaseService>();

// Account recovery / transactional email (ModuleMint account-recovery
// milestone). PublicBaseUrl is the only trusted source for a password-reset
// URL - never request Host/scheme. Resend is optional at the DI level the
// same way Stripe is optional: if Resend:ApiKey is blank, a safe no-op
// sender is registered instead (Development default) - outside Development
// this is instead enforced as a hard startup requirement, see below.
builder.Services.AddOptions<WPAIPlugin.Api.Configuration.AppOptions>()
    .BindConfiguration(WPAIPlugin.Api.Configuration.AppOptions.SectionName);
builder.Services.AddOptions<WPAIPlugin.Api.Configuration.SupportOptions>()
    .BindConfiguration(WPAIPlugin.Api.Configuration.SupportOptions.SectionName);
builder.Services.AddOptions<WPAIPlugin.Api.Configuration.EmailOptions>()
    .BindConfiguration(WPAIPlugin.Api.Configuration.EmailOptions.SectionName);
var resendApiKey = builder.Configuration["Resend:ApiKey"];
if (!string.IsNullOrWhiteSpace(resendApiKey))
{
    builder.Services.AddHttpClient<Resend.ResendClient>();
    builder.Services.Configure<Resend.ResendClientOptions>(options => options.ApiToken = resendApiKey);
    builder.Services.AddTransient<Resend.IResend, Resend.ResendClient>();
    builder.Services.AddScoped<WPAIPlugin.Api.Email.ITransactionalEmailSender, WPAIPlugin.Api.Email.ResendTransactionalEmailSender>();
}
else
{
    builder.Services.AddScoped<WPAIPlugin.Api.Email.ITransactionalEmailSender, WPAIPlugin.Api.Email.NoOpTransactionalEmailSender>();
}

// Deployment health checks (Microsoft.Extensions.Diagnostics.HealthChecks is
// part of the shared ASP.NET Core framework, no new package). /health/live
// never touches the database - just confirms the process is up. /health/ready
// additionally confirms the database is reachable, for orchestrators that
// gate traffic on readiness. A plain CanConnectAsync check avoids adding the
// separate Microsoft.Extensions.Diagnostics.HealthChecks.EntityFrameworkCore
// package for one query.
builder.Services.AddHealthChecks()
    .AddCheck<WPAIPlugin.Api.Health.DatabaseHealthCheck>("database", tags: ["ready"]);

// Framework exception logging includes arbitrary exception messages. Keep
// production boundary logs to categories; provider bodies/keys never belong there.
if (!builder.Environment.IsDevelopment())
    builder.Logging.AddFilter("Microsoft.AspNetCore.Diagnostics.ExceptionHandlerMiddleware", LogLevel.None);
var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    var configuration = app.Configuration;
    var provider = configuration["Planning:DefaultProvider"] ?? "openai";
    var providerKey = provider.ToLowerInvariant() switch
    {
        "openai" => configuration["Planning:OpenAI:ApiKey"],
        "anthropic" => configuration["Planning:Anthropic:ApiKey"],
        _ => null,
    };
    if (string.IsNullOrWhiteSpace(configuration.GetConnectionString("DefaultConnection"))
        || string.IsNullOrWhiteSpace(providerKey)
        || string.IsNullOrWhiteSpace(configuration["Artifacts:RootPath"]))
        throw new InvalidOperationException("Production requires database, selected planning provider and artifact storage configuration.");

    // Password reset is a core account-security feature, not an optional one
    // like Stripe - production must not silently fall back to the no-op
    // email sender. All five are required together so a real reset link/
    // email can always be built and sent.
    if (string.IsNullOrWhiteSpace(configuration["App:PublicBaseUrl"])
        || string.IsNullOrWhiteSpace(configuration["Support:Email"])
        || string.IsNullOrWhiteSpace(configuration["Email:FromAddress"])
        || string.IsNullOrWhiteSpace(configuration["Email:FromName"])
        || string.IsNullOrWhiteSpace(configuration["Resend:ApiKey"]))
        throw new InvalidOperationException("Production requires public base URL, support email, sender email identity, and Resend configuration.");
}
// Never allow configured artifact storage to become a static file directory.
var artifactRoot = Path.GetFullPath(Path.Combine(app.Environment.ContentRootPath,
    app.Services.GetRequiredService<IOptions<WPAIPlugin.Api.Storage.ArtifactStorageOptions>>().Value.RootPath));
var webRoot = Path.GetFullPath(app.Environment.WebRootPath ?? Path.Combine(app.Environment.ContentRootPath, "wwwroot"));
if (artifactRoot.Equals(webRoot, StringComparison.OrdinalIgnoreCase)
    || artifactRoot.StartsWith(webRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
    throw new InvalidOperationException("Artifact storage must be outside the web root.");

// Must run before anything that reads scheme/host/remote IP (HTTPS
// redirection, secure-cookie decisions, the account rate limiter's IP
// partition below). Only forwards from KnownProxies/KnownNetworks configured
// above - never trusts arbitrary clients.
app.UseForwardedHeaders();

app.Use(async (context, next) =>
{
    context.Response.OnStarting(() =>
    {
        context.Response.Headers.XContentTypeOptions = "nosniff";
        context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
        context.Response.Headers.XFrameOptions = "DENY";
        context.Response.Headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=()";
        context.Response.Headers.ContentSecurityPolicy = "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; connect-src 'self'; object-src 'none'; base-uri 'none'; frame-ancestors 'none'; form-action 'self'";
        // Swagger's own bootstrap is inline; this exception is Development only.
        if (app.Environment.IsDevelopment() && context.Request.Path.StartsWithSegments("/swagger"))
            context.Response.Headers.ContentSecurityPolicy = context.Response.Headers.ContentSecurityPolicy.ToString()
                .Replace("script-src 'self';", "script-src 'self' 'unsafe-inline';");
        if (context.Request.Path.StartsWithSegments("/api")) context.Response.Headers.CacheControl = "no-store";
        return Task.CompletedTask;
    });
    await next();
});
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler(error => error.Run(async context =>
    {
        var failure = context.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>()?.Error;
        if (failure is BadHttpRequestException badRequest)
        {
            context.Response.StatusCode = badRequest.StatusCode;
            await context.Response.WriteAsJsonAsync(new { error = badRequest.StatusCode == 413
                ? "Request body is too large." : "Invalid request." });
            return;
        }
        app.Logger.LogError("Request failed with an unhandled server error.");
        context.Response.StatusCode = 500;
        await context.Response.WriteAsJsonAsync(new { error = "The request could not be completed. Please try again." });
    }));
    app.UseHsts();
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseDefaultFiles();
app.UseStaticFiles();

app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

// Minimal liveness/readiness endpoints for deployment monitoring. Anonymous:
// orchestrators/load balancers polling these have no session to authenticate
// with, and the response body never includes connection strings, database
// names, exception details, or any other internal information - just a
// status word.
app.MapHealthChecks("/health/live", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = _ => false,
    ResponseWriter = (context, _) => context.Response.WriteAsync("Healthy"),
}).AllowAnonymous();
app.MapHealthChecks("/health/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready"),
    ResponseWriter = (context, report) =>
    {
        context.Response.ContentType = "text/plain";
        return context.Response.WriteAsync(report.Status.ToString());
    },
}).AllowAnonymous();

app.MapControllers();

using (var scope = app.Services.CreateScope())
{
    await WPAIPlugin.Api.Security.AdminBootstrapper.RunAsync(scope.ServiceProvider);
}

app.Run();

// Exposes the implicit Program class to WebApplicationFactory<Program> in integration tests.
public partial class Program { }
