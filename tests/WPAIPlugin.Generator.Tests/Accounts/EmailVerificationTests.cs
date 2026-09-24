using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WPAIPlugin.Api.Data;
using WPAIPlugin.Api.Security;
using Xunit;

namespace WPAIPlugin.Generator.Tests.Accounts;

/// <summary>
/// Email confirmation + unverified-account resource protection. Uses the
/// shared AccountTestFactory fixture (real 5-credit/2-free-build signup
/// defaults, one FakeTransactionalEmailSender across every test in this
/// class) - assertions always filter by this test's own unique email. No
/// Resend/internet/real OpenAI/Anthropic required anywhere.
/// </summary>
public class EmailVerificationTests : IClassFixture<AccountTestFactory>
{
    private const string Password = "Str0ng!Passw0rd";
    private readonly AccountTestFactory _factory;

    public EmailVerificationTests(AccountTestFactory factory)
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

    private static async Task<bool> GetEmailConfirmedAsync(HttpClient client)
    {
        var me = await client.GetFromJsonAsync<JsonElement>("/api/account/me");
        return me.GetProperty("emailConfirmed").GetBoolean();
    }

    // ---------- Registration ----------

    [Fact]
    public async Task Register_NewAccount_BeginsUnverified()
    {
        var client = NewClient(_factory);
        await RegisterAsync(client, $"unverified-{Guid.NewGuid()}@example.com");

        Assert.False(await GetEmailConfirmedAsync(client));
    }

    [Fact]
    public async Task Register_StillGrantsExactlyFiveCreditsAndTwoFreeBuilds()
    {
        var client = NewClient(_factory);
        await RegisterAsync(client, $"grants-{Guid.NewGuid()}@example.com");

        var credits = await client.GetFromJsonAsync<JsonElement>("/api/credits");
        Assert.Equal(5, credits.GetProperty("balance").GetInt32());
        Assert.Equal(2, credits.GetProperty("freeBuildsRemaining").GetInt32());
    }

    [Fact]
    public async Task Register_SendsVerificationEmailExactlyOnce()
    {
        var client = NewClient(_factory);
        var email = await RegisterAsync(client, $"sendonce-{Guid.NewGuid()}@example.com");

        Assert.Single(_factory.FakeEmailSender.EmailConfirmationsSent.Where(s => s.ToEmail == email));
    }

    [Fact]
    public async Task Register_ConfirmationUrlUsesConfiguredPublicBaseUrl_NotBrowserSuppliedHost()
    {
        var client = NewClient(_factory);
        var email = $"confirmurl-{Guid.NewGuid()}@example.com";

        var token = await client.GetFromJsonAsync<JsonElement>("/api/account/csrf");
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/account/register")
        {
            Content = JsonContent.Create(new { email, password = Password }),
        };
        request.Headers.Add("X-CSRF-TOKEN", token.GetProperty("token").GetString());
        request.Headers.Host = "evil-attacker.example";
        (await client.SendAsync(request)).EnsureSuccessStatusCode();

        var sent = _factory.FakeEmailSender.EmailConfirmationsSent.Last(s => s.ToEmail == email);
        Assert.StartsWith("https://modulemint.test/confirm-email.html?", sent.ConfirmUrl);
        Assert.DoesNotContain("evil-attacker.example", sent.ConfirmUrl);
    }

    // ---------- Email confirmation ----------

    [Fact]
    public async Task ConfirmEmail_ValidToken_ConfirmsAccount()
    {
        var client = NewClient(_factory);
        var email = await RegisterAsync(client, $"validtoken-{Guid.NewGuid()}@example.com");
        var confirmUrl = _factory.FakeEmailSender.EmailConfirmationsSent.Last(s => s.ToEmail == email).ConfirmUrl;
        var query = System.Web.HttpUtility.ParseQueryString(new Uri(confirmUrl).Query);

        var response = await client.PostJsonWithCsrfAsync("/api/account/confirm-email", new { email, token = query["token"] });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(await GetEmailConfirmedAsync(client));
    }

    [Fact]
    public async Task ConfirmEmail_InvalidToken_SafelyRejected()
    {
        var client = NewClient(_factory);
        var email = await RegisterAsync(client, $"badtoken-{Guid.NewGuid()}@example.com");

        var response = await client.PostJsonWithCsrfAsync("/api/account/confirm-email", new { email, token = "not-a-real-token" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var message = body.GetProperty("error").GetString();
        Assert.Contains("invalid or has expired", message);
        Assert.False(await GetEmailConfirmedAsync(client));
    }

    [Fact]
    public async Task ConfirmEmail_AlreadyConfirmed_HandledSafely()
    {
        var client = NewClient(_factory);
        var email = await RegisterAsync(client, $"already-{Guid.NewGuid()}@example.com");
        var confirmUrl = _factory.FakeEmailSender.EmailConfirmationsSent.Last(s => s.ToEmail == email).ConfirmUrl;
        var query = System.Web.HttpUtility.ParseQueryString(new Uri(confirmUrl).Query);
        (await client.PostJsonWithCsrfAsync("/api/account/confirm-email", new { email, token = query["token"] })).EnsureSuccessStatusCode();

        var second = await client.PostJsonWithCsrfAsync("/api/account/confirm-email", new { email, token = query["token"] });

        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.True(await GetEmailConfirmedAsync(client));
    }

    [Fact]
    public async Task ConfirmEmail_DoesNotGrantCreditsOrFreeBuildsAgain()
    {
        var client = NewClient(_factory);
        var email = await RegisterAsync(client, $"noregrant-{Guid.NewGuid()}@example.com");
        var confirmUrl = _factory.FakeEmailSender.EmailConfirmationsSent.Last(s => s.ToEmail == email).ConfirmUrl;
        var query = System.Web.HttpUtility.ParseQueryString(new Uri(confirmUrl).Query);

        (await client.PostJsonWithCsrfAsync("/api/account/confirm-email", new { email, token = query["token"] })).EnsureSuccessStatusCode();

        var credits = await client.GetFromJsonAsync<JsonElement>("/api/credits");
        Assert.Equal(5, credits.GetProperty("balance").GetInt32());
        Assert.Equal(2, credits.GetProperty("freeBuildsRemaining").GetInt32());
    }

    // ---------- Planning protection ----------

    [Fact]
    public async Task Plan_UnverifiedUser_Returns403AndNoAiUsageEventRecorded()
    {
        var client = NewClient(_factory);
        var email = await RegisterAsync(client, $"planblocked-{Guid.NewGuid()}@example.com");

        var response = await client.PostJsonWithCsrfAsync("/api/plugins/plan", new { description = "A staff directory plugin." });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Please verify your email address before creating WordPress plugins.", body.GetProperty("error").GetString());

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await db.Users.SingleAsync(u => u.Email == email);
        Assert.False(await db.AiUsageEvents.AnyAsync(e => e.UserId == user.Id));
    }

    [Fact]
    public async Task Plan_VerifiedUser_IsNotBlockedByVerificationGate()
    {
        var client = NewClient(_factory);
        var email = await RegisterAsync(client, $"planallowed-{Guid.NewGuid()}@example.com");
        var confirmUrl = _factory.FakeEmailSender.EmailConfirmationsSent.Last(s => s.ToEmail == email).ConfirmUrl;
        var query = System.Web.HttpUtility.ParseQueryString(new Uri(confirmUrl).Query);
        (await client.PostJsonWithCsrfAsync("/api/account/confirm-email", new { email, token = query["token"] })).EnsureSuccessStatusCode();

        var response = await client.PostJsonWithCsrfAsync("/api/plugins/plan", new { description = "A staff directory plugin." });

        // No real OpenAI/Anthropic key is configured in this test host, so
        // the call cannot fully succeed - the only thing under test is that
        // the verification gate itself no longer blocks it.
        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ---------- Build protection ----------

    [Fact]
    public async Task Build_UnverifiedUser_Returns403AndConsumesNothing()
    {
        var client = NewClient(_factory);
        var email = await RegisterAsync(client, $"buildblocked-{Guid.NewGuid()}@example.com");
        var spec = new
        {
            name = "Staff Directory", slug = "staff-directory", description = "d",
            version = "1.0.0", author = "a", features = new[] { "shortcode" },
        };

        var response = await client.PostJsonWithCsrfAsync("/api/projects/build", new { spec, validated = false });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        var credits = await client.GetFromJsonAsync<JsonElement>("/api/credits");
        Assert.Equal(5, credits.GetProperty("balance").GetInt32());
        Assert.Equal(2, credits.GetProperty("freeBuildsRemaining").GetInt32());

        var projects = await client.GetFromJsonAsync<JsonElement>("/api/projects");
        Assert.Equal(0, projects.GetArrayLength());
    }

    // ---------- Resend ----------

    [Fact]
    public async Task ResendVerification_UnverifiedAuthenticatedUser_SendsNewEmail()
    {
        var client = NewClient(_factory);
        var email = await RegisterAsync(client, $"resend-{Guid.NewGuid()}@example.com");
        var sentBefore = _factory.FakeEmailSender.EmailConfirmationsSent.Count(s => s.ToEmail == email);

        var response = await client.PostWithCsrfAsync("/api/account/resend-verification", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var sentAfter = _factory.FakeEmailSender.EmailConfirmationsSent.Count(s => s.ToEmail == email);
        Assert.Equal(sentBefore + 1, sentAfter);
    }

    [Fact]
    public async Task ResendVerification_AlreadyConfirmedUser_HandledSafelyAndSendsNoEmail()
    {
        var client = NewClient(_factory);
        var email = await RegisterAsync(client, $"resendconfirmed-{Guid.NewGuid()}@example.com");
        var confirmUrl = _factory.FakeEmailSender.EmailConfirmationsSent.Last(s => s.ToEmail == email).ConfirmUrl;
        var query = System.Web.HttpUtility.ParseQueryString(new Uri(confirmUrl).Query);
        (await client.PostJsonWithCsrfAsync("/api/account/confirm-email", new { email, token = query["token"] })).EnsureSuccessStatusCode();
        var sentBefore = _factory.FakeEmailSender.EmailConfirmationsSent.Count(s => s.ToEmail == email);

        var response = await client.PostWithCsrfAsync("/api/account/resend-verification", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("If verification is required, a new verification email has been sent.", body.GetProperty("message").GetString());
        Assert.Equal(sentBefore, _factory.FakeEmailSender.EmailConfirmationsSent.Count(s => s.ToEmail == email));
    }

    [Fact]
    public async Task ResendVerification_Unauthenticated_ReturnsUnauthorized()
    {
        var client = NewClient(_factory);

        var response = await client.PostWithCsrfAsync("/api/account/resend-verification", null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ResendVerification_RateLimitEnforced()
    {
        using var limited = _factory.WithWebHostBuilder(b => b.ConfigureServices(s =>
            s.PostConfigure<SecurityOptions>(o => o.EmailVerificationResendPerFiveMinutes = 1)));
        var client = limited.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        await RegisterAsync(client, $"resendlimit-{Guid.NewGuid()}@example.com");

        (await client.PostWithCsrfAsync("/api/account/resend-verification", null)).EnsureSuccessStatusCode();
        var second = await client.PostWithCsrfAsync("/api/account/resend-verification", null);

        Assert.Equal(HttpStatusCode.TooManyRequests, second.StatusCode);
    }

    [Fact]
    public void ResendVerification_HasNoPublicArbitraryEmailOverload()
    {
        // The endpoint takes no request body at all - it can only ever act on
        // the current authenticated user, never a client-supplied address.
        var method = typeof(WPAIPlugin.Api.Controllers.AccountController).GetMethod("ResendVerification");
        Assert.NotNull(method);
        Assert.DoesNotContain(method!.GetParameters(), p => p.ParameterType == typeof(string) || p.Name == "email");
    }
}
