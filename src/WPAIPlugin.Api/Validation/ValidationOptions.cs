namespace WPAIPlugin.Api.Validation;

/// <summary>
/// Configuration for the optional Docker-based build-and-validate path.
/// Bound from configuration section "Validation". Has no effect on the
/// normal, Docker-free POST /api/plugins/build endpoint.
/// </summary>
public sealed class ValidationOptions
{
    public const string SectionName = "Validation";

    public bool Enabled { get; set; } = true;

    public int TimeoutSeconds { get; set; } = 120;
}
