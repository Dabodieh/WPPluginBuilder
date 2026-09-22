namespace WPAIPlugin.Generator;

/// <summary>
/// Thrown when a <see cref="PluginSpec"/> fails validation and cannot be built.
/// </summary>
public sealed class PluginBuildException : Exception
{
    public IReadOnlyList<string> ValidationErrors { get; }

    public PluginBuildException(IReadOnlyList<string> validationErrors)
        : base("Plugin spec failed validation: " + string.Join("; ", validationErrors))
    {
        ValidationErrors = validationErrors;
    }
}
