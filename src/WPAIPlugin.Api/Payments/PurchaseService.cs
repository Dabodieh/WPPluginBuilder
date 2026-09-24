using Microsoft.EntityFrameworkCore;
using WPAIPlugin.Api.Credits;
using WPAIPlugin.Api.Data;
using WPAIPlugin.Api.Promotions;

namespace WPAIPlugin.Api.Payments;

/// <summary>
/// Authoritative purchase/credit-grant accounting (Milestone 16; promotion
/// snapshot/bonus/redemption behaviour added in the Promotions + Free Builds
/// milestone). A Purchase starts Pending when a Checkout Session is created;
/// only a verified Stripe webhook (never the browser success redirect) can
/// move it to Completed and grant credits, via the existing ledger-backed
/// CreditService - there is no direct CreditAccount.Balance write here.
/// Every step is idempotent: a ProviderCheckoutSessionId is unique per
/// attempt, and a completed Purchase is only ever granted credits once.
/// </summary>
public sealed class PurchaseService(AppDbContext db, CreditService creditService, PromotionService promotionService)
{
    public async Task<Purchase> CreatePendingAsync(
        string userId, string packId, CreditPack pack, string checkoutSessionId, PromotionResolution resolution,
        CancellationToken cancellationToken = default)
    {
        var purchase = new Purchase
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Provider = PurchaseProvider.Stripe,
            PackId = packId,
            ProviderCheckoutSessionId = checkoutSessionId,
            Currency = pack.Currency,
            AmountMinor = resolution.PaidAmountMinor,
            BaseAmountMinor = resolution.BaseAmountMinor,
            CreditsPurchased = pack.Credits,
            BonusCredits = resolution.BonusCredits,
            PromotionId = resolution.Promotion?.Id,
            PromotionCodeSnapshot = resolution.Promotion?.Code,
            PromotionNameSnapshot = resolution.Promotion?.Name,
            Status = PurchaseStatus.Pending,
            CreatedAtUtc = DateTime.UtcNow,
        };
        db.Purchases.Add(purchase);
        await db.SaveChangesAsync(cancellationToken);
        return purchase;
    }

    /// <summary>
    /// Marks the matching Pending purchase Completed and grants credits via
    /// CreditService, in that order within the same logical operation. Safe
    /// to call more than once for the same session (webhook retries): a
    /// non-Pending purchase is left untouched and no second grant occurs.
    /// Returns false if no matching Pending purchase exists (unknown/already-
    /// processed session).
    ///
    /// The Purchase's own snapshot fields are the sole source of truth for
    /// what the customer agreed to buy and what they are owed - the
    /// referenced Promotion (if any) is never reloaded or re-resolved here.
    /// A Promotion that has since expired, been disabled, or been edited must
    /// not un-honour an already-created Purchase: the customer paid the
    /// agreed (possibly discounted) amount, so the agreed credits/bonus are
    /// granted and the redemption is recorded regardless of the Promotion's
    /// current state. Redemption limits are enforced once, at Checkout
    /// creation (PromotionService.ResolveForPackAsync) - not re-checked here.
    /// </summary>
    public async Task<bool> CompletePurchaseAsync(string checkoutSessionId, string? paymentIntentId, CancellationToken cancellationToken = default)
    {
        var purchase = await db.Purchases.SingleOrDefaultAsync(p => p.ProviderCheckoutSessionId == checkoutSessionId, cancellationToken);
        if (purchase is null)
        {
            return false;
        }

        if (purchase.Status != PurchaseStatus.Pending)
        {
            // Already completed (a prior webhook delivery) or already failed/cancelled - never re-grant.
            return purchase.Status == PurchaseStatus.Completed;
        }

        purchase.Status = PurchaseStatus.Completed;
        purchase.ProviderPaymentIntentId = paymentIntentId;
        purchase.CompletedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        await creditService.GrantPurchaseCreditsAsync(
            purchase.UserId, purchase.CreditsPurchased, $"purchase:{purchase.Id}", cancellationToken);

        if (purchase.BonusCredits > 0)
        {
            await creditService.GrantPromotionBonusAsync(
                purchase.UserId, purchase.BonusCredits, $"purchase-bonus:{purchase.Id}", cancellationToken);
        }

        if (purchase.PromotionId.HasValue)
        {
            // Benefit type/amount derived purely from the Purchase snapshot -
            // never from the current Promotion.Type, which may have changed.
            var benefitType = purchase.BonusCredits > 0 ? PromotionBenefitType.BonusCredits : PromotionBenefitType.PriceDiscount;
            var benefitAmount = purchase.BonusCredits > 0 ? purchase.BonusCredits : purchase.BaseAmountMinor - purchase.AmountMinor;
            await promotionService.RecordRedemptionAsync(
                purchase.PromotionId.Value, purchase.UserId, purchase.Id, benefitType, benefitAmount, cancellationToken);
        }

        return true;
    }

    public async Task MarkFailedOrCancelledAsync(string checkoutSessionId, string status, CancellationToken cancellationToken = default)
    {
        var purchase = await db.Purchases.SingleOrDefaultAsync(p => p.ProviderCheckoutSessionId == checkoutSessionId, cancellationToken);
        if (purchase is null || purchase.Status != PurchaseStatus.Pending)
        {
            return;
        }

        purchase.Status = status;
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Records that a Stripe webhook event ID has been processed, for
    /// idempotent handling. Returns false (and does nothing) if this event
    /// was already recorded - the caller must then skip all side effects.
    /// </summary>
    public async Task<bool> TryRecordEventAsync(string providerEventId, string eventType, CancellationToken cancellationToken = default)
    {
        if (await db.ProcessedPaymentEvents.AnyAsync(e => e.ProviderEventId == providerEventId, cancellationToken))
        {
            return false;
        }

        db.ProcessedPaymentEvents.Add(new ProcessedPaymentEvent
        {
            ProviderEventId = providerEventId, EventType = eventType, ProcessedAtUtc = DateTime.UtcNow,
        });

        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException)
        {
            // Another concurrent delivery of the same event won the race.
            db.ChangeTracker.Clear();
            return false;
        }
    }
}
