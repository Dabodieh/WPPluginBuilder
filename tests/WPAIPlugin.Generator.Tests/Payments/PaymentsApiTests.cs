using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WPAIPlugin.Api.Data;
using WPAIPlugin.Api.Payments;
using WPAIPlugin.Generator.Tests.Projects;
using Xunit;

namespace WPAIPlugin.Generator.Tests.Payments;

/// <summary>
/// Stripe credit purchase checkout/webhook/accounting (Milestone 16). Reuses
/// ProjectsTestFactory's FakePaymentGateway - no live Stripe, network,
/// PostgreSQL, or Docker required.
/// </summary>
public class PaymentsApiTests
{
    private const string Password = "Str0ng!Passw0rd";

    private static async Task<HttpClient> Register(ProjectsTestFactory factory, string email)
    {
        var client = factory.CreateClient();
        (await client.PostJsonWithCsrfAsync("/api/account/register", new { email, password = Password })).EnsureSuccessStatusCode();
        return client;
    }

    private static async Task<string> GetUserIdAsync(ProjectsTestFactory factory, string email)
    {
        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<Microsoft.AspNetCore.Identity.IdentityUser>>();
        var user = await userManager.FindByEmailAsync(email);
        return user!.Id;
    }

    // --- Checkout ---------------------------------------------------------

    [Fact]
    public async Task Checkout_RequiresAuthentication()
    {
        using var factory = new ProjectsTestFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/payments/checkout", new { packId = "starter" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Checkout_ExceedingPerMinuteLimit_Returns429()
    {
        using var factory = new ProjectsTestFactory();
        using var client = await Register(factory, $"buyer-{Guid.NewGuid()}@example.com");

        for (var i = 0; i < 6; i++)
        {
            (await client.PostJsonWithCsrfAsync("/api/payments/checkout", new { packId = "starter" })).EnsureSuccessStatusCode();
        }
        var rejected = await client.PostJsonWithCsrfAsync("/api/payments/checkout", new { packId = "starter" });

        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        Assert.NotNull(rejected.Headers.RetryAfter);
    }

    [Fact]
    public async Task Checkout_ServerDeterminesPriceFromPackId_NotFromBrowser()
    {
        using var factory = new ProjectsTestFactory();
        using var client = await Register(factory, $"buyer-{Guid.NewGuid()}@example.com");

        (await client.PostJsonWithCsrfAsync("/api/payments/checkout", new { packId = "starter" })).EnsureSuccessStatusCode();

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var purchase = await db.Purchases.SingleAsync();
        Assert.Equal(499, purchase.AmountMinor);
        Assert.Equal("GBP", purchase.Currency);
        Assert.Equal(25, purchase.CreditsPurchased);
        Assert.Equal(PurchaseStatus.Pending, purchase.Status);
    }

    [Fact]
    public async Task Checkout_FakeBrowserPrice_Ignored()
    {
        using var factory = new ProjectsTestFactory();
        using var client = await Register(factory, $"buyer-{Guid.NewGuid()}@example.com");

        // amountMinor/price/credits are not fields PaymentsController.Checkout
        // binds from - an attacker-supplied value here must have zero effect.
        (await client.PostJsonWithCsrfAsync("/api/payments/checkout", new
        {
            packId = "starter", amountMinor = 1, price = 1, credits = 999999,
        })).EnsureSuccessStatusCode();

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var purchase = await db.Purchases.SingleAsync();
        Assert.Equal(499, purchase.AmountMinor);
        Assert.Equal(25, purchase.CreditsPurchased);
    }

    [Fact]
    public async Task Checkout_UnknownPackId_Rejected()
    {
        using var factory = new ProjectsTestFactory();
        using var client = await Register(factory, $"buyer-{Guid.NewGuid()}@example.com");

        var response = await client.PostJsonWithCsrfAsync("/api/payments/checkout", new { packId = "does-not-exist" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Empty(db.Purchases);
    }

    [Fact]
    public async Task Checkout_PurchaseOwnedByCorrectUser()
    {
        using var factory = new ProjectsTestFactory();
        var email = $"buyer-{Guid.NewGuid()}@example.com";
        using var client = await Register(factory, email);
        var userId = await GetUserIdAsync(factory, email);

        (await client.PostJsonWithCsrfAsync("/api/payments/checkout", new { packId = "builder" })).EnsureSuccessStatusCode();

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var purchase = await db.Purchases.SingleAsync();
        Assert.Equal(userId, purchase.UserId);
    }

    // --- Webhook ------------------------------------------------------------

    [Fact]
    public async Task Webhook_MissingSignature_Rejected()
    {
        using var factory = new ProjectsTestFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsync("/api/payments/webhook", new StringContent("{}"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Webhook_WrongSignature_Rejected()
    {
        using var factory = new ProjectsTestFactory();
        using var client = factory.CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/payments/webhook") { Content = new StringContent("{}") };
        request.Headers.Add("Stripe-Signature", "wrong");
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Webhook_MalformedEvent_RejectedSafely()
    {
        using var factory = new ProjectsTestFactory();
        using var client = factory.CreateClient();
        factory.FakePaymentGateway.NextEvent = null; // simulates a parse failure

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/payments/webhook") { Content = new StringContent("not json") };
        request.Headers.Add("Stripe-Signature", factory.FakePaymentGateway.ExpectedSignature);
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Webhook_SuccessfulEvent_GrantsCreditsOnce()
    {
        using var factory = new ProjectsTestFactory();
        var email = $"buyer-{Guid.NewGuid()}@example.com";
        using var client = await Register(factory, email);
        var userId = await GetUserIdAsync(factory, email);
        var startingBalance = await GetBalanceAsync(client);

        (await client.PostJsonWithCsrfAsync("/api/payments/checkout", new { packId = "starter" })).EnsureSuccessStatusCode();
        var sessionId = factory.FakePaymentGateway.LastCreatedSessionId!;

        factory.FakePaymentGateway.NextEvent = _ => new PaymentWebhookEvent
        {
            EventId = "evt_1", Type = PaymentWebhookEventType.CheckoutSessionCompleted,
            CheckoutSessionId = sessionId, PaymentIntentId = "pi_1", PaymentSucceeded = true,
        };
        var webhookResponse = await PostWebhookAsync(factory, "{}");

        Assert.Equal(HttpStatusCode.OK, webhookResponse.StatusCode);
        Assert.Equal(startingBalance + 25, await GetBalanceAsync(client));

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var purchase = await db.Purchases.SingleAsync();
        Assert.Equal(PurchaseStatus.Completed, purchase.Status);
        Assert.Equal("pi_1", purchase.ProviderPaymentIntentId);
        Assert.NotNull(purchase.CompletedAtUtc);
        var ledgerEntry = await db.CreditTransactions.SingleAsync(t => t.Type == CreditTransactionType.CreditPurchase);
        Assert.Equal(25, ledgerEntry.Amount);
        Assert.Equal(userId, ledgerEntry.UserId);
    }

    [Fact]
    public async Task Webhook_DuplicateEvent_GrantsOnce()
    {
        using var factory = new ProjectsTestFactory();
        using var client = await Register(factory, $"buyer-{Guid.NewGuid()}@example.com");
        var startingBalance = await GetBalanceAsync(client);

        (await client.PostJsonWithCsrfAsync("/api/payments/checkout", new { packId = "starter" })).EnsureSuccessStatusCode();
        var sessionId = factory.FakePaymentGateway.LastCreatedSessionId!;
        factory.FakePaymentGateway.NextEvent = _ => new PaymentWebhookEvent
        {
            EventId = "evt_dup", Type = PaymentWebhookEventType.CheckoutSessionCompleted,
            CheckoutSessionId = sessionId, PaymentIntentId = "pi_1", PaymentSucceeded = true,
        };

        (await PostWebhookAsync(factory, "{}")).EnsureSuccessStatusCode();
        (await PostWebhookAsync(factory, "{}")).EnsureSuccessStatusCode();

        Assert.Equal(startingBalance + 25, await GetBalanceAsync(client));
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(1, await db.CreditTransactions.CountAsync(t => t.Type == CreditTransactionType.CreditPurchase));
    }

    [Fact]
    public async Task Webhook_DifferentEventIdSameSession_StillGrantsOnce()
    {
        // Defends against a duplicate-but-differently-IDed retry scenario at
        // the Purchase-status level, not just the event-ID level.
        using var factory = new ProjectsTestFactory();
        using var client = await Register(factory, $"buyer-{Guid.NewGuid()}@example.com");
        var startingBalance = await GetBalanceAsync(client);

        (await client.PostJsonWithCsrfAsync("/api/payments/checkout", new { packId = "starter" })).EnsureSuccessStatusCode();
        var sessionId = factory.FakePaymentGateway.LastCreatedSessionId!;

        factory.FakePaymentGateway.NextEvent = _ => new PaymentWebhookEvent
        {
            EventId = "evt_a", Type = PaymentWebhookEventType.CheckoutSessionCompleted,
            CheckoutSessionId = sessionId, PaymentIntentId = "pi_1", PaymentSucceeded = true,
        };
        (await PostWebhookAsync(factory, "{}")).EnsureSuccessStatusCode();

        factory.FakePaymentGateway.NextEvent = _ => new PaymentWebhookEvent
        {
            EventId = "evt_b", Type = PaymentWebhookEventType.CheckoutSessionCompleted,
            CheckoutSessionId = sessionId, PaymentIntentId = "pi_1", PaymentSucceeded = true,
        };
        (await PostWebhookAsync(factory, "{}")).EnsureSuccessStatusCode();

        Assert.Equal(startingBalance + 25, await GetBalanceAsync(client));
    }

    [Fact]
    public async Task Webhook_FailedPayment_GrantsZeroCredits()
    {
        using var factory = new ProjectsTestFactory();
        using var client = await Register(factory, $"buyer-{Guid.NewGuid()}@example.com");
        var startingBalance = await GetBalanceAsync(client);

        (await client.PostJsonWithCsrfAsync("/api/payments/checkout", new { packId = "starter" })).EnsureSuccessStatusCode();
        var sessionId = factory.FakePaymentGateway.LastCreatedSessionId!;
        factory.FakePaymentGateway.NextEvent = _ => new PaymentWebhookEvent
        {
            EventId = "evt_fail", Type = PaymentWebhookEventType.PaymentIntentPaymentFailed,
            PaymentIntentId = "pi_1", PaymentSucceeded = false,
        };

        (await PostWebhookAsync(factory, "{}")).EnsureSuccessStatusCode();

        Assert.Equal(startingBalance, await GetBalanceAsync(client));
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(PurchaseStatus.Pending, (await db.Purchases.SingleAsync()).Status);
    }

    [Fact]
    public async Task Webhook_CancelledCheckout_GrantsZeroCredits()
    {
        using var factory = new ProjectsTestFactory();
        using var client = await Register(factory, $"buyer-{Guid.NewGuid()}@example.com");
        var startingBalance = await GetBalanceAsync(client);

        (await client.PostJsonWithCsrfAsync("/api/payments/checkout", new { packId = "starter" })).EnsureSuccessStatusCode();
        var sessionId = factory.FakePaymentGateway.LastCreatedSessionId!;
        factory.FakePaymentGateway.NextEvent = _ => new PaymentWebhookEvent
        {
            EventId = "evt_cancel", Type = PaymentWebhookEventType.CheckoutSessionExpired,
            CheckoutSessionId = sessionId, PaymentSucceeded = false,
        };

        (await PostWebhookAsync(factory, "{}")).EnsureSuccessStatusCode();

        Assert.Equal(startingBalance, await GetBalanceAsync(client));
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(PurchaseStatus.Cancelled, (await db.Purchases.SingleAsync()).Status);
    }

    [Fact]
    public async Task Webhook_SecretNeverAppearsInResponse()
    {
        using var factory = new ProjectsTestFactory();
        using var client = factory.CreateClient();

        var response = await PostWebhookAsync(factory, "{}", validSignature: false);
        var body = await response.Content.ReadAsStringAsync();

        Assert.DoesNotContain("sk_test_fake", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("whsec", body, StringComparison.OrdinalIgnoreCase);
    }

    // --- Accounting -----------------------------------------------------

    [Fact]
    public async Task Accounting_MoneyStoredInMinorUnits()
    {
        using var factory = new ProjectsTestFactory();
        using var client = await Register(factory, $"buyer-{Guid.NewGuid()}@example.com");

        (await client.PostJsonWithCsrfAsync("/api/payments/checkout", new { packId = "pro" })).EnsureSuccessStatusCode();

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var purchase = await db.Purchases.SingleAsync();
        Assert.Equal(1999, purchase.AmountMinor); // £19.99 as an integer, never 19.99m/19.99d
    }

    [Fact]
    public async Task Accounting_ConcurrentIdenticalWebhookDeliveries_GrantOnce()
    {
        using var factory = new ProjectsTestFactory();
        using var client = await Register(factory, $"buyer-{Guid.NewGuid()}@example.com");
        var startingBalance = await GetBalanceAsync(client);

        (await client.PostJsonWithCsrfAsync("/api/payments/checkout", new { packId = "starter" })).EnsureSuccessStatusCode();
        var sessionId = factory.FakePaymentGateway.LastCreatedSessionId!;
        factory.FakePaymentGateway.NextEvent = _ => new PaymentWebhookEvent
        {
            EventId = "evt_concurrent", Type = PaymentWebhookEventType.CheckoutSessionCompleted,
            CheckoutSessionId = sessionId, PaymentIntentId = "pi_1", PaymentSucceeded = true,
        };

        var responses = await Task.WhenAll(PostWebhookAsync(factory, "{}"), PostWebhookAsync(factory, "{}"));

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        Assert.Equal(startingBalance + 25, await GetBalanceAsync(client));
    }

    // --- Customer purchase history -------------------------------------

    [Fact]
    public async Task History_OnlyShowsOwnPurchases()
    {
        using var factory = new ProjectsTestFactory();
        using var buyerOne = await Register(factory, $"buyer1-{Guid.NewGuid()}@example.com");
        using var buyerTwo = await Register(factory, $"buyer2-{Guid.NewGuid()}@example.com");

        (await buyerOne.PostJsonWithCsrfAsync("/api/payments/checkout", new { packId = "starter" })).EnsureSuccessStatusCode();
        (await buyerTwo.PostJsonWithCsrfAsync("/api/payments/checkout", new { packId = "builder" })).EnsureSuccessStatusCode();

        var historyOne = await buyerOne.GetFromJsonAsync<JsonElement[]>("/api/payments/history");
        Assert.Single(historyOne!);
        Assert.Equal("starter", historyOne![0].GetProperty("packId").GetString());
    }

    [Fact]
    public async Task History_RequiresAuthentication()
    {
        using var factory = new ProjectsTestFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/payments/history");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static async Task<int> GetBalanceAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<JsonElement>("/api/credits")).GetProperty("balance").GetInt32();

    private static Task<HttpResponseMessage> PostWebhookAsync(ProjectsTestFactory factory, string payload, bool validSignature = true)
    {
        var client = factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/payments/webhook") { Content = new StringContent(payload) };
        request.Headers.Add("Stripe-Signature", validSignature ? factory.FakePaymentGateway.ExpectedSignature : "wrong");
        return client.SendAsync(request);
    }
}
