using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using WPAIPlugin.Api.Data;
using WPAIPlugin.Api.Security;
using Xunit;

namespace WPAIPlugin.Generator.Tests.Accounts;

/// <summary>
/// Forgot/reset password (account-recovery milestone). Uses the shared
/// AccountTestFactory fixture (one FakeTransactionalEmailSender across every
/// test in this class), so assertions always filter by this test's own
/// unique email - never assume the sent-email list contains only this test's
/// own entries. No Resend/internet required.
/// </summary>
public class PasswordRecoveryTests : IClassFixture<AccountTestFactory>
{
    private const string Password = "Str0ng!Passw0rd";
    private readonly AccountTestFactory _factory;

    public PasswordRecoveryTests(AccountTestFactory factory)
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
    public async Task ForgotPassword_KnownAccount_ReturnsGenericResultAndSendsEmail()
    {
        var client = NewClient(_factory);
        var email = await RegisterAsync(client, $"known-{Guid.NewGuid()}@example.com");

        var response = await client.PostJsonWithCsrfAsync("/api/account/forgot-password", new { email });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("If an account exists for that email address, a password reset link has been sent.", body.GetProperty("message").GetString());
        Assert.Contains(_factory.FakeEmailSender.PasswordResetsSent, r => r.ToEmail == email);
    }

    [Fact]
    public async Task ForgotPassword_UnknownAccount_ReturnsSameGenericResultAndSendsNoEmail()
    {
        var client = NewClient(_factory);
        var email = $"unknown-{Guid.NewGuid()}@example.com";

        var response = await client.PostJsonWithCsrfAsync("/api/account/forgot-password", new { email });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("If an account exists for that email address, a password reset link has been sent.", body.GetProperty("message").GetString());
        Assert.DoesNotContain(_factory.FakeEmailSender.PasswordResetsSent, r => r.ToEmail == email);
    }

    [Fact]
    public async Task ForgotPassword_KnownAndUnknown_ProduceIdenticalResponseBody()
    {
        var client = NewClient(_factory);
        var knownEmail = await RegisterAsync(client, $"same-shape-{Guid.NewGuid()}@example.com");
        var unknownEmail = $"same-shape-unknown-{Guid.NewGuid()}@example.com";

        var knownResponse = await client.PostJsonWithCsrfAsync("/api/account/forgot-password", new { email = knownEmail });
        var unknownResponse = await client.PostJsonWithCsrfAsync("/api/account/forgot-password", new { email = unknownEmail });

        Assert.Equal(knownResponse.StatusCode, unknownResponse.StatusCode);
        var knownBody = await knownResponse.Content.ReadAsStringAsync();
        var unknownBody = await unknownResponse.Content.ReadAsStringAsync();
        Assert.Equal(knownBody, unknownBody);
    }

    [Fact]
    public async Task ForgotPassword_ResetUrlUsesConfiguredPublicBaseUrl_NotBrowserSuppliedHost()
    {
        var client = NewClient(_factory);
        var email = await RegisterAsync(client, $"baseurl-{Guid.NewGuid()}@example.com");

        // Even a spoofed Host header must never influence the reset URL -
        // BuildResetUrl only ever reads App:PublicBaseUrl.
        var token = await client.GetFromJsonAsync<JsonElement>("/api/account/csrf");
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/account/forgot-password")
        {
            Content = JsonContent.Create(new { email }),
        };
        request.Headers.Add("X-CSRF-TOKEN", token.GetProperty("token").GetString());
        request.Headers.Host = "evil-attacker.example";
        (await client.SendAsync(request)).EnsureSuccessStatusCode();

        var sent = _factory.FakeEmailSender.PasswordResetsSent.Last(r => r.ToEmail == email);
        Assert.StartsWith("https://modulemint.test/reset-password.html?", sent.ResetUrl);
        Assert.DoesNotContain("evil-attacker.example", sent.ResetUrl);
    }

    [Fact]
    public async Task ResetPassword_ValidToken_ResetsPassword_OldFailsNewSucceeds()
    {
        var client = NewClient(_factory);
        var email = await RegisterAsync(client, $"reset-{Guid.NewGuid()}@example.com");
        (await client.PostJsonWithCsrfAsync("/api/account/forgot-password", new { email })).EnsureSuccessStatusCode();
        var resetUrl = _factory.FakeEmailSender.PasswordResetsSent.Last(r => r.ToEmail == email).ResetUrl;
        var token = System.Web.HttpUtility.ParseQueryString(new Uri(resetUrl).Query)["token"]!;

        const string newPassword = "N3wStr0ng!Passw0rd";
        var resetResponse = await client.PostJsonWithCsrfAsync("/api/account/reset-password", new
        {
            email, token, newPassword, confirmPassword = newPassword,
        });
        Assert.Equal(HttpStatusCode.OK, resetResponse.StatusCode);

        (await client.PostWithCsrfAsync("/api/account/logout", null)).EnsureSuccessStatusCode();
        var oldLogin = await client.PostJsonWithCsrfAsync("/api/account/login", new { email, password = Password });
        Assert.Equal(HttpStatusCode.Unauthorized, oldLogin.StatusCode);
        var newLogin = await client.PostJsonWithCsrfAsync("/api/account/login", new { email, password = newPassword });
        Assert.Equal(HttpStatusCode.OK, newLogin.StatusCode);
    }

    [Fact]
    public async Task ResetPassword_InvalidToken_RejectedSafely()
    {
        var client = NewClient(_factory);
        var email = await RegisterAsync(client, $"badtoken-{Guid.NewGuid()}@example.com");

        var response = await client.PostJsonWithCsrfAsync("/api/account/reset-password", new
        {
            email, token = "not-a-real-token", newPassword = "N3wStr0ng!Passw0rd", confirmPassword = "N3wStr0ng!Passw0rd",
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("This reset link is invalid or has expired. Please request a new one.", body.GetProperty("error").GetString());
    }

    [Fact]
    public async Task ResetPassword_UnknownEmail_ReturnsSameInvalidLinkMessageAsInvalidToken()
    {
        var client = NewClient(_factory);

        var response = await client.PostJsonWithCsrfAsync("/api/account/reset-password", new
        {
            email = $"never-registered-{Guid.NewGuid()}@example.com", token = "whatever",
            newPassword = "N3wStr0ng!Passw0rd", confirmPassword = "N3wStr0ng!Passw0rd",
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("This reset link is invalid or has expired. Please request a new one.", body.GetProperty("error").GetString());
    }

    [Fact]
    public async Task ResetPassword_PasswordConfirmationMismatch_Rejected()
    {
        var client = NewClient(_factory);
        var email = await RegisterAsync(client, $"mismatch-{Guid.NewGuid()}@example.com");
        (await client.PostJsonWithCsrfAsync("/api/account/forgot-password", new { email })).EnsureSuccessStatusCode();
        var resetUrl = _factory.FakeEmailSender.PasswordResetsSent.Last(r => r.ToEmail == email).ResetUrl;
        var token = System.Web.HttpUtility.ParseQueryString(new Uri(resetUrl).Query)["token"]!;

        var response = await client.PostJsonWithCsrfAsync("/api/account/reset-password", new
        {
            email, token, newPassword = "N3wStr0ng!Passw0rd", confirmPassword = "Different!Pass1",
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Passwords do not match.", body.GetProperty("error").GetString());
    }

    [Fact]
    public async Task ForgotPassword_RateLimitEnforced()
    {
        using var limited = _factory.WithWebHostBuilder(b => b.ConfigureServices(s =>
            s.PostConfigure<SecurityOptions>(o => o.PasswordRecoveryPerFiveMinutes = 1)));
        var client = limited.CreateClient();
        var email = $"ratelimit-{Guid.NewGuid()}@example.com";

        (await client.PostJsonWithCsrfAsync("/api/account/forgot-password", new { email })).EnsureSuccessStatusCode();
        var second = await client.PostJsonWithCsrfAsync("/api/account/forgot-password", new { email });

        Assert.Equal(HttpStatusCode.TooManyRequests, second.StatusCode);
        Assert.NotNull(second.Headers.RetryAfter);
    }
}
