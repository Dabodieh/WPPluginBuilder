using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace WPAIPlugin.Planning.Providers.Anthropic;

/// <summary>
/// <see cref="IPlanningProvider"/> backed by the Anthropic Messages API.
/// Forces structured JSON output via a single forced tool call (the provider's
/// supported structured-output mechanism), so no Markdown/code-fence parsing
/// of prose is required. All Anthropic-specific request/response shapes are
/// private to this class; nothing vendor-specific escapes <see cref="PlanAsync"/>.
/// </summary>
public sealed class AnthropicPlanningProvider : IPlanningProvider
{
    public string Name => "anthropic";

    private const string ToolName = "propose_plugin_spec";

    private const string SystemPrompt = """
        You are a planning assistant for an automated WordPress plugin generator.

        Your ONLY job is to turn a user's natural-language request into structured
        planning data by calling the propose_plugin_spec tool. You must call that
        tool exactly once, with your best proposal.

        Strict rules:
        - Return structured data only. Never return PHP code. Never return Markdown.
        - Never invent or claim support for features the generator does not have.
        - The ONLY currently supported feature is "shortcode". The features array
          may contain only values from this set (it may be empty).
        - version must default to "1.0.0" unless the user explicitly requests a different version.
        - author must default to "WPAI Plugin Builder" unless the user explicitly names an author.
        - slug must be a URL-safe WordPress plugin slug: lowercase letters, digits,
          and hyphens only, must not start or end with a hyphen, and must not contain
          spaces, underscores, dots, slashes, or backslashes.
        - If the user's request needs functionality beyond what is currently supported
          (e.g. booking calendars, payments, email notifications, admin UI beyond a
          shortcode, custom database tables), do NOT add it to features and do NOT
          pretend it is supported. Instead list each such requirement, in the user's
          own terms, in unsupportedRequirements.
        - Do not fabricate functionality that was not requested.
        """;

    private readonly HttpClient _httpClient;
    private readonly AnthropicOptions _options;
    private readonly ILogger<AnthropicPlanningProvider> _logger;

    public AnthropicPlanningProvider(HttpClient httpClient, IOptions<PlanningOptions> options, ILogger<AnthropicPlanningProvider> logger)
    {
        _httpClient = httpClient;
        _options = options.Value.Anthropic;
        _logger = logger;
    }

    public async Task<PlanningResult> PlanAsync(PlanningRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            _logger.LogError("Anthropic planning provider is not configured: missing API key.");
            throw new PluginPlanException(PluginPlanFailureReason.ProviderFailure, "The AI planning provider is not configured.");
        }

        var model = string.IsNullOrWhiteSpace(request.Model) ? _options.Model : request.Model;

        var payload = new AnthropicMessageRequest
        {
            Model = model,
            MaxTokens = 1024,
            System = SystemPrompt,
            Messages = new[]
            {
                new AnthropicMessage { Role = "user", Content = request.Description },
            },
            Tools = new[] { BuildToolDefinition() },
            ToolChoice = new AnthropicToolChoice { Type = "tool", Name = ToolName },
        };

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"{_options.BaseUrl.TrimEnd('/')}/v1/messages")
        {
            Content = JsonContent.Create(payload),
        };
        httpRequest.Headers.Add("x-api-key", _options.ApiKey);
        httpRequest.Headers.Add("anthropic-version", "2023-06-01");

        HttpResponseMessage httpResponse;
        try
        {
            httpResponse = await _httpClient.SendAsync(httpRequest, cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogError("Anthropic planning request timed out.");
            throw new PluginPlanException(PluginPlanFailureReason.Timeout, "The AI planning provider timed out.");
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Anthropic planning request failed.");
            throw new PluginPlanException(PluginPlanFailureReason.ProviderFailure, "The AI planning provider could not be reached.");
        }

        using (httpResponse)
        {
            if (httpResponse.StatusCode == HttpStatusCode.TooManyRequests || (int)httpResponse.StatusCode >= 500)
            {
                _logger.LogError("Anthropic planning request failed with status {StatusCode}.", (int)httpResponse.StatusCode);
                throw new PluginPlanException(PluginPlanFailureReason.ProviderFailure, "The AI planning provider is currently unavailable.");
            }

            if (!httpResponse.IsSuccessStatusCode)
            {
                // Do not surface response body: it may echo request content or provider diagnostics.
                _logger.LogError("Anthropic planning request rejected with status {StatusCode}.", (int)httpResponse.StatusCode);
                throw new PluginPlanException(PluginPlanFailureReason.ProviderFailure, "The AI planning provider rejected the request.");
            }

            AnthropicMessageResponse? parsed;
            try
            {
                parsed = await httpResponse.Content.ReadFromJsonAsync<AnthropicMessageResponse>(cancellationToken: cancellationToken);
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "Anthropic planning response was not valid JSON.");
                throw new PluginPlanException(PluginPlanFailureReason.MalformedProviderOutput, "The AI planning provider returned a malformed response.");
            }

            return ExtractPlanningResult(parsed);
        }
    }

    private static AnthropicToolDefinition BuildToolDefinition()
    {
        // JSON Schema describing the exact structured shape we require back.
        var schema = new
        {
            type = "object",
            properties = new
            {
                name = new { type = "string" },
                slug = new { type = "string" },
                description = new { type = "string" },
                version = new { type = "string" },
                author = new { type = "string" },
                features = new { type = "array", items = new { type = "string" } },
                unsupportedRequirements = new { type = "array", items = new { type = "string" } },
            },
            required = new[] { "name", "slug", "description", "version", "author", "features", "unsupportedRequirements" },
        };

        return new AnthropicToolDefinition
        {
            Name = ToolName,
            Description = "Propose a structured WordPress plugin specification for the requested plugin.",
            InputSchema = JsonSerializer.SerializeToElement(schema),
        };
    }

    private PlanningResult ExtractPlanningResult(AnthropicMessageResponse? response)
    {
        var toolUseBlock = response?.Content?.FirstOrDefault(c => c.Type == "tool_use" && c.Name == ToolName);
        if (toolUseBlock?.Input is not JsonElement input || input.ValueKind != JsonValueKind.Object)
        {
            _logger.LogError("Anthropic planning response did not contain the expected tool_use block.");
            throw new PluginPlanException(PluginPlanFailureReason.MalformedProviderOutput, "The AI planning provider returned an unexpected response shape.");
        }

        try
        {
            var name = GetRequiredString(input, "name");
            var slug = GetRequiredString(input, "slug");
            var description = GetRequiredString(input, "description");
            var version = GetRequiredString(input, "version");
            var author = GetRequiredString(input, "author");
            var features = GetStringArray(input, "features");
            var unsupported = GetStringArray(input, "unsupportedRequirements");

            return new PlanningResult
            {
                Name = name,
                Slug = slug,
                Description = description,
                Version = version,
                Author = author,
                Features = features,
                UnsupportedRequirements = unsupported,
            };
        }
        catch (KeyNotFoundException ex)
        {
            _logger.LogError(ex, "Anthropic planning tool input was missing a required field.");
            throw new PluginPlanException(PluginPlanFailureReason.MalformedProviderOutput, "The AI planning provider returned an incomplete response.");
        }
    }

    private static string GetRequiredString(JsonElement obj, string propertyName)
    {
        if (!obj.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.String)
        {
            throw new KeyNotFoundException(propertyName);
        }

        return value.GetString()!;
    }

    private static IReadOnlyList<string> GetStringArray(JsonElement obj, string propertyName)
    {
        if (!obj.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<string>();
        }

        return value.EnumerateArray()
            .Where(e => e.ValueKind == JsonValueKind.String)
            .Select(e => e.GetString()!)
            .ToList();
    }

    private sealed class AnthropicMessageRequest
    {
        [JsonPropertyName("model")]
        public required string Model { get; init; }

        [JsonPropertyName("max_tokens")]
        public required int MaxTokens { get; init; }

        [JsonPropertyName("system")]
        public required string System { get; init; }

        [JsonPropertyName("messages")]
        public required AnthropicMessage[] Messages { get; init; }

        [JsonPropertyName("tools")]
        public required AnthropicToolDefinition[] Tools { get; init; }

        [JsonPropertyName("tool_choice")]
        public required AnthropicToolChoice ToolChoice { get; init; }
    }

    private sealed class AnthropicMessage
    {
        [JsonPropertyName("role")]
        public required string Role { get; init; }

        [JsonPropertyName("content")]
        public required string Content { get; init; }
    }

    private sealed class AnthropicToolDefinition
    {
        [JsonPropertyName("name")]
        public required string Name { get; init; }

        [JsonPropertyName("description")]
        public required string Description { get; init; }

        [JsonPropertyName("input_schema")]
        public required JsonElement InputSchema { get; init; }
    }

    private sealed class AnthropicToolChoice
    {
        [JsonPropertyName("type")]
        public required string Type { get; init; }

        [JsonPropertyName("name")]
        public required string Name { get; init; }
    }

    private sealed class AnthropicMessageResponse
    {
        [JsonPropertyName("content")]
        public AnthropicContentBlock[]? Content { get; init; }
    }

    private sealed class AnthropicContentBlock
    {
        [JsonPropertyName("type")]
        public string? Type { get; init; }

        [JsonPropertyName("name")]
        public string? Name { get; init; }

        [JsonPropertyName("input")]
        public JsonElement? Input { get; init; }
    }
}
