namespace WPAIPlugin.Generator.Models;

/// <summary>
/// Feature identifiers recognized by the deterministic generator.
/// </summary>
public static class PluginFeature
{
    public const string Shortcode = "shortcode";

    public const string CustomPostType = "custom-post-type";

    public const string SettingsPage = "settings-page";

    public const string CustomFields = "custom-fields";

    public const string ScheduledTask = "scheduled-task";

    public static readonly IReadOnlySet<string> Known = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        Shortcode,
        CustomPostType,
        SettingsPage,
        CustomFields,
        ScheduledTask,
    };
}
