namespace WPAIPlugin.Generator.Models;

/// <summary>
/// Data needed to register a single WordPress admin settings page.
/// </summary>
public sealed class SettingsPageSpec
{
    public string PageTitle { get; set; } = string.Empty;

    public string MenuTitle { get; set; } = string.Empty;

    public List<SettingsFieldSpec> Fields { get; set; } = new();
}
