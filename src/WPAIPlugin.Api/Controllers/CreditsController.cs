using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using WPAIPlugin.Api.Credits;
using WPAIPlugin.Api.Entitlements;
using WPAIPlugin.Api.Validation;

namespace WPAIPlugin.Api.Controllers;

/// <summary>
/// Authenticated credit balance + free-build entitlement endpoint (Milestone
/// 12; freeBuildsRemaining added in the Promotions + Free Builds milestone -
/// a distinct entitlement from credits, never merged into the balance
/// figure). Never returns UserId, transaction IDs, internal references, or
/// provider information - only the current user's own balance/entitlement
/// and the server-configured build costs.
/// </summary>
[ApiController]
[Route("api/credits")]
[Authorize]
public sealed class CreditsController : ControllerBase
{
    private readonly CreditService _creditService;
    private readonly CreditOptions _creditOptions;
    private readonly BuildEntitlementService _entitlementService;
    private readonly UserManager<IdentityUser> _userManager;
    private readonly ValidationOptions _validationOptions;

    public CreditsController(
        CreditService creditService, IOptions<CreditOptions> creditOptions,
        BuildEntitlementService entitlementService, UserManager<IdentityUser> userManager,
        IOptions<ValidationOptions> validationOptions)
    {
        _creditService = creditService;
        _creditOptions = creditOptions.Value;
        _entitlementService = entitlementService;
        _userManager = userManager;
        _validationOptions = validationOptions.Value;
    }

    [HttpGet]
    public async Task<IActionResult> GetBalance(CancellationToken cancellationToken)
    {
        var userId = _userManager.GetUserId(User);
        if (userId is null)
        {
            return Unauthorized();
        }

        var balance = await _creditService.GetBalanceAsync(userId, cancellationToken);
        var freeBuildsRemaining = await _entitlementService.GetRemainingAsync(userId, cancellationToken);

        return Ok(new
        {
            balance,
            freeBuildsRemaining,
            standardBuildCost = _creditOptions.StandardBuildCost,
            validatedBuildCost = _creditOptions.ValidatedBuildCost,
            validationEnabled = _validationOptions.Enabled,
        });
    }
}
