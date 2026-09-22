namespace WPAIPlugin.Planning;

/// <summary>
/// Resolves an <see cref="IPlanningProvider"/> by name (case-insensitive) from
/// the set of registered providers, falling back to a configured default.
/// </summary>
public sealed class PlanningProviderResolver : IPlanningProviderResolver
{
    private readonly IReadOnlyDictionary<string, IPlanningProvider> _providersByName;
    private readonly string _defaultProviderName;

    public PlanningProviderResolver(IEnumerable<IPlanningProvider> providers, string defaultProviderName)
    {
        _providersByName = providers.ToDictionary(p => p.Name, StringComparer.OrdinalIgnoreCase);
        _defaultProviderName = defaultProviderName;
    }

    public IPlanningProvider Resolve(string? providerName)
    {
        var name = string.IsNullOrWhiteSpace(providerName) ? _defaultProviderName : providerName;

        if (_providersByName.TryGetValue(name, out var provider))
        {
            return provider;
        }

        throw new PluginPlanException(
            PluginPlanFailureReason.ProviderNotFound,
            $"Unknown AI planning provider: '{name}'.");
    }
}
