namespace WPAIPlugin.Api.Data;

// One row per Identity user (Milestone 12). Balance is the fast,
// concurrency-safe current balance; CreditTransactions is the immutable
// ledger of every change that produced it. Every balance change must have a
// corresponding CreditTransaction written in the same database transaction.
//
// Version is an application-managed EF Core concurrency token (not a
// PostgreSQL-specific xmin): CreditService increments it on every balance
// change and lets EF's optimistic-concurrency check reject a conflicting
// concurrent write, which CreditService then retries. This works identically
// against Npgsql and the EF Core InMemory provider used by tests.
public sealed class CreditAccount
{
    public required string UserId { get; set; }

    public int Balance { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public int Version { get; set; }

    /// <summary>
    /// Set once, at registration (Promotions + Free Builds milestone) - the
    /// closest available proxy for "when this user registered", since
    /// IdentityUser itself has no creation timestamp. Existing rows backfilled
    /// to DateTime.MinValue - a deliberate "unknown/never new" sentinel, not a
    /// guess - so pre-existing accounts can never satisfy
    /// PromotionEligibility.NewRegistrations (registeredAtUtc &lt;
    /// promotion.StartsAtUtc always holds for them). See
    /// PROMOTIONS-FREE-BUILDS-COMPLETION.md.
    /// </summary>
    public DateTime CreatedAtUtc { get; set; }
}
