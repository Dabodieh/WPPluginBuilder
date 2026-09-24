using System.Threading.RateLimiting;

namespace WPAIPlugin.Generator.Tests.Planning;

/// <summary>
/// Real, in-process rate limiters for PluginsController tests - never mocked,
/// so tests that need to exercise a rejection can use a low permit limit, and
/// all other tests can use a generous one that never trips.
/// </summary>
public static class TestPlanningRateLimiters
{
    public static PartitionedRateLimiter<string> Generous() => Create(int.MaxValue, TimeSpan.FromDays(1));

    public static PartitionedRateLimiter<string> Create(int permitLimit, TimeSpan window) =>
        PartitionedRateLimiter.Create<string, string>(userId => RateLimitPartition.GetFixedWindowLimiter(userId,
            _ => new FixedWindowRateLimiterOptions { PermitLimit = permitLimit, Window = window, QueueLimit = 0 }));
}
