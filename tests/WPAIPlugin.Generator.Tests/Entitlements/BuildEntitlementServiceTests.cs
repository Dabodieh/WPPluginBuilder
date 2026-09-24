using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using WPAIPlugin.Api.Data;
using WPAIPlugin.Api.Entitlements;
using Xunit;

namespace WPAIPlugin.Generator.Tests.Entitlements;

public class BuildEntitlementServiceTests
{
    [Fact]
    public async Task TwoSimultaneousConsumes_OnlyOneSucceeds_WhenOneFreeBuildRemains()
    {
        var barrier = new OverlapInterceptor();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).ReplaceService<IDatabase, BuildEntitlementConcurrencyDatabase>().AddInterceptors(barrier).Options;
        await using (var seed = new AppDbContext(options))
        {
            await new BuildEntitlementService(seed).GrantAsync("user", 1, BuildEntitlementTransactionType.SignupFreeBuildGrant, "signup:one");
        }
        barrier.Enabled = true;
        await using var first = new AppDbContext(options);
        await using var second = new AppDbContext(options);
        var results = await Task.WhenAll(
            new BuildEntitlementService(first).TryConsumeAsync("user", "build:one"),
            new BuildEntitlementService(second).TryConsumeAsync("user", "build:two"));

        Assert.Single(results.Where(r => r));
        Assert.Single(results.Where(r => !r));
        await using var check = new AppDbContext(options);
        var account = await check.BuildEntitlementAccounts.SingleAsync();
        Assert.Equal(0, account.RemainingBuilds);
        Assert.Single(await check.BuildEntitlementTransactions.Where(t => t.Type == BuildEntitlementTransactionType.FreeBuildConsumed).ToListAsync());
        Assert.True(barrier.Arrivals >= 2);
        Assert.True(barrier.Conflicts >= 1);
    }

    [Fact]
    public async Task CannotConsumeBelowZero()
    {
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var service = new BuildEntitlementService(db);
        Assert.Equal(0, await service.GetRemainingAsync("missing"));
        Assert.False(await service.TryConsumeAsync("missing", "build:one"));

        await service.GrantAsync("user", 1, BuildEntitlementTransactionType.SignupFreeBuildGrant, "signup:one");
        Assert.True(await service.TryConsumeAsync("user", "build:one"));
        Assert.False(await service.TryConsumeAsync("user", "build:two"));
        Assert.Equal(0, await service.GetRemainingAsync("user"));
    }

    [Fact]
    public async Task RefundRestoresExactlyOnce()
    {
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var service = new BuildEntitlementService(db);
        await service.GrantAsync("user", 1, BuildEntitlementTransactionType.SignupFreeBuildGrant, "signup:one");
        Assert.True(await service.TryConsumeAsync("user", "build:one"));

        await service.RefundAsync("user", "build:one");
        await service.RefundAsync("user", "build:one"); // duplicate refund - must not double-restore

        Assert.Equal(1, await service.GetRemainingAsync("user"));
        Assert.Equal(1, await db.BuildEntitlementTransactions.CountAsync(t => t.Type == BuildEntitlementTransactionType.FreeBuildRefund));
    }

    [Fact]
    public async Task RefundRequiresMatchingConsumption()
    {
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var service = new BuildEntitlementService(db);
        await service.GrantAsync("user", 1, BuildEntitlementTransactionType.SignupFreeBuildGrant, "signup:one");
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.RefundAsync("user", "unknown"));
    }

    [Fact]
    public async Task GrantAsync_UpsertsExistingAccount_AndRecordsPromotionId()
    {
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var service = new BuildEntitlementService(db);
        await service.GrantAsync("user", 2, BuildEntitlementTransactionType.SignupFreeBuildGrant, "signup:one");
        var promotionId = Guid.NewGuid();
        await service.GrantAsync("user", 3, BuildEntitlementTransactionType.PromotionGrant, "promotion:one", promotionId);

        Assert.Equal(5, await service.GetRemainingAsync("user"));
        var promoTransaction = await db.BuildEntitlementTransactions.SingleAsync(t => t.Type == BuildEntitlementTransactionType.PromotionGrant);
        Assert.Equal(promotionId, promoTransaction.PromotionId);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task InvalidGrantRejected(int amount)
    {
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            new BuildEntitlementService(db).GrantAsync("user", amount, BuildEntitlementTransactionType.SignupFreeBuildGrant, "signup:one"));
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
