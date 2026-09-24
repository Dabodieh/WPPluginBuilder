namespace WPAIPlugin.Api.Data;

public static class PurchaseProvider
{
    public const string Stripe = "Stripe";
}

public static class PurchaseStatus
{
    public const string Pending = "Pending";
    public const string Completed = "Completed";
    public const string Failed = "Failed";
    public const string Cancelled = "Cancelled";
}

/// <summary>
/// A single credit-pack purchase attempt (Milestone 16; promotion snapshot
/// fields added in the Promotions + Free Builds milestone). Created Pending
/// when a Checkout Session is started; moves to Completed only after a
/// verified Stripe webhook confirms payment, at which point one
/// CreditTransaction (Type = CreditPurchase, amount = CreditsPurchased) plus,
/// if BonusCredits &gt; 0, a second CreditTransaction (Type = PromotionBonus)
/// are written and CreditAccount is increased by both. AmountMinor is the
/// integer minor-currency-unit price actually charged (e.g. 499 = £4.99) -
/// always server-determined, post-discount, never from the browser.
/// RefundedAmountMinor tracks a Stripe monetary refund for admin visibility
/// only - it never automatically adjusts CreditAccount.
///
/// The promotion fields are a checkout-time snapshot, not a live reference:
/// once a Purchase is created, its commercial terms are fixed even if the
/// referenced Promotion is later edited, disabled, or expires before the
/// customer completes payment - see PROMOTIONS-FREE-BUILDS-COMPLETION.md
/// "Checkout snapshot behaviour".
/// </summary>
public sealed class Purchase
{
    public Guid Id { get; set; }

    public required string UserId { get; set; }

    public required string Provider { get; set; }

    public required string PackId { get; set; }

    /// <summary>Stripe Checkout Session ID - unique per checkout attempt.</summary>
    public required string ProviderCheckoutSessionId { get; set; }

    /// <summary>Populated once Stripe reports a PaymentIntent for this session.</summary>
    public string? ProviderPaymentIntentId { get; set; }

    public required string Currency { get; set; }

    /// <summary>Amount actually charged/paid - after any PackPriceDiscount promotion. Never a float/double.</summary>
    public required int AmountMinor { get; set; }

    /// <summary>The configured pack's undiscounted price at checkout time - equal to AmountMinor unless a PackPriceDiscount promotion applied.</summary>
    public required int BaseAmountMinor { get; set; }

    /// <summary>Base credits purchased (unaffected by any promotion) - unchanged meaning from Milestone 16.</summary>
    public required int CreditsPurchased { get; set; }

    /// <summary>Extra credits granted by a BonusCredits promotion, on top of CreditsPurchased. 0 when no such promotion applied.</summary>
    public int BonusCredits { get; set; }

    /// <summary>The promotion applied at checkout time, if any - never re-resolved later.</summary>
    public Guid? PromotionId { get; set; }

    public string? PromotionCodeSnapshot { get; set; }

    public string? PromotionNameSnapshot { get; set; }

    public required string Status { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime? CompletedAtUtc { get; set; }

    public int RefundedAmountMinor { get; set; }
}
