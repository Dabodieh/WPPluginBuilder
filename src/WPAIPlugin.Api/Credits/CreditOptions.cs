namespace WPAIPlugin.Api.Credits;

public sealed class CreditOptions
{
    public const string SectionName = "Credits";

    public int SignupGrant { get; set; } = 100;

    public int StandardBuildCost { get; set; } = 1;

    public int ValidatedBuildCost { get; set; } = 2;

    /// <summary>Largest magnitude an admin credit adjustment may have in either direction.</summary>
    public int MaxAdminAdjustmentMagnitude { get; set; } = 10_000;
}
