using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using WPAIPlugin.Api.Validation;
using WPAIPlugin.Generator;
using WPAIPlugin.Generator.Models;
using WPAIPlugin.Generator.Validation;
using WPAIPlugin.Planning;

namespace WPAIPlugin.Api.Controllers;

[ApiController]
[Route("api/plugins")]
public sealed class PluginsController : ControllerBase
{
    private readonly PluginBuilder _pluginBuilder;
    private readonly IPluginPlanner _pluginPlanner;
    private readonly DockerPluginValidator _pluginValidator;
    private readonly ValidationOptions _validationOptions;
    private readonly ILogger<PluginsController> _logger;

    public PluginsController(
        PluginBuilder pluginBuilder,
        IPluginPlanner pluginPlanner,
        DockerPluginValidator pluginValidator,
        IOptions<ValidationOptions> validationOptions,
        ILogger<PluginsController> logger)
    {
        _pluginBuilder = pluginBuilder;
        _pluginPlanner = pluginPlanner;
        _pluginValidator = pluginValidator;
        _validationOptions = validationOptions.Value;
        _logger = logger;
    }

    /// <summary>
    /// Builds a deterministic WordPress plugin ZIP from the supplied <see cref="PluginSpec"/>.
    ///
    /// TEMPORARY/LEGACY EXCEPTION (Milestone 11): this endpoint remains
    /// anonymously accessible only because scripts/Validate-GeneratedPlugin.ps1
    /// and the local build-validation harness currently depend on it. It
    /// consumes no paid/external resource (deterministic template generation
    /// only, no AI provider call). The SaaS browser UI no longer calls this -
    /// it uses the authenticated POST /api/projects/build instead. This
    /// exception is intended to be removed/locked down in a later hardening
    /// milestone.
    /// </summary>
    [HttpPost("build")]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public IActionResult Build([FromBody] PluginSpec spec)
    {
        try
        {
            var result = _pluginBuilder.Build(spec);
            return File(result.ZipBytes, "application/zip", result.FileName);
        }
        catch (PluginBuildException ex)
        {
            return BadRequest(new { errors = ex.ValidationErrors });
        }
    }

    /// <summary>
    /// Builds a plugin exactly like <see cref="Build"/>, then runs the generated
    /// ZIP through a disposable Docker WordPress environment (php -l, install,
    /// activate) before returning it. The AI planning layer is never involved.
    /// Returns the ZIP only if validation passes.
    /// </summary>
    [Authorize]
    [HttpPost("build-validated")]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(BuildValidatedErrorResponse), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> BuildValidated([FromBody] PluginSpec spec, CancellationToken cancellationToken)
    {
        if (!_validationOptions.Enabled)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = "Plugin validation is currently unavailable." });
        }

        // Same validator/build path as the normal endpoint - never bypassed.
        var validation = PluginSpecValidator.Validate(spec);
        if (!validation.IsValid)
        {
            return BadRequest(new { errors = validation.Errors });
        }

        PluginBuildResult buildResult;
        try
        {
            buildResult = _pluginBuilder.Build(spec);
        }
        catch (PluginBuildException ex)
        {
            return BadRequest(new { errors = ex.ValidationErrors });
        }

        var validationResult = await _pluginValidator.ValidateAsync(buildResult.ZipBytes, spec.Slug, cancellationToken);

        if (!validationResult.Success)
        {
            _logger.LogWarning("Plugin build-validated failed with reason {Reason}.", validationResult.FailureReason);

            var errorResponse = new BuildValidatedErrorResponse
            {
                Error = validationResult.Error ?? "Plugin validation failed.",
                Reason = validationResult.FailureReason ?? PluginValidationFailureReason.ValidationUnavailable,
                PhpLintPassed = validationResult.PhpLintPassed,
                WordPressInstalled = validationResult.WordPressInstalled,
                PluginInstalled = validationResult.PluginInstalled,
                PluginActivated = validationResult.PluginActivated,
            };

            return validationResult.FailureReason == PluginValidationFailureReason.ValidationUnavailable
                ? StatusCode(StatusCodes.Status503ServiceUnavailable, errorResponse)
                : UnprocessableEntity(errorResponse);
        }

        return File(buildResult.ZipBytes, "application/zip", buildResult.FileName);
    }

    /// <summary>
    /// Converts a natural-language plugin description into a proposed, validated
    /// <see cref="PluginSpec"/>. Does not build or return a plugin ZIP.
    /// </summary>
    [Authorize]
    [HttpPost("plan")]
    [ProducesResponseType(typeof(PlanPluginResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    [ProducesResponseType(StatusCodes.Status504GatewayTimeout)]
    public async Task<IActionResult> Plan([FromBody] PlanPluginRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _pluginPlanner.PlanAsync(
                request.Description ?? string.Empty,
                request.Provider,
                request.Model,
                cancellationToken);

            return Ok(new PlanPluginResponse
            {
                Spec = result.Spec,
                UnsupportedRequirements = result.UnsupportedRequirements,
            });
        }
        catch (PluginPlanException ex)
        {
            _logger.LogWarning(ex, "Plugin planning failed with reason {Reason}.", ex.Reason);

            return ex.Reason switch
            {
                PluginPlanFailureReason.InvalidInput => BadRequest(new { errors = new[] { ex.Message } }),
                PluginPlanFailureReason.GeneratedSpecInvalid => BadRequest(new { errors = ex.ValidationErrors.Count > 0 ? ex.ValidationErrors : new[] { ex.Message } }),
                PluginPlanFailureReason.ProviderNotFound => BadRequest(new { errors = new[] { ex.Message } }),
                PluginPlanFailureReason.MalformedProviderOutput => StatusCode(StatusCodes.Status502BadGateway, new { errors = new[] { ex.Message } }),
                PluginPlanFailureReason.Timeout => StatusCode(StatusCodes.Status504GatewayTimeout, new { errors = new[] { ex.Message } }),
                PluginPlanFailureReason.Cancelled => StatusCode(499, new { errors = new[] { ex.Message } }),
                _ => StatusCode(StatusCodes.Status502BadGateway, new { errors = new[] { ex.Message } }),
            };
        }
    }
}
