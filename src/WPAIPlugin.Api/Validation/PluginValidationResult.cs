namespace WPAIPlugin.Api.Validation;

public enum PluginValidationFailureReason
{
    ValidationUnavailable,
    PhpLintFailed,
    WordPressSetupFailed,
    PluginInstallFailed,
    PluginActivationFailed,
    Timeout,
    Cancelled,
}

/// <summary>
/// Small structured result of running a generated plugin ZIP through the
/// disposable Docker WordPress validation environment.
/// </summary>
public sealed class PluginValidationResult
{
    public required bool Success { get; init; }

    public bool PhpLintPassed { get; init; }

    public bool WordPressInstalled { get; init; }

    public bool PluginInstalled { get; init; }

    public bool PluginActivated { get; init; }

    public PluginValidationFailureReason? FailureReason { get; init; }

    /// <summary>Short, safe-to-display explanation. Never contains stack traces, paths, or secrets.</summary>
    public string? Error { get; init; }

    public static PluginValidationResult Passed() => new()
    {
        Success = true,
        PhpLintPassed = true,
        WordPressInstalled = true,
        PluginInstalled = true,
        PluginActivated = true,
    };

    public static PluginValidationResult Failed(
        PluginValidationFailureReason reason,
        string error,
        bool phpLintPassed = false,
        bool wordPressInstalled = false,
        bool pluginInstalled = false,
        bool pluginActivated = false) => new()
    {
        Success = false,
        FailureReason = reason,
        Error = error,
        PhpLintPassed = phpLintPassed,
        WordPressInstalled = wordPressInstalled,
        PluginInstalled = pluginInstalled,
        PluginActivated = pluginActivated,
    };
}
