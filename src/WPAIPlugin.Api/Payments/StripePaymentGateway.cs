using Microsoft.Extensions.Options;
using Stripe;
using Stripe.Checkout;

namespace WPAIPlugin.Api.Payments;

/// <summary>
/// Real Stripe-backed IPaymentGateway. The only file in this app that
/// references the Stripe SDK directly. Uses Stripe Checkout (Stripe-hosted
/// payment page) - this app never handles raw card details and never builds
/// a custom card form.
/// </summary>
public sealed class StripePaymentGateway : IPaymentGateway
{
    private readonly StripeOptions _options;
    private readonly ILogger<StripePaymentGateway> _logger;

    public StripePaymentGateway(IOptions<StripeOptions> options, ILogger<StripePaymentGateway> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task<CheckoutSessionResult> CreateCheckoutSessionAsync(
        string userId, CreditPack pack, string packId, string successUrl, string cancelUrl, CancellationToken cancellationToken = default)
    {
        var service = new SessionService(new StripeClient(_options.SecretKey));
        var session = await service.CreateAsync(new SessionCreateOptions
        {
            Mode = "payment",
            SuccessUrl = successUrl,
            CancelUrl = cancelUrl,
            ClientReferenceId = userId,
            LineItems =
            [
                new SessionLineItemOptions
                {
                    Quantity = 1,
                    PriceData = new SessionLineItemPriceDataOptions
                    {
                        Currency = pack.Currency,
                        UnitAmount = pack.AmountMinor,
                        ProductData = new SessionLineItemPriceDataProductDataOptions
                        {
                            Name = pack.DisplayName,
                        },
                    },
                },
            ],
            Metadata = new Dictionary<string, string>
            {
                ["userId"] = userId,
                ["packId"] = packId,
                ["credits"] = pack.Credits.ToString(System.Globalization.CultureInfo.InvariantCulture),
            },
        }, cancellationToken: cancellationToken);

        return new CheckoutSessionResult { SessionId = session.Id, CheckoutUrl = session.Url };
    }

    public PaymentWebhookEvent ParseWebhookEvent(string payload, string? signatureHeader)
    {
        if (string.IsNullOrEmpty(signatureHeader))
        {
            throw new PaymentGatewayException("Missing Stripe signature header.");
        }

        Event stripeEvent;
        try
        {
            // Never logs the webhook secret or the raw payload - only the safe,
            // already-verified event type/ID are ever surfaced to callers.
            stripeEvent = EventUtility.ConstructEvent(payload, signatureHeader, _options.WebhookSecret);
        }
        catch (StripeException ex)
        {
            _logger.LogWarning("Stripe webhook signature verification failed. Failure category: {FailureType}.", ex.GetType().Name);
            throw new PaymentGatewayException("Stripe webhook signature verification failed.", ex);
        }

        return stripeEvent.Type switch
        {
            "checkout.session.completed" => FromCheckoutSession(stripeEvent, PaymentWebhookEventType.CheckoutSessionCompleted, succeeded: true),
            "checkout.session.expired" => FromCheckoutSession(stripeEvent, PaymentWebhookEventType.CheckoutSessionExpired, succeeded: false),
            "payment_intent.payment_failed" => FromPaymentIntent(stripeEvent),
            _ => new PaymentWebhookEvent { EventId = stripeEvent.Id, Type = PaymentWebhookEventType.Other, PaymentSucceeded = false },
        };
    }

    private static PaymentWebhookEvent FromCheckoutSession(Event stripeEvent, PaymentWebhookEventType type, bool succeeded)
    {
        var session = stripeEvent.Data.Object as Session;
        return new PaymentWebhookEvent
        {
            EventId = stripeEvent.Id,
            Type = type,
            CheckoutSessionId = session?.Id,
            PaymentIntentId = session?.PaymentIntentId,
            PaymentSucceeded = succeeded && session?.PaymentStatus == "paid",
        };
    }

    private static PaymentWebhookEvent FromPaymentIntent(Event stripeEvent)
    {
        var paymentIntent = stripeEvent.Data.Object as PaymentIntent;
        return new PaymentWebhookEvent
        {
            EventId = stripeEvent.Id,
            Type = PaymentWebhookEventType.PaymentIntentPaymentFailed,
            PaymentIntentId = paymentIntent?.Id,
            PaymentSucceeded = false,
        };
    }
}
