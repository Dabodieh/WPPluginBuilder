namespace WPAIPlugin.Generator.Models;

/// <summary>
/// Data needed to register a single WordPress custom post type.
/// </summary>
public sealed class CustomPostTypeSpec
{
    public string SingularName { get; set; } = string.Empty;

    public string PluralName { get; set; } = string.Empty;

    public string Slug { get; set; } = string.Empty;

    public bool Public { get; set; } = true;

    public bool HasArchive { get; set; }
}
