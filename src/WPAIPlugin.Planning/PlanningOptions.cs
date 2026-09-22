namespace WPAIPlugin.Planning;

/// <summary>
/// Root planning configuration, bound from configuration section "Planning".
/// API keys come from standard .NET configuration/environment variables
/// (e.g. Planning__Anthropic__ApiKey or ANTHROPIC_API_KEY), never hard-coded.
/// </summary>
public sealed class PlanningOptions
{
    public const string SectionName = "Planning";

    public string DefaultProvider { get; set; } = "anthropic";

    public AnthropicOptions Anthropic { get; set; } = new();
}

public sealed class AnthropicOptions
{
    public string? ApiKey { get; set; }

    public string Model { get; set; } = "claude-sonnet-5";

    public string BaseUrl { get; set; } = "https://api.anthropic.com";
}
