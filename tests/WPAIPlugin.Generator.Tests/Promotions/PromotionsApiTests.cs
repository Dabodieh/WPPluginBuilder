using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WPAIPlugin.Api.Data;
using WPAIPlugin.Api.Payments;
using WPAIPlugin.Generator.Tests.Projects;
using Xunit;

namespace WPAIPlugin.Generator.Tests.Promotions;

/// <summary>
/// Promotion resolution, checkout integration, and redemption accounting
/// (Promotions + Free Builds milestone). Reuses ProjectsTestFactory's
/// FakePaymentGateway - no live Stripe, network, PostgreSQL, or Docker
/// required. Promotions are seeded directly against the DbContext (the
/// simplest way to set up a known state); AdminPromotionsApiTests covers the
/// admin HTTP surface that creates them in production.
/// </summary>
public class PromotionsApiTests
{
    private const string Password = "Str0ng!Passw0rd";

    private static async Task<HttpClient> Register(ProjectsTestFactory factory, string email)
    {
        var client = factory.CreateClient();
        (await client.PostJsonWithCsrfAsync("/api/account/register", new { email, password = Password })).EnsureSuccessStatusCode();
        return client;
    }

    private static async Task SeedAsync(ProjectsTestFactory factory, Action<AppDbContext> seed)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        seed(db);
        await db.SaveChangesAsync();
    }

    private static Promotion PackDiscount(string code, int value, string? packId = null, bool requiresCode = true,
        DateTime? starts = null, DateTime? ends = null, bool enabled = true, int priority = 0,
        int? maxRedemptions = null, int? maxPerUser = null, string eligibility = PromotionEligibility.Everyone) => new()
    {
        Id = Guid.NewGuid(), Name = code, Code = code, Type = PromotionType.PackPriceDiscount,
        Value = value, AppliesToPackId = packId, RequiresCode = requiresCode,
        StartsAtUtc = starts ?? DateTime.UtcNow.AddDays(-1), EndsAtUtc = ends, IsEnabled = enabled, Priority = priority,
        MaxRedemptions = maxRedemptions, MaxRedemptionsPerUser = maxPerUser, Eligibility = eligibility,
        CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow,
    };

    private static Promotion BonusCredits(string? code, int value, bool requiresCode, string? packId = null,
        int priority = 0, string eligibility = PromotionEligibility.Everyone) => new()
    {
        Id = Guid.NewGuid(), Name = code ?? "Automatic bonus", Code = code, Type = PromotionType.BonusCredits,
        Value = value, AppliesToPackId = packId, RequiresCode = requiresCode,
        StartsAtUtc = DateTime.UtcNow.AddDays(-1), EndsAtUtc = null, IsEnabled = true, Priority = priority,
        Eligibility = eligibility, CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow,
    };

    // --- Scheduling / state ------------------------------------------------

    [Fact]
    public async Task ScheduledPromotion_NotActiveBeforeStart_CodeRejected()
    {
        using var factory = new ProjectsTestFactory();
        var promotion = PackDiscount("FUTURE25", 25, starts: DateTime.UtcNow.AddDays(1));
        await SeedAsync(factory, db => db.Promotions.Add(promotion));
        using var client = await Register(factory, $"buyer-{Guid.NewGuid()}@example.com");

        var response = await client.PostJsonWithCsrfAsync("/api/payments/checkout", new { packId = "starter", promoCode = "FUTURE25" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ExpiredPromotion_CodeRejected()
    {
        using var factory = new ProjectsTestFactory();
        var promotion = PackDiscount("EXPIRED25", 25, starts: DateTime.UtcNow.AddDays(-10), ends: DateTime.UtcNow.AddDays(-1));
        await SeedAsync(factory, db => db.Promotions.Add(promotion));
        using var client = await Register(factory, $"buyer-{Guid.NewGuid()}@example.com");

        var response = await client.PostJsonWithCsrfAsync("/api/payments/checkout", new { packId = "starter", promoCode = "EXPIRED25" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task DisabledPromotion_Ignored()
    {
        using var factory = new ProjectsTestFactory();
        var promotion = PackDiscount("DISABLED25", 25, enabled: false);
        await SeedAsync(factory, db => db.Promotions.Add(promotion));
        using var client = await Register(factory, $"buyer-{Guid.NewGuid()}@example.com");

        var response = await client.PostJsonWithCsrfAsync("/api/payments/checkout", new { packId = "starter", promoCode = "DISABLED25" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task InvalidCode_RejectedSafely_NoInternalDetailLeaked()
    {
        using var factory = new ProjectsTestFactory();
        using var client = await Register(factory, $"buyer-{Guid.NewGuid()}@example.com");

        var response = await client.PostJsonWithCsrfAsync("/api/payments/checkout", new { packId = "starter", promoCode = "NOT-A-REAL-CODE" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal("{\"error\":\"Invalid or expired code.\"}", body);
    }

    // --- Valid code / discount math -----------------------------------------

    [Fact]
    public async Task ValidCode_AppliesServerCalculatedDiscount_MatchingWorkedExample()
    {
        using var factory = new ProjectsTestFactory();
        var promotion = PackDiscount("BLACKFRIDAY", 25);
        await SeedAsync(factory, db => db.Promotions.Add(promotion));
        using var client = await Register(factory, $"buyer-{Guid.NewGuid()}@example.com");

        (await client.PostJsonWithCsrfAsync("/api/payments/checkout", new { packId = "builder", promoCode = "blackfriday" }))
            .EnsureSuccessStatusCode(); // lowercase input - code comparison is normalized

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var purchase = await db.Purchases.SingleAsync();
        Assert.Equal(999, purchase.BaseAmountMinor);
        Assert.Equal(749, purchase.AmountMinor); // 999 * 25% = 249.75 -> rounds to 250 -> 999-250=749 (£7.49)
        Assert.Equal(promotion.Id, purchase.PromotionId);
        Assert.Equal("BLACKFRIDAY", purchase.PromotionCodeSnapshot);
    }

    [Fact]
    public async Task ForgedBrowserDiscountOrBonus_HasNoEffect()
    {
        using var factory = new ProjectsTestFactory();
        using var client = await Register(factory, $"buyer-{Guid.NewGuid()}@example.com");

        (await client.PostJsonWithCsrfAsync("/api/payments/checkout", new
        {
            packId = "starter", discountedAmountMinor = 1, bonusCredits = 999999, amountMinor = 1,
        })).EnsureSuccessStatusCode();

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var purchase = await db.Purchases.SingleAsync();
        Assert.Equal(499, purchase.AmountMinor);
        Assert.Equal(0, purchase.BonusCredits);
    }

    // --- Eligibility / limits ------------------------------------------------

    [Fact]
    public async Task PackScopedPromotion_RejectedForDifferentPack()
    {
        using var factory = new ProjectsTestFactory();
        var promotion = PackDiscount("BUILDERONLY", 10, packId: "builder");
        await SeedAsync(factory, db => db.Promotions.Add(promotion));
        using var client = await Register(factory, $"buyer-{Guid.NewGuid()}@example.com");

        var wrongPack = await client.PostJsonWithCsrfAsync("/api/payments/checkout", new { packId = "starter", promoCode = "BUILDERONLY" });
        Assert.Equal(HttpStatusCode.BadRequest, wrongPack.StatusCode);

        var rightPack = await client.PostJsonWithCsrfAsync("/api/payments/checkout", new { packId = "builder", promoCode = "BUILDERONLY" });
        rightPack.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task FirstPurchaseOnlyPromotion_RejectedAfterAPriorCompletedPurchase()
    {
        using var factory = new ProjectsTestFactory();
        var promotion = PackDiscount("FIRSTBUY", 10, eligibility: PromotionEligibility.FirstPurchaseOnly);
        await SeedAsync(factory, db => db.Promotions.Add(promotion));
        using var client = await Register(factory, $"buyer-{Guid.NewGuid()}@example.com");

        // Complete an unrelated purchase first.
        (await client.PostJsonWithCsrfAsync("/api/payments/checkout", new { packId = "starter" })).EnsureSuccessStatusCode();
        var sessionId = factory.FakePaymentGateway.LastCreatedSessionId!;
        factory.FakePaymentGateway.NextEvent = _ => new PaymentWebhookEvent
        {
            EventId = "evt_first", Type = PaymentWebhookEventType.CheckoutSessionCompleted,
            CheckoutSessionId = sessionId, PaymentIntentId = "pi_1", PaymentSucceeded = true,
        };
        var webhookClient = factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/payments/webhook") { Content = new StringContent("{}") };
        request.Headers.Add("Stripe-Signature", factory.FakePaymentGateway.ExpectedSignature);
        (await webhookClient.SendAsync(request)).EnsureSuccessStatusCode();

        var secondAttempt = await client.PostJsonWithCsrfAsync("/api/payments/checkout", new { packId = "builder", promoCode = "FIRSTBUY" });
        Assert.Equal(HttpStatusCode.BadRequest, secondAttempt.StatusCode);
    }

    [Fact]
    public async Task MaxRedemptionsPerUser_EnforcedAtCheckoutTime()
    {
        using var factory = new ProjectsTestFactory();
        var promotion = PackDiscount("ONEUSE", 10, maxPerUser: 1);
        using var client = await Register(factory, $"buyer-{Guid.NewGuid()}@example.com");
        string userId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Promotions.Add(promotion);
            userId = (await db.Users.SingleAsync()).Id;
            db.PromotionRedemptions.Add(new PromotionRedemption
            {
                Id = Guid.NewGuid(), PromotionId = promotion.Id, UserId = userId, PurchaseId = Guid.NewGuid(),
                BenefitType = PromotionBenefitType.PriceDiscount, BenefitAmount = 50, CreatedAtUtc = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        var response = await client.PostJsonWithCsrfAsync("/api/payments/checkout", new { packId = "starter", promoCode = "ONEUSE" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task MaxRedemptionsGlobal_EnforcedAtCheckoutTime()
    {
        using var factory = new ProjectsTestFactory();
        var promotion = PackDiscount("LIMITED", 10, maxRedemptions: 1);
        using var otherClient = await Register(factory, $"other-{Guid.NewGuid()}@example.com");
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Promotions.Add(promotion);
            var otherUserId = (await db.Users.SingleAsync()).Id;
            db.PromotionRedemptions.Add(new PromotionRedemption
            {
                Id = Guid.NewGuid(), PromotionId = promotion.Id, UserId = otherUserId, PurchaseId = Guid.NewGuid(),
                BenefitType = PromotionBenefitType.PriceDiscount, BenefitAmount = 50, CreatedAtUtc = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }
        using var client = await Register(factory, $"buyer-{Guid.NewGuid()}@example.com");

        var response = await client.PostJsonWithCsrfAsync("/api/payments/checkout", new { packId = "starter", promoCode = "LIMITED" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // --- Automatic resolution / precedence / priority -----------------------

    [Fact]
    public async Task AutomaticPromotion_AppliesWithoutACode()
    {
        using var factory = new ProjectsTestFactory();
        var promotion = BonusCredits(null, 10, requiresCode: false);
        await SeedAsync(factory, db => db.Promotions.Add(promotion));
        using var client = await Register(factory, $"buyer-{Guid.NewGuid()}@example.com");

        (await client.PostJsonWithCsrfAsync("/api/payments/checkout", new { packId = "starter" })).EnsureSuccessStatusCode();

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var purchase = await db.Purchases.SingleAsync();
        Assert.Equal(10, purchase.BonusCredits);
        Assert.Equal(promotion.Id, purchase.PromotionId);
    }

    [Fact]
    public async Task ExplicitCode_WinsOverAutomaticPromotion_NoStacking()
    {
        using var factory = new ProjectsTestFactory();
        var automatic = BonusCredits(null, 10, requiresCode: false);
        var coded = PackDiscount("CODE10", 10);
        await SeedAsync(factory, db => db.Promotions.AddRange(automatic, coded));
        using var client = await Register(factory, $"buyer-{Guid.NewGuid()}@example.com");

        (await client.PostJsonWithCsrfAsync("/api/payments/checkout", new { packId = "starter", promoCode = "CODE10" })).EnsureSuccessStatusCode();

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var purchase = await db.Purchases.SingleAsync();
        // Only the code's benefit applied - never both at once.
        Assert.Equal(coded.Id, purchase.PromotionId);
        Assert.Equal(0, purchase.BonusCredits);
        Assert.True(purchase.AmountMinor < purchase.BaseAmountMinor);
    }

    [Fact]
    public async Task HigherPriorityAutomaticPromotion_WinsDeterministically()
    {
        using var factory = new ProjectsTestFactory();
        var low = BonusCredits(null, 5, requiresCode: false, priority: 1);
        var high = BonusCredits(null, 20, requiresCode: false, priority: 10);
        await SeedAsync(factory, db => db.Promotions.AddRange(low, high));
        using var client = await Register(factory, $"buyer-{Guid.NewGuid()}@example.com");

        (await client.PostJsonWithCsrfAsync("/api/payments/checkout", new { packId = "starter" })).EnsureSuccessStatusCode();

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var purchase = await db.Purchases.SingleAsync();
        Assert.Equal(high.Id, purchase.PromotionId);
        Assert.Equal(20, purchase.BonusCredits);
    }

    // --- Webhook completion: bonus credits + redemption ----------------------

    [Fact]
    public async Task BonusCreditsPromotion_GrantsBaseAndBonusSeparately_OnceEach()
    {
        using var factory = new ProjectsTestFactory();
        var promotion = BonusCredits("BONUS25", 25, requiresCode: true);
        await SeedAsync(factory, db => db.Promotions.Add(promotion));
        using var client = await Register(factory, $"buyer-{Guid.NewGuid()}@example.com");

        (await client.PostJsonWithCsrfAsync("/api/payments/checkout", new { packId = "builder", promoCode = "BONUS25" })).EnsureSuccessStatusCode();
        var sessionId = factory.FakePaymentGateway.LastCreatedSessionId!;
        factory.FakePaymentGateway.NextEvent = _ => new PaymentWebhookEvent
        {
            EventId = "evt_bonus", Type = PaymentWebhookEventType.CheckoutSessionCompleted,
            CheckoutSessionId = sessionId, PaymentIntentId = "pi_1", PaymentSucceeded = true,
        };
        var webhookClient = factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/payments/webhook") { Content = new StringContent("{}") };
        request.Headers.Add("Stripe-Signature", factory.FakePaymentGateway.ExpectedSignature);
        (await webhookClient.SendAsync(request)).EnsureSuccessStatusCode();

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var purchaseGrant = await db.CreditTransactions.SingleAsync(t => t.Type == CreditTransactionType.CreditPurchase);
        Assert.Equal(75, purchaseGrant.Amount);
        var bonusGrant = await db.CreditTransactions.SingleAsync(t => t.Type == CreditTransactionType.PromotionBonus);
        Assert.Equal(25, bonusGrant.Amount);
        var redemption = await db.PromotionRedemptions.SingleAsync();
        Assert.Equal(promotion.Id, redemption.PromotionId);
        Assert.Equal(PromotionBenefitType.BonusCredits, redemption.BenefitType);
        Assert.Equal(25, redemption.BenefitAmount);
    }

    [Fact]
    public async Task DuplicateWebhookDelivery_DoesNotDuplicateBonusOrRedemption()
    {
        using var factory = new ProjectsTestFactory();
        var promotion = BonusCredits("BONUSDUP", 25, requiresCode: true);
        await SeedAsync(factory, db => db.Promotions.Add(promotion));
        using var client = await Register(factory, $"buyer-{Guid.NewGuid()}@example.com");

        (await client.PostJsonWithCsrfAsync("/api/payments/checkout", new { packId = "builder", promoCode = "BONUSDUP" })).EnsureSuccessStatusCode();
        var sessionId = factory.FakePaymentGateway.LastCreatedSessionId!;
        factory.FakePaymentGateway.NextEvent = _ => new PaymentWebhookEvent
        {
            EventId = "evt_dup_bonus", Type = PaymentWebhookEventType.CheckoutSessionCompleted,
            CheckoutSessionId = sessionId, PaymentIntentId = "pi_1", PaymentSucceeded = true,
        };
        for (var i = 0; i < 2; i++)
        {
            var webhookClient = factory.CreateClient();
            var request = new HttpRequestMessage(HttpMethod.Post, "/api/payments/webhook") { Content = new StringContent("{}") };
            request.Headers.Add("Stripe-Signature", factory.FakePaymentGateway.ExpectedSignature);
            (await webhookClient.SendAsync(request)).EnsureSuccessStatusCode();
        }

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(1, await db.CreditTransactions.CountAsync(t => t.Type == CreditTransactionType.PromotionBonus));
        Assert.Equal(1, await db.PromotionRedemptions.CountAsync());
    }

    [Fact]
    public async Task ExpiredAfterCheckoutCreation_PaymentCompletes_SnapshotHonoured_RedemptionRecorded()
    {
        using var factory = new ProjectsTestFactory();
        var promotion = PackDiscount("SOONEXPIRE", 25);
        await SeedAsync(factory, db => db.Promotions.Add(promotion));
        using var client = await Register(factory, $"buyer-{Guid.NewGuid()}@example.com");

        (await client.PostJsonWithCsrfAsync("/api/payments/checkout", new { packId = "builder", promoCode = "SOONEXPIRE" })).EnsureSuccessStatusCode();
        var sessionId = factory.FakePaymentGateway.LastCreatedSessionId!;

        // The promotion expires between checkout creation and payment completion.
        await SeedAsync(factory, db =>
        {
            var tracked = db.Promotions.Find(promotion.Id)!;
            tracked.EndsAtUtc = DateTime.UtcNow.AddSeconds(-1);
        });

        factory.FakePaymentGateway.NextEvent = _ => new PaymentWebhookEvent
        {
            EventId = "evt_expired_after", Type = PaymentWebhookEventType.CheckoutSessionCompleted,
            CheckoutSessionId = sessionId, PaymentIntentId = "pi_1", PaymentSucceeded = true,
        };
        var webhookClient = factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/payments/webhook") { Content = new StringContent("{}") };
        request.Headers.Add("Stripe-Signature", factory.FakePaymentGateway.ExpectedSignature);
        (await webhookClient.SendAsync(request)).EnsureSuccessStatusCode();

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var purchase = await db.Purchases.SingleAsync();
        Assert.Equal(PurchaseStatus.Completed, purchase.Status);
        Assert.Equal(749, purchase.AmountMinor); // the agreed, snapshotted price - unchanged by the later expiry
        var purchaseGrant = await db.CreditTransactions.SingleAsync(t => t.Type == CreditTransactionType.CreditPurchase);
        Assert.Equal(75, purchaseGrant.Amount);
        // The already-agreed transaction is honoured in full - a Promotion
        // expiring after Checkout creation must never un-record its redemption.
        var redemption = await db.PromotionRedemptions.SingleAsync();
        Assert.Equal(promotion.Id, redemption.PromotionId);
        Assert.Equal(purchase.Id, redemption.PurchaseId);
        Assert.Equal(PromotionBenefitType.PriceDiscount, redemption.BenefitType);
        Assert.Equal(250, redemption.BenefitAmount); // 999 - 749
    }

    [Fact]
    public async Task DisabledAfterCheckoutCreation_PaymentCompletes_RedemptionRecorded()
    {
        using var factory = new ProjectsTestFactory();
        var promotion = PackDiscount("SOONDISABLE", 25);
        await SeedAsync(factory, db => db.Promotions.Add(promotion));
        using var client = await Register(factory, $"buyer-{Guid.NewGuid()}@example.com");

        (await client.PostJsonWithCsrfAsync("/api/payments/checkout", new { packId = "builder", promoCode = "SOONDISABLE" })).EnsureSuccessStatusCode();
        var sessionId = factory.FakePaymentGateway.LastCreatedSessionId!;

        // The promotion is disabled by an admin between checkout creation and payment completion.
        await SeedAsync(factory, db => { db.Promotions.Find(promotion.Id)!.IsEnabled = false; });

        factory.FakePaymentGateway.NextEvent = _ => new PaymentWebhookEvent
        {
            EventId = "evt_disabled_after", Type = PaymentWebhookEventType.CheckoutSessionCompleted,
            CheckoutSessionId = sessionId, PaymentIntentId = "pi_1", PaymentSucceeded = true,
        };
        var webhookClient = factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/payments/webhook") { Content = new StringContent("{}") };
        request.Headers.Add("Stripe-Signature", factory.FakePaymentGateway.ExpectedSignature);
        (await webhookClient.SendAsync(request)).EnsureSuccessStatusCode();

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var purchase = await db.Purchases.SingleAsync();
        Assert.Equal(PurchaseStatus.Completed, purchase.Status);
        Assert.Equal(749, purchase.AmountMinor);
        var redemption = await db.PromotionRedemptions.SingleAsync();
        Assert.Equal(promotion.Id, redemption.PromotionId);
    }

    [Fact]
    public async Task EditedAfterCheckoutCreation_OriginalSnapshotTermsHonoured_RedemptionRecorded()
    {
        using var factory = new ProjectsTestFactory();
        var promotion = PackDiscount("SOONEDIT", 25);
        await SeedAsync(factory, db => db.Promotions.Add(promotion));
        using var client = await Register(factory, $"buyer-{Guid.NewGuid()}@example.com");

        (await client.PostJsonWithCsrfAsync("/api/payments/checkout", new { packId = "builder", promoCode = "SOONEDIT" })).EnsureSuccessStatusCode();
        var sessionId = factory.FakePaymentGateway.LastCreatedSessionId!;

        // An admin edits the promotion's discount value between checkout creation and payment completion.
        await SeedAsync(factory, db => { db.Promotions.Find(promotion.Id)!.Value = 50; });

        factory.FakePaymentGateway.NextEvent = _ => new PaymentWebhookEvent
        {
            EventId = "evt_edited_after", Type = PaymentWebhookEventType.CheckoutSessionCompleted,
            CheckoutSessionId = sessionId, PaymentIntentId = "pi_1", PaymentSucceeded = true,
        };
        var webhookClient = factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/payments/webhook") { Content = new StringContent("{}") };
        request.Headers.Add("Stripe-Signature", factory.FakePaymentGateway.ExpectedSignature);
        (await webhookClient.SendAsync(request)).EnsureSuccessStatusCode();

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var purchase = await db.Purchases.SingleAsync();
        // Original 25%-off terms (749p), never the edited 50%-off value.
        Assert.Equal(749, purchase.AmountMinor);
        var redemption = await db.PromotionRedemptions.SingleAsync();
        Assert.Equal(250, redemption.BenefitAmount); // original discount, not the edited one
    }

    [Fact]
    public async Task BonusCreditsPromotion_ExpiresAfterCheckoutCreation_BonusGrantedOnce_RedemptionRecordedOnce()
    {
        using var factory = new ProjectsTestFactory();
        var promotion = BonusCredits("SOONEXPIREBONUS", 25, requiresCode: true);
        await SeedAsync(factory, db => db.Promotions.Add(promotion));
        using var client = await Register(factory, $"buyer-{Guid.NewGuid()}@example.com");

        (await client.PostJsonWithCsrfAsync("/api/payments/checkout", new { packId = "builder", promoCode = "SOONEXPIREBONUS" })).EnsureSuccessStatusCode();
        var sessionId = factory.FakePaymentGateway.LastCreatedSessionId!;

        await SeedAsync(factory, db => { db.Promotions.Find(promotion.Id)!.EndsAtUtc = DateTime.UtcNow.AddSeconds(-1); });

        factory.FakePaymentGateway.NextEvent = _ => new PaymentWebhookEvent
        {
            EventId = "evt_bonus_expired_after", Type = PaymentWebhookEventType.CheckoutSessionCompleted,
            CheckoutSessionId = sessionId, PaymentIntentId = "pi_1", PaymentSucceeded = true,
        };
        var webhookClient = factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/payments/webhook") { Content = new StringContent("{}") };
        request.Headers.Add("Stripe-Signature", factory.FakePaymentGateway.ExpectedSignature);
        (await webhookClient.SendAsync(request)).EnsureSuccessStatusCode();

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(1, await db.CreditTransactions.CountAsync(t => t.Type == CreditTransactionType.PromotionBonus));
        var bonusGrant = await db.CreditTransactions.SingleAsync(t => t.Type == CreditTransactionType.PromotionBonus);
        Assert.Equal(25, bonusGrant.Amount);
        Assert.Equal(1, await db.PromotionRedemptions.CountAsync());
        var redemption = await db.PromotionRedemptions.SingleAsync();
        Assert.Equal(PromotionBenefitType.BonusCredits, redemption.BenefitType);
        Assert.Equal(25, redemption.BenefitAmount);
    }

    // --- FreeBuilds code redemption ------------------------------------------

    [Fact]
    public async Task FreeBuildsCode_GrantsEntitlementAndRecordsRedemption()
    {
        using var factory = new ProjectsTestFactory();
        var promotion = new Promotion
        {
            Id = Guid.NewGuid(), Name = "Holiday bonus", Code = "HOLIDAY2", Type = PromotionType.FreeBuilds,
            Value = 2, RequiresCode = true, StartsAtUtc = DateTime.UtcNow.AddDays(-1), IsEnabled = true,
            Eligibility = PromotionEligibility.Everyone, CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow,
        };
        await SeedAsync(factory, db => db.Promotions.Add(promotion));
        using var client = await Register(factory, $"buyer-{Guid.NewGuid()}@example.com");

        var response = await client.PostJsonWithCsrfAsync("/api/promotions/redeem", new { code = "holiday2" });
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(2, body.GetProperty("freeBuildsGranted").GetInt32());
        Assert.Equal(2, body.GetProperty("freeBuildsRemaining").GetInt32());

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var redemption = await db.PromotionRedemptions.SingleAsync();
        Assert.Equal(promotion.Id, redemption.PromotionId);
        Assert.Equal(PromotionBenefitType.FreeBuilds, redemption.BenefitType);
        Assert.Null(redemption.PurchaseId);
    }

    [Fact]
    public async Task FreeBuildsCode_InvalidCode_Rejected()
    {
        using var factory = new ProjectsTestFactory();
        using var client = await Register(factory, $"buyer-{Guid.NewGuid()}@example.com");

        var response = await client.PostJsonWithCsrfAsync("/api/promotions/redeem", new { code = "NOPE" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task FreeBuildsCode_RequiresAuthentication()
    {
        using var factory = new ProjectsTestFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/promotions/redeem", new { code = "ANY" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // --- FreeBuilds redemption limits (concurrency-race fix regression) ------
    //
    // These verify the *sequential* correctness of RedeemFreeBuildsCodeAsync's
    // atomic claim-then-grant flow: exactly the right number of redemptions
    // succeed, no more, no fewer. They intentionally do NOT attempt to prove
    // the concurrent-request race itself is closed - EF Core's InMemory
    // provider (used by ProjectsTestFactory) has no real transaction/row-
    // locking support, so PromotionService.RedeemFreeBuildsCodeAsync always
    // takes its non-locking fallback path here (see IsRelational() in that
    // method) and a true parallel-request proof would pass or fail on
    // InMemory scheduling accidents, not on the fix itself. The real guard -
    // PostgreSQL's SELECT ... FOR UPDATE - only exists on a relational
    // connection; verifying it requires a live PostgreSQL instance, which
    // this sandboxed environment does not have running (see the security fix
    // completion report for exact manual verification steps against
    // docker/docker-compose.saas.yml's db service).

    private static Promotion FreeBuilds(string code, int value, int? maxRedemptions = null, int? maxPerUser = null) => new()
    {
        Id = Guid.NewGuid(), Name = code, Code = code, Type = PromotionType.FreeBuilds,
        Value = value, RequiresCode = true, StartsAtUtc = DateTime.UtcNow.AddDays(-1), IsEnabled = true,
        MaxRedemptions = maxRedemptions, MaxRedemptionsPerUser = maxPerUser,
        Eligibility = PromotionEligibility.Everyone, CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow,
    };

    [Fact]
    public async Task FreeBuildsCode_SecondSequentialRedemption_RejectedAsAlreadyRedeemed()
    {
        using var factory = new ProjectsTestFactory();
        var promotion = FreeBuilds("ONEUSE", value: 5, maxPerUser: 1);
        await SeedAsync(factory, db => db.Promotions.Add(promotion));
        using var client = await Register(factory, $"buyer-{Guid.NewGuid()}@example.com");

        var first = await client.PostJsonWithCsrfAsync("/api/promotions/redeem", new { code = "ONEUSE" });
        var second = await client.PostJsonWithCsrfAsync("/api/promotions/redeem", new { code = "ONEUSE" });

        first.EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.BadRequest, second.StatusCode);
        var secondBody = await second.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("This promotion has already been redeemed.", secondBody.GetProperty("error").GetString());

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(1, await db.PromotionRedemptions.CountAsync(r => r.PromotionId == promotion.Id));
        var account = await db.BuildEntitlementAccounts.SingleAsync();
        Assert.Equal(5, account.RemainingBuilds);
    }

    [Fact]
    public async Task FreeBuildsCode_MaxRedemptionsPerUserAboveOne_AllowsExactlyThatManyThenRejects()
    {
        // Guards against a naive fix that clamps every FreeBuilds code to a
        // single redemption per user regardless of the admin-configured
        // MaxRedemptionsPerUser value.
        using var factory = new ProjectsTestFactory();
        var promotion = FreeBuilds("TWICE", value: 3, maxPerUser: 2);
        await SeedAsync(factory, db => db.Promotions.Add(promotion));
        using var client = await Register(factory, $"buyer-{Guid.NewGuid()}@example.com");

        var first = await client.PostJsonWithCsrfAsync("/api/promotions/redeem", new { code = "TWICE" });
        var second = await client.PostJsonWithCsrfAsync("/api/promotions/redeem", new { code = "TWICE" });
        var third = await client.PostJsonWithCsrfAsync("/api/promotions/redeem", new { code = "TWICE" });

        first.EnsureSuccessStatusCode();
        second.EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.BadRequest, third.StatusCode);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(2, await db.PromotionRedemptions.CountAsync(r => r.PromotionId == promotion.Id));
        var account = await db.BuildEntitlementAccounts.SingleAsync();
        Assert.Equal(6, account.RemainingBuilds);
    }

    [Fact]
    public async Task FreeBuildsCode_GlobalMaxRedemptionsReached_RejectsFurtherRedemptionsAcrossUsers()
    {
        using var factory = new ProjectsTestFactory();
        var promotion = FreeBuilds("LIMITED", value: 4, maxRedemptions: 1);
        await SeedAsync(factory, db => db.Promotions.Add(promotion));
        using var firstClient = await Register(factory, $"buyer-{Guid.NewGuid()}@example.com");
        using var secondClient = await Register(factory, $"buyer-{Guid.NewGuid()}@example.com");

        var first = await firstClient.PostJsonWithCsrfAsync("/api/promotions/redeem", new { code = "LIMITED" });
        var second = await secondClient.PostJsonWithCsrfAsync("/api/promotions/redeem", new { code = "LIMITED" });

        first.EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.BadRequest, second.StatusCode);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(1, await db.PromotionRedemptions.CountAsync(r => r.PromotionId == promotion.Id));
    }
}
