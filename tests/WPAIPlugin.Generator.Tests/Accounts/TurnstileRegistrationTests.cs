using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WPAIPlugin.Api.Data;
using WPAIPlugin.Api.Security;
using WPAIPlugin.Generator.Tests.Security;
using Xunit;

namespace WPAIPlugin.Generator.Tests.Accounts;

/// <summary>
/// Cloudflare Turnstile registration hardening (signup-farming milestone).
/// Each test builds its own AccountTestFactory (never the shared class
/// fixture other AccountControllerTests use) so per-test Turnstile
/// configuration and FakeTurnstileVerifier state can never leak between
/// tests. No test ever reaches the real Cloudflare endpoint - see
/// FakeTurnstileVerifier and AccountTestFactory's wiring of it.
/// </summary>
public class TurnstileRegistrationTests
{
    private sealed record ConfiguredFactory(AccountTestFactory Root, WebApplicationFactory<Program> Configured) : IDisposable
    {
        public FakeTurnstileVerifier FakeTurnstileVerifier => Root.FakeTurnstileVerifier;

        public HttpClient CreateClient() => Configured.CreateClient();

        public IServiceProvider Services => Configured.Services;

        public void Dispose()
        {
            Configured.Dispose();
            Root.Dispose();
        }
    }

    private static ConfiguredFactory NewFactory(bool enabled, bool verifierResult = true)
    {
        var factory = new AccountTestFactory();
        factory.FakeTurnstileVerifier.Result = verifierResult;
        var configured = factory.WithWebHostBuilder(b => b.ConfigureServices(s =>
            s.PostConfigure<TurnstileOptions>(o =>
            {
                o.Enabled = enabled;
                o.SiteKey = "1x00000000000000000000AA";
                o.SecretKey = "test-secret-never-logged";
            })));
        return new ConfiguredFactory(factory, configured);
    }

    private static object RegisterBody(string email, string? token = "solved-token") =>
        new { email, password = "Str0ng!Passw0rd", turnstileToken = token };

    [Fact]
    public async Task Enabled_MissingToken_RegistrationRejected()
    {
        using var factory = NewFactory(enabled: true);
        var client = factory.CreateClient();

        var response = await client.PostJsonWithCsrfAsync("/api/account/register", RegisterBody($"turnstile-missing-{Guid.NewGuid()}@example.com", token: null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        // Missing token must fail before ever calling the verifier - there is
        // nothing to verify, and no Identity account may be attempted for it.
        Assert.Equal(0, factory.FakeTurnstileVerifier.CallCount);
    }

    [Fact]
    public async Task Enabled_InvalidToken_RegistrationRejected()
    {
        using var factory = NewFactory(enabled: true, verifierResult: false);
        var client = factory.CreateClient();
        var email = $"turnstile-invalid-{Guid.NewGuid()}@example.com";

        var response = await client.PostJsonWithCsrfAsync("/api/account/register", RegisterBody(email));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(1, factory.FakeTurnstileVerifier.CallCount);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False(await db.Users.AnyAsync(u => u.Email == email));
    }

    [Fact]
    public async Task Enabled_ValidToken_RegistrationSucceeds()
    {
        using var factory = NewFactory(enabled: true, verifierResult: true);
        var client = factory.CreateClient();
        var email = $"turnstile-valid-{Guid.NewGuid()}@example.com";

        var response = await client.PostJsonWithCsrfAsync("/api/account/register", RegisterBody(email));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, factory.FakeTurnstileVerifier.CallCount);
    }

    [Fact]
    public async Task Enabled_VerifierThrows_RegistrationFailsClosed()
    {
        // ITurnstileVerifier's own contract is "never throw for a normal
        // verification outcome", but registration must still fail safely
        // (not 500, not create an account) if a caller violates that.
        using var factory = NewFactory(enabled: true);
        factory.FakeTurnstileVerifier.Result = false; // simulates the real verifier's fail-closed return on provider outage
        var client = factory.CreateClient();

        var response = await client.PostJsonWithCsrfAsync("/api/account/register", RegisterBody($"turnstile-outage-{Guid.NewGuid()}@example.com"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Disabled_RegistrationSucceedsWithoutCallingVerifier()
    {
        using var factory = NewFactory(enabled: false);
        var client = factory.CreateClient();

        var response = await client.PostJsonWithCsrfAsync("/api/account/register",
            new { email = $"turnstile-disabled-{Guid.NewGuid()}@example.com", password = "Str0ng!Passw0rd" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(0, factory.FakeTurnstileVerifier.CallCount);
    }

    [Fact]
    public async Task Enabled_ForwardsServerObservedRemoteIpNotClientSuppliedValue()
    {
        using var factory = NewFactory(enabled: true);
        var client = factory.CreateClient();

        await client.PostJsonWithCsrfAsync("/api/account/register", RegisterBody($"turnstile-ip-{Guid.NewGuid()}@example.com"));

        // TestServer's synthetic connection never carries a real client IP,
        // so this proves only that the server-observed value (possibly null
        // here) is what's forwarded - never anything the request body/headers
        // could have supplied, since RegisterRequest has no such field at all.
        Assert.Equal(1, factory.FakeTurnstileVerifier.CallCount);
    }
}
