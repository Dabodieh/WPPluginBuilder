namespace WPAIPlugin.Api.Security;

public sealed class SecurityOptions
{
    public int PlanningPerMinute { get; set; } = 10;

    /// <summary>Per-account, in addition to PlanningPerMinute - free planning is the cheapest-to-abuse AI surface (unlike builds, it costs no credits).</summary>
    public int PlanningPerHour { get; set; } = 10;

    /// <summary>Per-account, in addition to PlanningPerMinute/PlanningPerHour.</summary>
    public int PlanningPerDay { get; set; } = 20;

    public int BuildsPerMinute { get; set; } = 6;
    public int ValidatedBuildsPerMinute { get; set; } = 2;
    public int AccountRequestsPerFiveMinutes { get; set; } = 10;
    public int CheckoutPerMinute { get; set; } = 6;
    public int PromoCodePerMinute { get; set; } = 10;

    /// <summary>IP-based (the account may not exist/authenticate) - protects both forgot-password and reset-password against automated abuse/email bombing.</summary>
    public int PasswordRecoveryPerFiveMinutes { get; set; } = 5;

    /// <summary>Per-account (authenticated endpoint only) - protects the resend-verification-email endpoint against email bombing/automation.</summary>
    public int EmailVerificationResendPerFiveMinutes { get; set; } = 3;

    /// <summary>Per-account (authenticated endpoints only) - protects change-password and change-email against credential-stuffing/automation.</summary>
    public int AccountSecurityPerFiveMinutes { get; set; } = 5;
}
