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

    /// <summary>
    /// The planner determined the request's primary intent is not to create or
    /// modify a WordPress plugin (off-topic, or an attempt to redirect the
    /// model's behaviour). A real provider call was made and may carry real
    /// token usage - see <see cref="Usage"/>.
    /// </summary>
    OutOfScope,
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

    /// <summary>Token usage for the provider call that produced this outcome, when available (e.g. a real "reject" response). Null when no provider call was made.</summary>
    public PlanningUsage? Usage { get; }

    /// <summary>Short, safe, non-user-text detail code (e.g. a model-reported rejection reason such as "not_wordpress_plugin_request"). Never raw user input.</summary>
    public string? Detail { get; }

    public PluginPlanException(PluginPlanFailureReason reason, string message, IReadOnlyList<string>? validationErrors = null, Exception? innerException = null, PlanningUsage? usage = null, string? detail = null)
        : base(message, innerException)
    {
        Reason = reason;
        ValidationErrors = validationErrors ?? Array.Empty<string>();
        Usage = usage;
        Detail = detail;
    }
}
