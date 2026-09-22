using WPAIPlugin.Api.Validation;

namespace WPAIPlugin.Api.Controllers;

/// <summary>
/// Error response body for POST /api/plugins/build-validated when validation fails.
/// </summary>
public sealed class BuildValidatedErrorResponse
{
    public required string Error { get; init; }

    public required PluginValidationFailureReason Reason { get; init; }

    public bool PhpLintPassed { get; init; }

    public bool WordPressInstalled { get; init; }

    public bool PluginInstalled { get; init; }

    public bool PluginActivated { get; init; }
}
