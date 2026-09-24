using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using WPAIPlugin.Api.Configuration;
using WPAIPlugin.Api.Payments;
using WPAIPlugin.Api.Promotions;

namespace WPAIPlugin.Api.Controllers;

/// <summary>
/// The only non-secret, public-safe configuration values a static page ever
/// needs: the support address (Privacy/Terms/Refunds/Support) and the public
/// offer shown on the logged-out homepage (signup free builds and the
/// cheapest pack's base price - never a user-specific promotion). Never
/// returns anything resembling a secret/API key.
/// </summary>
[ApiController]
[Route("api/config")]
[AllowAnonymous]
public sealed class PublicConfigController : ControllerBase
{
    private readonly SupportOptions _supportOptions;
    private readonly PromotionsOptions _promotionsOptions;
    private readonly CreditPackOptions _packOptions;

    public PublicConfigController(
        IOptions<SupportOptions> supportOptions,
        IOptions<PromotionsOptions> promotionsOptions,
        IOptions<CreditPackOptions> packOptions)
    {
        _supportOptions = supportOptions.Value;
        _promotionsOptions = promotionsOptions.Value;
        _packOptions = packOptions.Value;
    }

    [HttpGet("public")]
    public IActionResult Public()
    {
        var startingPack = _packOptions.Packs.Values
            .OrderBy(p => p.AmountMinor)
            .Select(p => new { credits = p.Credits, amountMinor = p.AmountMinor, currency = p.Currency })
            .FirstOrDefault();

        return Ok(new
        {
            supportEmail = _supportOptions.Email,
            signupFreeBuilds = _promotionsOptions.SignupFreeBuilds,
            startingPack,
        });
    }
}
