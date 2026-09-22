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
}
