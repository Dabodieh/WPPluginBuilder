namespace WPAIPlugin.Api.Data;

public static class BuildEntitlementTransactionType
{
    public const string SignupFreeBuildGrant = "SignupFreeBuildGrant";
    public const string FreeBuildConsumed = "FreeBuildConsumed";
    public const string FreeBuildRefund = "FreeBuildRefund";
    public const string PromotionGrant = "PromotionGrant";
}

// Immutable ledger entry, exact counterpart to CreditTransaction for free
// build entitlements. Never updated or deleted after creation.
// BuildEntitlementAccount.RemainingBuilds is derived from applying these in
// order, kept as a maintained column for fast/concurrency-safe reads. Amount
// is positive for builds granted, negative for builds consumed.
public sealed class BuildEntitlementTransaction
{
    public Guid Id { get; set; }

    public required string UserId { get; set; }

    public int Amount { get; set; }

    public required string Type { get; set; }

    /// <summary>Set only for Type = PromotionGrant - which promotion granted these builds.</summary>
    public Guid? PromotionId { get; set; }

    /// <summary>
    /// Server-generated reference correlating a consumption with its refund
    /// (e.g. "build:&lt;guid&gt;", shared with the CreditTransaction reference
    /// for the same build operation). Never an email address, prompt, API
    /// key, or filesystem path.
    /// </summary>
    public required string Reference { get; set; }

    public DateTime CreatedAtUtc { get; set; }
}
