using Microsoft.Extensions.Diagnostics.HealthChecks;
using WPAIPlugin.Api.Data;

namespace WPAIPlugin.Api.Health;

/// <summary>
/// Readiness check for GET /health/ready. Only confirms the database is
/// reachable - never returns connection details, table names, or exception
/// text to the caller.
/// </summary>
public sealed class DatabaseHealthCheck : IHealthCheck
{
    private readonly AppDbContext _db;

    public DatabaseHealthCheck(AppDbContext db)
    {
        _db = db;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            return await _db.Database.CanConnectAsync(cancellationToken)
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy();
        }
        catch
        {
            return HealthCheckResult.Unhealthy();
        }
    }
}
