namespace WPAIPlugin.Api.Data;

/// <summary>
/// One row per successfully processed Stripe webhook event ID (Milestone 16).
/// Stripe's own event ID is globally unique and stable across retries, so
/// recording it here before acting on an event makes webhook handling
/// idempotent - a duplicate delivery of the same event is a no-op rather than
/// a second credit grant. Never logs the webhook payload or secret.
/// </summary>
public sealed class ProcessedPaymentEvent
{
    public required string ProviderEventId { get; set; }

    public required string EventType { get; set; }

    public DateTime ProcessedAtUtc { get; set; }
}
