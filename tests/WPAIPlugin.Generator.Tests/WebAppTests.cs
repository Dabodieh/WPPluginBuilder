using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using WPAIPlugin.Generator.Models;
using Xunit;

namespace WPAIPlugin.Generator.Tests;

/// <summary>
/// End-to-end HTTP-level checks that the public homepage and application UI are served and
/// that it did not change the behavior of the existing /plan and /build
/// endpoints.
/// </summary>
public class WebAppTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public WebAppTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Root_ServesLandingPage()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/");

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Build WordPress plugins with", body);
        Assert.Contains("href=\"register.html\"", body);
        Assert.Contains("href=\"login.html\"", body);
        Assert.Contains("id=\"how-it-works\"", body);
        Assert.Contains("id=\"features\"", body);
        Assert.DoesNotContain("/api/plugins/build", body);
        Assert.DoesNotContain("apiKey", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("OpenAI", body, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("/builder.html", "Create Plan")]
    [InlineData("/dashboard.html", "My Plugins")]
    public async Task Application_ServesUiPage(string path, string expectedText)
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync(path);

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains(expectedText, body);
    }

    [Theory]
    [InlineData("/privacy.html", "Privacy Policy")]
    [InlineData("/terms.html", "Terms of Service")]
    [InlineData("/refunds.html", "Refund Policy")]
    [InlineData("/support.html", "Support")]
    [InlineData("/forgot-password.html", "Reset your password")]
    [InlineData("/reset-password.html", "Choose a new password")]
    public async Task LegalAndRecoveryPage_IsAvailable(string path, string expectedText)
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync(path);

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains(expectedText, body);
    }

    [Fact]
    public async Task Homepage_FooterLinksToLegalPages()
    {
        var client = _factory.CreateClient();

        var body = await client.GetStringAsync("/");

        Assert.Contains("href=\"privacy.html\"", body);
        Assert.Contains("href=\"terms.html\"", body);
        Assert.Contains("href=\"refunds.html\"", body);
        Assert.Contains("href=\"support.html\"", body);
    }

    [Fact]
    public async Task PublicConfig_ExposesSignupOfferAndCheapestBasePack_Anonymously()
    {
        using var configured = _factory.WithWebHostBuilder(b => b.ConfigureServices(s =>
        {
            s.PostConfigure<WPAIPlugin.Api.Promotions.PromotionsOptions>(o => o.SignupFreeBuilds = 3);
            s.PostConfigure<WPAIPlugin.Api.Payments.CreditPackOptions>(o => o.Packs = new Dictionary<string, WPAIPlugin.Api.Payments.CreditPack>
            {
                ["big"] = new() { DisplayName = "Big", Credits = 200, AmountMinor = 1999, Currency = "GBP" },
                ["small"] = new() { DisplayName = "Small", Credits = 25, AmountMinor = 499, Currency = "GBP" },
            });
        }));
        var client = configured.CreateClient();

        var response = await client.GetAsync("/api/config/public");

        response.EnsureSuccessStatusCode();
        var raw = await response.Content.ReadAsStringAsync();
        var body = System.Text.Json.JsonDocument.Parse(raw).RootElement;
        Assert.Equal(3, body.GetProperty("signupFreeBuilds").GetInt32());
        var pack = body.GetProperty("startingPack");
        Assert.Equal(25, pack.GetProperty("credits").GetInt32());
        Assert.Equal(499, pack.GetProperty("amountMinor").GetInt32());
        Assert.Equal("GBP", pack.GetProperty("currency").GetString());
        Assert.DoesNotContain("key", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret", raw, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Login_LinksToForgotPassword()
    {
        var client = _factory.CreateClient();

        var body = await client.GetStringAsync("/login.html");

        Assert.Contains("href=\"forgot-password.html\"", body);
    }

    [Fact]
    public async Task Billing_LinksToTermsAndRefunds()
    {
        var client = _factory.CreateClient();

        var body = await client.GetStringAsync("/billing.html");

        Assert.Contains("href=\"terms.html\"", body);
        Assert.Contains("href=\"refunds.html\"", body);
    }

    [Fact]
    public async Task ModuleMintBranding_ShownOnPublicPages()
    {
        var client = _factory.CreateClient();

        foreach (var path in new[] { "/", "/login.html", "/register.html", "/forgot-password.html" })
        {
            var body = await client.GetStringAsync(path);
            Assert.Contains("ModuleMint", body);
        }
    }

    [Fact]
    public async Task Plan_Unauthenticated_ReturnsUnauthorized()
    {
        // /api/plugins/plan requires authentication (Milestone 11): it consumes
        // a paid AI provider call. Covered further in Security/AuthenticationBoundaryTests.
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/plugins/plan", new { description = "" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Build_ValidSpec_ReturnsZip()
    {
        var client = _factory.CreateClient();
        var spec = new PluginSpec
        {
            Name = "Staff Directory",
            Slug = "staff-directory",
            Description = "Simple staff directory plugin",
            Version = "1.0.0",
            Author = "AI Plugin Builder",
            Features = new List<string> { "shortcode" },
        };

        var response = await client.PostAsJsonAsync("/api/plugins/build", spec);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/zip", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Build_InvalidSlug_ReturnsBadRequest()
    {
        var client = _factory.CreateClient();
        var spec = new PluginSpec
        {
            Name = "Staff Directory",
            Slug = "../evil",
            Description = "Simple staff directory plugin",
            Version = "1.0.0",
            Author = "AI Plugin Builder",
            Features = new List<string>(),
        };

        var response = await client.PostAsJsonAsync("/api/plugins/build", spec);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
