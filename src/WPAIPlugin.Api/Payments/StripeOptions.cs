namespace WPAIPlugin.Api.Payments;

/// <summary>
/// Stripe configuration, bound from configuration section "Stripe". SecretKey
/// and WebhookSecret are server-side only: never returned from any controller,
/// never sent to the browser, never logged. PublishableKey is the one value
/// Stripe itself designs to be public/embeddable, but this app uses Stripe
/// Checkout (redirect-based), so even that is only needed server-side to
/// build the Checkout Session - no client-side Stripe.js is loaded.
/// </summary>
public sealed class StripeOptions
{
    public const string SectionName = "Stripe";

    public string? SecretKey { get; set; }

    public string? WebhookSecret { get; set; }

    /// <summary>Absolute base URL used to build Checkout success/cancel redirect URLs (e.g. https://app.example.com).</summary>
    public string? PublicBaseUrl { get; set; }
}
