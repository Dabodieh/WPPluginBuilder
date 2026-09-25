using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using WPAIPlugin.Api.Data;
using WPAIPlugin.Api.Payments;
using WPAIPlugin.Api.Storage;
using WPAIPlugin.Api.Validation;
using WPAIPlugin.Generator.Tests.Payments;
using WPAIPlugin.Generator.Tests.Planning;
using WPAIPlugin.Generator.Tests.Validation;
using WPAIPlugin.Planning;

namespace WPAIPlugin.Generator.Tests.Projects;

/// <summary>
/// Full DI-pipeline test factory for the SaaS project/version/download flow
/// (Milestone 11): isolated EF Core InMemory database, isolated temp-directory
/// artifact store, and a fake planning provider/Docker validator so tests
/// never touch a live database, filesystem outside temp, OpenAI/Anthropic, or
/// real Docker.
/// </summary>
public sealed class ProjectsTestFactory : WebApplicationFactory<Program>
{
    private readonly string _databaseName = Guid.NewGuid().ToString();
    public readonly string ArtifactRoot = Path.Combine(Path.GetTempPath(), "wpaiplugin-test-artifacts-" + Guid.NewGuid().ToString("N"));

    public Action<DbContextOptionsBuilder>? ConfigureDatabase { get; set; }

    public FakeDockerPluginValidator FakeValidator { get; } = new();

    public FakePlanningProvider FakePlanningProvider { get; } = new();

    public FakePaymentGateway FakePaymentGateway { get; } = new();

    public WPAIPlugin.Generator.Tests.Security.FakeTurnstileVerifier FakeTurnstileVerifier { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            services.PostConfigure<WPAIPlugin.Api.Security.SecurityOptions>(o =>
            {
                o.AccountRequestsPerFiveMinutes = 1000;
                o.PlanningPerMinute = 1000; o.BuildsPerMinute = 1000; o.ValidatedBuildsPerMinute = 1000;
            });
            // Existing tests assert a 100-credit signup grant; pinned here
            // explicitly so they stay independent of whatever the production
            // appsettings.json default happens to be (currently 5).
            services.PostConfigure<WPAIPlugin.Api.Credits.CreditOptions>(o => o.SignupGrant = 100);
            // Existing tests assert exact per-build credit charges; pinned to
            // 0 so registrations in this shared factory don't incidentally
            // cover the first builds for free and desync those assertions
            // from the production default (currently 2). Free-build-specific
            // behaviour is tested with its own explicit PostConfigure - see
            // FreeBuildsTests.cs.
            services.PostConfigure<WPAIPlugin.Api.Promotions.PromotionsOptions>(o => o.SignupFreeBuilds = 0);
            services.RemoveAll<DbContextOptions<AppDbContext>>();
            services.AddDbContext<AppDbContext>(options =>
                {
                    options.UseInMemoryDatabase(_databaseName);
                    // This factory tests the authenticated build/credit/plan
                    // flow, not the email-verification feature itself (see
                    // EmailVerificationTests.cs, built on AccountTestFactory,
                    // for unverified-account behaviour) - every account
                    // registered here starts pre-verified so existing
                    // "verified users retain current behaviour exactly"
                    // build/plan tests are unaffected by the verification gate.
                    options.AddInterceptors(new AutoConfirmEmailInterceptor());
                    ConfigureDatabase?.Invoke(options);
                });

            services.RemoveAll<DockerPluginValidator>();
            services.AddSingleton<DockerPluginValidator>(FakeValidator);

            services.RemoveAll<IPlanningProvider>();
            services.AddSingleton<IPlanningProvider>(FakePlanningProvider);
            services.RemoveAll<IPlanningProviderResolver>();
            services.AddSingleton<IPlanningProviderResolver>(sp =>
                new PlanningProviderResolver(sp.GetServices<IPlanningProvider>(), "fake"));

            services.PostConfigure<ArtifactStorageOptions>(options => options.RootPath = ArtifactRoot);

            services.RemoveAll<IPaymentGateway>();
            services.AddSingleton<IPaymentGateway>(FakePaymentGateway);

            // Never call the real Cloudflare endpoint from a test. Registered
            // regardless of SignupProtection:Turnstile:Enabled, so any test
            // that turns Turnstile on via PostConfigure still exercises a
            // deterministic verifier, not a live network call.
            services.RemoveAll<WPAIPlugin.Api.Security.ITurnstileVerifier>();
            services.AddSingleton<WPAIPlugin.Api.Security.ITurnstileVerifier>(FakeTurnstileVerifier);
            services.PostConfigure<StripeOptions>(o =>
            {
                o.SecretKey = "sk_test_fake";
                o.PublicBaseUrl = "https://app.test.example";
            });
            services.PostConfigure<CreditPackOptions>(o => o.Packs = new Dictionary<string, CreditPack>
            {
                ["starter"] = new() { DisplayName = "Starter", Credits = 25, AmountMinor = 499, Currency = "GBP" },
                ["builder"] = new() { DisplayName = "Builder", Credits = 75, AmountMinor = 999, Currency = "GBP" },
                ["pro"] = new() { DisplayName = "Pro", Credits = 200, AmountMinor = 1999, Currency = "GBP" },
            });
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        try
        {
            if (Directory.Exists(ArtifactRoot))
            {
                Directory.Delete(ArtifactRoot, recursive: true);
            }
        }
        catch (IOException)
        {
            // Best effort cleanup only.
        }
    }

    private sealed class AutoConfirmEmailInterceptor : SaveChangesInterceptor
    {
        public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
        {
            ConfirmAddedUsers(eventData);
            return result;
        }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            ConfirmAddedUsers(eventData);
            return ValueTask.FromResult(result);
        }

        private static void ConfirmAddedUsers(DbContextEventData eventData)
        {
            foreach (var entry in eventData.Context!.ChangeTracker.Entries<IdentityUser>())
            {
                if (entry.State == EntityState.Added)
                {
                    entry.Entity.EmailConfirmed = true;
                }
            }
        }
    }
}
