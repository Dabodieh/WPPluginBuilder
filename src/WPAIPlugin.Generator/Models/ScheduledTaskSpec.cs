namespace WPAIPlugin.Generator.Models;

/// <summary>
/// Data needed to register a single WordPress cron event on plugin activation.
/// </summary>
public sealed class ScheduledTaskSpec
{
    public string TaskName { get; set; } = string.Empty;

    /// <summary>One of: "hourly", "twicedaily", "daily".</summary>
    public string Schedule { get; set; } = string.Empty;

    /// <summary>WordPress action hook name the cron event fires.</summary>
    public string HookName { get; set; } = string.Empty;
}
