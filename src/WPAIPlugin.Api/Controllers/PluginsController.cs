using Microsoft.AspNetCore.Mvc;
using WPAIPlugin.Generator;
using WPAIPlugin.Generator.Models;
using WPAIPlugin.Planning;

namespace WPAIPlugin.Api.Controllers;

[ApiController]
[Route("api/plugins")]
public sealed class PluginsController : ControllerBase
{
    private readonly PluginBuilder _pluginBuilder;
    private readonly IPluginPlanner _pluginPlanner;
    private readonly ILogger<PluginsController> _logger;

    public PluginsController(PluginBuilder pluginBuilder, IPluginPlanner pluginPlanner, ILogger<PluginsController> logger)
    {
        _pluginBuilder = pluginBuilder;
        _pluginPlanner = pluginPlanner;
        _logger = logger;
    }

    /// <summary>
    /// Builds a deterministic WordPress plugin ZIP from the supplied <see cref="PluginSpec"/>.
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
    /// Converts a natural-language plugin description into a proposed, validated
    /// <see cref="PluginSpec"/>. Does not build or return a plugin ZIP.
    /// </summary>
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
