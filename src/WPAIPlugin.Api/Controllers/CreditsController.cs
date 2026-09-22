using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using WPAIPlugin.Api.Credits;

namespace WPAIPlugin.Api.Controllers;

/// <summary>
/// Authenticated credit balance endpoint (Milestone 12). Never returns
/// UserId, transaction IDs, internal references, or provider information -
/// only the current user's own balance and the server-configured build costs.
/// </summary>
[ApiController]
[Route("api/credits")]
[Authorize]
public sealed class CreditsController : ControllerBase
{
    private readonly CreditService _creditService;
    private readonly CreditOptions _creditOptions;
    private readonly UserManager<IdentityUser> _userManager;

    public CreditsController(CreditService creditService, IOptions<CreditOptions> creditOptions, UserManager<IdentityUser> userManager)
    {
        _creditService = creditService;
        _creditOptions = creditOptions.Value;
        _userManager = userManager;
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

        return Ok(new
        {
            balance,
            standardBuildCost = _creditOptions.StandardBuildCost,
            validatedBuildCost = _creditOptions.ValidatedBuildCost,
        });
    }
}
