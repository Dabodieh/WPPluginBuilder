using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using WPAIPlugin.Api.Data;
using WPAIPlugin.Api.Entitlements;
using WPAIPlugin.Api.Promotions;

namespace WPAIPlugin.Api.Controllers;

/// <summary>
/// Customer-facing promo-code redemption for FreeBuilds promotions
/// (Promotions + Free Builds milestone). PackPriceDiscount/BonusCredits
/// promo-code resolution lives on PaymentsController.ResolveCode, since
/// those are always pack-checkout-scoped; a FreeBuilds code has no pack or
/// Checkout involved at all, so it is redeemed immediately here instead.
/// </summary>
[ApiController]
[Route("api/promotions")]
[Authorize]
public sealed class PromotionsController : ControllerBase
{
    private readonly UserManager<IdentityUser> _userManager;
    private readonly PromotionService _promotionService;
    private readonly BuildEntitlementService _entitlementService;

    public PromotionsController(
        UserManager<IdentityUser> userManager, PromotionService promotionService, BuildEntitlementService entitlementService)
    {
        _userManager = userManager;
        _promotionService = promotionService;
        _entitlementService = entitlementService;
    }

    [HttpPost("redeem")]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(1024)]
    [EnableRateLimiting("promoCode")]
    [ProducesResponseType(typeof(RedeemPromotionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Redeem([FromBody] RedeemPromotionRequest request, CancellationToken cancellationToken)
    {
        var userId = _userManager.GetUserId(User);
        if (userId is null)
        {
            return Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(request.Code))
        {
            return BadRequest(new { error = "Invalid or expired code." });
        }

        var promotion = await _promotionService.FindValidFreeBuildsCodeAsync(userId, request.Code, cancellationToken);
        if (promotion is null)
        {
            return BadRequest(new { error = "Invalid or expired code." });
        }

        var reference = $"promotion:{Guid.NewGuid()}";
        await _entitlementService.GrantAsync(
            userId, promotion.Value, BuildEntitlementTransactionType.PromotionGrant, reference, promotion.Id, cancellationToken);
        await _promotionService.RecordRedemptionAsync(
            promotion.Id, userId, purchaseId: null, PromotionBenefitType.FreeBuilds, promotion.Value, cancellationToken);

        return Ok(new RedeemPromotionResponse
        {
            FreeBuildsGranted = promotion.Value,
            FreeBuildsRemaining = await _entitlementService.GetRemainingAsync(userId, cancellationToken),
        });
    }
}

public sealed class RedeemPromotionRequest
{
    public string? Code { get; set; }
}

public sealed class RedeemPromotionResponse
{
    public required int FreeBuildsGranted { get; init; }

    public required int FreeBuildsRemaining { get; init; }
}
