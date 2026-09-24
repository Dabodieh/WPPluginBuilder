namespace WPAIPlugin.Api.Email;

/// <summary>
/// Used only when Resend:ApiKey is not configured (Development, or any
/// deployment that hasn't opted into email yet). Never sends anything and,
/// per this milestone's explicit requirement, never logs the reset token or
/// the complete reset URL - only a fixed, safe line confirming a reset was
/// requested. Production requires Resend:ApiKey (see Program.cs's startup
/// validation), so this implementation is never selected outside Development
/// once deployed correctly.
/// </summary>
public sealed class NoOpTransactionalEmailSender(ILogger<NoOpTransactionalEmailSender> logger) : ITransactionalEmailSender
{
    public Task SendPasswordResetEmailAsync(string toEmail, string resetUrl, CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Password reset requested but no transactional email sender is configured (Resend:ApiKey is blank) - no email was sent.");
        return Task.CompletedTask;
    }

    public Task SendEmailConfirmationAsync(string toEmail, string confirmUrl, CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Email confirmation requested but no transactional email sender is configured (Resend:ApiKey is blank) - no email was sent.");
        return Task.CompletedTask;
    }

    public Task SendEmailChangeConfirmationAsync(string toEmail, string confirmUrl, CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Email change confirmation requested but no transactional email sender is configured (Resend:ApiKey is blank) - no email was sent.");
        return Task.CompletedTask;
    }
}
