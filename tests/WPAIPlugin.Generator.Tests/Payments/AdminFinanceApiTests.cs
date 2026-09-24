using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WPAIPlugin.Api.Data;
using WPAIPlugin.Api.Payments;
using WPAIPlugin.Api.Security;
using WPAIPlugin.Generator.Tests.Projects;
using Xunit;

namespace WPAIPlugin.Generator.Tests.Payments;

/// <summary>
/// Admin revenue/financial reporting (Milestone 16). Reuses
/// ProjectsTestFactory's FakePaymentGateway - no live Stripe required.
/// </summary>
public class AdminFinanceApiTests
{
    private const string Password = "Str0ng!Passw0rd";

    private static async Task<HttpClient> Register(ProjectsTestFactory factory, string email)
    {
        var client = factory.CreateClient();
        (await client.PostJsonWithCsrfAsync("/api/account/register", new { email, password = Password })).EnsureSuccessStatusCode();
        return client;
    }

    private static async Task PromoteToAdminAsync(ProjectsTestFactory factory, HttpClient client, string email)
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
        (await client.PostWithCsrfAsync("/api/account/logout", null)).EnsureSuccessStatusCode();
        (await client.PostJsonWithCsrfAsync("/api/account/login", new { email, password = Password })).EnsureSuccessStatusCode();
    }

    private static async Task CompletePurchaseAsync(ProjectsTestFactory factory, HttpClient client, string packId)
    {
        (await client.PostJsonWithCsrfAsync("/api/payments/checkout", new { packId })).EnsureSuccessStatusCode();
        var sessionId = factory.FakePaymentGateway.LastCreatedSessionId!;
        factory.FakePaymentGateway.NextEvent = _ => new PaymentWebhookEvent
        {
            EventId = Guid.NewGuid().ToString(), Type = PaymentWebhookEventType.CheckoutSessionCompleted,
            CheckoutSessionId = sessionId, PaymentIntentId = "pi_" + Guid.NewGuid(), PaymentSucceeded = true,
        };
        var webhookClient = factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/payments/webhook") { Content = new StringContent("{}") };
        request.Headers.Add("Stripe-Signature", factory.FakePaymentGateway.ExpectedSignature);
        (await webhookClient.SendAsync(request)).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task NormalUser_ForbiddenFromRevenue()
    {
        using var factory = new ProjectsTestFactory();
        using var client = await Register(factory, $"normal-{Guid.NewGuid()}@example.com");

        var response = await client.GetAsync("/api/admin/finance/revenue");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task AnonymousUser_ForbiddenFromRevenue()
    {
        using var factory = new ProjectsTestFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/admin/finance/revenue");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Revenue_AggregatesCorrectly_NoFloatingPointErrors()
    {
        using var factory = new ProjectsTestFactory();
        var email = $"admin-{Guid.NewGuid()}@example.com";
        using var admin = await Register(factory, email);
        await PromoteToAdminAsync(factory, admin, email);

        using var buyer1 = await Register(factory, $"buyer1-{Guid.NewGuid()}@example.com");
        using var buyer2 = await Register(factory, $"buyer2-{Guid.NewGuid()}@example.com");
        await CompletePurchaseAsync(factory, buyer1, "starter"); // 499
        await CompletePurchaseAsync(factory, buyer2, "builder"); // 999
        await CompletePurchaseAsync(factory, buyer2, "pro"); // 1999

        var revenue = await admin.GetFromJsonAsync<JsonElement>("/api/admin/finance/revenue");

        Assert.Equal(499 + 999 + 1999, revenue.GetProperty("grossRevenueMinor").GetInt32());
        Assert.Equal(0, revenue.GetProperty("refundedMinor").GetInt32());
        Assert.Equal(499 + 999 + 1999, revenue.GetProperty("netRevenueMinor").GetInt32());
        Assert.Equal("GBP", revenue.GetProperty("currency").GetString());
        Assert.Equal(3, revenue.GetProperty("successfulPurchases").GetInt32());
        Assert.Equal(2, revenue.GetProperty("purchasingCustomers").GetInt32());
        Assert.Equal(25 + 75 + 200, revenue.GetProperty("creditsSold").GetInt32());
        // Average must be an exact rational value derived from integers, never
        // a float/double artifact like 1165.6666666666667 rounding wrongly.
        var average = revenue.GetProperty("averagePurchaseMinor").GetDecimal();
        Assert.Equal((499m + 999m + 1999m) / 3m, average);
    }

    [Fact]
    public async Task Revenue_RefundCalculation_NeverAutomaticallyNegative()
    {
        using var factory = new ProjectsTestFactory();
        var email = $"admin-{Guid.NewGuid()}@example.com";
        using var admin = await Register(factory, email);
        await PromoteToAdminAsync(factory, admin, email);

        using var buyer = await Register(factory, $"buyer-{Guid.NewGuid()}@example.com");
        await CompletePurchaseAsync(factory, buyer, "starter");

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var purchase = await db.Purchases.SingleAsync();
        purchase.RefundedAmountMinor = 499; // simulates an admin recording a manual Stripe refund
        await db.SaveChangesAsync();

        var revenue = await admin.GetFromJsonAsync<JsonElement>("/api/admin/finance/revenue");
        Assert.Equal(499, revenue.GetProperty("grossRevenueMinor").GetInt32());
        Assert.Equal(499, revenue.GetProperty("refundedMinor").GetInt32());
        Assert.Equal(0, revenue.GetProperty("netRevenueMinor").GetInt32());

        // The refund must not have touched the customer's credit balance.
        var creditsResponse = await buyer.GetFromJsonAsync<JsonElement>("/api/credits");
        Assert.True(creditsResponse.GetProperty("balance").GetInt32() >= 25); // signup grant + 25 purchased, never clawed back
    }

    [Fact]
    public async Task Contribution_CombinesRevenueAndAiCost_NeverCalledProfit()
    {
        using var factory = new ProjectsTestFactory();
        var email = $"admin-{Guid.NewGuid()}@example.com";
        using var admin = await Register(factory, email);
        await PromoteToAdminAsync(factory, admin, email);

        using var buyer = await Register(factory, $"buyer-{Guid.NewGuid()}@example.com");
        await CompletePurchaseAsync(factory, buyer, "starter");
        (await buyer.PostJsonWithCsrfAsync("/api/plugins/plan", new { description = "A simple staff directory plugin with a shortcode." }))
            .EnsureSuccessStatusCode();

        var response = await admin.GetFromJsonAsync<JsonElement>("/api/admin/finance/contribution");

        Assert.Equal(499, response.GetProperty("revenueMinor").GetInt32());
        // The response must not expose a field presented as "profit" - the
        // disclaimer note is allowed to use the word to explain why it isn't one.
        Assert.All(response.EnumerateObject(), p => Assert.DoesNotContain("profit", p.Name, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Operational_SupportsDateRangeFiltering()
    {
        using var factory = new ProjectsTestFactory();
        var email = $"admin-{Guid.NewGuid()}@example.com";
        using var admin = await Register(factory, email);
        await PromoteToAdminAsync(factory, admin, email);

        using var buyer = await Register(factory, $"buyer-{Guid.NewGuid()}@example.com");
        await CompletePurchaseAsync(factory, buyer, "starter");

        var future = DateTime.UtcNow.AddDays(1).ToString("O");
        var farFuture = DateTime.UtcNow.AddDays(2).ToString("O");
        var excluded = await admin.GetFromJsonAsync<JsonElement>($"/api/admin/finance/operational?from={future}&to={farFuture}");
        Assert.Equal(0, excluded.GetProperty("purchases").GetInt32());

        var included = await admin.GetFromJsonAsync<JsonElement>("/api/admin/finance/operational");
        Assert.Equal(1, included.GetProperty("purchases").GetInt32());
        Assert.Equal(499, included.GetProperty("revenueMinor").GetInt32());
    }

    [Fact]
    public async Task UserDetail_IncludesLifetimeEconomics()
    {
        using var factory = new ProjectsTestFactory();
        var adminEmail = $"admin-{Guid.NewGuid()}@example.com";
        using var admin = await Register(factory, adminEmail);
        await PromoteToAdminAsync(factory, admin, adminEmail);

        var buyerEmail = $"buyer-{Guid.NewGuid()}@example.com";
        using var buyer = await Register(factory, buyerEmail);
        await CompletePurchaseAsync(factory, buyer, "builder");

        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
        var buyerId = (await userManager.FindByEmailAsync(buyerEmail))!.Id;

        var detail = await admin.GetFromJsonAsync<JsonElement>($"/api/admin/users/{buyerId}");

        Assert.Equal(1, detail.GetProperty("lifetimePurchases").GetInt32());
        Assert.Equal(999, detail.GetProperty("lifetimeRevenueMinor").GetInt32());
        Assert.Equal(75, detail.GetProperty("creditsPurchased").GetInt32());
        Assert.Equal("GBP", detail.GetProperty("purchaseCurrency").GetString());
    }
}
