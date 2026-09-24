using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace WPAIPlugin.Generator.Tests.Accounts;

/// <summary>
/// GET /api/account (Account Management milestone) - the account-page-
/// specific view of the current user, separate from GET /api/account/me
/// which nav.js still uses. Uses the shared AccountTestFactory fixture.
/// </summary>
public class AccountInfoTests : IClassFixture<AccountTestFactory>
{
    private const string Password = "Str0ng!Passw0rd";
    private readonly AccountTestFactory _factory;

    public AccountInfoTests(AccountTestFactory factory)
    {
        _factory = factory;
    }

    private static HttpClient NewClient(AccountTestFactory factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });

    [Fact]
    public async Task GetAccount_Unauthenticated_ReturnsUnauthorized()
    {
        var client = NewClient(_factory);

        var response = await client.GetAsync("/api/account");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetAccount_Authenticated_ReturnsSafeEmailAndStatus()
    {
        var client = NewClient(_factory);
        var email = $"info-{Guid.NewGuid()}@example.com";
        (await client.PostJsonWithCsrfAsync("/api/account/register", new { email, password = Password })).EnsureSuccessStatusCode();

        var response = await client.GetAsync("/api/account");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(email, body.GetProperty("email").GetString());
        Assert.False(body.GetProperty("emailConfirmed").GetBoolean());
    }

    [Fact]
    public async Task GetAccount_ResponseDoesNotExposeIdentityInternals()
    {
        var client = NewClient(_factory);
        var email = $"safeinfo-{Guid.NewGuid()}@example.com";
        (await client.PostJsonWithCsrfAsync("/api/account/register", new { email, password = Password })).EnsureSuccessStatusCode();

        var response = await client.GetAsync("/api/account");
        var raw = await response.Content.ReadAsStringAsync();

        Assert.DoesNotContain("userId", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("securityStamp", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("concurrencyStamp", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("passwordHash", raw, StringComparison.OrdinalIgnoreCase);
        var body = JsonDocument.Parse(raw).RootElement;
        Assert.Equal(2, body.EnumerateObject().Count());
    }

    [Fact]
    public async Task PasswordPolicy_ReturnsConfiguredIdentityDefaults()
    {
        var client = NewClient(_factory);

        var response = await client.GetAsync("/api/account/password-policy");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.GetProperty("requiredLength").GetInt32() > 0);
    }
}
