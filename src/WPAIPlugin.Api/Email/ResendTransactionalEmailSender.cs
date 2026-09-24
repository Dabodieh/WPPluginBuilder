using Microsoft.Extensions.Options;
using Resend;
using WPAIPlugin.Api.Configuration;

namespace WPAIPlugin.Api.Email;

/// <summary>
/// The only file in this app that references the Resend SDK directly -
/// mirrors the IPaymentGateway/IPlanningProvider seam pattern already used
/// for Stripe/OpenAI/Anthropic. Server-to-server only; no Resend credential
/// or API response ever reaches the browser. Never logs the email body/reset
/// URL - only a fixed, safe log line.
/// </summary>
public sealed class ResendTransactionalEmailSender(
    IResend resend, IOptions<EmailOptions> emailOptions, ILogger<ResendTransactionalEmailSender> logger)
    : ITransactionalEmailSender
{
    private readonly EmailOptions _emailOptions = emailOptions.Value;

    public async Task SendPasswordResetEmailAsync(string toEmail, string resetUrl, CancellationToken cancellationToken = default)
    {
        var message = new EmailMessage
        {
            From = $"{_emailOptions.FromName} <{_emailOptions.FromAddress}>",
            Subject = "Reset your ModuleMint password",
            TextBody = "A password reset was requested for your ModuleMint account.\n\n"
                + $"Reset your password: {resetUrl}\n\n"
                + "If you didn't request this, you can safely ignore this email - your password will not be changed.\n\n"
                + "This link will expire soon.",
            HtmlBody = "<p>A password reset was requested for your ModuleMint account.</p>"
                + $"<p><a href=\"{System.Net.WebUtility.HtmlEncode(resetUrl)}\">Reset your password</a></p>"
                + "<p>If you didn't request this, you can safely ignore this email - your password will not be changed.</p>"
                + "<p>This link will expire soon.</p>",
        };
        message.To.Add(toEmail);

        try
        {
            await resend.EmailSendAsync(message);
        }
        catch (Exception ex)
        {
            // Never log the recipient-specific reset URL/token - only that
            // sending failed and its exception category.
            logger.LogError("Password reset email send failed. Failure category: {FailureType}.", ex.GetType().Name);
            throw;
        }
    }

    public async Task SendEmailConfirmationAsync(string toEmail, string confirmUrl, CancellationToken cancellationToken = default)
    {
        var message = new EmailMessage
        {
            From = $"{_emailOptions.FromName} <{_emailOptions.FromAddress}>",
            Subject = "Verify your ModuleMint email",
            TextBody = "Verify your email to activate ModuleMint plugin creation.\n\n"
                + $"Verify your email: {confirmUrl}\n\n"
                + "If you didn't create this account, you can safely ignore this email.",
            HtmlBody = "<p>Verify your email to activate ModuleMint plugin creation.</p>"
                + $"<p><a href=\"{System.Net.WebUtility.HtmlEncode(confirmUrl)}\">Verify your email</a></p>"
                + "<p>If you didn't create this account, you can safely ignore this email.</p>",
        };
        message.To.Add(toEmail);

        try
        {
            await resend.EmailSendAsync(message);
        }
        catch (Exception ex)
        {
            // Never log the recipient-specific confirmation URL/token - only
            // that sending failed and its exception category.
            logger.LogError("Email confirmation send failed. Failure category: {FailureType}.", ex.GetType().Name);
            throw;
        }
    }
}
