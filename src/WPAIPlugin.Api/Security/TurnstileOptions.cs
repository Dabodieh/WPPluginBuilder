namespace WPAIPlugin.Api.Security;

/// <summary>
/// Cloudflare Turnstile configuration for registration (signup-farming
/// hardening milestone). SiteKey is public - safe to serve to registration
/// JS via PublicConfigController. SecretKey is server-only - it must never
/// appear in any HTTP response, static file, log line, or exception message;
/// it is used exclusively by TurnstileVerifier's outbound call to Cloudflare.
/// Disabled by default (Development/test ergonomics); production startup
/// enforces SiteKey/SecretKey are set whenever Enabled is true - see
/// Program.cs's production-only validation block.
/// </summary>
public sealed class TurnstileOptions
{
    public const string SectionName = "SignupProtection:Turnstile";

    public bool Enabled { get; set; }

    public string? SiteKey { get; set; }

    public string? SecretKey { get; set; }
}
