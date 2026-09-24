namespace WPAIPlugin.Api.Configuration;

/// <summary>
/// Site-wide configuration (ModuleMint branding + account-recovery milestone).
/// PublicBaseUrl is the only trusted source for building an absolute URL that
/// leaves the server (e.g. a password-reset link) - request Host/scheme/
/// Origin headers are browser-supplied and must never be used for this.
/// </summary>
public sealed class AppOptions
{
    public const string SectionName = "App";

    public string? PublicBaseUrl { get; set; }
}
