namespace WPAIPlugin.Api.Promotions;

/// <summary>
/// The launch-critical new-account offer is a plain config value, not a
/// Promotion record - registration reliability must never depend on an
/// admin-created promotion existing/being enabled. The general Promotion
/// system (see PromotionService) may separately layer temporary signup
/// overrides on top of this later; it does not replace it.
/// </summary>
public sealed class PromotionsOptions
{
    public const string SectionName = "Promotions";

    public int SignupFreeBuilds { get; set; } = 2;
}
