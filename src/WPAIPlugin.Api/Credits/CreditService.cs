using Microsoft.EntityFrameworkCore;
using WPAIPlugin.Api.Data;

namespace WPAIPlugin.Api.Credits;

// Balance and ledger are saved together. Version is application-managed and
// every concurrency retry reloads both account and refund eligibility.
public sealed class CreditService(AppDbContext db)
{
    private const int MaxAttempts = 3;

    public async Task<int> GetBalanceAsync(string userId, CancellationToken cancellationToken = default) =>
        await db.CreditAccounts.AsNoTracking().Where(a => a.UserId == userId)
            .Select(a => (int?)a.Balance).SingleOrDefaultAsync(cancellationToken) ?? 0;

    public async Task GrantSignupCreditsAsync(string userId, int amount, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(amount);
        var now = DateTime.UtcNow;
        db.CreditAccounts.Add(new CreditAccount
        {
            UserId = userId, Balance = amount, UpdatedAtUtc = now, Version = 0, CreatedAtUtc = now,
        });
        db.CreditTransactions.Add(Transaction(userId, amount, CreditTransactionType.SignupGrant, $"signup:{Guid.NewGuid()}"));
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<(bool Success, int Balance)> TryChargeAsync(string userId, int amount, string type,
        string reference, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(amount);
        if (type != CreditTransactionType.PluginBuild && type != CreditTransactionType.ValidatedBuild)
            throw new ArgumentException("Invalid charge type.", nameof(type));

        for (var attempt = 0; attempt < MaxAttempts; attempt++)
        {
            var account = await db.CreditAccounts.SingleOrDefaultAsync(a => a.UserId == userId, cancellationToken);
            if (account is null || account.Balance < amount)
                return (false, account?.Balance ?? 0);

            account.Balance -= amount;
            account.Version = checked(account.Version + 1);
            account.UpdatedAtUtc = DateTime.UtcNow;
            db.CreditTransactions.Add(Transaction(userId, -amount, type, reference));
            try
            {
                await db.SaveChangesAsync(cancellationToken);
                return (true, account.Balance);
            }
            catch (DbUpdateConcurrencyException)
            {
                db.ChangeTracker.Clear();
            }
        }

        var balance = await GetBalanceAsync(userId, cancellationToken);
        if (balance < amount) return (false, balance);
        throw new DbUpdateConcurrencyException("Credit account is busy. Please retry.");
    }

    public async Task RefundAsync(string userId, int amount, string reference, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(amount);
        for (var attempt = 0; attempt < MaxAttempts; attempt++)
        {
            if (await HasRefundAsync(userId, reference, cancellationToken)) return;
            var account = await db.CreditAccounts.SingleAsync(a => a.UserId == userId, cancellationToken);
            // Close the gap between the first check and loading the account:
            // a competing refund may have committed in that interval.
            if (await HasRefundAsync(userId, reference, cancellationToken)) return;
            var chargeExists = await db.CreditTransactions.AnyAsync(t => t.UserId == userId
                && t.Reference == reference && t.Amount == -amount
                && (t.Type == CreditTransactionType.PluginBuild || t.Type == CreditTransactionType.ValidatedBuild), cancellationToken);
            if (!chargeExists) throw new InvalidOperationException("No matching build charge exists.");

            account.Balance = checked(account.Balance + amount);
            account.Version = checked(account.Version + 1);
            account.UpdatedAtUtc = DateTime.UtcNow;
            db.CreditTransactions.Add(Transaction(userId, amount, CreditTransactionType.Refund, reference));
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
        throw new DbUpdateConcurrencyException("Credit refund could not be saved.");
    }

    /// <summary>
    /// Signed admin credit adjustment (positive grants, negative deducts).
    /// idempotencyKey is a client-supplied token used only to detect a
    /// retried/duplicated submission - it never becomes a meaningful part of
    /// the reference itself beyond identity, and the server still owns the
    /// transaction's shape entirely. A duplicate call with the same key
    /// returns the original outcome instead of adjusting twice.
    /// </summary>
    public async Task<(bool Success, int Balance, bool AlreadyApplied)> AdjustCreditsAsync(
        string userId, int amount, Guid idempotencyKey, CancellationToken cancellationToken = default)
    {
        if (amount == 0) throw new ArgumentException("Adjustment amount must be non-zero.", nameof(amount));

        var reference = $"admin:{idempotencyKey}";
        var existing = await db.CreditTransactions.AsNoTracking().SingleOrDefaultAsync(
            t => t.Reference == reference && t.Type == CreditTransactionType.AdminAdjustment, cancellationToken);
        if (existing is not null)
        {
            var currentBalance = await GetBalanceAsync(userId, cancellationToken);
            return (true, currentBalance, true);
        }

        for (var attempt = 0; attempt < MaxAttempts; attempt++)
        {
            var account = await db.CreditAccounts.SingleOrDefaultAsync(a => a.UserId == userId, cancellationToken);
            if (account is null) return (false, 0, false);

            var newBalance = account.Balance + amount;
            if (newBalance < 0) return (false, account.Balance, false);

            account.Balance = newBalance;
            account.Version = checked(account.Version + 1);
            account.UpdatedAtUtc = DateTime.UtcNow;
            db.CreditTransactions.Add(Transaction(userId, amount, CreditTransactionType.AdminAdjustment, reference));
            try
            {
                await db.SaveChangesAsync(cancellationToken);
                return (true, account.Balance, false);
            }
            catch (DbUpdateConcurrencyException)
            {
                // Another concurrent request - possibly with the same idempotency
                // key - may have already committed this reference's ledger row.
                // Re-check before the next attempt would otherwise add a second one.
                db.ChangeTracker.Clear();
                if (await HasAdminAdjustmentAsync(reference, cancellationToken))
                {
                    var balance = await GetBalanceAsync(userId, cancellationToken);
                    return (true, balance, true);
                }
            }
            catch (DbUpdateException)
            {
                db.ChangeTracker.Clear();
                if (await HasAdminAdjustmentAsync(reference, cancellationToken))
                {
                    var balance = await GetBalanceAsync(userId, cancellationToken);
                    return (true, balance, true);
                }
                throw;
            }
        }

        throw new DbUpdateConcurrencyException("Credit account is busy. Please retry.");
    }

    /// <summary>
    /// Grants credits for a completed purchase. reference is server-generated
    /// (a "purchase:&lt;Purchase.Id&gt;" string, never derived from user input)
    /// and doubles as the idempotency key: a duplicate call with the same
    /// reference (e.g. a retried Stripe webhook) returns without granting
    /// twice. PurchaseService.CompletePurchaseAsync additionally guards this
    /// at the Purchase-status level, so this is defence in depth, not the
    /// only safeguard.
    /// </summary>
    public Task GrantPurchaseCreditsAsync(string userId, int amount, string reference, CancellationToken cancellationToken = default) =>
        GrantIdempotentAsync(userId, amount, CreditTransactionType.CreditPurchase, reference, cancellationToken);

    /// <summary>
    /// Grants a promotional bonus alongside a real purchase (Promotions +
    /// Free Builds milestone) - a distinct ledger type from CreditPurchase,
    /// so purchased vs. promotional credits stay reportable separately (never
    /// pretend more credits were purchased than actually were). Same
    /// idempotency/retry contract as GrantPurchaseCreditsAsync.
    /// </summary>
    public Task GrantPromotionBonusAsync(string userId, int amount, string reference, CancellationToken cancellationToken = default) =>
        GrantIdempotentAsync(userId, amount, CreditTransactionType.PromotionBonus, reference, cancellationToken);

    /// <summary>
    /// Shared implementation for GrantPurchaseCreditsAsync/GrantPromotionBonusAsync:
    /// reference is server-generated and doubles as the idempotency key - a
    /// duplicate call with the same reference (e.g. a retried Stripe webhook)
    /// returns without granting twice. PurchaseService additionally guards
    /// this at the Purchase-status level, so this is defence in depth, not
    /// the only safeguard.
    /// </summary>
    private async Task GrantIdempotentAsync(string userId, int amount, string type, string reference, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(amount);

        for (var attempt = 0; attempt < MaxAttempts; attempt++)
        {
            if (await HasTransactionAsync(reference, type, cancellationToken)) return;

            var account = await db.CreditAccounts.SingleOrDefaultAsync(a => a.UserId == userId, cancellationToken);
            if (account is null)
            {
                // Every registered user has a CreditAccount from signup; this
                // would indicate data corruption, not a normal race - surface it.
                throw new InvalidOperationException("No credit account exists for this user.");
            }

            account.Balance = checked(account.Balance + amount);
            account.Version = checked(account.Version + 1);
            account.UpdatedAtUtc = DateTime.UtcNow;
            db.CreditTransactions.Add(Transaction(userId, amount, type, reference));
            try
            {
                await db.SaveChangesAsync(cancellationToken);
                return;
            }
            catch (DbUpdateConcurrencyException)
            {
                db.ChangeTracker.Clear();
            }
            catch (DbUpdateException)
            {
                db.ChangeTracker.Clear();
                if (await HasTransactionAsync(reference, type, cancellationToken)) return;
                throw;
            }
        }

        if (await HasTransactionAsync(reference, type, cancellationToken)) return;
        throw new DbUpdateConcurrencyException("Credit grant could not be saved.");
    }

    private Task<bool> HasTransactionAsync(string reference, string type, CancellationToken cancellationToken) =>
        db.CreditTransactions.AnyAsync(t => t.Reference == reference && t.Type == type, cancellationToken);

    private Task<bool> HasAdminAdjustmentAsync(string reference, CancellationToken cancellationToken) =>
        db.CreditTransactions.AnyAsync(t => t.Reference == reference && t.Type == CreditTransactionType.AdminAdjustment, cancellationToken);

    private Task<bool> HasRefundAsync(string userId, string reference, CancellationToken cancellationToken) =>
        db.CreditTransactions.AnyAsync(t => t.UserId == userId && t.Reference == reference
            && t.Type == CreditTransactionType.Refund, cancellationToken);

    private static CreditTransaction Transaction(string userId, int amount, string type, string reference) => new()
    {
        Id = Guid.NewGuid(), UserId = userId, Amount = amount, Type = type,
        Reference = reference, CreatedAtUtc = DateTime.UtcNow,
    };
}
