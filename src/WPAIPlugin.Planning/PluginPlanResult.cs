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
}
