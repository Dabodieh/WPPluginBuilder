namespace WPAIPlugin.Generator.Models;

/// <summary>
/// A single field on a generated settings page.
/// </summary>
public sealed class SettingsFieldSpec
{
    public string Key { get; set; } = string.Empty;

    public string Label { get; set; } = string.Empty;

    /// <summary>One of: "text", "textarea", "checkbox".</summary>
    public string Type { get; set; } = string.Empty;

    public string? DefaultValue { get; set; }
}
