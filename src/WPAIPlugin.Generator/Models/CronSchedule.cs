namespace WPAIPlugin.Generator.Models;

/// <summary>
/// WordPress cron schedule identifiers recognized by the deterministic generator.
/// </summary>
public static class CronSchedule
{
    public const string Hourly = "hourly";

    public const string TwiceDaily = "twicedaily";

    public const string Daily = "daily";

    public static readonly IReadOnlySet<string> Known = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        Hourly,
        TwiceDaily,
        Daily,
    };
}
