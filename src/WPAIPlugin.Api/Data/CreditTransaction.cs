namespace WPAIPlugin.Api.Data;

public static class CreditTransactionType
{
    public const string SignupGrant = "SignupGrant";
    public const string PluginBuild = "PluginBuild";
    public const string ValidatedBuild = "ValidatedBuild";
    public const string Refund = "Refund";
    public const string AdminAdjustment = "AdminAdjustment";
    public const string CreditPurchase = "CreditPurchase";

    /// <summary>A promotional bonus credited alongside a real CreditPurchase - never merged into the purchased amount, so purchased vs. promotional credits stay distinguishable in reporting.</summary>
    public const string PromotionBonus = "PromotionBonus";
}

// Immutable ledger entry (Milestone 12). Never updated or deleted after
// creation - CreditAccount.Balance is derived from applying these in order,
// but is kept as a maintained column for fast/concurrency-safe reads.
// Amount is positive for credits added, negative for credits consumed.
public sealed class CreditTransaction
{
    public Guid Id { get; set; }

    public required string UserId { get; set; }

    public int Amount { get; set; }

    public required string Type { get; set; }

    /// <summary>
    /// Server-generated reference correlating a charge with its refund (e.g.
    /// "build:&lt;guid&gt;"). Never an email address, prompt, API key, or
    /// filesystem path.
    /// </summary>
    public required string Reference { get; set; }

    public DateTime CreatedAtUtc { get; set; }
}
