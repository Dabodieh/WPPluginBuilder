namespace WPAIPlugin.Api.Configuration;

/// <summary>
/// One configured support address, shown on the Support page and used for
/// privacy/billing/refund/account-access enquiries alike (ModuleMint
/// branding + account-recovery milestone) - no separate mailbox per concern.
/// </summary>
public sealed class SupportOptions
{
    public const string SectionName = "Support";

    public string? Email { get; set; }
}
