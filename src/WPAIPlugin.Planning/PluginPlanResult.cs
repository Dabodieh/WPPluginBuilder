using WPAIPlugin.Generator.Models;

namespace WPAIPlugin.Planning;

/// <summary>
/// Successful outcome of planning a plugin: a <see cref="PluginSpec"/> that has
/// already passed <see cref="Generator.Validation.PluginSpecValidator"/>, plus
/// any requirements the user asked for that the current generator cannot produce.
/// </summary>
public sealed class PluginPlanResult
{
    public required PluginSpec Spec { get; init; }

    public required IReadOnlyList<string> UnsupportedRequirements { get; init; }

    /// <summary>Name of the provider that actually produced this plan (see IPlanningProvider.Name).</summary>
    public required string Provider { get; init; }

    /// <summary>Token usage reported by the resolved provider, when available.</summary>
    public PlanningUsage? Usage { get; init; }

    /// <summary>The exact model string the provider actually used (server-resolved, never client-supplied).</summary>
    public string? Model { get; init; }
}
