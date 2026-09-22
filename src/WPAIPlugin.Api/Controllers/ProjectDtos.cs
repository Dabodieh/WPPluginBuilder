namespace WPAIPlugin.Api.Controllers;

/// <summary>
/// Response body for POST /api/projects/build. Never includes UserId,
/// ArtifactKey, or any filesystem path.
/// </summary>
public sealed class ProjectBuildResponse
{
    public required Guid ProjectId { get; init; }

    public required Guid VersionId { get; init; }

    public required int RevisionNumber { get; init; }

    public required bool Validated { get; init; }

    public required string DownloadUrl { get; init; }

    public required int CreditsCharged { get; init; }

    public required int CreditBalance { get; init; }
}

/// <summary>
/// Response body for POST /api/projects/build when the account does not have
/// enough credits. Never reveals internal ledger/transaction details.
/// </summary>
public sealed class InsufficientCreditsResponse
{
    public required string Error { get; init; }

    public required int Required { get; init; }

    public required int Balance { get; init; }
}

/// <summary>
/// Summary row for GET /api/projects. Never includes UserId or ArtifactKey.
/// </summary>
public sealed class ProjectSummaryResponse
{
    public required Guid Id { get; init; }

    public required string Name { get; init; }

    public required string Slug { get; init; }

    public required int LatestRevisionNumber { get; init; }

    public required string LatestPluginVersion { get; init; }

    public required bool Validated { get; init; }

    public required DateTime UpdatedAtUtc { get; init; }

    public required string DownloadUrl { get; init; }
}

/// <summary>
/// Detail body for GET /api/projects/{id}. Never includes UserId or ArtifactKey.
/// </summary>
public sealed class ProjectDetailResponse
{
    public required Guid Id { get; init; }

    public required string Name { get; init; }

    public required string Slug { get; init; }

    public required DateTime CreatedAtUtc { get; init; }

    public required DateTime UpdatedAtUtc { get; init; }

    public required IReadOnlyList<ProjectVersionResponse> Versions { get; init; }
}

public sealed class ProjectVersionResponse
{
    public required Guid Id { get; init; }

    public required int RevisionNumber { get; init; }

    public required string PluginVersion { get; init; }

    public required bool Validated { get; init; }

    public required DateTime CreatedAtUtc { get; init; }

    public required string DownloadUrl { get; init; }
}
