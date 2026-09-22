namespace WPAIPlugin.Planning;

/// <summary>
/// Root planning configuration, bound from configuration section "Planning".
/// API keys come from standard .NET configuration/environment variables
/// (e.g. Planning__Anthropic__ApiKey or ANTHROPIC_API_KEY,
/// Planning__OpenAI__ApiKey or OPENAI_API_KEY), never hard-coded. These keys
/// are server-side only: never logged, never returned from any controller or
/// provider, never sent to the browser.
/// </summary>
public sealed class PlanningOptions
{
    public const string SectionName = "Planning";

    public string DefaultProvider { get; set; } = "openai";

    public AnthropicOptions Anthropic { get; set; } = new();

    public OpenAIOptions OpenAI { get; set; } = new();
}

public sealed class AnthropicOptions
{
    public string? ApiKey { get; set; }

    public string Model { get; set; } = "claude-sonnet-5";

    public string BaseUrl { get; set; } = "https://api.anthropic.com";
}

// Server-side-only OpenAI configuration.
public sealed class OpenAIOptions
{
    public string? ApiKey { get; set; }

    public string Model { get; set; } = "gpt-5.6-luna";

    public string BaseUrl { get; set; } = "https://api.openai.com";
}
