namespace WPAIPlugin.Planning;

/// <summary>
/// Token usage reported by a provider for one planning request. Fields are
/// null when the provider's response did not include that figure - never
/// fabricated. Never carries prompt/response content.
/// </summary>
public sealed class PlanningUsage
{
    public int? InputTokens { get; init; }

    public int? OutputTokens { get; init; }

    public int? TotalTokens { get; init; }
}
