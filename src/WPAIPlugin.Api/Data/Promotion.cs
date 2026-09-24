namespace WPAIPlugin.Api.Data;

public static class PromotionType
{
    public const string FreeBuilds = "FreeBuilds";
    public const string BonusCredits = "BonusCredits";
    public const string PackPriceDiscount = "PackPriceDiscount";
}

/// <summary>
/// Who a promotion is offered to, beyond code/pack gating (which are their
/// own fields). Deliberately small - see PromotionService for exact
/// evaluation rules, including the documented limitation on NewRegistrations
/// (ASP.NET Core Identity has no user-creation timestamp, so this is
/// evaluated against CreditAccount.CreatedAtUtc, set once at registration).
/// </summary>
public static class PromotionEligibility
{
    public const string Everyone = "Everyone";
    public const string NewRegistrations = "NewRegistrations";
    public const string FirstPurchaseOnly = "FirstPurchaseOnly";
}

/// <summary>
/// A single promotional offer (Promotions + Free Builds milestone). Small,
/// deliberately generic across the three supported types - see PromotionType.
/// Value's meaning depends on Type: a whole-number percentage (1-100) for
/// PackPriceDiscount, a bonus credit count for BonusCredits, or a free-build
/// count for FreeBuilds. Never a float/double - all monetary/credit
/// arithmetic derived from this is done in integer/decimal.
///
/// State (Draft/Scheduled/Active/Expired) is intentionally not a stored
/// column - see PromotionState, computed from IsEnabled/StartsAtUtc/EndsAtUtc
/// against the current UTC time so it can never drift out of sync with the
/// data that defines it.
/// </summary>
public sealed class Promotion
{
    public Guid Id { get; set; }

    public required string Name { get; set; }

    /// <summary>Normalized to uppercase before storage/lookup. Null for an automatic (no-code) promotion.</summary>
    public string? Code { get; set; }

    public required string Type { get; set; }

    public DateTime StartsAtUtc { get; set; }

    public DateTime? EndsAtUtc { get; set; }

    public bool IsEnabled { get; set; }

    public bool RequiresCode { get; set; }

    /// <summary>Null = applies to every configured credit pack. Only meaningful for BonusCredits/PackPriceDiscount.</summary>
    public string? AppliesToPackId { get; set; }

    public int Value { get; set; }

    public int? MaxRedemptions { get; set; }

    public int? MaxRedemptionsPerUser { get; set; }

    public required string Eligibility { get; set; }

    /// <summary>Higher wins when multiple automatic promotions are eligible for the same pack. Ties broken by CreatedAtUtc then Id.</summary>
    public int Priority { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }
}

/// <summary>Derived, never stored - see Promotion's own doc comment.</summary>
public enum PromotionState
{
    Draft,
    Scheduled,
    Active,
    Expired,
}

public static class PromotionStateResolver
{
    public static PromotionState Resolve(Promotion promotion, DateTime nowUtc) =>
        Resolve(promotion.IsEnabled, promotion.StartsAtUtc, promotion.EndsAtUtc, nowUtc);

    public static PromotionState Resolve(bool isEnabled, DateTime startsAtUtc, DateTime? endsAtUtc, DateTime nowUtc)
    {
        if (!isEnabled) return PromotionState.Draft;
        if (endsAtUtc.HasValue && endsAtUtc.Value <= nowUtc) return PromotionState.Expired;
        if (startsAtUtc > nowUtc) return PromotionState.Scheduled;
        return PromotionState.Active;
    }
}
