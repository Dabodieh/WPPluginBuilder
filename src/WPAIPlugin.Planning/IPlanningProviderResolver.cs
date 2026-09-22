namespace WPAIPlugin.Planning;

/// <summary>
/// Resolves the <see cref="IPlanningProvider"/> to use for a request, by name,
/// falling back to the configured default provider when none is specified.
/// </summary>
public interface IPlanningProviderResolver
{
    /// <param name="providerName">
    /// Explicit provider name (e.g. "anthropic"), or null/whitespace to use the configured default.
    /// </param>
    /// <exception cref="PluginPlanException">
    /// Thrown with <see cref="PluginPlanFailureReason.ProviderNotFound"/> when
    /// <paramref name="providerName"/> does not match any registered provider.
    /// </exception>
    IPlanningProvider Resolve(string? providerName);
}
