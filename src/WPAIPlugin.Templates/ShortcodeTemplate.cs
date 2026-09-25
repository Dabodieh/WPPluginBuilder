using System.Text;

namespace WPAIPlugin.Templates;

/// <summary>
/// Renders a basic WordPress shortcode registration block.
/// </summary>
public static class ShortcodeTemplate
{
    /// <param name="slug">Plugin slug, used to derive the shortcode tag and PHP function name.</param>
    /// <param name="pluginName">Human-readable plugin name, used as the rendered output label.</param>
    public static string Render(string slug, string pluginName)
    {
        var phpPrefix = slug.Replace('-', '_');
        var shortcodeTag = phpPrefix;
        var functionName = $"{phpPrefix}_shortcode";

        var sb = new StringBuilder();
        sb.Append("// Shortcode: [").Append(shortcodeTag).Append("]\n");
        sb.Append("function ").Append(functionName).Append("( $atts = array() ) {\n");
        // pluginName is untrusted HTML output, not PHP source: PHP-string-
        // escaping it (EscapeSingleQuotedPhp) only stops it from breaking the
        // PHP string literal - it does nothing to stop it from carrying live
        // HTML/JS into the page when the shortcode renders. esc_html() is
        // the separate, required escaping for that separate context.
        sb.Append("\treturn '<div class=\"").Append(slug).Append("\">'\n");
        sb.Append("\t\t. \"\\n    \" . esc_html( '").Append(EscapeSingleQuotedPhp(pluginName)).Append("' )\n");
        sb.Append("\t\t. \"\\n</div>\";\n");
        sb.Append("}\n");
        sb.Append("add_shortcode( '").Append(shortcodeTag).Append("', '").Append(functionName).Append("' );\n");

        return sb.ToString();
    }

    private static string EscapeSingleQuotedPhp(string value)
    {
        return value.Replace("\\", "\\\\").Replace("'", "\\'");
    }
}
