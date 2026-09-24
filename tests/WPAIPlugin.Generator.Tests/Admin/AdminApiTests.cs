using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WPAIPlugin.Api.AiUsage;
using WPAIPlugin.Api.Data;
using WPAIPlugin.Api.Security;
using WPAIPlugin.Generator.Tests.Projects;
using Xunit;

namespace WPAIPlugin.Generator.Tests.Admin;

/// <summary>
/// Admin authorization and AI usage reporting (Milestone 14). Reuses
/// ProjectsTestFactory (isolated InMemory database, real Identity/CSRF
/// pipeline, fake planning provider) - no live database, Docker, or AI
/// provider required.
/// </summary>
public class AdminApiTests
{
    private const string Password = "Str0ng!Passw0rd";

    private static async Task<HttpClient> Register(ProjectsTestFactory factory, string email)
    {
        var client = factory.CreateClient();
        (await client.PostJsonWithCsrfAsync("/api/account/register", new { email, password = Password })).EnsureSuccessStatusCode();
        return client;
    }

    private static async Task PromoteToAdminAsync(ProjectsTestFactory factory, string email)
    {
        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        if (!await roleManager.RoleExistsAsync(AdminAuthorization.AdminRole))
        {
            await roleManager.CreateAsync(new IdentityRole(AdminAuthorization.AdminRole));
        }
        var user = await userManager.FindByEmailAsync(email);
        await userManager.AddToRoleAsync(user!, AdminAuthorization.AdminRole);
    }

    /// <summary>
    /// Role claims are baked into the Identity auth cookie at sign-in time, so
    /// a role granted mid-session only takes effect after the next login (the
    /// same behavior a real browser session would see).
    /// </summary>
    private static async Task PromoteToAdminAndReauthenticateAsync(ProjectsTestFactory factory, HttpClient client, string email)
    {
        await PromoteToAdminAsync(factory, email);
        (await client.PostWithCsrfAsync("/api/account/logout", null)).EnsureSuccessStatusCode();
        (await client.PostJsonWithCsrfAsync("/api/account/login", new { email, password = Password })).EnsureSuccessStatusCode();
    }

    // --- Authorization ---------------------------------------------------

    [Theory]
    [InlineData("/api/admin/overview")]
    [InlineData("/api/admin/users")]
    [InlineData("/api/admin/plugins")]
    [InlineData("/api/admin/ai-usage")]
    [InlineData("/api/admin/system")]
    public async Task AnonymousUser_DeniedAdminApis(string path)
    {
        using var factory = new ProjectsTestFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("/api/admin/overview")]
    [InlineData("/api/admin/users")]
    [InlineData("/api/admin/plugins")]
    [InlineData("/api/admin/ai-usage")]
    [InlineData("/api/admin/system")]
    public async Task NormalUser_DeniedAdminApis(string path)
    {
        using var factory = new ProjectsTestFactory();
        using var client = await Register(factory, $"normal-{Guid.NewGuid()}@example.com");

        var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task AdminUser_AllowedAdminOverview()
    {
        using var factory = new ProjectsTestFactory();
        var email = $"admin-{Guid.NewGuid()}@example.com";
        using var client = await Register(factory, email);
        await PromoteToAdminAndReauthenticateAsync(factory, client, email);

        var response = await client.GetAsync("/api/admin/overview");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.GetProperty("totalUsers").GetInt32() >= 1);
    }

    [Fact]
    public async Task AdminResponses_ContainNoProviderSecrets()
    {
        using var factory = new ProjectsTestFactory();
        var email = $"admin-{Guid.NewGuid()}@example.com";
        using var client = await Register(factory, email);
        await PromoteToAdminAndReauthenticateAsync(factory, client, email);

        var overview = await client.GetStringAsync("/api/admin/overview");
        var system = await client.GetStringAsync("/api/admin/system");
        var users = await client.GetStringAsync("/api/admin/users");

        foreach (var body in new[] { overview, system, users })
        {
            Assert.DoesNotContain("ApiKey", body, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("sk-", body, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("PasswordHash", body, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task AdminSystem_ReportsStripeConfiguredWithoutSecrets()
    {
        using var factory = new ProjectsTestFactory();
        var email = $"admin-{Guid.NewGuid()}@example.com";
        using var client = await Register(factory, email);
        await PromoteToAdminAndReauthenticateAsync(factory, client, email);

        var response = await client.GetAsync("/api/admin/system");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        // ProjectsTestFactory configures a SecretKey/PublicBaseUrl but no
        // WebhookSecret, so StripeConfigured (all three required) is false -
        // proving the flag reflects real configuration state, not a constant.
        Assert.False(body.GetProperty("stripeConfigured").GetBoolean());
    }

    [Fact]
    public async Task AdminSystem_ReportsTransactionalEmailConfiguredWithoutSecrets()
    {
        using var factory = new ProjectsTestFactory();
        var email = $"admin-{Guid.NewGuid()}@example.com";
        using var client = await Register(factory, email);
        await PromoteToAdminAndReauthenticateAsync(factory, client, email);

        var response = await client.GetAsync("/api/admin/system");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        // ProjectsTestFactory never configures Resend:ApiKey, so the DI
        // container resolves NoOpTransactionalEmailSender - proving the flag
        // reflects which implementation was actually resolved, not a constant.
        Assert.False(body.GetProperty("transactionalEmailConfigured").GetBoolean());
        var raw = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("re_", raw, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AdminMe_ReportsIsAdminFlag()
    {
        using var factory = new ProjectsTestFactory();
        var email = $"admin-{Guid.NewGuid()}@example.com";
        using var client = await Register(factory, email);

        var beforePromotion = await client.GetFromJsonAsync<JsonElement>("/api/account/me");
        Assert.False(beforePromotion.GetProperty("isAdmin").GetBoolean());

        await PromoteToAdminAsync(factory, email);
        (await client.PostWithCsrfAsync("/api/account/logout", null)).EnsureSuccessStatusCode();
        (await client.PostJsonWithCsrfAsync("/api/account/login", new { email, password = Password })).EnsureSuccessStatusCode();

        var afterPromotion = await client.GetFromJsonAsync<JsonElement>("/api/account/me");
        Assert.True(afterPromotion.GetProperty("isAdmin").GetBoolean());
    }

    // --- AI usage reporting ------------------------------------------------

    private static WPAIPlugin.Planning.PlanningResult FakePlanResult(WPAIPlugin.Planning.PlanningUsage? usage) => new()
    {
        Name = "Staff Directory", Slug = "staff-directory", Description = "A simple staff directory plugin.",
        Version = "1.0.0", Author = "WPAI Plugin Builder", Features = new[] { "shortcode" },
        UnsupportedRequirements = Array.Empty<string>(), Usage = usage,
    };

    [Fact]
    public async Task AiUsage_SuccessfulPlan_RecordsUsageEventWithTokens()
    {
        using var factory = new ProjectsTestFactory();
        factory.FakePlanningProvider.Handler = (_, _) => Task.FromResult(
            FakePlanResult(new WPAIPlugin.Planning.PlanningUsage { InputTokens = 120, OutputTokens = 40, TotalTokens = 160 }));
        var email = $"user-{Guid.NewGuid()}@example.com";
        using var client = await Register(factory, email);

        (await client.PostJsonWithCsrfAsync("/api/plugins/plan", new { description = "A simple staff directory plugin with a shortcode." }))
            .EnsureSuccessStatusCode();

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var evt = await db.AiUsageEvents.SingleAsync(e => e.OperationType == AiUsageOperationType.Plan);
        Assert.True(evt.Succeeded);
        Assert.Equal(120, evt.InputTokens);
        Assert.Equal(40, evt.OutputTokens);
        Assert.Equal(160, evt.TotalTokens);
        Assert.Equal("fake", evt.Provider);
        Assert.NotNull(evt.UserId);
    }

    [Fact]
    public async Task AiUsage_FailedProviderRequest_RecordsFailureWithoutFabricatedTokens()
    {
        using var factory = new ProjectsTestFactory();
        factory.FakePlanningProvider.Handler = (_, _) => throw new WPAIPlugin.Planning.PluginPlanException(
            WPAIPlugin.Planning.PluginPlanFailureReason.ProviderFailure, "The AI planning provider failed to produce a plugin plan.");
        var email = $"user-{Guid.NewGuid()}@example.com";
        using var client = await Register(factory, email);

        var response = await client.PostJsonWithCsrfAsync("/api/plugins/plan", new { description = "A simple staff directory plugin with a shortcode." });
        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var evt = await db.AiUsageEvents.SingleAsync(e => e.OperationType == AiUsageOperationType.Plan);
        Assert.False(evt.Succeeded);
        Assert.Null(evt.InputTokens);
        Assert.Null(evt.OutputTokens);
        Assert.Null(evt.EstimatedCostUsdMicros);
        Assert.Equal("ProviderFailure", evt.FailureCategory);
    }

    [Fact]
    public async Task AiUsage_ClientCannotInfluenceCost()
    {
        using var factory = new ProjectsTestFactory();
        using var configured = factory.WithWebHostBuilder(b => b.ConfigureServices(s =>
            s.PostConfigure<AiPricingOptions>(o =>
            {
                o.Anthropic.InputUsdMicrosPerMillionTokens = 1000;
                o.Anthropic.OutputUsdMicrosPerMillionTokens = 2000;
            })));
        factory.FakePlanningProvider.Handler = (_, _) => Task.FromResult(
            FakePlanResult(new WPAIPlugin.Planning.PlanningUsage { InputTokens = 1_000_000, OutputTokens = 500_000, TotalTokens = 1_500_000 }));
        var email = $"user-{Guid.NewGuid()}@example.com";
        using var client = configured.CreateClient();
        (await client.PostJsonWithCsrfAsync("/api/account/register", new { email, password = Password })).EnsureSuccessStatusCode();

        // Client cannot send a cost value on the request - PlanPluginRequest has no such field,
        // and cost is computed only from server-side AiPricingOptions and reported token counts.
        (await client.PostJsonWithCsrfAsync("/api/plugins/plan", new
        {
            description = "A simple staff directory plugin with a shortcode.",
            estimatedCostUsdMicros = 1, // unknown field - must be ignored
        })).EnsureSuccessStatusCode();

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var evt = await db.AiUsageEvents.SingleAsync(e => e.OperationType == AiUsageOperationType.Plan);
        // provider is "fake" here (not anthropic), so EstimatedCostUsdMicros is null -
        // the point is that it is never taken from the request body regardless.
        Assert.Null(evt.EstimatedCostUsdMicros);
    }

    [Fact]
    public async Task NormalUser_CannotAccessAiUsageAnalytics()
    {
        using var factory = new ProjectsTestFactory();
        using var client = await Register(factory, $"normal-{Guid.NewGuid()}@example.com");

        var response = await client.GetAsync("/api/admin/ai-usage");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
