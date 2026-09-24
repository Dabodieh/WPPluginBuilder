using WPAIPlugin.Api.Email;

namespace WPAIPlugin.Generator.Tests.Accounts;

/// <summary>
/// Test double for ITransactionalEmailSender - records every call instead of
/// sending anything, so tests can inspect recipient/reset URL without Resend
/// or internet access.
/// </summary>
public sealed class FakeTransactionalEmailSender : ITransactionalEmailSender
{
    public List<(string ToEmail, string ResetUrl)> PasswordResetsSent { get; } = [];

    public List<(string ToEmail, string ConfirmUrl)> EmailConfirmationsSent { get; } = [];

    public Task SendPasswordResetEmailAsync(string toEmail, string resetUrl, CancellationToken cancellationToken = default)
    {
        PasswordResetsSent.Add((toEmail, resetUrl));
        return Task.CompletedTask;
    }

    public Task SendEmailConfirmationAsync(string toEmail, string confirmUrl, CancellationToken cancellationToken = default)
    {
        EmailConfirmationsSent.Add((toEmail, confirmUrl));
        return Task.CompletedTask;
    }
}
