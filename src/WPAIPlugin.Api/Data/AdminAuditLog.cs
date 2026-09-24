namespace WPAIPlugin.Api.Data;

public static class AdminAuditAction
{
    public const string CreditAdjustment = "CreditAdjustment";
    public const string AccountLocked = "AccountLocked";
    public const string AccountUnlocked = "AccountUnlocked";
    public const string PromotionCreated = "PromotionCreated";
    public const string PromotionUpdated = "PromotionUpdated";
    public const string PromotionEnabled = "PromotionEnabled";
    public const string PromotionDisabled = "PromotionDisabled";
}

public static class AdminAuditTargetType
{
    public const string User = "User";
    public const string Promotion = "Promotion";
}

/// <summary>
/// Immutable record of an administrator action - never updated or deleted
/// after creation. Answers who (AdminUserId), what (Action), which account
/// (TargetType/TargetId), when (CreatedAtUtc), and why (Description - a
/// short operator-supplied reason, never a secret, password, API key, or
/// filesystem path).
/// </summary>
public sealed class AdminAuditLog
{
    public Guid Id { get; set; }

    public required string AdminUserId { get; set; }

    public required string Action { get; set; }

    public required string TargetType { get; set; }

    public required string TargetId { get; set; }

    public string? Description { get; set; }

    public DateTime CreatedAtUtc { get; set; }
}
