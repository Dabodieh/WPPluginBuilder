using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using WPAIPlugin.Api.Controllers;
using WPAIPlugin.Planning;
using Xunit;

namespace WPAIPlugin.Generator.Tests.Planning;

public class PluginsControllerPlanTests
{
    private static PluginsController CreateController(FakePluginPlanner planner) =>
        new(new global::WPAIPlugin.Generator.PluginBuilder(), planner, NullLogger<PluginsController>.Instance);

    [Fact]
    public async Task Plan_ValidDescription_Returns200WithSpecAndUnsupportedRequirements()
    {
        var controller = CreateController(new FakePluginPlanner());

        var result = await controller.Plan(new PlanPluginRequest { Description = "Create a simple staff directory plugin with a shortcode." }, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<PlanPluginResponse>(ok.Value);
        Assert.Equal("staff-directory", response.Spec.Slug);
        Assert.Empty(response.UnsupportedRequirements);
    }

    [Fact]
    public async Task Plan_InvalidInput_Returns400()
    {
        var planner = new FakePluginPlanner
        {
            Handler = (_, _, _, _) => throw new PluginPlanException(PluginPlanFailureReason.InvalidInput, "Description cannot be blank."),
        };
        var controller = CreateController(planner);

        var result = await controller.Plan(new PlanPluginRequest { Description = "" }, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task Plan_GeneratedSpecInvalid_Returns400WithValidationErrors()
    {
        var planner = new FakePluginPlanner
        {
            Handler = (_, _, _, _) => throw new PluginPlanException(
                PluginPlanFailureReason.GeneratedSpecInvalid,
                "invalid",
                new[] { "Slug must contain only lowercase letters, digits, and hyphens." }),
        };
        var controller = CreateController(planner);

        var result = await controller.Plan(new PlanPluginRequest { Description = "anything" }, CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.NotNull(badRequest.Value);
    }

    [Fact]
    public async Task Plan_ProviderFailure_Returns502()
    {
        var planner = new FakePluginPlanner
        {
            Handler = (_, _, _, _) => throw new PluginPlanException(PluginPlanFailureReason.ProviderFailure, "The AI planning provider failed."),
        };
        var controller = CreateController(planner);

        var result = await controller.Plan(new PlanPluginRequest { Description = "anything" }, CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status502BadGateway, objectResult.StatusCode);
    }

    [Fact]
    public async Task Plan_Timeout_Returns504()
    {
        var planner = new FakePluginPlanner
        {
            Handler = (_, _, _, _) => throw new PluginPlanException(PluginPlanFailureReason.Timeout, "The AI planning provider timed out."),
        };
        var controller = CreateController(planner);

        var result = await controller.Plan(new PlanPluginRequest { Description = "anything" }, CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status504GatewayTimeout, objectResult.StatusCode);
    }

    [Fact]
    public async Task Plan_MalformedProviderOutput_Returns502()
    {
        var planner = new FakePluginPlanner
        {
            Handler = (_, _, _, _) => throw new PluginPlanException(PluginPlanFailureReason.MalformedProviderOutput, "malformed"),
        };
        var controller = CreateController(planner);

        var result = await controller.Plan(new PlanPluginRequest { Description = "anything" }, CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status502BadGateway, objectResult.StatusCode);
    }

    [Fact]
    public async Task Plan_DoesNotExposeExceptionDetails()
    {
        var planner = new FakePluginPlanner
        {
            Handler = (_, _, _, _) => throw new PluginPlanException(PluginPlanFailureReason.ProviderFailure, "The AI planning provider failed to produce a plugin plan."),
        };
        var controller = CreateController(planner);

        var result = await controller.Plan(new PlanPluginRequest { Description = "anything" }, CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result);
        var json = System.Text.Json.JsonSerializer.Serialize(objectResult.Value);
        Assert.DoesNotContain("StackTrace", json);
        Assert.DoesNotContain("api_key", json, StringComparison.OrdinalIgnoreCase);
    }
}
