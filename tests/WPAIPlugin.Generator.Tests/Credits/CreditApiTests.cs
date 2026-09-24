using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using WPAIPlugin.Api.Credits;
using WPAIPlugin.Api.Data;
using WPAIPlugin.Api.Validation;
using WPAIPlugin.Generator.Tests.Projects;
using Xunit;

namespace WPAIPlugin.Generator.Tests.Credits;

public class CreditApiTests
{
    private const string Password = "Str0ng!Passw0rd";
    private static readonly object Spec = new { name = "Credit Test", slug = "credit-test", description = "Credit test",
        version = "1.0.0", author = "Test", features = new[] { "shortcode" } };

    private static async Task<HttpClient> Register(ProjectsTestFactory factory, string email = "credits@example.com")
    {
        var client = factory.CreateClient();
        (await client.PostJsonWithCsrfAsync("/api/account/register", new { email, password = Password })).EnsureSuccessStatusCode();
        return client;
    }
    private static async Task<int> Balance(HttpClient client) =>
        (await client.GetFromJsonAsync<JsonElement>("/api/credits")).GetProperty("balance").GetInt32();

    [Fact]
    public async Task SignupOnce_LoginPersists_AndOwnBalanceOnly()
    {
        using var factory = new ProjectsTestFactory();
        using var first = await Register(factory);
        Assert.Equal(100, await Balance(first));
        Assert.Equal(HttpStatusCode.BadRequest, (await first.PostJsonWithCsrfAsync("/api/account/register",
            new { email = "credits@example.com", password = Password })).StatusCode);
        (await first.PostJsonWithCsrfAsync("/api/projects/build", new { spec = Spec })).EnsureSuccessStatusCode();
        using var second = await Register(factory, "second@example.com");
        Assert.Equal(100, await Balance(second));
        Assert.Equal(99, await Balance(first));
        (await first.PostWithCsrfAsync("/api/account/logout", null)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Unauthorized, (await first.GetAsync("/api/credits")).StatusCode);
        (await first.PostJsonWithCsrfAsync("/api/account/login", new { email = "credits@example.com", password = Password })).EnsureSuccessStatusCode();
        Assert.Equal(99, await Balance(first));
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(2, await db.CreditTransactions.CountAsync(t => t.Type == CreditTransactionType.SignupGrant));
    }

    [Fact]
    public async Task ConfiguredGrantCostsAndResponseAreServerOwned()
    {
        using var factory = new ProjectsTestFactory();
        using var configured = factory.WithWebHostBuilder(b => b.ConfigureServices(s =>
            s.PostConfigure<CreditOptions>(o => { o.SignupGrant = 12; o.StandardBuildCost = 3; o.ValidatedBuildCost = 5; })));
        using var client = configured.CreateClient();
        (await client.PostJsonWithCsrfAsync("/api/account/register", new { email = "config@example.com", password = Password })).EnsureSuccessStatusCode();
        var credits = await client.GetFromJsonAsync<JsonElement>("/api/credits?userId=someone-else");
        Assert.Equal(new[] { "balance", "freeBuildsRemaining", "standardBuildCost", "validatedBuildCost" }, credits.EnumerateObject().Select(p => p.Name));
        Assert.Equal(12, credits.GetProperty("balance").GetInt32());
        Assert.Equal(3, credits.GetProperty("standardBuildCost").GetInt32());
        Assert.Equal(5, credits.GetProperty("validatedBuildCost").GetInt32());
        (await client.PostJsonWithCsrfAsync("/api/projects/build", new { spec = Spec, creditCost = 0, credits = 900 })).EnsureSuccessStatusCode();
        Assert.Equal(9, await Balance(client));
        foreach (var endpoint in new[] { "/api/credits", "/api/credits/grant", "/api/credits/refund", "/api/credits/add" })
            Assert.Contains((await client.PostJsonWithCsrfAsync(endpoint, new { balance = 1000 })).StatusCode,
                new[] { HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed });
        Assert.Equal(9, await Balance(client));
    }

    [Theory]
    [InlineData(false, 99)]
    [InlineData(true, 98)]
    public async Task BuildCostsAndFreePlanning(bool validated, int remaining)
    {
        using var factory = new ProjectsTestFactory();
        using var client = await Register(factory);
        (await client.PostJsonWithCsrfAsync("/api/plugins/plan", new { description = "A shortcode plugin" })).EnsureSuccessStatusCode();
        Assert.Equal(100, await Balance(client));
        var response = await client.PostJsonWithCsrfAsync("/api/projects/build", new { spec = Spec, validated, creditCost = -100 });
        response.EnsureSuccessStatusCode();
        Assert.Equal(remaining, await Balance(client));
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var charge = await db.CreditTransactions.SingleAsync(t => t.Amount < 0);
        Assert.Equal(remaining - 100, charge.Amount);
        Assert.Equal(validated ? CreditTransactionType.ValidatedBuild : CreditTransactionType.PluginBuild, charge.Type);
        Assert.StartsWith("build:", charge.Reference);
        Assert.DoesNotContain(charge.Reference, await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InsufficientCreditsPreventsAllBuildWork(bool validated)
    {
        using var factory = new ProjectsTestFactory();
        using var client = await Register(factory);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var account = await db.CreditAccounts.SingleAsync();
            await new CreditService(db).TryChargeAsync(account.UserId, 100, CreditTransactionType.PluginBuild, "build:spent");
        }
        var calls = 0;
        factory.FakeValidator.Handler = (_, _) => { calls++; throw new Exception("must not validate"); };
        var response = await client.PostJsonWithCsrfAsync("/api/projects/build", new { spec = Spec, validated });
        Assert.Equal(HttpStatusCode.PaymentRequired, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(validated ? 2 : 1, body.GetProperty("required").GetInt32());
        Assert.Equal(0, body.GetProperty("balance").GetInt32());
        Assert.Equal(0, calls);
        Assert.False(Directory.Exists(factory.ArtifactRoot));
        using var check = factory.Services.CreateScope();
        var checkDb = check.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Empty(checkDb.PluginProjects);
        Assert.Empty(checkDb.PluginVersions);
        Assert.Equal(2, await checkDb.CreditTransactions.CountAsync());
    }

    [Theory]
    [InlineData("validation")]
    [InlineData("artifact")]
    [InlineData("persistence")]
    public async Task FailedBuildRefundsOnceAndLeavesNoProject(string failure)
    {
        using var factory = new ProjectsTestFactory();
        if (failure == "persistence") factory.ConfigureDatabase = o => o.AddInterceptors(new FailSave(typeof(PluginProject)));
        using var client = await Register(factory);
        if (failure == "validation") factory.FakeValidator.Handler = (_, _) => new DockerPluginValidator.ProcessResult(1, "", "controlled failure");
        if (failure == "artifact") await File.WriteAllTextAsync(factory.ArtifactRoot, "blocks directory creation");
        try
        {
            var response = await client.PostJsonWithCsrfAsync("/api/projects/build", new { spec = Spec, validated = failure == "validation" });
            Assert.False(response.IsSuccessStatusCode);
            Assert.Equal(100, await Balance(client));
            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var refund = await db.CreditTransactions.SingleAsync(t => t.Type == CreditTransactionType.Refund);
            await new CreditService(db).RefundAsync(refund.UserId, refund.Amount, refund.Reference);
            Assert.Equal(1, await db.CreditTransactions.CountAsync(t => t.Type == CreditTransactionType.Refund));
            Assert.Equal(100, await db.CreditTransactions.SumAsync(t => t.Amount));
            Assert.Empty(db.PluginProjects);
            Assert.Empty(db.PluginVersions);
            if (Directory.Exists(factory.ArtifactRoot)) Assert.Empty(Directory.GetFiles(factory.ArtifactRoot, "*.zip", SearchOption.AllDirectories));
        }
        finally { if (File.Exists(factory.ArtifactRoot)) File.Delete(factory.ArtifactRoot); }
    }

    [Fact]
    public async Task FailedSignupGrantDoesNotLeaveSuccessfulRegistration()
    {
        using var factory = new ProjectsTestFactory();
        factory.ConfigureDatabase = o => o.AddInterceptors(new FailSave(typeof(CreditAccount)));
        using var client = factory.CreateClient();
        var response = await client.PostJsonWithCsrfAsync("/api/account/register", new { email = "fail@example.com", password = Password });
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Empty(db.Users);
        Assert.Empty(db.CreditAccounts);
        Assert.Empty(db.CreditTransactions);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/account/me")).StatusCode);
    }

    [Theory]
    [InlineData(-1, 1, 2)]
    [InlineData(100, 0, 2)]
    [InlineData(100, 1, -1)]
    public void InvalidCreditConfigurationFailsStartup(int grant, int standard, int validated)
    {
        using var factory = new ProjectsTestFactory();
        using var configured = factory.WithWebHostBuilder(b => b.ConfigureServices(s =>
            s.PostConfigure<CreditOptions>(o =>
            {
                o.SignupGrant = grant; o.StandardBuildCost = standard; o.ValidatedBuildCost = validated;
            })));
        Assert.Throws<Microsoft.Extensions.Options.OptionsValidationException>(() => configured.CreateClient());
    }

    [Fact]
    public async Task StaticUiUsesCreditsAndOnlyCustomerBuildRoute()
    {
        using var factory = new ProjectsTestFactory();
        using var client = factory.CreateClient();
        var js = await client.GetStringAsync("/app.js");
        Assert.Contains("/api/projects/build", js);
        Assert.Contains("/api/credits", js);
        Assert.DoesNotContain("/api/plugins/build", js);
        Assert.Contains("/api/credits", await client.GetStringAsync("/dashboard.js"));
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

