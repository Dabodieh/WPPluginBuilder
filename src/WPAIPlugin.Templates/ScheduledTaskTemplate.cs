using System.Text;

namespace WPAIPlugin.Templates;

/// <summary>
/// Renders a basic WordPress cron event: scheduled on activation, cleared on
/// deactivation, with a deterministic placeholder callback. Supports schedules:
/// hourly, twicedaily, daily.
/// </summary>
public static class ScheduledTaskTemplate
{
    /// <param name="slug">Plugin slug, used to derive PHP function names.</param>
    /// <param name="taskName">Human-readable task name, used in the placeholder callback comment.</param>
    /// <param name="schedule">One of: "hourly", "twicedaily", "daily".</param>
    /// <param name="hookName">WordPress action hook name the cron event fires.</param>
    public static string Render(string slug, string taskName, string schedule, string hookName)
    {
        var phpPrefix = slug.Replace('-', '_');
        var activateFunctionName = $"{phpPrefix}_schedule_{hookName}";
        var deactivateFunctionName = $"{phpPrefix}_unschedule_{hookName}";
        var callbackFunctionName = $"{phpPrefix}_run_{hookName}";

        var sb = new StringBuilder();

        sb.Append("// Scheduled Task: ").Append(EscapeComment(taskName)).Append('\n');

        // Activation: register the schedule if not already scheduled.
        sb.Append("function ").Append(activateFunctionName).Append("() {\n");
        sb.Append("\tif ( ! wp_next_scheduled( '").Append(hookName).Append("' ) ) {\n");
        sb.Append("\t\twp_schedule_event( time(), '").Append(schedule).Append("', '").Append(hookName).Append("' );\n");
        sb.Append("\t}\n");
        sb.Append("}\n");
        sb.Append("register_activation_hook( __FILE__, '").Append(activateFunctionName).Append("' );\n\n");

        // Deactivation: clear the scheduled event.
        sb.Append("function ").Append(deactivateFunctionName).Append("() {\n");
        sb.Append("\t$timestamp = wp_next_scheduled( '").Append(hookName).Append("' );\n");
        sb.Append("\tif ( $timestamp ) {\n");
        sb.Append("\t\twp_unschedule_event( $timestamp, '").Append(hookName).Append("' );\n");
        sb.Append("\t}\n");
        sb.Append("}\n");
        sb.Append("register_deactivation_hook( __FILE__, '").Append(deactivateFunctionName).Append("' );\n\n");

        // Callback: deterministic placeholder only. No AI-generated code runs here.
        sb.Append("function ").Append(callbackFunctionName).Append("() {\n");
        sb.Append("\t// Placeholder: implement the '").Append(EscapeComment(taskName)).Append("' task here.\n");
        sb.Append("}\n");
        sb.Append("add_action( '").Append(hookName).Append("', '").Append(callbackFunctionName).Append("' );\n");

        return sb.ToString();
    }

    private static string EscapeComment(string value)
    {
        // Placeholder task name is rendered only inside PHP // comments; strip
        // newlines so a value cannot break out of the comment onto a new line.
        return value.Replace("\r", " ").Replace("\n", " ").Trim();
    }
}
