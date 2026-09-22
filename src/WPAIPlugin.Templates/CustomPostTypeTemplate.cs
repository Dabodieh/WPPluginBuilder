using System.Text;

namespace WPAIPlugin.Templates;

/// <summary>
/// Renders a basic WordPress custom post type registration block, hooked on 'init'.
/// </summary>
public static class CustomPostTypeTemplate
{
    /// <param name="slug">Custom post type slug (post type key).</param>
    /// <param name="singularName">Singular display name (e.g. "Staff Member").</param>
    /// <param name="pluralName">Plural display name (e.g. "Staff Members").</param>
    /// <param name="isPublic">Whether the post type is public.</param>
    /// <param name="hasArchive">Whether the post type has an archive.</param>
    public static string Render(string slug, string singularName, string pluralName, bool isPublic, bool hasArchive)
    {
        var phpPrefix = slug.Replace('-', '_');
        var functionName = $"{phpPrefix}_register_post_type";
        var publicLiteral = isPublic ? "true" : "false";
        var hasArchiveLiteral = hasArchive ? "true" : "false";

        var sb = new StringBuilder();
        sb.Append("// Custom Post Type: ").Append(slug).Append('\n');
        sb.Append("function ").Append(functionName).Append("() {\n");
        sb.Append("\tregister_post_type( '").Append(EscapeSingleQuotedPhp(slug)).Append("', array(\n");
        sb.Append("\t\t'labels' => array(\n");
        sb.Append("\t\t\t'name' => __( '").Append(EscapeSingleQuotedPhp(pluralName)).Append("' ),\n");
        sb.Append("\t\t\t'singular_name' => __( '").Append(EscapeSingleQuotedPhp(singularName)).Append("' ),\n");
        sb.Append("\t\t),\n");
        sb.Append("\t\t'public' => ").Append(publicLiteral).Append(",\n");
        sb.Append("\t\t'has_archive' => ").Append(hasArchiveLiteral).Append(",\n");
        sb.Append("\t\t'show_ui' => true,\n");
        sb.Append("\t\t'supports' => array( 'title', 'editor' ),\n");
        sb.Append("\t) );\n");
        sb.Append("}\n");
        sb.Append("add_action( 'init', '").Append(functionName).Append("' );\n");

        return sb.ToString();
    }

    private static string EscapeSingleQuotedPhp(string value)
    {
        return value.Replace("\\", "\\\\").Replace("'", "\\'");
    }
}
