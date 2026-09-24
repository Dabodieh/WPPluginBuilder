using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using WPAIPlugin.Api.Credits;
using WPAIPlugin.Api.Data;
using WPAIPlugin.Api.Promotions;
using WPAIPlugin.Generator.Tests.Projects;
using Xunit;

namespace WPAIPlugin.Generator.Tests.Entitlements;

/// <summary>
/// New-customer free-build offer, end to end over HTTP (Promotions + Free
/// Builds milestone). ProjectsTestFactory pins Promotions:SignupFreeBuilds to
/// 0 so every other test suite stays on the pre-existing credit-only
/// behaviour; these tests explicitly opt back in via WithWebHostBuilder,
/// exactly like CreditApiTests.ConfiguredGrantCostsAndResponseAreServerOwned
/// already does for CreditOptions.
/// </summary>
public class FreeBuildsApiTests
{
    private const string Password = "Str0ng!Passw0rd";
    private static readonly object Spec = new { name = "Free Build Test", slug = "free-build-test", description = "test",
        version = "1.0.0", author = "Test", features = new[] { "shortcode" } };

    private static async Task<HttpClient> Register(HttpClient rootClient, string email) =>
        await RegisterOn(rootClient, email);

    private static async Task<HttpClient> RegisterOn(HttpClient client, string email)
    {
        (await client.PostJsonWithCsrfAsync("/api/account/register", new { email, password = Password })).EnsureSuccessStatusCode();
        return client;
    }

    private static async Task<JsonElement> GetCredits(HttpClient client) =>
        await client.GetFromJsonAsync<JsonElement>("/api/credits");

    [Fact]
    public async Task NewRegistration_GetsTwoFreeBuildsAndConfiguredCredits_ExactlyOnce()
    {
        using var factory = new ProjectsTestFactory();
        using var configured = factory.WithWebHostBuilder(b => b.ConfigureServices(s =>
            s.PostConfigure<PromotionsOptions>(o => o.SignupFreeBuilds = 2)));
        using var client = await RegisterOn(configured.CreateClient(), $"newuser-{Guid.NewGuid()}@example.com");

        var credits = await GetCredits(client);
        Assert.Equal(100, credits.GetProperty("balance").GetInt32());
        Assert.Equal(2, credits.GetProperty("freeBuildsRemaining").GetInt32());

        using var scope = configured.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(1, await db.CreditTransactions.CountAsync(t => t.Type == CreditTransactionType.SignupGrant));
        Assert.Equal(1, await db.BuildEntitlementTransactions.CountAsync(t => t.Type == BuildEntitlementTransactionType.SignupFreeBuildGrant));
        var entitlementTransaction = await db.BuildEntitlementTransactions.SingleAsync(t => t.Type == BuildEntitlementTransactionType.SignupFreeBuildGrant);
        Assert.Equal(2, entitlementTransaction.Amount);
    }

    [Fact]
    public async Task ExistingAccountWithNoEntitlementRow_DefaultsToZeroFreeBuilds()
    {
        // Simulates a user who registered before this milestone: a
        // CreditAccount exists but no BuildEntitlementAccount row was ever
        // created for them - GetRemainingAsync must return 0, never
        // silently grant the current configured signup amount.
        using var factory = new ProjectsTestFactory();
        using var client = await Register(factory.CreateClient(), $"preexisting-{Guid.NewGuid()}@example.com");

        var credits = await GetCredits(client);
        Assert.Equal(0, credits.GetProperty("freeBuildsRemaining").GetInt32());

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Empty(db.BuildEntitlementAccounts);
    }

    [Fact]
    public async Task StandardBuild_UsesFreeBuild_ChargesZeroCredits()
    {
        using var factory = new ProjectsTestFactory();
        using var configured = factory.WithWebHostBuilder(b => b.ConfigureServices(s =>
            s.PostConfigure<PromotionsOptions>(o => o.SignupFreeBuilds = 2)));
        using var client = await RegisterOn(configured.CreateClient(), $"buyer-{Guid.NewGuid()}@example.com");

        var response = await client.PostJsonWithCsrfAsync("/api/projects/build", new { spec = Spec, validated = false });
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.True(result.GetProperty("freeBuildUsed").GetBoolean());
        Assert.Equal(0, result.GetProperty("creditsCharged").GetInt32());
        Assert.Equal(1, result.GetProperty("freeBuildsRemaining").GetInt32());
        Assert.Equal(100, result.GetProperty("creditBalance").GetInt32());

        var credits = await GetCredits(client);
        Assert.Equal(100, credits.GetProperty("balance").GetInt32());
        Assert.Equal(1, credits.GetProperty("freeBuildsRemaining").GetInt32());
    }

    [Fact]
    public async Task ValidatedBuild_UsesFreeBuild_ChargesOnlyOneCredit()
    {
        using var factory = new ProjectsTestFactory();
        using var configured = factory.WithWebHostBuilder(b => b.ConfigureServices(s =>
            s.PostConfigure<PromotionsOptions>(o => o.SignupFreeBuilds = 2)));
        using var client = await RegisterOn(configured.CreateClient(), $"buyer-{Guid.NewGuid()}@example.com");

        var response = await client.PostJsonWithCsrfAsync("/api/projects/build", new { spec = Spec, validated = true });
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.True(result.GetProperty("freeBuildUsed").GetBoolean());
        Assert.Equal(1, result.GetProperty("creditsCharged").GetInt32());
        Assert.Equal(1, result.GetProperty("freeBuildsRemaining").GetInt32());
        Assert.Equal(99, result.GetProperty("creditBalance").GetInt32());
    }

    [Fact]
    public async Task ExampleFlow_TwoBuildsThenNormalCostsApply()
    {
        // Mirrors the milestone's own worked example exactly: 5 credits,
        // 2 free builds -> standard build (free, 5 credits) -> validated
        // build (free build + 1 credit, 4 credits) -> next standard build
        // costs the normal 1 credit (3 credits).
        using var factory = new ProjectsTestFactory();
        using var configured = factory.WithWebHostBuilder(b => b.ConfigureServices(s =>
        {
            s.PostConfigure<PromotionsOptions>(o => o.SignupFreeBuilds = 2);
            s.PostConfigure<CreditOptions>(o => { o.SignupGrant = 5; o.StandardBuildCost = 1; o.ValidatedBuildCost = 2; });
        }));
        using var client = await RegisterOn(configured.CreateClient(), $"buyer-{Guid.NewGuid()}@example.com");

        var first = await (await client.PostJsonWithCsrfAsync("/api/projects/build", new { spec = Spec, validated = false }))
            .Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, first.GetProperty("freeBuildsRemaining").GetInt32());
        Assert.Equal(5, first.GetProperty("creditBalance").GetInt32());

        var second = await (await client.PostJsonWithCsrfAsync("/api/projects/build", new { spec = Spec, validated = true }))
            .Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0, second.GetProperty("freeBuildsRemaining").GetInt32());
        Assert.Equal(4, second.GetProperty("creditBalance").GetInt32());

        var third = await client.PostJsonWithCsrfAsync("/api/projects/build", new { spec = Spec, validated = false });
        third.EnsureSuccessStatusCode();
        var thirdBody = await third.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(thirdBody.GetProperty("freeBuildUsed").GetBoolean());
        Assert.Equal(1, thirdBody.GetProperty("creditsCharged").GetInt32());
        Assert.Equal(3, thirdBody.GetProperty("creditBalance").GetInt32());
    }

    [Fact]
    public async Task NoFreeBuildsRemaining_FallsBackToNormalCosts()
    {
        using var factory = new ProjectsTestFactory();
        using var client = await Register(factory.CreateClient(), $"buyer-{Guid.NewGuid()}@example.com");
        // Factory default pins SignupFreeBuilds=0 - no free build ever exists here.

        var response = await client.PostJsonWithCsrfAsync("/api/projects/build", new { spec = Spec, validated = false });
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.False(result.GetProperty("freeBuildUsed").GetBoolean());
        Assert.Equal(1, result.GetProperty("creditsCharged").GetInt32());
        Assert.Equal(99, result.GetProperty("creditBalance").GetInt32());
    }

    [Fact]
    public async Task FailedStandardBuild_RestoresFreeBuild_NoCreditLedgerEntry()
    {
        using var factory = new ProjectsTestFactory();
        using var configured = factory.WithWebHostBuilder(b => b.ConfigureServices(s =>
            s.PostConfigure<PromotionsOptions>(o => o.SignupFreeBuilds = 2)));
        using var client = await RegisterOn(configured.CreateClient(), $"buyer-{Guid.NewGuid()}@example.com");
        await File.WriteAllTextAsync(factory.ArtifactRoot, "blocks directory creation");

        try
        {
            var response = await client.PostJsonWithCsrfAsync("/api/projects/build", new { spec = Spec, validated = false });
            Assert.False(response.IsSuccessStatusCode);
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("Build failed. Your free build was returned.", body.GetProperty("error").GetString());

            var credits = await GetCredits(client);
            Assert.Equal(2, credits.GetProperty("freeBuildsRemaining").GetInt32());
            Assert.Equal(100, credits.GetProperty("balance").GetInt32());

            using var scope = configured.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Empty(db.CreditTransactions.Where(t => t.Type == CreditTransactionType.PluginBuild || t.Type == CreditTransactionType.Refund));
            Assert.Equal(1, await db.BuildEntitlementTransactions.CountAsync(t => t.Type == BuildEntitlementTransactionType.FreeBuildRefund));
        }
        finally { if (File.Exists(factory.ArtifactRoot)) File.Delete(factory.ArtifactRoot); }
    }

    [Fact]
    public async Task FailedValidatedBuild_RestoresBothFreeBuildAndCredit()
    {
        using var factory = new ProjectsTestFactory();
        using var configured = factory.WithWebHostBuilder(b => b.ConfigureServices(s =>
            s.PostConfigure<PromotionsOptions>(o => o.SignupFreeBuilds = 2)));
        using var client = await RegisterOn(configured.CreateClient(), $"buyer-{Guid.NewGuid()}@example.com");
        factory.FakeValidator.Handler = (_, _) => new WPAIPlugin.Api.Validation.DockerPluginValidator.ProcessResult(1, "", "controlled failure");

        var response = await client.PostJsonWithCsrfAsync("/api/projects/build", new { spec = Spec, validated = true });
        Assert.False(response.IsSuccessStatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Build failed. Your free build and credits were returned.", body.GetProperty("error").GetString());

        var credits = await GetCredits(client);
        Assert.Equal(2, credits.GetProperty("freeBuildsRemaining").GetInt32());
        Assert.Equal(100, credits.GetProperty("balance").GetInt32());

        using var scope = configured.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(1, await db.CreditTransactions.CountAsync(t => t.Type == CreditTransactionType.ValidatedBuild));
        Assert.Equal(1, await db.CreditTransactions.CountAsync(t => t.Type == CreditTransactionType.Refund));
        Assert.Equal(1, await db.BuildEntitlementTransactions.CountAsync(t => t.Type == BuildEntitlementTransactionType.FreeBuildConsumed));
        Assert.Equal(1, await db.BuildEntitlementTransactions.CountAsync(t => t.Type == BuildEntitlementTransactionType.FreeBuildRefund));
    }

    [Fact]
    public async Task FailedEntitlementGrant_DoesNotLeaveSuccessfulRegistrationOrOrphanedCredits()
    {
        using var factory = new ProjectsTestFactory();
        factory.ConfigureDatabase = o => o.AddInterceptors(new FailSave(typeof(BuildEntitlementAccount)));
        using var configured = factory.WithWebHostBuilder(b => b.ConfigureServices(s =>
            s.PostConfigure<PromotionsOptions>(o => o.SignupFreeBuilds = 2)));
        using var client = configured.CreateClient();

        var response = await client.PostJsonWithCsrfAsync("/api/account/register", new { email = "fail@example.com", password = Password });

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        using var scope = configured.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Empty(db.Users);
        Assert.Empty(db.CreditAccounts);
        Assert.Empty(db.CreditTransactions);
        Assert.Empty(db.BuildEntitlementAccounts);
        Assert.Empty(db.BuildEntitlementTransactions);
    }

    private sealed class FailSave(Type entityType) : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context!.ChangeTracker.Entries().Any(e => e.Entity.GetType() == entityType && e.State == EntityState.Added))
                throw new DbUpdateException("Controlled persistence failure");
            return ValueTask.FromResult(result);
        }
    }
}
