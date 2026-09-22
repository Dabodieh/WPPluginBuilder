namespace WPAIPlugin.Planning;

/// <summary>
/// Converts a natural-language plugin description into a validated <see cref="Generator.Models.PluginSpec"/>.
/// Never generates PHP, writes files, or chooses filesystem paths.
/// </summary>
public interface IPluginPlanner
{
    /// <exception cref="PluginPlanException">
    /// Thrown when input is invalid, the provider fails, or the resulting spec
    /// fails validation. Never throws raw provider/SDK exceptions.
    /// </exception>
    Task<PluginPlanResult> PlanAsync(
        string description,
        string? provider = null,
        string? model = null,
        CancellationToken cancellationToken = default);
}
