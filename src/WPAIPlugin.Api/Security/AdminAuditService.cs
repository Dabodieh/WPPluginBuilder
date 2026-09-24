using WPAIPlugin.Api.Data;

namespace WPAIPlugin.Api.Security;

/// <summary>
/// Writes immutable AdminAuditLog rows. Description is a short, operator-
/// supplied reason string only - callers must never pass an email, API key,
/// or filesystem path into it.
/// </summary>
public sealed class AdminAuditService(WPAIPlugin.Api.Data.AppDbContext db)
{
    public async Task RecordAsync(
        string adminUserId, string action, string targetType, string targetId,
        string? description, CancellationToken cancellationToken = default)
    {
        db.AdminAuditLogs.Add(new AdminAuditLog
        {
            Id = Guid.NewGuid(),
            AdminUserId = adminUserId,
            Action = action,
            TargetType = targetType,
            TargetId = targetId,
            Description = description,
            CreatedAtUtc = DateTime.UtcNow,
        });
        await db.SaveChangesAsync(cancellationToken);
    }
}
