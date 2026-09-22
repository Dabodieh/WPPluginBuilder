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
        db.CreditAccounts.Add(new CreditAccount
        {
            UserId = userId, Balance = amount, UpdatedAtUtc = DateTime.UtcNow, Version = 0,
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

    private Task<bool> HasRefundAsync(string userId, string reference, CancellationToken cancellationToken) =>
        db.CreditTransactions.AnyAsync(t => t.UserId == userId && t.Reference == reference
            && t.Type == CreditTransactionType.Refund, cancellationToken);

    private static CreditTransaction Transaction(string userId, int amount, string type, string reference) => new()
    {
        Id = Guid.NewGuid(), UserId = userId, Amount = amount, Type = type,
        Reference = reference, CreatedAtUtc = DateTime.UtcNow,
    };
}
