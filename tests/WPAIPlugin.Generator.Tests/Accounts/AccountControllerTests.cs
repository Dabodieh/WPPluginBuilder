using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace WPAIPlugin.Generator.Tests.Accounts;

public class AccountControllerTests : IClassFixture<AccountTestFactory>
{
    private readonly AccountTestFactory _factory;

    public AccountControllerTests(AccountTestFactory factory)
    {
        _factory = factory;
    }

    private static HttpClient NewClient(AccountTestFactory factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });

    [Fact]
    public async Task Register_NewEmail_Succeeds()
    {
        var client = NewClient(_factory);

        var response = await client.PostAsJsonAsync("/api/account/register", new
        {
            email = $"user-{Guid.NewGuid()}@example.com",
            password = "Str0ng!Passw0rd",
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Register_DuplicateEmail_IsRejected()
    {
        var client = NewClient(_factory);
        var email = $"dup-{Guid.NewGuid()}@example.com";
        var request = new { email, password = "Str0ng!Passw0rd" };

        await client.PostAsJsonAsync("/api/account/register", request);
        var second = await client.PostAsJsonAsync("/api/account/register", request);

        Assert.Equal(HttpStatusCode.BadRequest, second.StatusCode);
    }

    [Fact]
    public async Task Login_CorrectCredentials_Succeeds()
    {
        var client = NewClient(_factory);
        var email = $"login-{Guid.NewGuid()}@example.com";
        var password = "Str0ng!Passw0rd";
        await client.PostAsJsonAsync("/api/account/register", new { email, password });
        await client.PostAsync("/api/account/logout", null);

        var response = await client.PostAsJsonAsync("/api/account/login", new { email, password });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Login_WrongPassword_Fails()
    {
        var client = NewClient(_factory);
        var email = $"wrongpw-{Guid.NewGuid()}@example.com";
        await client.PostAsJsonAsync("/api/account/register", new { email, password = "Str0ng!Passw0rd" });
        await client.PostAsync("/api/account/logout", null);

        var response = await client.PostAsJsonAsync("/api/account/login", new { email, password = "wrong-password" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Logout_RemovesAuthenticatedSession()
    {
        var client = NewClient(_factory);
        var email = $"logout-{Guid.NewGuid()}@example.com";
        await client.PostAsJsonAsync("/api/account/register", new { email, password = "Str0ng!Passw0rd" });

        await client.PostAsync("/api/account/logout", null);
        var meResponse = await client.GetAsync("/api/account/me");

        Assert.Equal(HttpStatusCode.Unauthorized, meResponse.StatusCode);
    }

    [Fact]
    public async Task Me_Authenticated_ReturnsEmail()
    {
        var client = NewClient(_factory);
        var email = $"me-{Guid.NewGuid()}@example.com";
        await client.PostAsJsonAsync("/api/account/register", new { email, password = "Str0ng!Passw0rd" });

        var response = await client.GetAsync("/api/account/me");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Me_Unauthenticated_ReturnsUnauthorized()
    {
        var client = NewClient(_factory);

        var response = await client.GetAsync("/api/account/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
