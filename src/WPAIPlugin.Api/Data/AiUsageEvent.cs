namespace WPAIPlugin.Api.Data;

public static class AiUsageOperationType
{
    public const string Plan = "Plan";
}

/// <summary>
/// Immutable operational/cost record of one AI provider request. Never stores
/// prompts, generated responses, API keys, or other provider secrets - only
/// token counts, timing, and an estimated cost computed at request time from
/// server-side pricing configuration (see AiPricingOptions). Cost is never
/// recalculated later from current pricing.
/// </summary>
public sealed class AiUsageEvent
{
    public Guid Id { get; set; }

    /// <summary>Null when the request could not be attributed to an authenticated user.</summary>
    public string? UserId { get; set; }

    public required string OperationType { get; set; }

    public required string Provider { get; set; }

    public string? Model { get; set; }

    public int? InputTokens { get; set; }

    public int? OutputTokens { get; set; }

    public int? TotalTokens { get; set; }

    /// <summary>
    /// Estimated provider cost in micro-dollars (1,000,000 = $1.00), computed
    /// at request time from server-side pricing configuration. Null when
    /// token counts or pricing were unavailable for this request.
    /// </summary>
    public long? EstimatedCostUsdMicros { get; set; }

    public long DurationMs { get; set; }

    public bool Succeeded { get; set; }

    /// <summary>Safe failure category (e.g. a PluginPlanFailureReason name) - never a raw exception message.</summary>
    public string? FailureCategory { get; set; }

    public DateTime CreatedAtUtc { get; set; }
}
