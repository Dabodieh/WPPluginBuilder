namespace WPAIPlugin.Api.Security;

/// <summary>
/// Verifies a Cloudflare Turnstile response token server-side. The only seam
/// registration depends on - tests substitute a deterministic fake instead
/// of ever calling Cloudflare.
/// </summary>
public interface ITurnstileVerifier
{
    /// <summary>
    /// True only if Cloudflare confirms the token as a genuine, unexpired,
    /// not-already-consumed solve. Any missing token, network failure,
    /// non-success provider response, or malformed provider response must
    /// return false (fail closed) - never throw for a normal verification
    /// outcome.
    /// </summary>
    Task<bool> VerifyAsync(string? token, string? remoteIp, CancellationToken cancellationToken = default);
}
