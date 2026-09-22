using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using WPAIPlugin.Api.Credits;
using WPAIPlugin.Api.Data;
using Xunit;

namespace WPAIPlugin.Generator.Tests.Credits;

public class CreditServiceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OverlappingChargesOrRefunds_ApplyExactlyOnce(bool refund)
    {
        var barrier = new OverlapInterceptor();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).ReplaceService<IDatabase, CreditConcurrencyDatabase>().AddInterceptors(barrier).Options;
        await using (var seed = new AppDbContext(options))
        {
            var service = new CreditService(seed);
            await service.GrantSignupCreditsAsync("user", 1);
            if (refund) await service.TryChargeAsync("user", 1, CreditTransactionType.PluginBuild, "build:one");
        }
        barrier.Enabled = true;
        await using var first = new AppDbContext(options);
        await using var second = new AppDbContext(options);
        if (refund)
        {
            await Task.WhenAll(new CreditService(first).RefundAsync("user", 1, "build:one"),
                new CreditService(second).RefundAsync("user", 1, "build:one"));
        }
        else
        {
            var results = await Task.WhenAll(
                new CreditService(first).TryChargeAsync("user", 1, CreditTransactionType.PluginBuild, "build:one"),
                new CreditService(second).TryChargeAsync("user", 1, CreditTransactionType.PluginBuild, "build:two"));
            Assert.Single(results.Where(r => r.Success));
            Assert.Single(results.Where(r => !r.Success));
        }
        await using var check = new AppDbContext(options);
        var account = await check.CreditAccounts.SingleAsync();
        Assert.Equal(refund ? 1 : 0, account.Balance);
        Assert.Equal(account.Balance, await check.CreditTransactions.SumAsync(t => t.Amount));
        Assert.Single(await check.CreditTransactions.Where(t => t.Type ==
            (refund ? CreditTransactionType.Refund : CreditTransactionType.PluginBuild)).ToListAsync());
        Assert.True(barrier.Arrivals >= 2);
        Assert.True(barrier.Conflicts >= 1);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task InvalidChargeRejected(int amount)
    {
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            new CreditService(db).TryChargeAsync("user", amount, CreditTransactionType.PluginBuild, "build:one"));
        Assert.Empty(db.CreditTransactions);
    }

    [Fact]
    public async Task MissingAccountGetsZero_AndRefundRequiresMatchingCharge()
    {
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var service = new CreditService(db);
        Assert.Equal(0, await service.GetBalanceAsync("missing"));
        Assert.False((await service.TryChargeAsync("missing", 1, CreditTransactionType.PluginBuild, "build:one")).Success);
        Assert.Empty(db.CreditAccounts);
        await service.GrantSignupCreditsAsync("user", 100);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.RefundAsync("user", 1, "unknown"));
        Assert.Equal(100, await service.GetBalanceAsync("user"));
    }

    // Both contexts have loaded and modified Version before either is allowed
    // to save. This proves actual token conflicts, not sequential spending.
    private sealed class OverlapInterceptor : SaveChangesInterceptor
    {
        public bool Enabled;
        public int Arrivals;
        public int Conflicts;
        private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (Enabled && Interlocked.Increment(ref Arrivals) <= 2)
            {
                if (Arrivals == 2) _ready.TrySetResult();
                await _ready.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
            }
            return result;
        }
        public override InterceptionResult ThrowingConcurrencyException(
            ConcurrencyExceptionEventData eventData, InterceptionResult result)
        {
            Interlocked.Increment(ref Conflicts);
            return result;
        }
    }
}
