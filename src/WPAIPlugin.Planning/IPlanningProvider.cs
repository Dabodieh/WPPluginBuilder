namespace WPAIPlugin.Planning;

/// <summary>
/// A single AI provider capable of turning a plugin description into a
/// <see cref="PlanningResult"/>. Implementations own all vendor-specific
/// request/response translation; nothing vendor-specific escapes this interface.
/// </summary>
public interface IPlanningProvider
{
    /// <summary>
    /// Provider name as referenced by clients/configuration (e.g. "anthropic").
    /// </summary>
    string Name { get; }

    Task<PlanningResult> PlanAsync(PlanningRequest request, CancellationToken cancellationToken = default);
}
