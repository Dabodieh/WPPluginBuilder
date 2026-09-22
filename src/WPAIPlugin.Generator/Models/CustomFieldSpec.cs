namespace WPAIPlugin.Generator.Models;

/// <summary>
/// A single custom field attached to a custom post type's meta box.
/// </summary>
public sealed class CustomFieldSpec
{
    public string Key { get; set; } = string.Empty;

    public string Label { get; set; } = string.Empty;

    /// <summary>One of: "text", "textarea", "checkbox".</summary>
    public string Type { get; set; } = string.Empty;
}
