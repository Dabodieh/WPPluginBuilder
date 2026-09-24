using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using WPAIPlugin.Api.Credits;
using WPAIPlugin.Api.Data;
using WPAIPlugin.Api.Entitlements;
using WPAIPlugin.Api.Storage;
using WPAIPlugin.Api.Validation;
using WPAIPlugin.Generator;
using WPAIPlugin.Generator.Validation;

namespace WPAIPlugin.Api.Controllers;

/// <summary>
/// Authenticated SaaS plugin projects (Milestone 11): build-and-save, list,
/// detail, and download. Every query is scoped to the current user's ID -
/// never trust a supplied project/version ID by itself. A non-owned or
/// unknown project/version always returns 404, never 403, so existence of
/// another user's project is never revealed.
/// </summary>
[ApiController]
[Route("api/projects")]
[Authorize]
public sealed class ProjectsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly PartitionedRateLimiter<string> _validatedBuildLimiter;
    private readonly PluginBuilder _pluginBuilder;
    private readonly DockerPluginValidator _pluginValidator;
    private readonly ValidationOptions _validationOptions;
    private readonly PluginArtifactStore _artifactStore;
    private readonly CreditService _creditService;
    private readonly CreditOptions _creditOptions;
    private readonly BuildEntitlementService _entitlementService;
    private readonly UserManager<IdentityUser> _userManager;
    private readonly ILogger<ProjectsController> _logger;

    public ProjectsController(
        AppDbContext db,
        PartitionedRateLimiter<string> validatedBuildLimiter,
        PluginBuilder pluginBuilder,
        DockerPluginValidator pluginValidator,
        IOptions<ValidationOptions> validationOptions,
        PluginArtifactStore artifactStore,
        CreditService creditService,
        IOptions<CreditOptions> creditOptions,
        BuildEntitlementService entitlementService,
        UserManager<IdentityUser> userManager,
        ILogger<ProjectsController> logger)
    {
        _db = db;
        _validatedBuildLimiter = validatedBuildLimiter;
        _pluginBuilder = pluginBuilder;
        _pluginValidator = pluginValidator;
        _validationOptions = validationOptions.Value;
        _artifactStore = artifactStore;
        _creditService = creditService;
        _creditOptions = creditOptions.Value;
        _entitlementService = entitlementService;
        _userManager = userManager;
        _logger = logger;
    }

    [ValidateAntiForgeryToken]
    [EnableRateLimiting("build")]
    [RequestSizeLimit(1024 * 1024)]
    [HttpPost("build")]
    [ProducesResponseType(typeof(ProjectBuildResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(InsufficientCreditsResponse), StatusCodes.Status402PaymentRequired)]
    [ProducesResponseType(typeof(BuildValidatedErrorResponse), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Build([FromBody] ProjectBuildRequest request, CancellationToken cancellationToken)
    {
        var userId = _userManager.GetUserId(User);
        if (userId is null)
        {
            return Unauthorized();
        }

        // Verification gate first, before any rate-limit lease, credit
        // charge, or free-build consumption - an unverified account must
        // never consume any generation resource.
        var user = await _userManager.GetUserAsync(User);
        if (user is null || !user.EmailConfirmed)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "Please verify your email address before creating WordPress plugins." });
        }

        var spec = request.Spec;
        var validation = PluginSpecValidator.Validate(spec);
        if (!validation.IsValid)
        {
            return BadRequest(new { errors = validation.Errors });
        }

        if (request.Validated && !_validationOptions.Enabled)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = "Plugin validation is currently unavailable." });
        }

        if (request.Validated)
        {
            using var lease = _validatedBuildLimiter.AttemptAcquire(userId);
            if (!lease.IsAcquired)
            {
                Response.Headers.RetryAfter = "60";
                return StatusCode(429, new { error = "Too many validated builds. Please try again later." });
            }
        }

        // Free builds and credits are distinct, server-decided entitlements -
        // the browser never chooses or influences which one covers a build.
        // One buildReference correlates the credit charge, the free-build
        // consumption, and both of their refunds for this single build
        // operation. A standard build fully covered by a free build costs 0
        // credits; a validated build covered by a free build still costs the
        // configured validation credit (1) - only the build itself is free.
        var buildReference = $"build:{Guid.NewGuid()}";
        var usedFreeBuild = await _entitlementService.TryConsumeAsync(userId, buildReference, CancellationToken.None);
        var cost = usedFreeBuild
            ? (request.Validated ? 1 : 0)
            : (request.Validated ? _creditOptions.ValidatedBuildCost : _creditOptions.StandardBuildCost);
        var chargeType = request.Validated ? CreditTransactionType.ValidatedBuild : CreditTransactionType.PluginBuild;

        cancellationToken.ThrowIfCancellationRequested();
        // Once charging starts, finish that short database unit independently
        // of a disconnected client so its outcome is known before compensation.
        bool charged;
        int balance;
        if (cost > 0)
        {
            (charged, balance) = await _creditService.TryChargeAsync(userId, cost, chargeType, buildReference, CancellationToken.None);
        }
        else
        {
            charged = true;
            balance = await _creditService.GetBalanceAsync(userId, CancellationToken.None);
        }
        if (!charged)
        {
            if (usedFreeBuild)
            {
                await _entitlementService.RefundAsync(userId, buildReference, CancellationToken.None);
            }
            return StatusCode(402, new InsufficientCreditsResponse
            {
                Error = "Insufficient credits.", Required = cost, Balance = balance,
                FreeBuildsRemaining = await _entitlementService.GetRemainingAsync(userId, CancellationToken.None),
            });
        }

        // Only says a resource was returned if it was actually consumed -
        // never claims a free build was returned when none was used, or vice
        // versa for credits (cost is always > 0 here when no free build
        // covered the build, since both configured build costs are positive).
        string ResourcesReturnedMessage() => usedFreeBuild
            ? (cost > 0 ? "Build failed. Your free build and credits were returned." : "Build failed. Your free build was returned.")
            : "Build failed. Your credits were returned.";

        var completed = false;
        string? artifactKey = null;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var buildResult = _pluginBuilder.Build(spec!);
            if (request.Validated)
            {
                var result = await _pluginValidator.ValidateAsync(buildResult.ZipBytes, spec!.Slug, cancellationToken);
                if (!result.Success)
                {
                    var error = new BuildValidatedErrorResponse
                    {
                        Error = ResourcesReturnedMessage(),
                        Reason = result.FailureReason ?? PluginValidationFailureReason.ValidationUnavailable,
                        PhpLintPassed = result.PhpLintPassed,
                        WordPressInstalled = result.WordPressInstalled,
                        PluginInstalled = result.PluginInstalled,
                        PluginActivated = result.PluginActivated,
                    };
                    return result.FailureReason == PluginValidationFailureReason.ValidationUnavailable
                        ? StatusCode(503, error) : UnprocessableEntity(error);
                }
            }

            var now = DateTime.UtcNow;
            var project = new PluginProject
            {
                Id = Guid.NewGuid(), UserId = userId, Name = spec!.Name,
                Slug = spec.Slug, CreatedAtUtc = now, UpdatedAtUtc = now,
            };
            var versionId = Guid.NewGuid();
            artifactKey = $"{userId}/{project.Id}/{versionId}";
            await _artifactStore.SaveAsync(userId, project.Id, versionId, buildResult.ZipBytes, cancellationToken);
            var version = new PluginVersion
            {
                Id = versionId, PluginProjectId = project.Id, RevisionNumber = 1,
                PluginVersionNumber = spec.Version, SpecJson = JsonSerializer.Serialize(spec),
                ArtifactKey = artifactKey, Validated = request.Validated, CreatedAtUtc = now,
            };
            _db.PluginProjects.Add(project);
            _db.PluginVersions.Add(version);
            cancellationToken.ThrowIfCancellationRequested();
            // Commit is the success boundary. A disconnect after this commit
            // must not refund a successfully saved, downloadable project.
            await _db.SaveChangesAsync(CancellationToken.None);
            completed = true;
            return Ok(new ProjectBuildResponse
            {
                ProjectId = project.Id, VersionId = version.Id,
                RevisionNumber = version.RevisionNumber, Validated = version.Validated,
                DownloadUrl = $"/api/projects/{project.Id}/versions/{version.Id}/download",
                CreditsCharged = cost, CreditBalance = balance,
                FreeBuildUsed = usedFreeBuild,
                FreeBuildsRemaining = await _entitlementService.GetRemainingAsync(userId, CancellationToken.None),
            });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError("SaaS build failed after charging ({FailureType}).", ex.GetType().Name);
            return StatusCode(500, new { error = ResourcesReturnedMessage() });
        }
        finally
        {
            if (!completed)
            {
                // Failed project inserts must never be retried by refund's SaveChanges.
                _db.ChangeTracker.Clear();
                if (cost > 0) await _creditService.RefundAsync(userId, cost, buildReference, CancellationToken.None);
                if (usedFreeBuild) await _entitlementService.RefundAsync(userId, buildReference, CancellationToken.None);
                if (artifactKey is not null) _artifactStore.TryDelete(artifactKey);
            }
        }
    }

    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<ProjectSummaryResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
    {
        var userId = _userManager.GetUserId(User);
        if (userId is null)
        {
            return Unauthorized();
        }

        var projects = await _db.PluginProjects
            .Where(p => p.UserId == userId)
            .Include(p => p.Versions)
            .OrderByDescending(p => p.UpdatedAtUtc)
            .ToListAsync(cancellationToken);

        var response = projects.Select(p =>
        {
            var latest = p.Versions.OrderByDescending(v => v.RevisionNumber).First();
            return new ProjectSummaryResponse
            {
                Id = p.Id,
                Name = p.Name,
                Slug = p.Slug,
                LatestRevisionNumber = latest.RevisionNumber,
                LatestPluginVersion = latest.PluginVersionNumber,
                Validated = latest.Validated,
                UpdatedAtUtc = p.UpdatedAtUtc,
                DownloadUrl = $"/api/projects/{p.Id}/versions/{latest.Id}/download",
            };
        }).ToList();

        return Ok(response);
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(ProjectDetailResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Detail(Guid id, CancellationToken cancellationToken)
    {
        var userId = _userManager.GetUserId(User);
        if (userId is null)
        {
            return Unauthorized();
        }

        var project = await _db.PluginProjects
            .Where(p => p.Id == id && p.UserId == userId)
            .Include(p => p.Versions)
            .FirstOrDefaultAsync(cancellationToken);

        if (project is null)
        {
            return NotFound();
        }

        var response = new ProjectDetailResponse
        {
            Id = project.Id,
            Name = project.Name,
            Slug = project.Slug,
            CreatedAtUtc = project.CreatedAtUtc,
            UpdatedAtUtc = project.UpdatedAtUtc,
            Versions = project.Versions
                .OrderByDescending(v => v.RevisionNumber)
                .Select(v => new ProjectVersionResponse
                {
                    Id = v.Id,
                    RevisionNumber = v.RevisionNumber,
                    PluginVersion = v.PluginVersionNumber,
                    Validated = v.Validated,
                    CreatedAtUtc = v.CreatedAtUtc,
                    DownloadUrl = $"/api/projects/{project.Id}/versions/{v.Id}/download",
                })
                .ToList(),
        };

        return Ok(response);
    }

    [HttpGet("{projectId:guid}/versions/{versionId:guid}/download")]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Download(Guid projectId, Guid versionId, CancellationToken cancellationToken)
    {
        var userId = _userManager.GetUserId(User);
        if (userId is null)
        {
            return Unauthorized();
        }

        var version = await _db.PluginVersions
            .Where(v => v.Id == versionId && v.PluginProjectId == projectId)
            .Include(v => v.PluginProject)
            .FirstOrDefaultAsync(cancellationToken);

        if (version is null || version.PluginProject is null || version.PluginProject.UserId != userId)
        {
            return NotFound();
        }

        var zipBytes = await _artifactStore.ReadAsync(version.ArtifactKey, cancellationToken);
        if (zipBytes is null)
        {
            _logger.LogError("Artifact missing on disk for version {VersionId}.", versionId);
            return NotFound();
        }

        var fileName = $"{version.PluginProject.Slug}.zip";
        return File(zipBytes, "application/zip", fileName);
    }
}
