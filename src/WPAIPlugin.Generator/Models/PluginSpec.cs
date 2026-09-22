namespace WPAIPlugin.Generator.Models;

/// <summary>
/// Structured specification describing a WordPress plugin to generate.
/// </summary>
public sealed class PluginSpec
{
    public string Name { get; set; } = string.Empty;

    public string Slug { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public string Version { get; set; } = string.Empty;

    public string Author { get; set; } = string.Empty;

    public List<string> Features { get; set; } = new();
}
