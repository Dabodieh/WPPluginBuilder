using System.Text;

namespace WPAIPlugin.Templates;

/// <summary>
/// Renders a basic WordPress admin settings page using the Settings API
/// (add_options_page, register_setting, settings sections/fields, submit_button).
/// Supports field types: text, textarea, checkbox.
/// </summary>
public static class SettingsPageTemplate
{
    /// <param name="slug">Plugin slug, used to derive the options group, page slug, and PHP function names.</param>
    /// <param name="pageTitle">Settings page title.</param>
    /// <param name="menuTitle">Settings menu label.</param>
    /// <param name="fields">Field key, label, type ("text"/"textarea"/"checkbox"), and default value.</param>
    public static string Render(string slug, string pageTitle, string menuTitle, IReadOnlyList<(string Key, string Label, string Type, string? DefaultValue)> fields)
    {
        var phpPrefix = slug.Replace('-', '_');
        var optionGroup = $"{phpPrefix}_settings_group";
        var optionName = $"{phpPrefix}_settings";
        var pageSlug = $"{slug}-settings";
        var sectionId = $"{phpPrefix}_settings_section";
        var menuFunctionName = $"{phpPrefix}_add_settings_page";
        var registerFunctionName = $"{phpPrefix}_register_settings";
        var renderPageFunctionName = $"{phpPrefix}_render_settings_page";
        var sanitizeFunctionName = $"{phpPrefix}_sanitize_settings";

        var sb = new StringBuilder();

        sb.Append("// Settings Page\n");

        // Admin menu registration.
        sb.Append("function ").Append(menuFunctionName).Append("() {\n");
        sb.Append("\tadd_options_page(\n");
        sb.Append("\t\t").Append(PhpString(pageTitle)).Append(",\n");
        sb.Append("\t\t").Append(PhpString(menuTitle)).Append(",\n");
        sb.Append("\t\t'manage_options',\n");
        sb.Append("\t\t").Append(PhpString(pageSlug)).Append(",\n");
        sb.Append("\t\t'").Append(renderPageFunctionName).Append("'\n");
        sb.Append("\t);\n");
        sb.Append("}\n");
        sb.Append("add_action( 'admin_menu', '").Append(menuFunctionName).Append("' );\n\n");

        // Setting + sections + fields registration.
        sb.Append("function ").Append(registerFunctionName).Append("() {\n");
        sb.Append("\tregister_setting( ").Append(PhpString(optionGroup)).Append(", ").Append(PhpString(optionName)).Append(", '").Append(sanitizeFunctionName).Append("' );\n\n");
        sb.Append("\tadd_settings_section(\n");
        sb.Append("\t\t").Append(PhpString(sectionId)).Append(",\n");
        sb.Append("\t\t'',\n");
        sb.Append("\t\t'__return_false',\n");
        sb.Append("\t\t").Append(PhpString(pageSlug)).Append("\n");
        sb.Append("\t);\n\n");

        foreach (var field in fields)
        {
            var fieldFunctionName = $"{phpPrefix}_field_{field.Key}";
            sb.Append("\tadd_settings_field(\n");
            sb.Append("\t\t").Append(PhpString(field.Key)).Append(",\n");
            sb.Append("\t\t").Append(PhpString(field.Label)).Append(",\n");
            sb.Append("\t\t'").Append(fieldFunctionName).Append("',\n");
            sb.Append("\t\t").Append(PhpString(pageSlug)).Append(",\n");
            sb.Append("\t\t").Append(PhpString(sectionId)).Append("\n");
            sb.Append("\t);\n\n");
        }

        sb.Append("}\n");
        sb.Append("add_action( 'admin_init', '").Append(registerFunctionName).Append("' );\n\n");

        // Sanitize callback.
        sb.Append("function ").Append(sanitizeFunctionName).Append("( $input ) {\n");
        sb.Append("\t$sanitized = array();\n");
        foreach (var field in fields)
        {
            sb.Append("\tif ( isset( $input['").Append(EscapeSingleQuotedPhp(field.Key)).Append("'] ) ) {\n");
            sb.Append("\t\t$sanitized['").Append(EscapeSingleQuotedPhp(field.Key)).Append("'] = ").Append(SanitizeCallExpression(field.Type, field.Key)).Append(";\n");
            sb.Append("\t} else {\n");
            sb.Append("\t\t$sanitized['").Append(EscapeSingleQuotedPhp(field.Key)).Append("'] = ").Append(field.Type == "checkbox" ? "0" : "''").Append(";\n");
            sb.Append("\t}\n");
        }
        sb.Append("\treturn $sanitized;\n");
        sb.Append("}\n\n");

        // Per-field render callbacks.
        foreach (var field in fields)
        {
            var fieldFunctionName = $"{phpPrefix}_field_{field.Key}";
            sb.Append("function ").Append(fieldFunctionName).Append("() {\n");
            sb.Append("\t$options = get_option( ").Append(PhpString(optionName)).Append(", array() );\n");
            sb.Append("\t$value = isset( $options['").Append(EscapeSingleQuotedPhp(field.Key)).Append("'] ) ? $options['").Append(EscapeSingleQuotedPhp(field.Key)).Append("'] : ").Append(PhpString(field.DefaultValue ?? string.Empty)).Append(";\n");
            sb.Append(RenderFieldControl(optionName, field.Key, field.Type));
            sb.Append("}\n\n");
        }

        // Page render callback.
        sb.Append("function ").Append(renderPageFunctionName).Append("() {\n");
        sb.Append("\tif ( ! current_user_can( 'manage_options' ) ) {\n");
        sb.Append("\t\treturn;\n");
        sb.Append("\t}\n");
        sb.Append("\techo '<div class=\"wrap\">';\n");
        sb.Append("\techo '<h1>' . esc_html( get_admin_page_title() ) . '</h1>';\n");
        sb.Append("\techo '<form method=\"post\" action=\"options.php\">';\n");
        sb.Append("\tsettings_fields( ").Append(PhpString(optionGroup)).Append(" );\n");
        sb.Append("\tdo_settings_sections( ").Append(PhpString(pageSlug)).Append(" );\n");
        sb.Append("\tsubmit_button();\n");
        sb.Append("\techo '</form>';\n");
        sb.Append("\techo '</div>';\n");
        sb.Append("}\n");

        return sb.ToString();
    }

    private static string RenderFieldControl(string optionName, string key, string type)
    {
        var name = $"{optionName}[{key}]";

        var escapedName = EscapeSingleQuotedPhp(name);

        return type switch
        {
            "textarea" =>
                $"\techo '<textarea name=\"" + escapedName + "\">' . esc_textarea( $value ) . '</textarea>';\n",
            "checkbox" =>
                "\techo '<input type=\"checkbox\" name=\"" + escapedName + "\" value=\"1\"' . checked( 1, $value, false ) . ' />';\n",
            _ =>
                "\techo '<input type=\"text\" name=\"" + escapedName + "\" value=\"' . esc_attr( $value ) . '\" />';\n",
        };
    }

    private static string SanitizeCallExpression(string type, string key)
    {
        var escapedKey = EscapeSingleQuotedPhp(key);

        return type switch
        {
            "textarea" => $"sanitize_textarea_field( $input['{escapedKey}'] )",
            "checkbox" => "1",
            _ => $"sanitize_text_field( $input['{escapedKey}'] )",
        };
    }

    private static string PhpString(string value) => "'" + EscapeSingleQuotedPhp(value) + "'";

    private static string EscapeSingleQuotedPhp(string value)
    {
        return value.Replace("\\", "\\\\").Replace("'", "\\'");
    }
}
