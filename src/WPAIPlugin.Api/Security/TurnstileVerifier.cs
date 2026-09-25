using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace WPAIPlugin.Api.Security;

/// <summary>
/// Real Cloudflare Turnstile verification via the fixed, hard-coded
/// siteverify endpoint - never a caller-influenced URL/host/scheme. Fails
/// closed on every ambiguous outcome (missing token, missing secret, network
/// failure, non-success HTTP status, malformed response): only an explicit
/// Cloudflare success:true is ever a pass. Never logs the token or the
/// configured secret - only status codes / exception type names, matching
/// this project's existing provider-failure logging convention.
/// </summary>
public sealed class TurnstileVerifier : ITurnstileVerifier
{
    private const string VerifyUrl = "https://challenges.cloudflare.com/turnstile/v0/siteverify";

    private readonly HttpClient _httpClient;
    private readonly IOptionsMonitor<TurnstileOptions> _options;
    private readonly ILogger<TurnstileVerifier> _logger;

    public TurnstileVerifier(HttpClient httpClient, IOptionsMonitor<TurnstileOptions> options, ILogger<TurnstileVerifier> logger)
    {
        _httpClient = httpClient;
        _options = options;
        _logger = logger;
    }

    public async Task<bool> VerifyAsync(string? token, string? remoteIp, CancellationToken cancellationToken = default)
    {
        var secret = _options.CurrentValue.SecretKey;
        if (string.IsNullOrWhiteSpace(secret) || string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        var fields = new Dictionary<string, string> { ["secret"] = secret, ["response"] = token };
        if (!string.IsNullOrWhiteSpace(remoteIp))
        {
            fields["remoteip"] = remoteIp;
        }

        try
        {
            using var content = new FormUrlEncodedContent(fields);
            using var response = await _httpClient.PostAsync(VerifyUrl, content, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Turnstile verification request failed with status {StatusCode}.", (int)response.StatusCode);
                return false;
            }

            var result = await response.Content.ReadFromJsonAsync<TurnstileVerifyResponse>(cancellationToken: cancellationToken);
            return result?.Success == true;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("Turnstile verification request timed out.");
            return false;
        }
        catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException)
        {
            _logger.LogWarning("Turnstile verification request could not be completed. Failure category: {FailureType}.", ex.GetType().Name);
            return false;
        }
    }

    private sealed class TurnstileVerifyResponse
    {
        [JsonPropertyName("success")]
        public bool Success { get; init; }
    }
}
