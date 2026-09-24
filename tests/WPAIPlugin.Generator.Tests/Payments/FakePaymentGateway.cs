using WPAIPlugin.Api.Payments;

namespace WPAIPlugin.Generator.Tests.Payments;

/// <summary>
/// Test double for IPaymentGateway. Never calls a real Stripe API or verifies
/// a real signature - tests set ExpectedSignature/NextEvent to control
/// behaviour deterministically.
/// </summary>
public sealed class FakePaymentGateway : IPaymentGateway
{
    private int _sessionCounter;

    public string ExpectedSignature { get; set; } = "valid-test-signature";

    /// <summary>Set by a test before posting to the webhook endpoint, to control what ParseWebhookEvent returns for a valid signature.</summary>
    public Func<string, PaymentWebhookEvent>? NextEvent { get; set; }

    public string? LastCreatedSessionId { get; private set; }

    public Task<CheckoutSessionResult> CreateCheckoutSessionAsync(
        string userId, CreditPack pack, string packId, string successUrl, string cancelUrl, CancellationToken cancellationToken = default)
    {
        var sessionId = $"cs_test_{Interlocked.Increment(ref _sessionCounter)}_{Guid.NewGuid():N}";
        LastCreatedSessionId = sessionId;
        return Task.FromResult(new CheckoutSessionResult { SessionId = sessionId, CheckoutUrl = $"https://checkout.stripe.example/{sessionId}" });
    }

    public PaymentWebhookEvent ParseWebhookEvent(string payload, string? signatureHeader)
    {
        if (string.IsNullOrEmpty(signatureHeader) || signatureHeader != ExpectedSignature)
        {
            throw new PaymentGatewayException("Invalid test signature.");
        }

        if (NextEvent is null)
        {
            throw new PaymentGatewayException("Malformed test payload.");
        }

        return NextEvent(payload);
    }
}
