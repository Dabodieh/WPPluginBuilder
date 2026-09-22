using System.Text;

namespace WPAIPlugin.Templates;

/// <summary>
/// Renders a basic WordPress meta box with custom fields attached to a post type,
/// including nonce verification, capability checks, autosave protection,
/// sanitization on save, and escaped output on render.
/// Supports field types: text, textarea, checkbox.
/// </summary>
public static class CustomFieldsTemplate
{
    /// <param name="slug">Plugin slug, used to derive nonce action/name and PHP function names.</param>
    /// <param name="postType">Custom post type slug the meta box attaches to.</param>
    /// <param name="fields">Field key, label, and type ("text"/"textarea"/"checkbox").</param>
    public static string Render(string slug, string postType, IReadOnlyList<(string Key, string Label, string Type)> fields)
    {
        var phpPrefix = slug.Replace('-', '_');
        var nonceAction = $"{phpPrefix}_custom_fields_save";
        var nonceName = $"{phpPrefix}_custom_fields_nonce";
        var metaBoxId = $"{phpPrefix}_custom_fields";
        var addMetaBoxFunctionName = $"{phpPrefix}_add_custom_fields_meta_box";
        var renderFunctionName = $"{phpPrefix}_render_custom_fields_meta_box";
        var saveFunctionName = $"{phpPrefix}_save_custom_fields";

        var sb = new StringBuilder();

        sb.Append("// Custom Fields\n");

        // Meta box registration.
        sb.Append("function ").Append(addMetaBoxFunctionName).Append("() {\n");
        sb.Append("\tadd_meta_box(\n");
        sb.Append("\t\t").Append(PhpString(metaBoxId)).Append(",\n");
        sb.Append("\t\t").Append(PhpString("Details")).Append(",\n");
        sb.Append("\t\t'").Append(renderFunctionName).Append("',\n");
        sb.Append("\t\t").Append(PhpString(postType)).Append("\n");
        sb.Append("\t);\n");
        sb.Append("}\n");
        sb.Append("add_action( 'add_meta_boxes', '").Append(addMetaBoxFunctionName).Append("' );\n\n");

        // Render callback.
        sb.Append("function ").Append(renderFunctionName).Append("( $post ) {\n");
        sb.Append("\twp_nonce_field( ").Append(PhpString(nonceAction)).Append(", ").Append(PhpString(nonceName)).Append(" );\n");
        foreach (var field in fields)
        {
            var metaKey = $"_{phpPrefix}_{field.Key}";
            sb.Append("\t$value = get_post_meta( $post->ID, ").Append(PhpString(metaKey)).Append(", true );\n");
            sb.Append("\techo '<p><label for=\"").Append(EscapeSingleQuotedPhp(field.Key)).Append("\">' . esc_html__( '").Append(EscapeSingleQuotedPhp(field.Label)).Append("' ) . '</label><br />';\n");
            sb.Append(RenderFieldControl(field.Key, field.Type));
            sb.Append("\techo '</p>';\n");
        }
        sb.Append("}\n\n");

        // Save callback: nonce verification, autosave protection, capability check, sanitize.
        sb.Append("function ").Append(saveFunctionName).Append("( $post_id ) {\n");
        sb.Append("\tif ( ! isset( $_POST['").Append(nonceName).Append("'] ) || ! wp_verify_nonce( $_POST['").Append(nonceName).Append("'], ").Append(PhpString(nonceAction)).Append(" ) ) {\n");
        sb.Append("\t\treturn;\n");
        sb.Append("\t}\n\n");
        sb.Append("\tif ( defined( 'DOING_AUTOSAVE' ) && DOING_AUTOSAVE ) {\n");
        sb.Append("\t\treturn;\n");
        sb.Append("\t}\n\n");
        sb.Append("\tif ( ! current_user_can( 'edit_post', $post_id ) ) {\n");
        sb.Append("\t\treturn;\n");
        sb.Append("\t}\n\n");
        foreach (var field in fields)
        {
            var metaKey = $"_{phpPrefix}_{field.Key}";
            sb.Append("\tif ( isset( $_POST['").Append(EscapeSingleQuotedPhp(field.Key)).Append("'] ) ) {\n");
            sb.Append("\t\tupdate_post_meta( $post_id, ").Append(PhpString(metaKey)).Append(", ").Append(SanitizeExpression(field.Type, field.Key)).Append(" );\n");
            sb.Append("\t} else {\n");
            sb.Append("\t\tupdate_post_meta( $post_id, ").Append(PhpString(metaKey)).Append(", ").Append(field.Type == "checkbox" ? "0" : "''").Append(" );\n");
            sb.Append("\t}\n");
        }
        sb.Append("}\n");
        sb.Append("add_action( 'save_post_").Append(postType).Append("', '").Append(saveFunctionName).Append("' );\n");

        return sb.ToString();
    }

    private static string RenderFieldControl(string key, string type)
    {
        var escapedKey = EscapeSingleQuotedPhp(key);

        return type switch
        {
            "textarea" =>
                $"\techo '<textarea name=\"{escapedKey}\" rows=\"4\" cols=\"40\">' . esc_textarea( $value ) . '</textarea>';\n",
            "checkbox" =>
                $"\techo '<input type=\"checkbox\" name=\"{escapedKey}\" value=\"1\"' . checked( 1, $value, false ) . ' />';\n",
            _ =>
                $"\techo '<input type=\"text\" name=\"{escapedKey}\" value=\"' . esc_attr( $value ) . '\" class=\"widefat\" />';\n",
        };
    }

    private static string SanitizeExpression(string type, string key)
    {
        var escapedKey = EscapeSingleQuotedPhp(key);

        return type switch
        {
            "textarea" => $"sanitize_textarea_field( $_POST['{escapedKey}'] )",
            "checkbox" => "1",
            _ => $"sanitize_text_field( $_POST['{escapedKey}'] )",
        };
    }

    private static string PhpString(string value) => "'" + EscapeSingleQuotedPhp(value) + "'";

    private static string EscapeSingleQuotedPhp(string value)
    {
        return value.Replace("\\", "\\\\").Replace("'", "\\'");
    }
}
