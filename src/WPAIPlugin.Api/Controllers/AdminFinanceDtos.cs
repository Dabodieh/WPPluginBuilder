namespace WPAIPlugin.Api.Controllers;

/// <summary>
/// Response bodies for admin revenue/financial reporting. Revenue is always
/// derived from Purchase records - never from CreditTransaction/credit
/// balances, which are a separate accounting concern. "Contribution" figures
/// are explicitly labelled as estimates, never called profit, since they do
/// not include every real business cost.
/// </summary>
public sealed class AdminRevenueResponse
{
    public required int GrossRevenueMinor { get; init; }

    public required int RefundedMinor { get; init; }

    public required int NetRevenueMinor { get; init; }

    public required string Currency { get; init; }

    public required int SuccessfulPurchases { get; init; }

    public required int FailedOrCancelledPurchases { get; init; }

    public required int CreditsSold { get; init; }

    public required int PurchasingCustomers { get; init; }

    public required decimal? AveragePurchaseMinor { get; init; }

    public required IReadOnlyList<AdminRevenueByPeriodRow> ByPeriod { get; init; }

    public required IReadOnlyList<AdminPurchaseRow> RecentPurchases { get; init; }
}

public sealed class AdminRevenueByPeriodRow
{
    public required DateOnly Date { get; init; }

    public required int GrossRevenueMinor { get; init; }

    public required int SuccessfulPurchases { get; init; }

    public required int CreditsSold { get; init; }
}

public sealed class AdminPurchaseRow
{
    public required Guid Id { get; init; }

    public required string UserId { get; init; }

    public required string? UserEmail { get; init; }

    public required string PackId { get; init; }

    public required int AmountMinor { get; init; }

    public required string Currency { get; init; }

    public required int CreditsPurchased { get; init; }

    public required string Status { get; init; }

    public required int RefundedAmountMinor { get; init; }

    public required DateTime CreatedAtUtc { get; init; }

    public required DateTime? CompletedAtUtc { get; init; }
}

/// <summary>
/// Combines Purchase revenue with AiUsageEvent estimated cost. Never calls
/// the result "profit" - Stripe fees are included only where genuinely known
/// (currently: not known, so omitted rather than invented), and no other
/// business cost (hosting, staff, etc.) is included at all.
/// </summary>
public sealed class AdminContributionResponse
{
    public required int RevenueMinor { get; init; }

    public required int RefundedRevenueMinor { get; init; }

    public required long? EstimatedAiSpendUsdMicros { get; init; }

    public required decimal? RevenuePerCustomerMinor { get; init; }

    public required decimal? AiCostPerCustomerUsdMicros { get; init; }

    public required decimal? RevenuePerBuildMinor { get; init; }

    public required decimal? AiCostPerBuildUsdMicros { get; init; }

    /// <summary>
    /// Revenue minus estimated AI spend only - a partial, explicitly-labelled
    /// estimate, never "profit". Null when currency units cannot be sensibly
    /// combined (kept separate here: GBP minor units vs USD micro-dollars).
    /// </summary>
    public required string Note { get; init; }
}

public sealed class AdminOperationalAnalyticsResponse
{
    public required DateTime? From { get; init; }

    public required DateTime? To { get; init; }

    public required int Registrations { get; init; }

    public required int PlanGenerations { get; init; }

    public required int PlanGenerationFailures { get; init; }

    public required int TotalBuilds { get; init; }

    public required int StandardBuilds { get; init; }

    public required int ValidatedBuilds { get; init; }

    public required int RefundedBuilds { get; init; }

    public required int CreditsConsumed { get; init; }

    public required int AiRequests { get; init; }

    public required int AiFailures { get; init; }

    public required int Purchases { get; init; }

    public required int RevenueMinor { get; init; }

    /// <summary>
    /// Always 0 in the current architecture - webhook failures (bad signature,
    /// malformed payload) return 400 without any persisted failure record, so
    /// this is reported honestly as 0 rather than invented. See the
    /// completion report for the reasoning.
    /// </summary>
    public required int PaymentWebhookFailures { get; init; }
}
