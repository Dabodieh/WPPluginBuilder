namespace WPAIPlugin.Api.Payments;

/// <summary>
/// Thin seam over the Stripe SDK so tests never need a live Stripe account or
/// network access. The real implementation (StripePaymentGateway) is the only
/// place Stripe.net is used; everything else in this app talks to this
/// interface. Never exposes a secret key or webhook secret through its return
/// types.
/// </summary>
public interface IPaymentGateway
{
    Task<CheckoutSessionResult> CreateCheckoutSessionAsync(
        string userId, CreditPack pack, string packId, string successUrl, string cancelUrl, CancellationToken cancellationToken = default);

    /// <exception cref="PaymentGatewayException">Thrown when the signature is missing/invalid or the payload cannot be parsed.</exception>
    PaymentWebhookEvent ParseWebhookEvent(string payload, string? signatureHeader);
}

public sealed class CheckoutSessionResult
{
    public required string SessionId { get; init; }

    public required string CheckoutUrl { get; init; }
}

public enum PaymentWebhookEventType
{
    Other,
    CheckoutSessionCompleted,
    CheckoutSessionExpired,
    PaymentIntentPaymentFailed,
}

/// <summary>
/// Vendor-neutral shape of a verified webhook event. No raw Stripe SDK type
/// escapes this boundary, matching the pattern used for AI planning providers.
/// </summary>
public sealed class PaymentWebhookEvent
{
    public required string EventId { get; init; }

    public required PaymentWebhookEventType Type { get; init; }

    public string? CheckoutSessionId { get; init; }

    public string? PaymentIntentId { get; init; }

    public bool PaymentSucceeded { get; init; }
}

public sealed class PaymentGatewayException(string message, Exception? innerException = null)
    : Exception(message, innerException);
