namespace WPAIPlugin.Planning;

/// <summary>
/// Provider-agnostic request to plan a WordPress plugin from a natural-language description.
/// </summary>
public sealed class PlanningRequest
{
    public required string Description { get; init; }

    /// <summary>
    /// Optional provider-specific model override. Null means "use the provider's default model".
    /// </summary>
    public string? Model { get; init; }
}
