namespace WPAIPlugin.Api.Controllers;

/// <summary>
/// Response bodies for the admin API. Never include passwords, password
/// hashes, auth tokens, cookies, API keys, provider secrets, or raw
/// Identity claims - only fields an operator legitimately needs.
/// </summary>
public sealed class AdminOverviewResponse
{
    public required int TotalUsers { get; init; }

    public required int UsersRegisteredLast7Days { get; init; }

    public required int TotalCreditBalance { get; init; }

    public required int GrossCreditsConsumed { get; init; }

    public required int CreditsRefunded { get; init; }

    public required int TotalPluginProjects { get; init; }

    public required int TotalVersions { get; init; }

    public required int StandardBuilds { get; init; }

    public required int ValidatedBuilds { get; init; }

    public required int AiRequests { get; init; }

    public required long AiTotalTokens { get; init; }

    public required long? AiEstimatedCostUsdMicros { get; init; }

    public required int ActivePromotions { get; init; }

    public required int PromotionalCreditsGranted { get; init; }

    public required int FreeBuildsRedeemed { get; init; }
}

public sealed class AdminUserSummaryResponse
{
    public required string Id { get; init; }

    public required string? Email { get; init; }

    public required bool LockedOut { get; init; }

    public required int CreditBalance { get; init; }

    public required int PluginCount { get; init; }

    public required int VersionCount { get; init; }
}

public sealed class AdminUserDetailResponse
{
    public required string Id { get; init; }

    public required string? Email { get; init; }

    public required bool LockedOut { get; init; }

    public required bool IsAdmin { get; init; }

    public required int CreditBalance { get; init; }

    public required int PluginCount { get; init; }

    public required int VersionCount { get; init; }

    public required int AiRequestCount { get; init; }

    public required long AiInputTokens { get; init; }

    public required long AiOutputTokens { get; init; }

    public required long AiTotalTokens { get; init; }

    public required long? AiEstimatedCostUsdMicros { get; init; }

    public required IReadOnlyList<AdminAiUsageEventResponse> RecentAiActivity { get; init; }

    public required IReadOnlyList<AdminCreditLedgerEntryResponse> RecentCreditActivity { get; init; }

    public required int LifetimePurchases { get; init; }

    public required int LifetimeRevenueMinor { get; init; }

    public required int LifetimeRefundedMinor { get; init; }

    public required string PurchaseCurrency { get; init; }

    public required int CreditsPurchased { get; init; }

    public required int CreditsConsumed { get; init; }

    /// <summary>Estimate only, explicitly labelled - never called profit. See AdminContributionResponse.Note for the same caveats.</summary>
    public required int? ApproximateContributionMinor { get; init; }
}

public sealed class AdminCreditLedgerEntryResponse
{
    public required string Type { get; init; }

    public required int Amount { get; init; }

    public required DateTime CreatedAtUtc { get; init; }
}

/// <summary>
/// Request body for POST /api/admin/users/{id}/credits/adjust. IdempotencyKey
/// is a client-generated token used only to detect a retried/duplicated
/// submission - the server still generates the actual ledger reference.
/// Reason is mandatory and stored only in the audit log, never in the ledger.
/// </summary>
public sealed class AdminCreditAdjustmentRequest
{
    public int Amount { get; set; }

    public string? Reason { get; set; }

    public Guid IdempotencyKey { get; set; }
}

public sealed class AdminCreditAdjustmentResponse
{
    public required int Amount { get; init; }

    public required int Balance { get; init; }

    public required bool AlreadyApplied { get; init; }
}

public sealed class AdminAuditLogEntryResponse
{
    public required Guid Id { get; init; }

    public required string AdminUserId { get; init; }

    public required string? AdminEmail { get; init; }

    public required string Action { get; init; }

    public required string TargetType { get; init; }

    public required string TargetId { get; init; }

    public required string? TargetEmail { get; init; }

    public required string? Description { get; init; }

    public required DateTime CreatedAtUtc { get; init; }
}

public sealed class AdminCreditAnalyticsResponse
{
    public required int TotalCreditsHeld { get; init; }

    public required int SignupCreditsGranted { get; init; }

    public required int AdminAdjustmentsNet { get; init; }

    public required int AdminAdjustmentsGranted { get; init; }

    public required int AdminAdjustmentsDeducted { get; init; }

    public required int GrossCreditsConsumed { get; init; }

    public required int CreditsRefunded { get; init; }

    public required int NetCreditsConsumed { get; init; }

    public required int StandardBuildUsage { get; init; }

    public required int ValidatedBuildUsage { get; init; }

    /// <summary>Sum of CreditPurchase ledger entries - real once purchases exist, 0 otherwise.</summary>
    public required int PurchasedCredits { get; init; }
}

public sealed class AdminPluginProjectResponse
{
    public required Guid Id { get; init; }

    public required string UserId { get; init; }

    public required string? UserEmail { get; init; }

    public required string Name { get; init; }

    public required string Slug { get; init; }

    public required DateTime CreatedAtUtc { get; init; }

    public required DateTime UpdatedAtUtc { get; init; }

    public required int VersionCount { get; init; }

    public required bool LatestValidated { get; init; }
}

public sealed class AdminAiUsageEventResponse
{
    public required Guid Id { get; init; }

    public required string? UserId { get; init; }

    public required string? UserEmail { get; init; }

    public required string OperationType { get; init; }

    public required string Provider { get; init; }

    public required string? Model { get; init; }

    public required int? InputTokens { get; init; }

    public required int? OutputTokens { get; init; }

    public required int? TotalTokens { get; init; }

    public required long? EstimatedCostUsdMicros { get; init; }

    public required long DurationMs { get; init; }

    public required bool Succeeded { get; init; }

    public required string? FailureCategory { get; init; }

    public required DateTime CreatedAtUtc { get; init; }
}

public sealed class AdminAiUsageAggregateResponse
{
    public required int Requests { get; init; }

    public required int SuccessfulRequests { get; init; }

    public required int FailedRequests { get; init; }

    public required long InputTokens { get; init; }

    public required long OutputTokens { get; init; }

    public required long TotalTokens { get; init; }

    public required long? EstimatedCostUsdMicros { get; init; }

    public required decimal? AverageCostUsdMicrosPerRequest { get; init; }

    public required IReadOnlyList<AdminAiUsageBreakdownRow> ByProvider { get; init; }

    public required IReadOnlyList<AdminAiUsageBreakdownRow> ByModel { get; init; }

    public required IReadOnlyList<AdminAiUsageDailyRow> ByDay { get; init; }

    public required IReadOnlyList<AdminAiUsageEventResponse> RecentEvents { get; init; }
}

public sealed class AdminAiUsageBreakdownRow
{
    public required string Key { get; init; }

    public required int Requests { get; init; }

    public required long TotalTokens { get; init; }

    public required long? EstimatedCostUsdMicros { get; init; }
}

public sealed class AdminAiUsageDailyRow
{
    public required DateOnly Date { get; init; }

    public required int Requests { get; init; }

    public required long TotalTokens { get; init; }

    public required long? EstimatedCostUsdMicros { get; init; }
}

public sealed class AdminSystemStatusResponse
{
    public required string? ApplicationVersion { get; init; }

    public required string Environment { get; init; }

    public required bool DatabaseHealthy { get; init; }

    public required string PlanningProvider { get; init; }

    public required bool PlanningProviderConfigured { get; init; }

    public required bool DockerValidationAvailable { get; init; }

    public required bool ArtifactStorageWritable { get; init; }

    public required bool StripeConfigured { get; init; }

    public required bool TransactionalEmailConfigured { get; init; }
}
