using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using WPAIPlugin.Api.AiUsage;
using WPAIPlugin.Api.Data;
using WPAIPlugin.Api.Security;
using WPAIPlugin.Api.Validation;
using WPAIPlugin.Generator;
using WPAIPlugin.Generator.Models;
using WPAIPlugin.Generator.Validation;
using WPAIPlugin.Planning;

namespace WPAIPlugin.Api.Controllers;

[ApiController]
[Route("api/plugins")]
[RequestSizeLimit(1024 * 1024)]
public sealed class PluginsController : ControllerBase
{
    private readonly PluginBuilder _pluginBuilder;
    private readonly IPluginPlanner _pluginPlanner;
    private readonly DockerPluginValidator _pluginValidator;
    private readonly ValidationOptions _validationOptions;
    private readonly AiUsageRecorder _aiUsageRecorder;
    private readonly AppDbContext _db;
    private readonly PartitionedRateLimiter<string> _planningHourlyLimiter;
    private readonly PartitionedRateLimiter<string> _planningDailyLimiter;
    private readonly AbuseOptions _abuseOptions;
    private readonly UserManager<IdentityUser> _userManager;
    private readonly ILogger<PluginsController> _logger;

    public PluginsController(
        PluginBuilder pluginBuilder,
        IPluginPlanner pluginPlanner,
        DockerPluginValidator pluginValidator,
        IOptions<ValidationOptions> validationOptions,
        AiUsageRecorder aiUsageRecorder,
        AppDbContext db,
        [FromKeyedServices("planningHourly")] PartitionedRateLimiter<string> planningHourlyLimiter,
        [FromKeyedServices("planningDaily")] PartitionedRateLimiter<string> planningDailyLimiter,
        IOptions<AbuseOptions> abuseOptions,
        UserManager<IdentityUser> userManager,
        ILogger<PluginsController> logger)
    {
        _pluginBuilder = pluginBuilder;
        _pluginPlanner = pluginPlanner;
        _pluginValidator = pluginValidator;
        _validationOptions = validationOptions.Value;
        _aiUsageRecorder = aiUsageRecorder;
        _db = db;
        _planningHourlyLimiter = planningHourlyLimiter;
        _planningDailyLimiter = planningDailyLimiter;
        _abuseOptions = abuseOptions.Value;
        _userManager = userManager;
        _logger = logger;
    }

    /// <summary>Fixed, consistent message for every AI/build endpoint an unverified account is blocked from - never leaks whether the account/email exists beyond what the caller already knows (they are authenticated as it).</summary>
    private const string EmailNotVerifiedMessage = "Please verify your email address before creating WordPress plugins.";

    /// <summary>
    /// Builds a deterministic WordPress plugin ZIP from the supplied <see cref="PluginSpec"/>.
    ///
    /// Development-only local validation harness. Not mapped in Production
    /// (or any non-Development environment). Customer builds use projects/build.
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
    [ValidateAntiForgeryToken]
    [EnableRateLimiting("build")]
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
    [ValidateAntiForgeryToken]
    [EnableRateLimiting("planning")]
    [RequestSizeLimit(64 * 1024)]
    [HttpPost("plan")]
    [ProducesResponseType(typeof(PlanPluginResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    [ProducesResponseType(StatusCodes.Status504GatewayTimeout)]
    public async Task<IActionResult> Plan([FromBody] PlanPluginRequest request, CancellationToken cancellationToken)
    {
        var userId = HttpContext?.User?.FindFirstValue(ClaimTypes.NameIdentifier);
        var requestedProvider = string.IsNullOrWhiteSpace(request.Provider) ? "default" : request.Provider;
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        // Verification gate first, before any rate-limit/cost check or AI
        // call - an unverified account must never consume any generation
        // resource, including rate-limit budget.
        if (userId is not null)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user is null || !user.EmailConfirmed)
            {
                return StatusCode(StatusCodes.Status403Forbidden, new { error = EmailNotVerifiedMessage });
            }
        }

        // Cheap, pre-flight abuse checks - none of these ever invoke the AI
        // provider. Free planning costs no credits, so it is the cheapest
        // surface to abuse; these are on top of the per-minute "planning"
        // rate-limit policy already enforced by [EnableRateLimiting] above.
        if (userId is not null)
        {
            using (var hourlyLease = _planningHourlyLimiter.AttemptAcquire(userId))
            {
                if (!hourlyLease.IsAcquired)
                {
                    return await RejectCheaplyAsync(userId, requestedProvider, "RateLimitedHourly", stopwatch,
                        StatusCodes.Status429TooManyRequests, "Too many planning requests this hour. Please try again later.");
                }
            }

            using (var dailyLease = _planningDailyLimiter.AttemptAcquire(userId))
            {
                if (!dailyLease.IsAcquired)
                {
                    return await RejectCheaplyAsync(userId, requestedProvider, "RateLimitedDaily", stopwatch,
                        StatusCodes.Status429TooManyRequests, "Too many planning requests today. Please try again tomorrow.");
                }
            }

            var todayUtc = DateTime.UtcNow.Date;
            var spentTodayUsdMicros = await _db.AiUsageEvents
                .Where(e => e.UserId == userId && e.OperationType == AiUsageOperationType.Plan && e.CreatedAtUtc >= todayUtc)
                .SumAsync(e => e.EstimatedCostUsdMicros ?? 0, cancellationToken);
            if (spentTodayUsdMicros >= _abuseOptions.MaxAiCostUsdMicrosPerUserPerDay)
            {
                return await RejectCheaplyAsync(userId, requestedProvider, "DailyCostBudgetExceeded", stopwatch,
                    StatusCodes.Status429TooManyRequests, "Too many planning requests today. Please try again tomorrow.");
            }
        }

        try
        {
            var result = await _pluginPlanner.PlanAsync(
                request.Description ?? string.Empty,
                request.Provider,
                model: null,
                cancellationToken);
            stopwatch.Stop();

            await _aiUsageRecorder.RecordSuccessAsync(
                userId, AiUsageOperationType.Plan, result.Provider, result.Model,
                result.Usage?.InputTokens, result.Usage?.OutputTokens, result.Usage?.TotalTokens,
                stopwatch.ElapsedMilliseconds, CancellationToken.None);

            return Ok(new PlanPluginResponse
            {
                Spec = result.Spec,
                UnsupportedRequirements = result.UnsupportedRequirements,
            });
        }
        catch (PluginPlanException ex)
        {
            stopwatch.Stop();
            _logger.LogWarning("Plugin planning failed with reason {Reason} detail {Detail}.", ex.Reason, ex.Detail);

            // A validation failure on an already-generated spec, or an
            // out-of-scope rejection, is still a real provider call - only
            // genuine input/provider/timeout/cancellation failures where no
            // billable call happened (or none is relevant) skip usage
            // recording.
            if (ex.Reason is PluginPlanFailureReason.InvalidInput or PluginPlanFailureReason.GeneratedSpecInvalid)
            {
                // No provider call was made (invalid input) or it already
                // succeeded (spec failed our own validation) - do not record
                // a failed AI usage event for either case.
            }
            else
            {
                await _aiUsageRecorder.RecordFailureAsync(
                    userId, AiUsageOperationType.Plan, requestedProvider, null,
                    ex.Reason.ToString(), stopwatch.ElapsedMilliseconds, CancellationToken.None,
                    ex.Usage?.InputTokens, ex.Usage?.OutputTokens, ex.Usage?.TotalTokens);
            }

            return ex.Reason switch
            {
                PluginPlanFailureReason.InvalidInput => BadRequest(new { errors = new[] { ex.Message } }),
                PluginPlanFailureReason.OutOfScope => BadRequest(new { errors = new[] { ex.Message } }),
                PluginPlanFailureReason.GeneratedSpecInvalid => BadRequest(new { errors = ex.ValidationErrors.Count > 0 ? ex.ValidationErrors : new[] { ex.Message } }),
                PluginPlanFailureReason.ProviderNotFound => BadRequest(new { errors = new[] { ex.Message } }),
                PluginPlanFailureReason.MalformedProviderOutput => StatusCode(StatusCodes.Status502BadGateway, new { errors = new[] { "Planning could not be completed." } }),
                PluginPlanFailureReason.Timeout => StatusCode(StatusCodes.Status504GatewayTimeout, new { errors = new[] { "Planning timed out. Please try again." } }),
                PluginPlanFailureReason.Cancelled => StatusCode(499, new { errors = new[] { "Planning was cancelled." } }),
                _ => StatusCode(StatusCodes.Status502BadGateway, new { errors = new[] { "Planning is currently unavailable." } }),
            };
        }
    }

    /// <summary>Records a pre-flight rejection (no AI provider call made) and returns a 429 without ever reaching the planner.</summary>
    private async Task<IActionResult> RejectCheaplyAsync(
        string? userId, string requestedProvider, string failureCategory,
        System.Diagnostics.Stopwatch stopwatch, int statusCode, string message)
    {
        stopwatch.Stop();
        _logger.LogWarning("Plugin planning rejected before provider call with reason {Reason}.", failureCategory);
        await _aiUsageRecorder.RecordFailureAsync(
            userId, AiUsageOperationType.Plan, requestedProvider, null,
            failureCategory, stopwatch.ElapsedMilliseconds, CancellationToken.None);
        Response.Headers.RetryAfter = "60";
        return StatusCode(statusCode, new { error = message });
    }
}
