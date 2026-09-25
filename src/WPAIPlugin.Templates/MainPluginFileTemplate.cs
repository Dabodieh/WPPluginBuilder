using System.Text;

namespace WPAIPlugin.Templates;

/// <summary>
/// Renders the main plugin PHP file, including the WordPress plugin header
/// and the ABSPATH direct-execution guard.
/// </summary>
public static class MainPluginFileTemplate
{
    /// <param name="pluginName">Human-readable plugin name (WordPress "Plugin Name" header).</param>
    /// <param name="slug">Plugin slug, used to derive the PHP prefix/text domain.</param>
    /// <param name="description">Plugin description.</param>
    /// <param name="version">Plugin version (e.g. 1.0.0).</param>
    /// <param name="author">Plugin author.</param>
    /// <param name="featureBlocks">Additional PHP code blocks contributed by requested features (e.g. shortcode registration).</param>
    public static string Render(
        string pluginName,
        string slug,
        string description,
        string version,
        string author,
        IEnumerable<string>? featureBlocks = null)
    {
        var phpPrefix = slug.Replace('-', '_');
        var constantPrefix = phpPrefix.ToUpperInvariant();

        var sb = new StringBuilder();
        sb.Append("<?php\n");
        sb.Append("/**\n");
        sb.Append(" * Plugin Name: ").Append(EscapeHeaderValue(pluginName)).Append('\n');
        sb.Append(" * Description: ").Append(EscapeHeaderValue(description)).Append('\n');
        sb.Append(" * Version: ").Append(EscapeHeaderValue(version)).Append('\n');
        sb.Append(" * Author: ").Append(EscapeHeaderValue(author)).Append('\n');
        sb.Append(" * Text Domain: ").Append(slug).Append('\n');
        sb.Append(" */\n\n");

        sb.Append("// Prevent direct file access.\n");
        sb.Append("if ( ! defined( 'ABSPATH' ) ) {\n");
        sb.Append("\texit;\n");
        sb.Append("}\n\n");

        sb.Append("define( '").Append(constantPrefix).Append("_VERSION', '").Append(version).Append("' );\n");

        if (featureBlocks is not null)
        {
            foreach (var block in featureBlocks)
            {
                sb.Append('\n').Append(block).Append('\n');
            }
        }

        return sb.ToString();
    }

    private static string EscapeHeaderValue(string value)
    {
        // WordPress plugin header values live inside a single /** ... */
        // PHP doc-comment. Newlines are stripped so a value cannot break
        // onto its own line, and any "*/" sequence is broken up so a value
        // can never terminate that comment early - without this, the rest
        // of the header (and everything up to the next literal "*/") would
        // stop being a comment and become live, executed PHP. The inserted
        // space is a deliberate, visible mutation: safety over preserving a
        // byte sequence that is never a meaningful part of a real plugin
        // name/description/author.
        return value.Replace("\r", " ").Replace("\n", " ").Replace("*/", "* /").Trim();
    }
}
