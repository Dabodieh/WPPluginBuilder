namespace WPAIPlugin.Api.Data;

// One row per Identity user (Promotions + Free Builds milestone). Mirrors
// CreditAccount exactly: RemainingBuilds is the fast, concurrency-safe
// current balance; BuildEntitlementTransactions is the immutable ledger of
// every change that produced it. Every balance change must have a
// corresponding BuildEntitlementTransaction written in the same database
// transaction. A free build is a distinct entitlement from credits - it is
// never converted to or from a credit amount.
//
// Version is an application-managed EF Core concurrency token (not a
// PostgreSQL-specific xmin), following the exact same pattern CreditService
// already uses for CreditAccount.
public sealed class BuildEntitlementAccount
{
    public required string UserId { get; set; }

    public int RemainingBuilds { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public int Version { get; set; }
}
