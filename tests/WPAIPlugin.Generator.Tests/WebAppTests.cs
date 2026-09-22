using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using WPAIPlugin.Generator.Models;
using Xunit;

namespace WPAIPlugin.Generator.Tests;

/// <summary>
/// End-to-end HTTP-level checks that the Milestone 7 static UI is served and
/// that it did not change the behavior of the existing /plan and /build
/// endpoints. Not a test of HTML/CSS/JS content.
/// </summary>
public class WebAppTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public WebAppTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Root_ServesUiIndexPage()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/");

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Plan Plugin", body);
    }

    [Fact]
    public async Task Plan_BlankDescription_ReturnsBadRequest()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/plugins/plan", new { description = "" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
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
