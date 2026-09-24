using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace WPAIPlugin.Generator.Tests.Accounts;

/// <summary>
/// POST /api/account/change-email and /api/account/confirm-email-change
/// (Account Management milestone). Uses Identity's GenerateChangeEmailToken/
/// ChangeEmailAsync token flow - never overwrites Email until the new
/// address is confirmed. Shared AccountTestFactory fixture, unique email per
/// test, one FakeTransactionalEmailSender across every test in this class.
/// </summary>
public class EmailChangeTests : IClassFixture<AccountTestFactory>
{
    private const string Password = "Str0ng!Passw0rd";
    private readonly AccountTestFactory _factory;

    public EmailChangeTests(AccountTestFactory factory)
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
    public async Task ChangeEmail_WrongCurrentPassword_IsRejected()
    {
        var client = NewClient(_factory);
        await RegisterAsync(client, $"emailwrongpw-{Guid.NewGuid()}@example.com");

        var response = await client.PostJsonWithCsrfAsync("/api/account/change-email", new
        {
            newEmail = $"newaddr-{Guid.NewGuid()}@example.com",
            currentPassword = "not-the-real-password",
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ChangeEmail_CorrectPassword_SendsConfirmationToNewAddressOnly_AndDoesNotChangeLoginEmailYet()
    {
        var client = NewClient(_factory);
        var email = await RegisterAsync(client, $"emailstart-{Guid.NewGuid()}@example.com");
        var newEmail = $"newaddr-{Guid.NewGuid()}@example.com";

        var response = await client.PostJsonWithCsrfAsync("/api/account/change-email", new { newEmail, currentPassword = Password });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Single(_factory.FakeEmailSender.EmailChangeConfirmationsSent.Where(s => s.ToEmail == newEmail));

        var me = await client.GetFromJsonAsync<JsonElement>("/api/account/me");
        Assert.Equal(email, me.GetProperty("email").GetString());
    }

    [Fact]
    public async Task ConfirmEmailChange_ValidToken_UpdatesEmailAndUserName()
    {
        var client = NewClient(_factory);
        var email = await RegisterAsync(client, $"emailconfirm-{Guid.NewGuid()}@example.com");
        var newEmail = $"newaddr-{Guid.NewGuid()}@example.com";
        (await client.PostJsonWithCsrfAsync("/api/account/change-email", new { newEmail, currentPassword = Password })).EnsureSuccessStatusCode();
        var confirmUrl = _factory.FakeEmailSender.EmailChangeConfirmationsSent.Last(s => s.ToEmail == newEmail).ConfirmUrl;
        var query = System.Web.HttpUtility.ParseQueryString(new Uri(confirmUrl).Query);

        var response = await client.PostJsonWithCsrfAsync("/api/account/confirm-email-change", new
        {
            currentEmail = email,
            newEmail,
            token = query["token"],
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var me = await client.GetFromJsonAsync<JsonElement>("/api/account/me");
        Assert.Equal(newEmail, me.GetProperty("email").GetString());

        // Login now requires the new email, proving UserName was kept in
        // sync with Email (registration sets UserName = Email).
        await client.PostWithCsrfAsync("/api/account/logout", null);
        var loginWithNew = await client.PostJsonWithCsrfAsync("/api/account/login", new { email = newEmail, password = Password });
        Assert.Equal(HttpStatusCode.OK, loginWithNew.StatusCode);
    }

    [Fact]
    public async Task ConfirmEmailChange_InvalidToken_IsSafelyRejected()
    {
        var client = NewClient(_factory);
        var email = await RegisterAsync(client, $"emailbadtoken-{Guid.NewGuid()}@example.com");
        var newEmail = $"newaddr-{Guid.NewGuid()}@example.com";

        var response = await client.PostJsonWithCsrfAsync("/api/account/confirm-email-change", new
        {
            currentEmail = email,
            newEmail,
            token = "not-a-real-token",
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var me = await client.GetFromJsonAsync<JsonElement>("/api/account/me");
        Assert.Equal(email, me.GetProperty("email").GetString());
    }

    [Fact]
    public async Task ChangeEmail_Unauthenticated_ReturnsUnauthorized()
    {
        var client = NewClient(_factory);

        var response = await client.PostJsonWithCsrfAsync("/api/account/change-email", new
        {
            newEmail = $"newaddr-{Guid.NewGuid()}@example.com",
            currentPassword = Password,
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
