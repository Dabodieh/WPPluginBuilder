using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WPAIPlugin.Planning;
using WPAIPlugin.Planning.Providers.Anthropic;
using Xunit;

namespace WPAIPlugin.Generator.Tests.Planning;

public class AnthropicPlanningProviderTests
{
    private static AnthropicPlanningProvider CreateProvider(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler,
        string apiKey = "test-key")
    {
        var httpClient = new HttpClient(new FakeHttpMessageHandler(handler));
        var options = new PlanningOptions
        {
            Anthropic = new AnthropicOptions { ApiKey = apiKey, Model = "claude-sonnet-5", BaseUrl = "https://api.anthropic.test" },
        };

        return new AnthropicPlanningProvider(httpClient, new FakeOptionsMonitor<PlanningOptions>(options), NullLogger<AnthropicPlanningProvider>.Instance);
    }

    [Fact]
    public async Task PlanAsync_ValidToolUseResponse_ReturnsPlanningResult()
    {
        const string responseJson = """
        {
          "content": [
            {
              "type": "tool_use",
              "name": "propose_plugin_spec",
              "input": {
                "name": "Staff Directory",
                "slug": "staff-directory",
                "description": "A simple staff directory plugin.",
                "version": "1.0.0",
                "author": "WPAI Plugin Builder",
                "features": ["shortcode"],
                "unsupportedRequirements": []
              }
            }
          ]
        }
        """;

        var provider = CreateProvider((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(responseJson, System.Text.Encoding.UTF8, "application/json"),
        }));

        var result = await provider.PlanAsync(new PlanningRequest { Description = "Create a staff directory plugin." });

        Assert.Equal("staff-directory", result.Slug);
        Assert.Contains("shortcode", result.Features);
        Assert.Empty(result.UnsupportedRequirements);
    }

    [Fact]
    public async Task PlanAsync_MissingApiKey_ThrowsProviderFailure()
    {
        var provider = CreateProvider((_, _) => throw new InvalidOperationException("should not be called"), apiKey: "");

        var ex = await Assert.ThrowsAsync<PluginPlanException>(() => provider.PlanAsync(new PlanningRequest { Description = "x" }));

        Assert.Equal(PluginPlanFailureReason.ProviderFailure, ex.Reason);
    }

    [Fact]
    public async Task PlanAsync_MalformedJsonResponse_ThrowsMalformedProviderOutput()
    {
        var provider = CreateProvider((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{ not valid json", System.Text.Encoding.UTF8, "application/json"),
        }));

        var ex = await Assert.ThrowsAsync<PluginPlanException>(() => provider.PlanAsync(new PlanningRequest { Description = "x" }));

        Assert.Equal(PluginPlanFailureReason.MalformedProviderOutput, ex.Reason);
    }

    [Fact]
    public async Task PlanAsync_ResponseMissingToolUseBlock_ThrowsMalformedProviderOutput()
    {
        const string responseJson = """{ "content": [ { "type": "text", "text": "sorry, I cannot help" } ] }""";

        var provider = CreateProvider((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(responseJson, System.Text.Encoding.UTF8, "application/json"),
        }));

        var ex = await Assert.ThrowsAsync<PluginPlanException>(() => provider.PlanAsync(new PlanningRequest { Description = "x" }));

        Assert.Equal(PluginPlanFailureReason.MalformedProviderOutput, ex.Reason);
    }

    [Fact]
    public async Task PlanAsync_ServerErrorStatus_ThrowsProviderFailure()
    {
        var provider = CreateProvider((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json"),
        }));

        var ex = await Assert.ThrowsAsync<PluginPlanException>(() => provider.PlanAsync(new PlanningRequest { Description = "x" }));

        Assert.Equal(PluginPlanFailureReason.ProviderFailure, ex.Reason);
    }

    [Fact]
    public async Task PlanAsync_HttpRequestException_ThrowsProviderFailure()
    {
        var provider = CreateProvider((_, _) => throw new HttpRequestException("connection refused"));

        var ex = await Assert.ThrowsAsync<PluginPlanException>(() => provider.PlanAsync(new PlanningRequest { Description = "x" }));

        Assert.Equal(PluginPlanFailureReason.ProviderFailure, ex.Reason);
        Assert.DoesNotContain("connection refused", ex.Message);
    }

    [Fact]
    public async Task PlanAsync_ExternalCancellation_PropagatesOperationCanceledException()
    {
        using var cts = new CancellationTokenSource();
        var provider = CreateProvider((_, ct) =>
        {
            cts.Cancel();
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => provider.PlanAsync(new PlanningRequest { Description = "x" }, cts.Token));
    }
}
