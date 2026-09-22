namespace WPAIPlugin.Api.Data;

// SaaS-owned plugin project (Milestone 11). Every project belongs to exactly
// one authenticated user; every query must filter on UserId.
public sealed class PluginProject
{
    public Guid Id { get; set; }

    public required string UserId { get; set; }

    public required string Name { get; set; }

    public required string Slug { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public List<PluginVersion> Versions { get; set; } = new();
}
