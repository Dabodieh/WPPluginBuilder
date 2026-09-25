using Microsoft.EntityFrameworkCore;
using WPAIPlugin.Api.Data;
using WPAIPlugin.Api.Entitlements;
using WPAIPlugin.Api.Payments;

namespace WPAIPlugin.Api.Promotions;

/// <summary>Result of resolving a pack + optional code into final commercial terms. Never a float/double.</summary>
public sealed class PromotionResolution
{
    public Promotion? Promotion { get; init; }

    /// <summary>Amount to actually charge via Stripe - equals BaseAmountMinor unless a PackPriceDiscount applied.</summary>
    public required int PaidAmountMinor { get; init; }

    public required int BaseAmountMinor { get; init; }

    public required int BonusCredits { get; init; }
}

/// <summary>Thrown when a customer-submitted promo code is invalid, expired, or ineligible. Message is always the same generic text - never reveals which specific rule failed, so campaign details/limits are not enumerable.</summary>
public sealed class PromotionCodeException() : Exception("Invalid or expired code.");

/// <summary>Outcome of <see cref="PromotionService.RedeemFreeBuildsCodeAsync"/>.</summary>
public enum FreeBuildsRedemptionOutcome
{
    Success,
    InvalidCode,
    AlreadyRedeemed,
}

/// <summary>Result of <see cref="PromotionService.RedeemFreeBuildsCodeAsync"/>. FreeBuildsGranted is 0 unless Outcome is Success.</summary>
public sealed record FreeBuildsRedemptionResult(FreeBuildsRedemptionOutcome Outcome, int FreeBuildsGranted);

/// <summary>
/// Resolves, validates, and records promotions (Promotions + Free Builds
/// milestone). Deliberately small: three promotion types, five eligibility/
/// gating axes (window, enabled, pack, code, redemption limits), no
/// stacking - at most one promotion applies per purchase. An explicit valid
/// promo code always wins over any automatic promotion; among automatic
/// promotions, highest Priority wins, ties broken by CreatedAtUtc then Id -
/// both deterministic, never dependent on browser time.
/// </summary>
public sealed class PromotionService(
    AppDbContext db,
    Microsoft.Extensions.Options.IOptions<CreditPackOptions> packOptions,
    BuildEntitlementService entitlementService)
{
    private readonly CreditPackOptions _packOptions = packOptions.Value;

    public static string? NormalizeCode(string? code) =>
        string.IsNullOrWhiteSpace(code) ? null : code.Trim().ToUpperInvariant();

    /// <summary>
    /// Resolves the final checkout terms for a pack, applying an explicit
    /// code if supplied (must be valid or this throws PromotionCodeException
    /// - an invalid code is never silently ignored/substituted), otherwise
    /// the best eligible automatic promotion, otherwise no promotion at all.
    /// </summary>
    public async Task<PromotionResolution> ResolveForPackAsync(string userId, string packId, string? code, CancellationToken cancellationToken = default)
    {
        if (!_packOptions.Packs.TryGetValue(packId, out var pack))
        {
            throw new ArgumentException("Unknown credit pack.", nameof(packId));
        }

        Promotion? promotion;
        var normalizedCode = NormalizeCode(code);
        if (normalizedCode is not null)
        {
            promotion = await db.Promotions.AsNoTracking().SingleOrDefaultAsync(p => p.Code == normalizedCode, cancellationToken);
            if (promotion is null || promotion.Type == PromotionType.FreeBuilds
                || !await IsEligibleAsync(promotion, userId, packId, cancellationToken))
            {
                throw new PromotionCodeException();
            }
        }
        else
        {
            promotion = await FindBestAutomaticPromotionAsync(userId, packId, cancellationToken);
        }

        return Apply(pack, promotion);
    }

    /// <summary>Same resolution used by GET /api/payments/packs to show automatic deals without a code.</summary>
    public Task<Promotion?> FindBestAutomaticPromotionAsync(string userId, string packId, CancellationToken cancellationToken = default) =>
        FindBestAutomaticPromotionInternalAsync(userId, packId, cancellationToken);

    private async Task<Promotion?> FindBestAutomaticPromotionInternalAsync(string userId, string packId, CancellationToken cancellationToken)
    {
        var candidates = await db.Promotions.AsNoTracking()
            .Where(p => !p.RequiresCode && p.IsEnabled
                && (p.Type == PromotionType.PackPriceDiscount || p.Type == PromotionType.BonusCredits))
            .OrderByDescending(p => p.Priority).ThenBy(p => p.CreatedAtUtc).ThenBy(p => p.Id)
            .ToListAsync(cancellationToken);

        foreach (var candidate in candidates)
        {
            if (await IsEligibleAsync(candidate, userId, packId, cancellationToken))
            {
                return candidate;
            }
        }
        return null;
    }

    public PromotionResolution Apply(CreditPack pack, Promotion? promotion)
    {
        if (promotion is null || promotion.Type == PromotionType.FreeBuilds)
        {
            return new PromotionResolution { Promotion = null, PaidAmountMinor = pack.AmountMinor, BaseAmountMinor = pack.AmountMinor, BonusCredits = 0 };
        }

        if (promotion.Type == PromotionType.PackPriceDiscount)
        {
            // Decimal arithmetic, rounded to the nearest whole minor unit,
            // .5 rounds away from zero. Example: 999p at 25% -> 249.75 ->
            // 250p discount -> 749p paid (matches the spec's own worked
            // example exactly).
            var discount = (int)Math.Round(pack.AmountMinor * promotion.Value / 100m, MidpointRounding.AwayFromZero);
            var paid = Math.Max(0, pack.AmountMinor - discount);
            return new PromotionResolution { Promotion = promotion, PaidAmountMinor = paid, BaseAmountMinor = pack.AmountMinor, BonusCredits = 0 };
        }

        // BonusCredits: price is unaffected, Value is an absolute bonus credit count.
        return new PromotionResolution { Promotion = promotion, PaidAmountMinor = pack.AmountMinor, BaseAmountMinor = pack.AmountMinor, BonusCredits = promotion.Value };
    }

    /// <summary>
    /// Atomically validates and redeems a FreeBuilds promo code for one user,
    /// then grants the entitlement - all inside a single database
    /// transaction guarded by a row-level lock on the Promotion row
    /// (PostgreSQL <c>SELECT ... FOR UPDATE</c>). That lock serializes every
    /// concurrent redemption attempt for this promotion, across every
    /// application instance, so the eligibility check below (which counts
    /// existing redemption rows) can never read stale data: a losing
    /// concurrent request blocks on the lock, then re-evaluates eligibility
    /// against the winning request's already-committed redemption row.
    ///
    /// A plain unique index on (PromotionId, UserId) would be simpler, but
    /// Promotion.MaxRedemptionsPerUser is admin-configurable to any positive
    /// count, or unlimited (null) - a hard 1-row-per-user constraint would
    /// silently break any promotion an admin configured for more than one
    /// redemption per user. The row lock instead enforces whatever count the
    /// promotion actually specifies, exactly as IsEligibleAsync already did,
    /// just without the race.
    ///
    /// The lock is only acquired against a relational database. EF Core's
    /// InMemory provider (used by this project's test suite) has no
    /// transaction/row-locking support at all - production always runs on
    /// PostgreSQL (see Program.cs's UseNpgsql), where the lock is the real
    /// concurrency guard.
    /// </summary>
    public async Task<FreeBuildsRedemptionResult> RedeemFreeBuildsCodeAsync(
        string userId, string code, CancellationToken cancellationToken = default)
    {
        var normalized = NormalizeCode(code);
        if (normalized is null)
            return new FreeBuildsRedemptionResult(FreeBuildsRedemptionOutcome.InvalidCode, 0);

        var promotionId = await db.Promotions.AsNoTracking()
            .Where(p => p.Code == normalized && p.Type == PromotionType.FreeBuilds)
            .Select(p => (Guid?)p.Id).SingleOrDefaultAsync(cancellationToken);
        if (promotionId is null)
            return new FreeBuildsRedemptionResult(FreeBuildsRedemptionOutcome.InvalidCode, 0);

        // EF Core's InMemory provider (this project's test host) cannot open
        // a real transaction at all - it logs/throws on BeginTransactionAsync
        // rather than silently no-op'ing. Only open one against a relational
        // (production: PostgreSQL) connection, where it is also what makes
        // the row lock below and the redemption+grant commit/rollback
        // atomic.
        var transaction = db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync(cancellationToken)
            : null;
        await using (transaction)
        {
            Promotion? promotion;
            if (db.Database.IsRelational())
            {
                var locked = await db.Promotions
                    .FromSqlInterpolated($"SELECT * FROM \"Promotions\" WHERE \"Id\" = {promotionId.Value} FOR UPDATE")
                    .ToListAsync(cancellationToken);
                promotion = locked.SingleOrDefault();
            }
            else
            {
                promotion = await db.Promotions.SingleOrDefaultAsync(p => p.Id == promotionId.Value, cancellationToken);
            }

            if (promotion is null || promotion.Type != PromotionType.FreeBuilds)
                return new FreeBuildsRedemptionResult(FreeBuildsRedemptionOutcome.InvalidCode, 0);

            if (!await IsEligibleAsync(promotion, userId, packId: null, cancellationToken))
                return new FreeBuildsRedemptionResult(FreeBuildsRedemptionOutcome.AlreadyRedeemed, 0);

            db.PromotionRedemptions.Add(new PromotionRedemption
            {
                Id = Guid.NewGuid(), PromotionId = promotion.Id, UserId = userId, PurchaseId = null,
                BenefitType = PromotionBenefitType.FreeBuilds, BenefitAmount = promotion.Value, CreatedAtUtc = DateTime.UtcNow,
            });
            await db.SaveChangesAsync(cancellationToken);

            // Only after this request has won the redemption row above does
            // it grant the entitlement - same transaction, so a grant
            // failure rolls back the redemption claim too; neither is ever
            // left half-applied.
            var reference = $"promotion:{Guid.NewGuid()}";
            await entitlementService.GrantAsync(
                userId, promotion.Value, BuildEntitlementTransactionType.PromotionGrant, reference, promotion.Id, cancellationToken);

            if (transaction is not null)
                await transaction.CommitAsync(cancellationToken);
            return new FreeBuildsRedemptionResult(FreeBuildsRedemptionOutcome.Success, promotion.Value);
        }
    }

    /// <summary>
    /// Idempotent for purchase-tied redemptions (the unique index on
    /// PurchaseId is the real guard - a losing concurrent insert returns
    /// false rather than throwing). Callers of a code-tied redemption must
    /// only call this once per successful entitlement/credit grant.
    /// </summary>
    public async Task<bool> RecordRedemptionAsync(
        Guid promotionId, string userId, Guid? purchaseId, string benefitType, int benefitAmount, CancellationToken cancellationToken = default)
    {
        if (purchaseId.HasValue && await db.PromotionRedemptions.AnyAsync(r => r.PurchaseId == purchaseId.Value, cancellationToken))
        {
            return false;
        }

        db.PromotionRedemptions.Add(new PromotionRedemption
        {
            Id = Guid.NewGuid(), PromotionId = promotionId, UserId = userId, PurchaseId = purchaseId,
            BenefitType = benefitType, BenefitAmount = benefitAmount, CreatedAtUtc = DateTime.UtcNow,
        });
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
            return false;
        }
    }

    private async Task<bool> IsEligibleAsync(Promotion promotion, string userId, string? packId, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        if (PromotionStateResolver.Resolve(promotion, now) != PromotionState.Active) return false;
        if (promotion.AppliesToPackId is not null && packId is not null && promotion.AppliesToPackId != packId) return false;

        if (promotion.Eligibility == PromotionEligibility.FirstPurchaseOnly)
        {
            var hasCompletedPurchase = await db.Purchases.AnyAsync(
                p => p.UserId == userId && p.Status == PurchaseStatus.Completed, cancellationToken);
            if (hasCompletedPurchase) return false;
        }
        else if (promotion.Eligibility == PromotionEligibility.NewRegistrations)
        {
            // ASP.NET Core Identity has no user-creation timestamp - a user is
            // "new" relative to this promotion if their CreditAccount (created
            // exactly once, at registration) postdates the promotion's own
            // start. See CreditAccount.CreatedAtUtc's doc comment.
            var registeredAtUtc = await db.CreditAccounts.AsNoTracking()
                .Where(a => a.UserId == userId).Select(a => (DateTime?)a.CreatedAtUtc).SingleOrDefaultAsync(cancellationToken);
            if (registeredAtUtc is null || registeredAtUtc.Value < promotion.StartsAtUtc) return false;
        }

        if (promotion.MaxRedemptions.HasValue)
        {
            var totalRedemptions = await db.PromotionRedemptions.CountAsync(r => r.PromotionId == promotion.Id, cancellationToken);
            if (totalRedemptions >= promotion.MaxRedemptions.Value) return false;
        }
        if (promotion.MaxRedemptionsPerUser.HasValue)
        {
            var userRedemptions = await db.PromotionRedemptions.CountAsync(
                r => r.PromotionId == promotion.Id && r.UserId == userId, cancellationToken);
            if (userRedemptions >= promotion.MaxRedemptionsPerUser.Value) return false;
        }

        return true;
    }
}
