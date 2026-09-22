namespace WPAIPlugin.Generator.Models;

/// <summary>
/// Custom fields (meta box) attached to a custom post type defined in the same <see cref="PluginSpec"/>.
/// </summary>
public sealed class CustomFieldsSpec
{
    /// <summary>Slug of the custom post type these fields attach to. Must match <see cref="PluginSpec.CustomPostType"/>'s slug.</summary>
    public string PostType { get; set; } = string.Empty;

    public List<CustomFieldSpec> Fields { get; set; } = new();
}
