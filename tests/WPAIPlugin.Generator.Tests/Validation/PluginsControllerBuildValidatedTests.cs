using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WPAIPlugin.Api.AiUsage;
using WPAIPlugin.Api.Controllers;
using WPAIPlugin.Api.Data;
using WPAIPlugin.Api.Security;
using WPAIPlugin.Api.Validation;
using WPAIPlugin.Generator.Models;
using WPAIPlugin.Generator.Tests.Planning;
using Xunit;

namespace WPAIPlugin.Generator.Tests.Validation;

/// <summary>
/// Controller-level tests for POST /api/plugins/build-validated. Uses
/// <see cref="FakeDockerPluginValidator"/> so no real Docker command ever
/// runs; dotnet test stays Docker-independent.
/// </summary>
public class PluginsControllerBuildValidatedTests
{
    private static PluginSpec ValidSpec() => new()
    {
        Name = "Staff Directory",
        Slug = "staff-directory",
        Description = "Simple staff directory plugin",
        Version = "1.0.0",
        Author = "AI Plugin Builder",
        Features = new List<string> { "shortcode" },
    };

    private static PluginsController CreateController(
        FakeDockerPluginValidator validator,
        ValidationOptions? options = null)
    {
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        return new(
            new global::WPAIPlugin.Generator.PluginBuilder(),
            new FakePluginPlanner(),
            validator,
            Options.Create(options ?? new ValidationOptions()),
            new AiUsageRecorder(db, Options.Create(new AiPricingOptions())),
            db,
            TestPlanningRateLimiters.Generous(),
            TestPlanningRateLimiters.Generous(),
            Options.Create(new AbuseOptions()),
            TestUserManagerFactory.Create(db),
            NullLogger<PluginsController>.Instance);
    }

    [Fact]
    public async Task BuildValidated_ValidationPasses_ReturnsZip()
    {
        var validator = new FakeDockerPluginValidator
        {
            Handler = (_, _) => new DockerPluginValidator.ProcessResult(0, string.Empty, string.Empty),
        };
        var controller = CreateController(validator);

        var result = await controller.BuildValidated(ValidSpec(), CancellationToken.None);

        var fileResult = Assert.IsType<FileContentResult>(result);
        Assert.Equal("application/zip", fileResult.ContentType);
        Assert.True(fileResult.FileContents.Length > 0);
    }

    [Fact]
    public async Task BuildValidated_DockerVersionCheckFails_Returns503()
    {
        var validator = new FakeDockerPluginValidator
        {
            Handler = (_, args) => args.FirstOrDefault() == "version"
                ? new DockerPluginValidator.ProcessResult(-1, string.Empty, string.Empty)
                : new DockerPluginValidator.ProcessResult(0, string.Empty, string.Empty),
        };
        var controller = CreateController(validator);

        var result = await controller.BuildValidated(ValidSpec(), CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, objectResult.StatusCode);
        var body = Assert.IsType<BuildValidatedErrorResponse>(objectResult.Value);
        Assert.Equal(PluginValidationFailureReason.ValidationUnavailable, body.Reason);
    }

    [Fact]
    public async Task BuildValidated_ValidationDisabled_Returns503WithoutRunningDocker()
    {
        var runCount = 0;
        var validator = new FakeDockerPluginValidator
        {
            Handler = (_, _) => { runCount++; return new DockerPluginValidator.ProcessResult(0, string.Empty, string.Empty); },
        };
        var controller = CreateController(validator, new ValidationOptions { Enabled = false });

        var result = await controller.BuildValidated(ValidSpec(), CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, objectResult.StatusCode);
        Assert.Equal(0, runCount);
    }

    [Fact]
    public async Task BuildValidated_PhpLintFails_ReturnsUnprocessableEntityWithoutZip()
    {
        var validator = new FakeDockerPluginValidator
        {
            Handler = (_, args) =>
            {
                if (args.Contains("php")) { return new DockerPluginValidator.ProcessResult(1, string.Empty, "syntax error"); }
                return new DockerPluginValidator.ProcessResult(0, string.Empty, string.Empty);
            },
        };
        var controller = CreateController(validator);

        var result = await controller.BuildValidated(ValidSpec(), CancellationToken.None);

        var objectResult = Assert.IsType<UnprocessableEntityObjectResult>(result);
        var body = Assert.IsType<BuildValidatedErrorResponse>(objectResult.Value);
        Assert.Equal(PluginValidationFailureReason.PhpLintFailed, body.Reason);
        Assert.False(body.PhpLintPassed);
    }

    [Fact]
    public async Task BuildValidated_PluginActivationFails_ReturnsUnprocessableEntity()
    {
        var validator = new FakeDockerPluginValidator
        {
            Handler = (_, args) =>
            {
                if (args.Contains("activate")) { return new DockerPluginValidator.ProcessResult(1, string.Empty, "activation failed"); }
                return new DockerPluginValidator.ProcessResult(0, string.Empty, string.Empty);
            },
        };
        var controller = CreateController(validator);

        var result = await controller.BuildValidated(ValidSpec(), CancellationToken.None);

        var objectResult = Assert.IsType<UnprocessableEntityObjectResult>(result);
        var body = Assert.IsType<BuildValidatedErrorResponse>(objectResult.Value);
        Assert.Equal(PluginValidationFailureReason.PluginActivationFailed, body.Reason);
        Assert.True(body.PluginInstalled);
        Assert.False(body.PluginActivated);
    }

    [Fact]
    public async Task BuildValidated_InvalidSlug_Returns400WithoutInvokingValidator()
    {
        var runCount = 0;
        var validator = new FakeDockerPluginValidator
        {
            Handler = (_, _) => { runCount++; return new DockerPluginValidator.ProcessResult(0, string.Empty, string.Empty); },
        };
        var controller = CreateController(validator);
        var spec = ValidSpec();
        spec.Slug = "../evil";

        var result = await controller.BuildValidated(spec, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal(0, runCount);
    }

    [Fact]
    public async Task BuildValidated_UnknownFeature_Returns400_ValidatorNeverBypassed()
    {
        var validator = new FakeDockerPluginValidator();
        var controller = CreateController(validator);
        var spec = ValidSpec();
        spec.Features = new List<string> { "not-a-real-feature" };

        var result = await controller.BuildValidated(spec, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
    }
}
