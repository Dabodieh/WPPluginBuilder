namespace WPAIPlugin.Planning;

/// <summary>
/// Vendor-neutral result of a planning call. Every <see cref="IPlanningProvider"/>
/// implementation must translate its vendor-specific response into this shape.
/// No vendor SDK or vendor-specific type may leak past this boundary.
/// </summary>
public sealed class PlanningResult
{
    public required string Name { get; init; }

    public required string Slug { get; init; }

    public required string Description { get; init; }

    public required string Version { get; init; }

    public required string Author { get; init; }

    public required IReadOnlyList<string> Features { get; init; }

    public required IReadOnlyList<string> UnsupportedRequirements { get; init; }
}
