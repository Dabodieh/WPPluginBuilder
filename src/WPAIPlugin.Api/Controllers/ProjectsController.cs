using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using WPAIPlugin.Api.Credits;
using WPAIPlugin.Api.Data;
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
    private readonly PluginBuilder _pluginBuilder;
    private readonly DockerPluginValidator _pluginValidator;
    private readonly ValidationOptions _validationOptions;
    private readonly PluginArtifactStore _artifactStore;
    private readonly CreditService _creditService;
    private readonly CreditOptions _creditOptions;
    private readonly UserManager<IdentityUser> _userManager;
    private readonly ILogger<ProjectsController> _logger;

    public ProjectsController(
        AppDbContext db,
        PluginBuilder pluginBuilder,
        DockerPluginValidator pluginValidator,
        IOptions<ValidationOptions> validationOptions,
        PluginArtifactStore artifactStore,
        CreditService creditService,
        IOptions<CreditOptions> creditOptions,
        UserManager<IdentityUser> userManager,
        ILogger<ProjectsController> logger)
    {
        _db = db;
        _pluginBuilder = pluginBuilder;
        _pluginValidator = pluginValidator;
        _validationOptions = validationOptions.Value;
        _artifactStore = artifactStore;
        _creditService = creditService;
        _creditOptions = creditOptions.Value;
        _userManager = userManager;
        _logger = logger;
    }

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

        // Cost is always computed server-side from configuration - the
        // request body's "validated" flag only selects which server-known
        // cost applies; the browser never supplies a cost value itself.
        var cost = request.Validated ? _creditOptions.ValidatedBuildCost : _creditOptions.StandardBuildCost;
        var chargeType = request.Validated ? CreditTransactionType.ValidatedBuild : CreditTransactionType.PluginBuild;
        var buildReference = $"build:{Guid.NewGuid()}";

        cancellationToken.ThrowIfCancellationRequested();
        // Once charging starts, finish that short database unit independently
        // of a disconnected client so its outcome is known before compensation.
        var (charged, balance) = await _creditService.TryChargeAsync(userId, cost, chargeType, buildReference, CancellationToken.None);
        if (!charged)
            return StatusCode(402, new InsufficientCreditsResponse
            {
                Error = "Insufficient credits.", Required = cost, Balance = balance,
            });

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
                        Error = "Build failed. Your credits were returned.",
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
            });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SaaS build failed after charging.");
            return StatusCode(500, new { error = "Build failed. Your credits were returned." });
        }
        finally
        {
            if (!completed)
            {
                // Failed project inserts must never be retried by refund's SaveChanges.
                _db.ChangeTracker.Clear();
                await _creditService.RefundAsync(userId, cost, buildReference, CancellationToken.None);
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
