using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using WPAIPlugin.Api.Data;
using WPAIPlugin.Api.Payments;
using WPAIPlugin.Api.Promotions;

namespace WPAIPlugin.Api.Controllers;

/// <summary>
/// Stripe Checkout credit purchases (Milestone 16). The browser may only ever
/// send a PackId - price, currency, and credit amount are always resolved
/// server-side from CreditPackOptions. Credits are granted exclusively by the
/// webhook endpoint after Stripe signature verification; the browser success
/// redirect never grants credits, and PackReturn only re-reads the
/// authoritative server balance/purchase status.
/// </summary>
[ApiController]
[Route("api/payments")]
public sealed class PaymentsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly UserManager<IdentityUser> _userManager;
    private readonly IPaymentGateway _gateway;
    private readonly PurchaseService _purchaseService;
    private readonly PromotionService _promotionService;
    private readonly CreditPackOptions _packOptions;
    private readonly StripeOptions _stripeOptions;
    private readonly ILogger<PaymentsController> _logger;

    public PaymentsController(
        AppDbContext db,
        UserManager<IdentityUser> userManager,
        IPaymentGateway gateway,
        PurchaseService purchaseService,
        PromotionService promotionService,
        IOptions<CreditPackOptions> packOptions,
        IOptions<StripeOptions> stripeOptions,
        ILogger<PaymentsController> logger)
    {
        _db = db;
        _userManager = userManager;
        _gateway = gateway;
        _purchaseService = purchaseService;
        _promotionService = promotionService;
        _packOptions = packOptions.Value;
        _stripeOptions = stripeOptions.Value;
        _logger = logger;
    }

    /// <summary>
    /// Includes each pack's currently-resolved automatic-promotion pricing
    /// (no code) so the billing page can show an active deal without the
    /// customer typing anything - the server determines whether a sale is
    /// active, never the browser/JavaScript.
    /// </summary>
    [HttpGet("packs")]
    [Authorize]
    [ProducesResponseType(typeof(IReadOnlyList<CreditPackResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Packs(CancellationToken cancellationToken)
    {
        var userId = _userManager.GetUserId(User);
        if (userId is null)
        {
            return Unauthorized();
        }

        var response = new List<CreditPackResponse>();
        foreach (var (packId, pack) in _packOptions.Packs)
        {
            var promotion = await _promotionService.FindBestAutomaticPromotionAsync(userId, packId, cancellationToken);
            var resolution = _promotionService.Apply(pack, promotion);
            response.Add(new CreditPackResponse
            {
                PackId = packId, DisplayName = pack.DisplayName, Credits = pack.Credits,
                AmountMinor = pack.AmountMinor, Currency = pack.Currency,
                DiscountedAmountMinor = resolution.PaidAmountMinor != pack.AmountMinor ? resolution.PaidAmountMinor : null,
                BonusCredits = resolution.BonusCredits > 0 ? resolution.BonusCredits : null,
                PromotionName = promotion?.Name,
            });
        }
        return Ok(response);
    }

    /// <summary>Resolves a specific promo code against a pack without creating anything - used by the billing page's "Apply code" preview before the customer confirms checkout.</summary>
    [HttpPost("resolve-code")]
    [Authorize]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(1024)]
    [EnableRateLimiting("promoCode")]
    [ProducesResponseType(typeof(CreditPackResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ResolveCode([FromBody] ResolveCodeRequest request, CancellationToken cancellationToken)
    {
        var userId = _userManager.GetUserId(User);
        if (userId is null)
        {
            return Unauthorized();
        }

        if (request.PackId is null || !_packOptions.Packs.TryGetValue(request.PackId, out var pack))
        {
            return BadRequest(new { error = "Unknown credit pack." });
        }

        PromotionResolution resolution;
        try
        {
            resolution = await _promotionService.ResolveForPackAsync(userId, request.PackId, request.Code, cancellationToken);
        }
        catch (PromotionCodeException ex)
        {
            return BadRequest(new { error = ex.Message });
        }

        return Ok(new CreditPackResponse
        {
            PackId = request.PackId, DisplayName = pack.DisplayName, Credits = pack.Credits,
            AmountMinor = pack.AmountMinor, Currency = pack.Currency,
            DiscountedAmountMinor = resolution.PaidAmountMinor != pack.AmountMinor ? resolution.PaidAmountMinor : null,
            BonusCredits = resolution.BonusCredits > 0 ? resolution.BonusCredits : null,
            PromotionName = resolution.Promotion?.Name,
        });
    }

    [Authorize]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(1024)]
    [EnableRateLimiting("checkout")]
    [HttpPost("checkout")]
    [ProducesResponseType(typeof(CheckoutResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Checkout([FromBody] CheckoutRequest request, CancellationToken cancellationToken)
    {
        var userId = _userManager.GetUserId(User);
        if (userId is null)
        {
            return Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(_stripeOptions.SecretKey) || string.IsNullOrWhiteSpace(_stripeOptions.PublicBaseUrl))
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = "Credit purchases are currently unavailable." });
        }

        // Server determines the pack's price/currency/credits from
        // configuration by PackId alone - nothing else from the request body
        // is ever trusted for these values.
        if (request.PackId is null || !_packOptions.Packs.TryGetValue(request.PackId, out var pack))
        {
            return BadRequest(new { error = "Unknown credit pack." });
        }

        // Resolves at most one promotion (explicit code always wins over an
        // automatic deal) into final commercial terms - the browser can never
        // set a discount, bonus, or final price itself.
        PromotionResolution resolution;
        try
        {
            resolution = await _promotionService.ResolveForPackAsync(userId, request.PackId, request.PromoCode, cancellationToken);
        }
        catch (PromotionCodeException ex)
        {
            return BadRequest(new { error = ex.Message });
        }

        var baseUrl = _stripeOptions.PublicBaseUrl.TrimEnd('/');
        CheckoutSessionResult session;
        try
        {
            // Stripe only ever sees the resolved (possibly discounted) price -
            // never the browser, never an unresolved catalog price when a
            // discount applies. Only AmountMinor is overridden; Credits/
            // DisplayName/Currency are unchanged from the configured pack.
            var resolvedPack = new CreditPack
            {
                DisplayName = pack.DisplayName, Credits = pack.Credits,
                AmountMinor = resolution.PaidAmountMinor, Currency = pack.Currency,
            };
            session = await _gateway.CreateCheckoutSessionAsync(
                userId, resolvedPack, request.PackId,
                successUrl: $"{baseUrl}/billing.html?checkout=success",
                cancelUrl: $"{baseUrl}/billing.html?checkout=cancelled",
                cancellationToken);
        }
        catch (PaymentGatewayException ex)
        {
            _logger.LogError("Stripe checkout session creation failed. Failure category: {FailureType}.", ex.GetType().Name);
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = "Credit purchases are currently unavailable." });
        }

        await _purchaseService.CreatePendingAsync(userId, request.PackId, pack, session.SessionId, resolution, cancellationToken);

        return Ok(new CheckoutResponse { CheckoutUrl = session.CheckoutUrl });
    }

    /// <summary>
    /// Stripe webhook receiver. Anonymous (Stripe cannot present a login
    /// session or CSRF token) but every event is rejected unless its
    /// signature verifies against the server-side webhook secret. This is
    /// the only code path that ever grants purchase credits.
    /// </summary>
    [AllowAnonymous]
    [HttpPost("webhook")]
    [RequestSizeLimit(64 * 1024)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Webhook(CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(Request.Body);
        var payload = await reader.ReadToEndAsync(cancellationToken);
        var signatureHeader = Request.Headers["Stripe-Signature"].FirstOrDefault();

        PaymentWebhookEvent webhookEvent;
        try
        {
            webhookEvent = _gateway.ParseWebhookEvent(payload, signatureHeader);
        }
        catch (PaymentGatewayException)
        {
            // Never echo the payload or signature back - just reject.
            return BadRequest();
        }

        if (!await _purchaseService.TryRecordEventAsync(webhookEvent.EventId, webhookEvent.Type.ToString(), cancellationToken))
        {
            // Already processed - webhook retries must be safe no-ops.
            return Ok();
        }

        switch (webhookEvent.Type)
        {
            case PaymentWebhookEventType.CheckoutSessionCompleted when webhookEvent.PaymentSucceeded && webhookEvent.CheckoutSessionId is not null:
                await _purchaseService.CompletePurchaseAsync(webhookEvent.CheckoutSessionId, webhookEvent.PaymentIntentId, cancellationToken);
                break;
            case PaymentWebhookEventType.CheckoutSessionExpired when webhookEvent.CheckoutSessionId is not null:
                await _purchaseService.MarkFailedOrCancelledAsync(webhookEvent.CheckoutSessionId, PurchaseStatus.Cancelled, cancellationToken);
                break;
            case PaymentWebhookEventType.PaymentIntentPaymentFailed:
                // No CheckoutSessionId is available on a payment_intent event;
                // the Pending purchase is left as-is and will be marked
                // Cancelled if/when Stripe also sends checkout.session.expired.
                break;
        }

        return Ok();
    }

    [Authorize]
    [HttpGet("history")]
    [ProducesResponseType(typeof(IReadOnlyList<PurchaseHistoryResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> History(CancellationToken cancellationToken)
    {
        var userId = _userManager.GetUserId(User);
        if (userId is null)
        {
            return Unauthorized();
        }

        var purchases = await _db.Purchases
            .Where(p => p.UserId == userId)
            .OrderByDescending(p => p.CreatedAtUtc)
            .Take(100)
            .Select(p => new PurchaseHistoryResponse
            {
                CreatedAtUtc = p.CreatedAtUtc,
                PackId = p.PackId,
                AmountMinor = p.AmountMinor,
                Currency = p.Currency,
                CreditsPurchased = p.CreditsPurchased,
                BonusCredits = p.BonusCredits,
                PromotionNameSnapshot = p.PromotionNameSnapshot,
                Status = p.Status,
            })
            .ToListAsync(cancellationToken);

        return Ok(purchases);
    }
}

public sealed class CheckoutRequest
{
    public string? PackId { get; set; }

    /// <summary>Optional promo code. An invalid/ineligible code is rejected outright - never silently ignored in favour of an automatic deal.</summary>
    public string? PromoCode { get; set; }
}

public sealed class CheckoutResponse
{
    public required string CheckoutUrl { get; init; }
}

public sealed class ResolveCodeRequest
{
    public string? PackId { get; set; }

    public string? Code { get; set; }
}

public sealed class CreditPackResponse
{
    public required string PackId { get; init; }

    public required string DisplayName { get; init; }

    public required int Credits { get; init; }

    public required int AmountMinor { get; init; }

    public required string Currency { get; init; }

    /// <summary>Set only when a currently-applicable promotion discounts this pack's price.</summary>
    public int? DiscountedAmountMinor { get; init; }

    /// <summary>Set only when a currently-applicable promotion adds bonus credits to this pack.</summary>
    public int? BonusCredits { get; init; }

    /// <summary>Name of the applied/applicable promotion, if any - safe to show, never an internal ID.</summary>
    public string? PromotionName { get; init; }
}

public sealed class PurchaseHistoryResponse
{
    public required DateTime CreatedAtUtc { get; init; }

    public required string PackId { get; init; }

    public required int AmountMinor { get; init; }

    public required string Currency { get; init; }

    public required int CreditsPurchased { get; init; }

    public int BonusCredits { get; init; }

    public string? PromotionNameSnapshot { get; init; }

    public required string Status { get; init; }
}
