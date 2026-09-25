using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using WPAIPlugin.Api.Configuration;
using WPAIPlugin.Api.Payments;
using WPAIPlugin.Api.Promotions;
using WPAIPlugin.Api.Security;

namespace WPAIPlugin.Api.Controllers;

/// <summary>
/// The only non-secret, public-safe configuration values a static page ever
/// needs: the support address (Privacy/Terms/Refunds/Support), the public
/// offer shown on the logged-out homepage (signup free builds and the
/// cheapest pack's base price - never a user-specific promotion), and
/// whether/how the registration page should render Cloudflare Turnstile.
/// TurnstileOptions.SiteKey is the one value here that names a real external
/// service key - it is the public half of a Turnstile pair, designed by
/// Cloudflare to be embedded in page JS; TurnstileOptions.SecretKey is never
/// referenced anywhere in this controller. Never returns anything else
/// resembling a secret/API key.
/// </summary>
[ApiController]
[Route("api/config")]
[AllowAnonymous]
public sealed class PublicConfigController : ControllerBase
{
    private readonly SupportOptions _supportOptions;
    private readonly PromotionsOptions _promotionsOptions;
    private readonly CreditPackOptions _packOptions;
    private readonly TurnstileOptions _turnstileOptions;

    public PublicConfigController(
        IOptions<SupportOptions> supportOptions,
        IOptions<PromotionsOptions> promotionsOptions,
        IOptions<CreditPackOptions> packOptions,
        IOptions<TurnstileOptions> turnstileOptions)
    {
        _supportOptions = supportOptions.Value;
        _promotionsOptions = promotionsOptions.Value;
        _packOptions = packOptions.Value;
        _turnstileOptions = turnstileOptions.Value;
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
            turnstileEnabled = _turnstileOptions.Enabled,
            turnstileSiteKey = _turnstileOptions.Enabled ? _turnstileOptions.SiteKey : null,
        });
    }
}
