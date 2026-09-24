namespace WPAIPlugin.Api.Email;

/// <summary>
/// Transactional account email only (account-recovery milestone) - never
/// newsletters, marketing campaigns, mailing lists, or promotional
/// automation. Deliberately narrow: one method for the one transactional
/// message this application currently sends. ResendTransactionalEmailSender
/// is the only production implementation; NoOpTransactionalEmailSender covers
/// Development/unconfigured deployments; tests use their own fake (see
/// FakeTransactionalEmailSender) - none of them ever require Resend or
/// internet access.
/// </summary>
public interface ITransactionalEmailSender
{
    Task SendPasswordResetEmailAsync(string toEmail, string resetUrl, CancellationToken cancellationToken = default);

    Task SendEmailConfirmationAsync(string toEmail, string confirmUrl, CancellationToken cancellationToken = default);

    /// <summary>Sent to the NEW address only, as part of Identity's change-email token flow - never overwrites the login email until this link is confirmed.</summary>
    Task SendEmailChangeConfirmationAsync(string toEmail, string confirmUrl, CancellationToken cancellationToken = default);
}
