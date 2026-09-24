namespace WPAIPlugin.Api.Controllers;

/// <summary>
/// Response/request bodies for admin promotion management. State is always
/// server-computed (PromotionStateResolver) - never a stored, driftable
/// column. Never expose Stripe secrets or anything beyond what an operator
/// legitimately needs to manage a promotion.
/// </summary>
public class AdminPromotionResponse
{
    public required Guid Id { get; init; }

    public required string Name { get; init; }

    public required string? Code { get; init; }

    public required string Type { get; init; }

    public required DateTime StartsAtUtc { get; init; }

    public required DateTime? EndsAtUtc { get; init; }

    public required bool IsEnabled { get; init; }

    public required bool RequiresCode { get; init; }

    public required string? AppliesToPackId { get; init; }

    public required int Value { get; init; }

    public required int? MaxRedemptions { get; init; }

    public required int? MaxRedemptionsPerUser { get; init; }

    public required string Eligibility { get; init; }

    public required int Priority { get; init; }

    public required string State { get; init; }

    public required int RedemptionCount { get; init; }

    public required DateTime CreatedAtUtc { get; init; }

    public required DateTime UpdatedAtUtc { get; init; }
}

public sealed class AdminPromotionDetailResponse : AdminPromotionResponse
{
    public required int PurchasingCustomers { get; init; }

    public required int CreditsGranted { get; init; }

    public required int FreeBuildsGranted { get; init; }

    public required int DiscountMinorGranted { get; init; }

    public required int RevenueMinor { get; init; }
}

/// <summary>
/// Create/edit request. Server-side validated in full - see
/// AdminPromotionsController.Validate. Value's meaning depends on Type (see
/// Promotion's own doc comment); the browser can propose any values here,
/// but only an admin's authenticated, AdminOnly-gated request can ever reach
/// this endpoint, and every value is re-validated regardless of source.
/// </summary>
public sealed class AdminPromotionRequest
{
    public string? Name { get; set; }

    public string? Code { get; set; }

    public string? Type { get; set; }

    public DateTime StartsAtUtc { get; set; }

    public DateTime? EndsAtUtc { get; set; }

    public bool IsEnabled { get; set; }

    public bool RequiresCode { get; set; }

    public string? AppliesToPackId { get; set; }

    public int Value { get; set; }

    public int? MaxRedemptions { get; set; }

    public int? MaxRedemptionsPerUser { get; set; }

    public string? Eligibility { get; set; }

    public int Priority { get; set; }
}
