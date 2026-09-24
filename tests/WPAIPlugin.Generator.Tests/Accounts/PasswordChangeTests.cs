using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace WPAIPlugin.Generator.Tests.Accounts;

/// <summary>
/// POST /api/account/change-password (Account Management milestone). Uses
/// UserManager.ChangePasswordAsync exclusively - no manual hashing anywhere
/// under test. Shared AccountTestFactory fixture, unique email per test.
/// </summary>
public class PasswordChangeTests : IClassFixture<AccountTestFactory>
{
    private const string Password = "Str0ng!Passw0rd";
    private const string NewPassword = "Ev3nStr0nger!Pw";
    private readonly AccountTestFactory _factory;

    public PasswordChangeTests(AccountTestFactory factory)
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
    public async Task ChangePassword_CorrectCurrentPassword_Succeeds()
    {
        var client = NewClient(_factory);
        await RegisterAsync(client, $"pwok-{Guid.NewGuid()}@example.com");

        var response = await client.PostJsonWithCsrfAsync("/api/account/change-password", new
        {
            currentPassword = Password,
            newPassword = NewPassword,
            confirmPassword = NewPassword,
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.GetProperty("success").GetBoolean());
    }

    [Fact]
    public async Task ChangePassword_WrongCurrentPassword_IsRejected()
    {
        var client = NewClient(_factory);
        await RegisterAsync(client, $"pwwrong-{Guid.NewGuid()}@example.com");

        var response = await client.PostJsonWithCsrfAsync("/api/account/change-password", new
        {
            currentPassword = "not-the-real-password",
            newPassword = NewPassword,
            confirmPassword = NewPassword,
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Current password is incorrect.", body.GetProperty("error").GetString());
    }

    [Fact]
    public async Task ChangePassword_WeakNewPassword_IsRejected()
    {
        var client = NewClient(_factory);
        await RegisterAsync(client, $"pwweak-{Guid.NewGuid()}@example.com");

        var response = await client.PostJsonWithCsrfAsync("/api/account/change-password", new
        {
            currentPassword = Password,
            newPassword = "weak",
            confirmPassword = "weak",
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ChangePassword_MismatchedConfirmation_IsRejected()
    {
        var client = NewClient(_factory);
        await RegisterAsync(client, $"pwmismatch-{Guid.NewGuid()}@example.com");

        var response = await client.PostJsonWithCsrfAsync("/api/account/change-password", new
        {
            currentPassword = Password,
            newPassword = NewPassword,
            confirmPassword = "Different!Pw1",
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("New password and confirmation do not match.", body.GetProperty("error").GetString());
    }

    [Fact]
    public async Task ChangePassword_OldPasswordNoLongerWorksAfterChange()
    {
        var client = NewClient(_factory);
        var email = await RegisterAsync(client, $"pwold-{Guid.NewGuid()}@example.com");
        (await client.PostJsonWithCsrfAsync("/api/account/change-password", new
        {
            currentPassword = Password,
            newPassword = NewPassword,
            confirmPassword = NewPassword,
        })).EnsureSuccessStatusCode();
        await client.PostWithCsrfAsync("/api/account/logout", null);

        var response = await client.PostJsonWithCsrfAsync("/api/account/login", new { email, password = Password });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ChangePassword_NewPasswordWorksAfterChange()
    {
        var client = NewClient(_factory);
        var email = await RegisterAsync(client, $"pwnew-{Guid.NewGuid()}@example.com");
        (await client.PostJsonWithCsrfAsync("/api/account/change-password", new
        {
            currentPassword = Password,
            newPassword = NewPassword,
            confirmPassword = NewPassword,
        })).EnsureSuccessStatusCode();
        await client.PostWithCsrfAsync("/api/account/logout", null);

        var response = await client.PostJsonWithCsrfAsync("/api/account/login", new { email, password = NewPassword });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ChangePassword_Unauthenticated_ReturnsUnauthorized()
    {
        var client = NewClient(_factory);

        var response = await client.PostJsonWithCsrfAsync("/api/account/change-password", new
        {
            currentPassword = Password,
            newPassword = NewPassword,
            confirmPassword = NewPassword,
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ChangePassword_WrongCurrentPassword_ErrorBodyNeverContainsSubmittedPasswords()
    {
        var client = NewClient(_factory);
        await RegisterAsync(client, $"pwleak-{Guid.NewGuid()}@example.com");
        const string attemptedCurrent = "totally-wrong-Passw0rd!";
        const string attemptedNew = "Br4nd-New-Passw0rd!";

        var response = await client.PostJsonWithCsrfAsync("/api/account/change-password", new
        {
            currentPassword = attemptedCurrent,
            newPassword = attemptedNew,
            confirmPassword = attemptedNew,
        });

        var raw = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(attemptedCurrent, raw);
        Assert.DoesNotContain(attemptedNew, raw);
    }
}
