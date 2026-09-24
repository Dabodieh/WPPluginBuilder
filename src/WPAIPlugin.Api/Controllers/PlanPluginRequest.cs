namespace WPAIPlugin.Api.Controllers;

/// <summary>
/// Request body for POST /api/plugins/plan.
/// </summary>
public sealed class PlanPluginRequest
{
    public string? Description { get; set; }

    /// <summary>Optional AI provider name (e.g. "anthropic"). Omit to use the configured default.</summary>
    public string? Provider { get; set; }

    // No client-supplied Model, temperature, systemPrompt, or similar field
    // exists here by design - which model/prompt is used is always a
    // server-side decision (see PlanningOptions), never client-influenced.
}
