using WPAIPlugin.Api.Security;

namespace WPAIPlugin.Generator.Tests.Security;

/// <summary>
/// Deterministic <see cref="ITurnstileVerifier"/> substitute - no test ever
/// calls the real Cloudflare endpoint. Defaults to always-succeed so every
/// existing registration flow across the test suite keeps working
/// regardless of whether a given test enables Turnstile; individual tests
/// that specifically exercise the reject/error paths set <see cref="Result"/>.
/// </summary>
public sealed class FakeTurnstileVerifier : ITurnstileVerifier
{
    public bool Result { get; set; } = true;

    public string? LastToken { get; private set; }

    public string? LastRemoteIp { get; private set; }

    public int CallCount { get; private set; }

    public Task<bool> VerifyAsync(string? token, string? remoteIp, CancellationToken cancellationToken = default)
    {
        CallCount++;
        LastToken = token;
        LastRemoteIp = remoteIp;
        return Task.FromResult(Result);
    }
}
