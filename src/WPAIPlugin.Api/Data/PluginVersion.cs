namespace WPAIPlugin.Api.Data;

// A single saved build of a PluginProject (Milestone 11). ArtifactKey is an
// opaque, server-generated identifier resolved by PluginArtifactStore - it is
// never a raw filesystem path and is never returned to the browser.
public sealed class PluginVersion
{
    public Guid Id { get; set; }

    public Guid PluginProjectId { get; set; }

    public PluginProject? PluginProject { get; set; }

    public int RevisionNumber { get; set; }

    public required string PluginVersionNumber { get; set; }

    public required string SpecJson { get; set; }

    public required string ArtifactKey { get; set; }

    public bool Validated { get; set; }

    public DateTime CreatedAtUtc { get; set; }
}
