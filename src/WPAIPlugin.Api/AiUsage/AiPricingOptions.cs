namespace WPAIPlugin.Api.AiUsage;

/// <summary>
/// Server-side AI provider pricing, bound from configuration section
/// "AiPricing". The browser never supplies or influences pricing. Prices are
/// USD micros (1,000,000 = $1.00) per million tokens, matching common vendor
/// pricing pages, converted to a per-token estimate at usage-recording time.
/// Values are this application's own estimate of provider cost unless an
/// authoritative billing API says otherwise.
/// </summary>
public sealed class AiPricingOptions
{
    public const string SectionName = "AiPricing";

    public ProviderPricing Anthropic { get; set; } = new();

    public ProviderPricing OpenAI { get; set; } = new();
}

public sealed class ProviderPricing
{
    public long InputUsdMicrosPerMillionTokens { get; set; }

    public long OutputUsdMicrosPerMillionTokens { get; set; }
}
