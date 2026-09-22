namespace WPAIPlugin.Generator.Models;

/// <summary>
/// Settings field type identifiers recognized by the deterministic generator.
/// </summary>
public static class SettingsFieldType
{
    public const string Text = "text";

    public const string Textarea = "textarea";

    public const string Checkbox = "checkbox";

    public static readonly IReadOnlySet<string> Known = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        Text,
        Textarea,
        Checkbox,
    };
}
