namespace WPAIPlugin.Api.Payments;

/// <summary>
/// Server-side-only credit pack catalog, bound from configuration section
/// "CreditPacks". The browser may send only a PackId; price, currency, and
/// credit amount are always resolved from this configuration, never trusted
/// from a request body. AmountMinor is an integer minor-currency-unit price
/// (e.g. 499 = £4.99) - never a float/double.
/// </summary>
public sealed class CreditPackOptions
{
    public const string SectionName = "CreditPacks";

    public Dictionary<string, CreditPack> Packs { get; set; } = new();
}

public sealed class CreditPack
{
    public string DisplayName { get; set; } = string.Empty;

    public int Credits { get; set; }

    public int AmountMinor { get; set; }

    public string Currency { get; set; } = "GBP";
}
