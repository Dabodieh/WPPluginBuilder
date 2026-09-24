using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WPAIPlugin.Planning;
using WPAIPlugin.Planning.Providers.OpenAI;
using Xunit;

namespace WPAIPlugin.Generator.Tests.Planning;

public class OpenAIPlanningProviderTests
{
    private static OpenAIPlanningProvider CreateProvider(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler,
        string apiKey = "test-key")
    {
        var httpClient = new HttpClient(new FakeHttpMessageHandler(handler));
        var options = new PlanningOptions
        {
            OpenAI = new OpenAIOptions { ApiKey = apiKey, Model = "gpt-5.6-luna", BaseUrl = "https://api.openai.test" },
        };

        return new OpenAIPlanningProvider(httpClient, new FakeOptionsMonitor<PlanningOptions>(options), NullLogger<OpenAIPlanningProvider>.Instance);
    }

    private static string WrapOutputText(string jsonSpec) => $$"""
        {
          "output": [
            {
              "type": "message",
              "content": [
                { "type": "output_text", "text": {{System.Text.Json.JsonSerializer.Serialize(jsonSpec)}} }
              ]
            }
          ]
        }
        """;

    [Fact]
    public async Task PlanAsync_ValidStructuredResponse_ReturnsPlanningResult()
    {
        const string specJson = """
        {
          "decision": "allow",
          "rejectionReason": null,
          "name": "Staff Directory",
          "slug": "staff-directory",
          "description": "A simple staff directory plugin.",
          "version": "1.0.0",
          "author": "WPAI Plugin Builder",
          "features": ["shortcode"],
          "unsupportedRequirements": [],
          "customPostType": null,
          "settingsPage": null,
          "customFields": null,
          "scheduledTask": null
        }
        """;

        var provider = CreateProvider((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(WrapOutputText(specJson), System.Text.Encoding.UTF8, "application/json"),
        }));

        var result = await provider.PlanAsync(new PlanningRequest { Description = "Create a staff directory plugin." });

        Assert.Equal("staff-directory", result.Slug);
        Assert.Contains("shortcode", result.Features);
        Assert.Empty(result.UnsupportedRequirements);
    }

    [Fact]
    public async Task PlanAsync_RejectDecision_ThrowsOutOfScopeWithFixedSafeMessageAndUsage()
    {
        const string specJson = """
        {
          "decision": "reject",
          "rejectionReason": "not_wordpress_plugin_request",
          "name": "", "slug": "", "description": "", "version": "", "author": "",
          "features": [], "unsupportedRequirements": [],
          "customPostType": null, "settingsPage": null, "customFields": null, "scheduledTask": null
        }
        """;

        var provider = CreateProvider((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent($$"""
                {
                  "output": [
                    {
                      "type": "message",
                      "content": [
                        { "type": "output_text", "text": {{System.Text.Json.JsonSerializer.Serialize(specJson)}} }
                      ]
                    }
                  ],
                  "usage": { "input_tokens": 120, "output_tokens": 40, "total_tokens": 160 }
                }
                """, System.Text.Encoding.UTF8, "application/json"),
        }));

        var ex = await Assert.ThrowsAsync<PluginPlanException>(
            () => provider.PlanAsync(new PlanningRequest { Description = "Write me an essay about cats." }));

        Assert.Equal(PluginPlanFailureReason.OutOfScope, ex.Reason);
        Assert.Equal("ModuleMint can only process requests related to creating or modifying WordPress plugins.", ex.Message);
        Assert.Equal("not_wordpress_plugin_request", ex.Detail);
        Assert.Equal(120, ex.Usage?.InputTokens);
        Assert.Equal(40, ex.Usage?.OutputTokens);
    }

    [Fact]
    public async Task PlanAsync_MissingDecisionField_TreatedAsRejectionNotAllow()
    {
        const string specJson = """
        {
          "name": "Staff Directory", "slug": "staff-directory", "description": "d",
          "version": "1.0.0", "author": "a", "features": ["shortcode"], "unsupportedRequirements": [],
          "customPostType": null, "settingsPage": null, "customFields": null, "scheduledTask": null
        }
        """;

        var provider = CreateProvider((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(WrapOutputText(specJson), System.Text.Encoding.UTF8, "application/json"),
        }));

        var ex = await Assert.ThrowsAsync<PluginPlanException>(
            () => provider.PlanAsync(new PlanningRequest { Description = "anything" }));

        Assert.Equal(PluginPlanFailureReason.OutOfScope, ex.Reason);
    }

    [Fact]
    public async Task PlanAsync_MissingApiKey_ThrowsProviderFailure()
    {
        var provider = CreateProvider((_, _) => throw new InvalidOperationException("should not be called"), apiKey: "");

        var ex = await Assert.ThrowsAsync<PluginPlanException>(() => provider.PlanAsync(new PlanningRequest { Description = "x" }));

        Assert.Equal(PluginPlanFailureReason.ProviderFailure, ex.Reason);
    }

    [Fact]
    public async Task PlanAsync_UnauthorizedStatus_ThrowsProviderFailure()
    {
        var provider = CreateProvider((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized)
        {
            Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json"),
        }));

        var ex = await Assert.ThrowsAsync<PluginPlanException>(() => provider.PlanAsync(new PlanningRequest { Description = "x" }));

        Assert.Equal(PluginPlanFailureReason.ProviderFailure, ex.Reason);
    }

    [Fact]
    public async Task PlanAsync_RateLimited_ThrowsProviderFailure()
    {
        var provider = CreateProvider((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.TooManyRequests)
        {
            Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json"),
        }));

        var ex = await Assert.ThrowsAsync<PluginPlanException>(() => provider.PlanAsync(new PlanningRequest { Description = "x" }));

        Assert.Equal(PluginPlanFailureReason.ProviderFailure, ex.Reason);
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
    public async Task PlanAsync_ResponseMissingOutputText_ThrowsMalformedProviderOutput()
    {
        const string responseJson = """{ "output": [ { "type": "reasoning", "content": [] } ] }""";

        var provider = CreateProvider((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(responseJson, System.Text.Encoding.UTF8, "application/json"),
        }));

        var ex = await Assert.ThrowsAsync<PluginPlanException>(() => provider.PlanAsync(new PlanningRequest { Description = "x" }));

        Assert.Equal(PluginPlanFailureReason.MalformedProviderOutput, ex.Reason);
    }

    [Fact]
    public async Task PlanAsync_StructuredOutputInvalidJson_ThrowsMalformedProviderOutput()
    {
        var provider = CreateProvider((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(WrapOutputText("{ not valid"), System.Text.Encoding.UTF8, "application/json"),
        }));

        var ex = await Assert.ThrowsAsync<PluginPlanException>(() => provider.PlanAsync(new PlanningRequest { Description = "x" }));

        Assert.Equal(PluginPlanFailureReason.MalformedProviderOutput, ex.Reason);
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

    [Fact]
    public async Task PlanAsync_FailureResponse_NeverContainsApiKeyMarker()
    {
        const string fakeKey = "sk-test-marker-should-never-appear";
        var provider = CreateProvider((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json"),
        }), apiKey: fakeKey);

        var ex = await Assert.ThrowsAsync<PluginPlanException>(() => provider.PlanAsync(new PlanningRequest { Description = "x" }));

        Assert.DoesNotContain(fakeKey, ex.Message);
    }

    [Fact]
    public async Task PlanAsync_Request_UsesBearerAuthorizationHeader()
    {
        HttpRequestMessage? capturedRequest = null;
        const string specJson = """
        {
          "decision": "allow", "rejectionReason": null,
          "name": "Staff Directory", "slug": "staff-directory", "description": "d",
          "version": "1.0.0", "author": "a", "features": [], "unsupportedRequirements": [],
          "customPostType": null, "settingsPage": null, "customFields": null, "scheduledTask": null
        }
        """;

        var provider = CreateProvider((req, _) =>
        {
            capturedRequest = req;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(WrapOutputText(specJson), System.Text.Encoding.UTF8, "application/json"),
            });
        }, apiKey: "sk-test-authz-key");

        await provider.PlanAsync(new PlanningRequest { Description = "x" });

        Assert.NotNull(capturedRequest);
        Assert.Equal("Bearer", capturedRequest!.Headers.Authorization?.Scheme);
        Assert.Equal("sk-test-authz-key", capturedRequest.Headers.Authorization?.Parameter);
    }
}
