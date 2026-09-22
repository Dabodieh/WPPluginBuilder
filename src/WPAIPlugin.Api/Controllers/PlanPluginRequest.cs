namespace WPAIPlugin.Api.Controllers;

/// <summary>
/// Request body for POST /api/plugins/plan.
/// </summary>
public sealed class PlanPluginRequest
{
    public string? Description { get; set; }

    /// <summary>Optional AI provider name (e.g. "anthropic"). Omit to use the configured default.</summary>
    public string? Provider { get; set; }

    /// <summary>Optional provider-specific model override.</summary>
    public string? Model { get; set; }
}
