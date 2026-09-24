using Microsoft.EntityFrameworkCore;
using WPAIPlugin.Api.Data;

namespace WPAIPlugin.Api.Entitlements;

/// <summary>
/// Free-build entitlement balance and ledger (Promotions + Free Builds
/// milestone). Deliberately the same shape as CreditService, since free
/// builds are a distinct entitlement from credits with identical concurrency
/// and auditability requirements - never a generic credit, never a bare
/// unaudited counter. Balance and ledger are saved together; Version is
/// application-managed and every concurrency retry reloads the account.
/// </summary>
public sealed class BuildEntitlementService(AppDbContext db)
{
    private const int MaxAttempts = 3;

    public async Task<int> GetRemainingAsync(string userId, CancellationToken cancellationToken = default) =>
        await db.BuildEntitlementAccounts.AsNoTracking().Where(a => a.UserId == userId)
            .Select(a => (int?)a.RemainingBuilds).SingleOrDefaultAsync(cancellationToken) ?? 0;

    /// <summary>
    /// Grants free builds - used both for the deterministic signup grant and
    /// for a FreeBuilds promotion redemption (promotionId set in that case).
    /// Creates the account on first grant; otherwise adds to the existing
    /// balance under the same optimistic-concurrency retry as every other
    /// method here. amount must be positive - this method only ever grants,
    /// never deducts.
    /// </summary>
    public async Task GrantAsync(
        string userId, int amount, string type, string reference, Guid? promotionId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(amount);

        for (var attempt = 0; attempt < MaxAttempts; attempt++)
        {
            var account = await db.BuildEntitlementAccounts.SingleOrDefaultAsync(a => a.UserId == userId, cancellationToken);
            if (account is null)
            {
                db.BuildEntitlementAccounts.Add(new BuildEntitlementAccount
                {
                    UserId = userId, RemainingBuilds = amount, UpdatedAtUtc = DateTime.UtcNow, Version = 0,
                });
            }
            else
            {
                account.RemainingBuilds = checked(account.RemainingBuilds + amount);
                account.Version = checked(account.Version + 1);
                account.UpdatedAtUtc = DateTime.UtcNow;
            }
            db.BuildEntitlementTransactions.Add(Transaction(userId, amount, type, reference, promotionId));
            try
            {
                await db.SaveChangesAsync(cancellationToken);
                return;
            }
            catch (DbUpdateConcurrencyException)
            {
                db.ChangeTracker.Clear();
            }
        }

        throw new DbUpdateConcurrencyException("Build entitlement account is busy. Please retry.");
    }

    /// <summary>
    /// Consumes exactly one free build if available. Two simultaneous builds
    /// can never both consume the last remaining one - the same
    /// load/check/update/increment-Version/SaveChanges/bounded-retry pattern
    /// CreditService.TryChargeAsync already uses.
    /// </summary>
    public async Task<bool> TryConsumeAsync(string userId, string reference, CancellationToken cancellationToken = default)
    {
        for (var attempt = 0; attempt < MaxAttempts; attempt++)
        {
            var account = await db.BuildEntitlementAccounts.SingleOrDefaultAsync(a => a.UserId == userId, cancellationToken);
            if (account is null || account.RemainingBuilds < 1)
                return false;

            account.RemainingBuilds -= 1;
            account.Version = checked(account.Version + 1);
            account.UpdatedAtUtc = DateTime.UtcNow;
            db.BuildEntitlementTransactions.Add(Transaction(userId, -1, BuildEntitlementTransactionType.FreeBuildConsumed, reference, null));
            try
            {
                await db.SaveChangesAsync(cancellationToken);
                return true;
            }
            catch (DbUpdateConcurrencyException)
            {
                db.ChangeTracker.Clear();
            }
        }

        var remaining = await GetRemainingAsync(userId, cancellationToken);
        if (remaining < 1) return false;
        throw new DbUpdateConcurrencyException("Build entitlement account is busy. Please retry.");
    }

    /// <summary>
    /// Restores exactly one free build previously consumed under the given
    /// reference. Idempotent: a reference that has already been refunded (or
    /// was never consumed) is a safe no-op / throws, matching
    /// CreditService.RefundAsync's own contract exactly.
    /// </summary>
    public async Task RefundAsync(string userId, string reference, CancellationToken cancellationToken = default)
    {
        for (var attempt = 0; attempt < MaxAttempts; attempt++)
        {
            if (await HasRefundAsync(userId, reference, cancellationToken)) return;
            var account = await db.BuildEntitlementAccounts.SingleAsync(a => a.UserId == userId, cancellationToken);
            // Close the gap between the first check and loading the account:
            // a competing refund may have committed in that interval.
            if (await HasRefundAsync(userId, reference, cancellationToken)) return;
            var consumedExists = await db.BuildEntitlementTransactions.AnyAsync(t => t.UserId == userId
                && t.Reference == reference && t.Amount == -1 && t.Type == BuildEntitlementTransactionType.FreeBuildConsumed, cancellationToken);
            if (!consumedExists) throw new InvalidOperationException("No matching free-build consumption exists.");

            account.RemainingBuilds = checked(account.RemainingBuilds + 1);
            account.Version = checked(account.Version + 1);
            account.UpdatedAtUtc = DateTime.UtcNow;
            db.BuildEntitlementTransactions.Add(Transaction(userId, 1, BuildEntitlementTransactionType.FreeBuildRefund, reference, null));
            try
            {
                await db.SaveChangesAsync(cancellationToken);
                return;
            }
            catch (DbUpdateConcurrencyException)
            {
                db.ChangeTracker.Clear();
            }
        }
        if (await HasRefundAsync(userId, reference, cancellationToken)) return;
        throw new DbUpdateConcurrencyException("Free-build refund could not be saved.");
    }

    private Task<bool> HasRefundAsync(string userId, string reference, CancellationToken cancellationToken) =>
        db.BuildEntitlementTransactions.AnyAsync(t => t.UserId == userId && t.Reference == reference
            && t.Type == BuildEntitlementTransactionType.FreeBuildRefund, cancellationToken);

    private static BuildEntitlementTransaction Transaction(string userId, int amount, string type, string reference, Guid? promotionId) => new()
    {
        Id = Guid.NewGuid(), UserId = userId, Amount = amount, Type = type,
        PromotionId = promotionId, Reference = reference, CreatedAtUtc = DateTime.UtcNow,
    };
}
