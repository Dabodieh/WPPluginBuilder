namespace WPAIPlugin.Api.Security;

/// <summary>
/// Internal safety-net cost ceilings, separate from the customer-facing
/// credit/free-build system. Defaults are deliberately generous so a
/// legitimate user never notices them - they exist to bound worst-case AI
/// spend from a single compromised or abusive account, not to meter normal
/// usage. Bound from configuration section "Abuse" (e.g.
/// Abuse__MaxAiCostUsdMicrosPerUserPerDay).
/// </summary>
public sealed class AbuseOptions
{
    /// <summary>Max estimated AI cost (in micro-dollars; 1,000,000 = $1.00) one account may incur across all planning calls per rolling UTC day, before further planning calls are refused without invoking the AI provider.</summary>
    public long MaxAiCostUsdMicrosPerUserPerDay { get; set; } = 5_000_000;
}
