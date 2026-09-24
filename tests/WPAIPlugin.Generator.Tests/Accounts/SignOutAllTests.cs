using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace WPAIPlugin.Generator.Tests.Accounts;

/// <summary>
/// POST /api/account/signout-all (Account Management milestone). Rotates
/// the Identity security stamp so any other outstanding auth cookie for the
/// account is rejected on its next SecurityStampValidator pass, and signs
/// out the current request immediately. No custom session table.
/// </summary>
public class SignOutAllTests : IClassFixture<AccountTestFactory>
{
    private const string Password = "Str0ng!Passw0rd";
    private readonly AccountTestFactory _factory;

    public SignOutAllTests(AccountTestFactory factory)
    {
        _factory = factory;
    }

    private static HttpClient NewClient(AccountTestFactory factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });

    private async Task<string> RegisterAsync(HttpClient client, string email)
    {
        (await client.PostJsonWithCsrfAsync("/api/account/register", new { email, password = Password })).EnsureSuccessStatusCode();
        return email;
    }

    [Fact]
    public async Task SignOutAll_Unauthenticated_ReturnsUnauthorized()
    {
        var client = NewClient(_factory);

        var response = await client.PostWithCsrfAsync("/api/account/signout-all", null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task SignOutAll_SignsOutCurrentSession()
    {
        var client = NewClient(_factory);
        await RegisterAsync(client, $"signoutall-current-{Guid.NewGuid()}@example.com");

        (await client.PostWithCsrfAsync("/api/account/signout-all", null)).EnsureSuccessStatusCode();
        var meResponse = await client.GetAsync("/api/account/me");

        Assert.Equal(HttpStatusCode.Unauthorized, meResponse.StatusCode);
    }

    [Fact]
    public async Task SignOutAll_InvalidatesASeparateExistingSession()
    {
        // SecurityStampValidator normally revalidates on a timed interval
        // rather than every request; force it to revalidate immediately so
        // this test can observe invalidation deterministically.
        using var factory = _factory.WithWebHostBuilder(b => b.ConfigureServices(s =>
            s.Configure<SecurityStampValidatorOptions>(o => o.ValidationInterval = TimeSpan.Zero)));

        // Two separate HttpClients simulate two devices signed into the same
        // account - each holds its own cookie container.
        var deviceA = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        var email = $"signoutall-other-{Guid.NewGuid()}@example.com";
        (await deviceA.PostJsonWithCsrfAsync("/api/account/register", new { email, password = Password })).EnsureSuccessStatusCode();

        var deviceB = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        (await deviceB.PostJsonWithCsrfAsync("/api/account/login", new { email, password = Password })).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.OK, (await deviceB.GetAsync("/api/account/me")).StatusCode);

        (await deviceA.PostWithCsrfAsync("/api/account/signout-all", null)).EnsureSuccessStatusCode();

        var deviceBAfter = await deviceB.GetAsync("/api/account/me");
        Assert.Equal(HttpStatusCode.Unauthorized, deviceBAfter.StatusCode);
    }
}
