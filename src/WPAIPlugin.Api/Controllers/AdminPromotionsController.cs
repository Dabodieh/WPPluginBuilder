using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using WPAIPlugin.Api.Data;
using WPAIPlugin.Api.Payments;
using WPAIPlugin.Api.Security;

namespace WPAIPlugin.Api.Controllers;

/// <summary>
/// Admin-only promotion management (Promotions + Free Builds milestone):
/// create/edit/enable/disable/duplicate and per-promotion reporting.
/// AdminOnly-gated exactly like every other admin controller. No hard
/// delete anywhere - once a promotion exists it can only be disabled/
/// archived, never removed, so historical Purchase/redemption snapshots
/// always remain understandable (see Purchase's own doc comment).
/// </summary>
[ApiController]
[Route("api/admin/promotions")]
[Authorize(Policy = AdminAuthorization.AdminOnlyPolicy)]
[RequestSizeLimit(16 * 1024)]
public sealed class AdminPromotionsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly CreditPackOptions _packOptions;
    private readonly AdminAuditService _auditService;
    private readonly Microsoft.AspNetCore.Identity.UserManager<Microsoft.AspNetCore.Identity.IdentityUser> _userManager;

    public AdminPromotionsController(
        AppDbContext db, IOptions<CreditPackOptions> packOptions, AdminAuditService auditService,
        Microsoft.AspNetCore.Identity.UserManager<Microsoft.AspNetCore.Identity.IdentityUser> userManager)
    {
        _db = db;
        _packOptions = packOptions.Value;
        _auditService = auditService;
        _userManager = userManager;
    }

    private string CurrentAdminId => _userManager.GetUserId(User)
        ?? throw new InvalidOperationException("AdminOnly policy guarantees an authenticated user.");

    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<AdminPromotionResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
    {
        var promotions = await _db.Promotions.OrderByDescending(p => p.CreatedAtUtc).ToListAsync(cancellationToken);
        var counts = await _db.PromotionRedemptions.GroupBy(r => r.PromotionId)
            .Select(g => new { PromotionId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.PromotionId, g => g.Count, cancellationToken);

        return Ok(promotions.Select(p => ToResponse(p, counts.GetValueOrDefault(p.Id, 0))).ToList());
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(AdminPromotionDetailResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Detail(Guid id, CancellationToken cancellationToken)
    {
        var promotion = await _db.Promotions.FindAsync([id], cancellationToken);
        if (promotion is null)
        {
            return NotFound();
        }

        var redemptions = _db.PromotionRedemptions.Where(r => r.PromotionId == id);
        var redemptionCount = await redemptions.CountAsync(cancellationToken);
        var purchasingCustomers = await redemptions.Where(r => r.PurchaseId != null)
            .Select(r => r.UserId).Distinct().CountAsync(cancellationToken);
        var creditsGranted = await redemptions.Where(r => r.BenefitType == PromotionBenefitType.BonusCredits)
            .SumAsync(r => (int?)r.BenefitAmount, cancellationToken) ?? 0;
        var freeBuildsGranted = await redemptions.Where(r => r.BenefitType == PromotionBenefitType.FreeBuilds)
            .SumAsync(r => (int?)r.BenefitAmount, cancellationToken) ?? 0;
        var discountGranted = await redemptions.Where(r => r.BenefitType == PromotionBenefitType.PriceDiscount)
            .SumAsync(r => (int?)r.BenefitAmount, cancellationToken) ?? 0;
        var revenue = await _db.Purchases.Where(p => p.PromotionId == id && p.Status == PurchaseStatus.Completed)
            .SumAsync(p => (int?)p.AmountMinor, cancellationToken) ?? 0;

        var basic = ToResponse(promotion, redemptionCount);
        return Ok(new AdminPromotionDetailResponse
        {
            Id = basic.Id, Name = basic.Name, Code = basic.Code, Type = basic.Type,
            StartsAtUtc = basic.StartsAtUtc, EndsAtUtc = basic.EndsAtUtc, IsEnabled = basic.IsEnabled,
            RequiresCode = basic.RequiresCode, AppliesToPackId = basic.AppliesToPackId, Value = basic.Value,
            MaxRedemptions = basic.MaxRedemptions, MaxRedemptionsPerUser = basic.MaxRedemptionsPerUser,
            Eligibility = basic.Eligibility, Priority = basic.Priority, State = basic.State,
            RedemptionCount = basic.RedemptionCount, CreatedAtUtc = basic.CreatedAtUtc, UpdatedAtUtc = basic.UpdatedAtUtc,
            PurchasingCustomers = purchasingCustomers, CreditsGranted = creditsGranted,
            FreeBuildsGranted = freeBuildsGranted, DiscountMinorGranted = discountGranted, RevenueMinor = revenue,
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [ProducesResponseType(typeof(AdminPromotionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create([FromBody] AdminPromotionRequest request, CancellationToken cancellationToken)
    {
        var error = await Validate(request, existingId: null, cancellationToken);
        if (error is not null)
        {
            return BadRequest(new { error });
        }

        var now = DateTime.UtcNow;
        var promotion = new Promotion
        {
            Id = Guid.NewGuid(),
            Name = request.Name!.Trim(),
            Code = NormalizeCode(request.Code),
            Type = request.Type!,
            StartsAtUtc = request.StartsAtUtc,
            EndsAtUtc = request.EndsAtUtc,
            IsEnabled = request.IsEnabled,
            RequiresCode = request.RequiresCode,
            AppliesToPackId = request.AppliesToPackId,
            Value = request.Value,
            MaxRedemptions = request.MaxRedemptions,
            MaxRedemptionsPerUser = request.MaxRedemptionsPerUser,
            Eligibility = request.Eligibility!,
            Priority = request.Priority,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };
        _db.Promotions.Add(promotion);
        await _db.SaveChangesAsync(cancellationToken);
        await _auditService.RecordAsync(
            CurrentAdminId, AdminAuditAction.PromotionCreated, AdminAuditTargetType.Promotion, promotion.Id.ToString(),
            $"{promotion.Name} ({promotion.Type})", cancellationToken);

        return Ok(ToResponse(promotion, 0));
    }

    [HttpPut("{id:guid}")]
    [ValidateAntiForgeryToken]
    [ProducesResponseType(typeof(AdminPromotionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(Guid id, [FromBody] AdminPromotionRequest request, CancellationToken cancellationToken)
    {
        var promotion = await _db.Promotions.FindAsync([id], cancellationToken);
        if (promotion is null)
        {
            return NotFound();
        }

        var error = await Validate(request, existingId: id, cancellationToken);
        if (error is not null)
        {
            return BadRequest(new { error });
        }

        promotion.Name = request.Name!.Trim();
        promotion.Code = NormalizeCode(request.Code);
        promotion.Type = request.Type!;
        promotion.StartsAtUtc = request.StartsAtUtc;
        promotion.EndsAtUtc = request.EndsAtUtc;
        promotion.IsEnabled = request.IsEnabled;
        promotion.RequiresCode = request.RequiresCode;
        promotion.AppliesToPackId = request.AppliesToPackId;
        promotion.Value = request.Value;
        promotion.MaxRedemptions = request.MaxRedemptions;
        promotion.MaxRedemptionsPerUser = request.MaxRedemptionsPerUser;
        promotion.Eligibility = request.Eligibility!;
        promotion.Priority = request.Priority;
        promotion.UpdatedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
        await _auditService.RecordAsync(
            CurrentAdminId, AdminAuditAction.PromotionUpdated, AdminAuditTargetType.Promotion, promotion.Id.ToString(),
            $"{promotion.Name} ({promotion.Type})", cancellationToken);

        var redemptionCount = await _db.PromotionRedemptions.CountAsync(r => r.PromotionId == id, cancellationToken);
        return Ok(ToResponse(promotion, redemptionCount));
    }

    [HttpPost("{id:guid}/enable")]
    [ValidateAntiForgeryToken]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<IActionResult> Enable(Guid id, CancellationToken cancellationToken) => SetEnabled(id, true, cancellationToken);

    [HttpPost("{id:guid}/disable")]
    [ValidateAntiForgeryToken]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<IActionResult> Disable(Guid id, CancellationToken cancellationToken) => SetEnabled(id, false, cancellationToken);

    private async Task<IActionResult> SetEnabled(Guid id, bool enabled, CancellationToken cancellationToken)
    {
        var promotion = await _db.Promotions.FindAsync([id], cancellationToken);
        if (promotion is null)
        {
            return NotFound();
        }

        promotion.IsEnabled = enabled;
        promotion.UpdatedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
        await _auditService.RecordAsync(
            CurrentAdminId, enabled ? AdminAuditAction.PromotionEnabled : AdminAuditAction.PromotionDisabled,
            AdminAuditTargetType.Promotion, id.ToString(), promotion.Name, cancellationToken);

        return Ok();
    }

    /// <summary>Creates a disabled copy with no code (codes must stay unique) - the admin sets a new code and enables it explicitly.</summary>
    [HttpPost("{id:guid}/duplicate")]
    [ValidateAntiForgeryToken]
    [ProducesResponseType(typeof(AdminPromotionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Duplicate(Guid id, CancellationToken cancellationToken)
    {
        var source = await _db.Promotions.FindAsync([id], cancellationToken);
        if (source is null)
        {
            return NotFound();
        }

        var now = DateTime.UtcNow;
        var copy = new Promotion
        {
            Id = Guid.NewGuid(),
            Name = $"{source.Name} (copy)",
            Code = null,
            Type = source.Type,
            StartsAtUtc = source.StartsAtUtc,
            EndsAtUtc = source.EndsAtUtc,
            IsEnabled = false,
            RequiresCode = source.RequiresCode,
            AppliesToPackId = source.AppliesToPackId,
            Value = source.Value,
            MaxRedemptions = source.MaxRedemptions,
            MaxRedemptionsPerUser = source.MaxRedemptionsPerUser,
            Eligibility = source.Eligibility,
            Priority = source.Priority,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };
        _db.Promotions.Add(copy);
        await _db.SaveChangesAsync(cancellationToken);
        await _auditService.RecordAsync(
            CurrentAdminId, AdminAuditAction.PromotionCreated, AdminAuditTargetType.Promotion, copy.Id.ToString(),
            $"{copy.Name} (duplicated from {source.Id})", cancellationToken);

        return Ok(ToResponse(copy, 0));
    }

    private static string? NormalizeCode(string? code) =>
        string.IsNullOrWhiteSpace(code) ? null : code.Trim().ToUpperInvariant();

    private async Task<string?> Validate(AdminPromotionRequest request, Guid? existingId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Name)) return "Name is required.";

        if (request.Type is not (PromotionType.FreeBuilds or PromotionType.BonusCredits or PromotionType.PackPriceDiscount))
            return "Type must be FreeBuilds, BonusCredits, or PackPriceDiscount.";

        if (request.Eligibility is not (PromotionEligibility.Everyone or PromotionEligibility.NewRegistrations or PromotionEligibility.FirstPurchaseOnly))
            return "Eligibility must be Everyone, NewRegistrations, or FirstPurchaseOnly.";

        if (request.EndsAtUtc.HasValue && request.EndsAtUtc.Value <= request.StartsAtUtc)
            return "EndsAtUtc must be after StartsAtUtc.";

        if (request.Type == PromotionType.PackPriceDiscount && (request.Value < 1 || request.Value > 100))
            return "A price-discount percentage must be between 1 and 100.";
        if (request.Type != PromotionType.PackPriceDiscount && request.Value <= 0)
            return "Value must be a positive whole number.";

        if (request.AppliesToPackId is not null && !_packOptions.Packs.ContainsKey(request.AppliesToPackId))
            return "AppliesToPackId must be a configured credit pack.";
        if (request.Type == PromotionType.FreeBuilds && request.AppliesToPackId is not null)
            return "FreeBuilds promotions are not pack-scoped.";

        if (request.RequiresCode && string.IsNullOrWhiteSpace(request.Code))
            return "A code is required when RequiresCode is true.";
        if (request.Type == PromotionType.FreeBuilds && !request.RequiresCode)
            return "FreeBuilds promotions require a code - there is no automatic redemption trigger for this type.";

        if (request.MaxRedemptions.HasValue && request.MaxRedemptions.Value <= 0)
            return "MaxRedemptions must be positive.";
        if (request.MaxRedemptionsPerUser.HasValue && request.MaxRedemptionsPerUser.Value <= 0)
            return "MaxRedemptionsPerUser must be positive.";

        var normalizedCode = NormalizeCode(request.Code);
        if (normalizedCode is not null)
        {
            var conflict = await _db.Promotions.AsNoTracking()
                .Where(p => p.Code == normalizedCode && (existingId == null || p.Id != existingId))
                .AnyAsync(cancellationToken);
            if (conflict) return "That code is already used by another promotion.";
        }

        return null;
    }

    private static AdminPromotionResponse ToResponse(Promotion p, int redemptionCount) => new()
    {
        Id = p.Id, Name = p.Name, Code = p.Code, Type = p.Type,
        StartsAtUtc = p.StartsAtUtc, EndsAtUtc = p.EndsAtUtc, IsEnabled = p.IsEnabled,
        RequiresCode = p.RequiresCode, AppliesToPackId = p.AppliesToPackId, Value = p.Value,
        MaxRedemptions = p.MaxRedemptions, MaxRedemptionsPerUser = p.MaxRedemptionsPerUser,
        Eligibility = p.Eligibility, Priority = p.Priority,
        State = PromotionStateResolver.Resolve(p, DateTime.UtcNow).ToString(),
        RedemptionCount = redemptionCount, CreatedAtUtc = p.CreatedAtUtc, UpdatedAtUtc = p.UpdatedAtUtc,
    };
}
