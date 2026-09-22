namespace WPAIPlugin.Generator.Models;

/// <summary>
/// Feature identifiers recognized by the deterministic generator.
/// </summary>
public static class PluginFeature
{
    public const string Shortcode = "shortcode";

    public static readonly IReadOnlySet<string> Known = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        Shortcode,
    };
}
