namespace WPAIPlugin.Api.Data;

public static class PromotionBenefitType
{
    public const string FreeBuilds = "FreeBuilds";
    public const string BonusCredits = "BonusCredits";
    public const string PriceDiscount = "PriceDiscount";
}

/// <summary>
/// Immutable record of one promotion benefit actually granted to one user -
/// never updated or deleted after creation. For a pack-purchase promotion
/// (BonusCredits/PackPriceDiscount), PurchaseId is set and is the redemption's
/// natural dedup key (unique index - a webhook retry can only ever produce
/// one redemption row per Purchase). For a FreeBuilds code redemption there is
/// no Purchase, so PurchaseId is null; per-user/global limits are enforced by
/// counting existing rows for the promotion under a row-level lock on the
/// Promotion itself (see PromotionService.RedeemFreeBuildsCodeAsync), which
/// closes the concurrent-redemption race a plain count check or unique index
/// alone cannot.
/// </summary>
public sealed class PromotionRedemption
{
    public Guid Id { get; set; }

    public required Guid PromotionId { get; set; }

    public required string UserId { get; set; }

    public Guid? PurchaseId { get; set; }

    public required string BenefitType { get; set; }

    /// <summary>Free builds granted, bonus credits granted, or discount amount in minor currency units - meaning depends on BenefitType.</summary>
    public required int BenefitAmount { get; set; }

    public DateTime CreatedAtUtc { get; set; }
}
