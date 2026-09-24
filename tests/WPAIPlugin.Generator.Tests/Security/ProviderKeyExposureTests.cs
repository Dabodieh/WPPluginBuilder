using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace WPAIPlugin.Generator.Tests.Security;

/// <summary>
/// Regression guards for the provider-API-key security hardening milestone:
/// the browser-facing settings endpoint must be gone, and no customer-facing
/// response or static file may carry provider key configuration.
/// </summary>
public class ProviderKeyExposureTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public ProviderKeyExposureTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task SettingsApiKeyRoute_NoLongerExists()
    {
        var client = _factory.CreateClient();

        var getResponse = await client.GetAsync("/api/settings/anthropic-api-key");
        var postResponse = await client.PostAsJsonAsync("/api/settings/anthropic-api-key", new { apiKey = "sk-ant-test" });

        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, postResponse.StatusCode);
    }

    [Fact]
    public async Task PlanFailureResponse_NeverContainsConfiguredKey()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/plugins/plan", new { description = "Build a staff directory" });
        var body = await response.Content.ReadAsStringAsync();

        Assert.DoesNotContain("ApiKey", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sk-ant-", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sk-", body, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("/index.html")]
    [InlineData("/index.js")]
    [InlineData("/site.css")]
    [InlineData("/builder.html")]
    [InlineData("/login.html")]
    [InlineData("/register.html")]
    [InlineData("/dashboard.html")]
    [InlineData("/app.js")]
    public async Task StaticFrontendFiles_NeverContainKeyConfiguration(string path)
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync(path);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync();

        Assert.DoesNotContain("apiKey", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("api-key", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sk-ant-", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("OPENAI_API_KEY", body, StringComparison.OrdinalIgnoreCase);
    }
}
