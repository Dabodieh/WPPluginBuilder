using WPAIPlugin.Api.Data;

namespace WPAIPlugin.Api.AiUsage;

/// <summary>
/// Writes one immutable AiUsageEvent row per AI provider request. Cost is
/// computed once, at record time, from server-side pricing configuration -
/// never recalculated later, never influenced by the browser.
/// </summary>
public sealed class AiUsageRecorder(AppDbContext db, Microsoft.Extensions.Options.IOptions<AiPricingOptions> pricingOptions)
{
    private readonly AiPricingOptions _pricing = pricingOptions.Value;

    public async Task RecordSuccessAsync(
        string? userId, string operationType, string provider, string? model,
        int? inputTokens, int? outputTokens, int? totalTokens, long durationMs,
        CancellationToken cancellationToken = default)
    {
        db.AiUsageEvents.Add(new AiUsageEvent
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            OperationType = operationType,
            Provider = provider,
            Model = model,
            InputTokens = inputTokens,
            OutputTokens = outputTokens,
            TotalTokens = totalTokens,
            EstimatedCostUsdMicros = EstimateCostUsdMicros(provider, inputTokens, outputTokens),
            DurationMs = durationMs,
            Succeeded = true,
            CreatedAtUtc = DateTime.UtcNow,
        });
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task RecordFailureAsync(
        string? userId, string operationType, string provider, string? model,
        string? failureCategory, long durationMs, CancellationToken cancellationToken = default,
        int? inputTokens = null, int? outputTokens = null, int? totalTokens = null)
    {
        db.AiUsageEvents.Add(new AiUsageEvent
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            OperationType = operationType,
            Provider = provider,
            Model = model,
            InputTokens = inputTokens,
            OutputTokens = outputTokens,
            TotalTokens = totalTokens,
            // A "failed" outcome can still carry a real, costed provider call
            // (e.g. an out-of-scope rejection the model still produced tokens
            // for) - cost is estimated the same way a success would be,
            // never fabricated when tokens are unavailable (see helper below).
            EstimatedCostUsdMicros = EstimateCostUsdMicros(provider, inputTokens, outputTokens),
            DurationMs = durationMs,
            Succeeded = false,
            FailureCategory = failureCategory,
            CreatedAtUtc = DateTime.UtcNow,
        });
        await db.SaveChangesAsync(cancellationToken);
    }

    private long? EstimateCostUsdMicros(string provider, int? inputTokens, int? outputTokens)
    {
        if (inputTokens is null || outputTokens is null)
        {
            return null;
        }

        var pricing = provider.Equals("anthropic", StringComparison.OrdinalIgnoreCase) ? _pricing.Anthropic
            : provider.Equals("openai", StringComparison.OrdinalIgnoreCase) ? _pricing.OpenAI
            : null;
        if (pricing is null)
        {
            return null;
        }

        var inputCost = inputTokens.Value * pricing.InputUsdMicrosPerMillionTokens / 1_000_000m;
        var outputCost = outputTokens.Value * pricing.OutputUsdMicrosPerMillionTokens / 1_000_000m;
        return (long)Math.Round(inputCost + outputCost, MidpointRounding.AwayFromZero);
    }
}
