using WPAIPlugin.Generator.Models;

namespace WPAIPlugin.Api.Controllers;

/// <summary>
/// Response body for POST /api/plugins/plan.
/// </summary>
public sealed class PlanPluginResponse
{
    public required PluginSpec Spec { get; init; }

    public required IReadOnlyList<string> UnsupportedRequirements { get; init; }
}
