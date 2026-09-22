namespace WPAIPlugin.Planning;

public enum PluginPlanFailureReason
{
    InvalidInput,
    ProviderNotFound,
    ProviderFailure,
    Timeout,
    Cancelled,
    MalformedProviderOutput,
    GeneratedSpecInvalid,
}

/// <summary>
/// Thrown when a plugin plan cannot be produced. Carries a safe, client-facing
/// message and reason code; never wraps or exposes raw provider payloads,
/// stack traces, or API keys.
/// </summary>
public sealed class PluginPlanException : Exception
{
    public PluginPlanFailureReason Reason { get; }

    public IReadOnlyList<string> ValidationErrors { get; }

    public PluginPlanException(PluginPlanFailureReason reason, string message, IReadOnlyList<string>? validationErrors = null, Exception? innerException = null)
        : base(message, innerException)
    {
        Reason = reason;
        ValidationErrors = validationErrors ?? Array.Empty<string>();
    }
}
