namespace WPAIPlugin.Api.Security;

/// <summary>
/// Admin bootstrap configuration, bound from configuration section "Admin".
/// BootstrapEmail is the only user ever automatically granted the Admin role,
/// and only once (see AdminBootstrapper) - it never re-grants after the role
/// has been removed, and never nominates any other user.
/// </summary>
public sealed class AdminOptions
{
    public const string SectionName = "Admin";

    public string? BootstrapEmail { get; set; }
}
