using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using WPAIPlugin.Api.AiUsage;
using WPAIPlugin.Api.Configuration;
using WPAIPlugin.Api.Credits;
using WPAIPlugin.Api.Data;
using WPAIPlugin.Api.Payments;
using WPAIPlugin.Api.Security;
using WPAIPlugin.Api.Storage;
using WPAIPlugin.Api.Validation;
using WPAIPlugin.Planning;

namespace WPAIPlugin.Api.Controllers;

/// <summary>
/// Admin-only operational visibility and controlled mutation (Milestones 14
/// and 15): overview metrics, user administration, credit adjustment and
/// analytics, plugin/build visibility, AI usage/cost reporting, audit log,
/// and system status. Every action requires the AdminOnly policy (the
/// "Admin" Identity role) - anonymous and normal-authenticated users receive
/// 401/403, never admin data. Responses never include passwords, password
/// hashes, auth tokens, cookies, API keys, provider secrets, or raw Identity
/// claims. No direct balance mutation - every credit change goes through
/// CreditService and produces an immutable ledger entry. Revenue/financial
/// reporting sourced from Purchase records lives in AdminFinanceController.
/// </summary>
[ApiController]
[Route("api/admin")]
[Authorize(Policy = AdminAuthorization.AdminOnlyPolicy)]
[RequestSizeLimit(16 * 1024)]
public sealed class AdminController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly UserManager<IdentityUser> _userManager;
    private readonly CreditService _creditService;
    private readonly CreditOptions _creditOptions;
    private readonly AdminAuditService _auditService;
    private readonly IOptions<PlanningOptions> _planningOptions;
    private readonly ValidationOptions _validationOptions;
    private readonly ArtifactStorageOptions _artifactOptions;
    private readonly StripeOptions _stripeOptions;
    private readonly bool _transactionalEmailConfigured;
    private readonly IWebHostEnvironment _environment;
    private readonly IHostEnvironment _hostEnvironment;

    public AdminController(
        AppDbContext db,
        UserManager<IdentityUser> userManager,
        CreditService creditService,
        IOptions<CreditOptions> creditOptions,
        AdminAuditService auditService,
        IOptions<PlanningOptions> planningOptions,
        IOptions<ValidationOptions> validationOptions,
        IOptions<ArtifactStorageOptions> artifactOptions,
        IOptions<StripeOptions> stripeOptions,
        WPAIPlugin.Api.Email.ITransactionalEmailSender emailSender,
        IWebHostEnvironment environment,
        IHostEnvironment hostEnvironment)
    {
        _db = db;
        _userManager = userManager;
        _creditService = creditService;
        _creditOptions = creditOptions.Value;
        _auditService = auditService;
        _planningOptions = planningOptions;
        _validationOptions = validationOptions.Value;
        _artifactOptions = artifactOptions.Value;
        _stripeOptions = stripeOptions.Value;
        // Never the API key itself - just which implementation DI resolved
        // (ResendTransactionalEmailSender only when Resend:ApiKey is set).
        _transactionalEmailConfigured = emailSender is WPAIPlugin.Api.Email.ResendTransactionalEmailSender;
        _environment = environment;
        _hostEnvironment = hostEnvironment;
    }

    private string CurrentAdminId => _userManager.GetUserId(User)
        ?? throw new InvalidOperationException("AdminOnly policy guarantees an authenticated user.");

    [HttpGet("overview")]
    [ProducesResponseType(typeof(AdminOverviewResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Overview(CancellationToken cancellationToken)
    {
        var totalUsers = await _db.Users.CountAsync(cancellationToken);

        // ASP.NET Core Identity's own user table has no CreatedAtUtc column -
        // "registered recently" is only reportable once that field genuinely
        // exists, so it is intentionally omitted rather than approximated.
        var totalCreditBalance = await _db.CreditAccounts.SumAsync(a => (int?)a.Balance, cancellationToken) ?? 0;
        var grossConsumed = await _db.CreditTransactions
            .Where(t => t.Type == CreditTransactionType.PluginBuild || t.Type == CreditTransactionType.ValidatedBuild)
            .SumAsync(t => (int?)-t.Amount, cancellationToken) ?? 0;
        var refunded = await _db.CreditTransactions
            .Where(t => t.Type == CreditTransactionType.Refund)
            .SumAsync(t => (int?)t.Amount, cancellationToken) ?? 0;

        var totalProjects = await _db.PluginProjects.CountAsync(cancellationToken);
        var totalVersions = await _db.PluginVersions.CountAsync(cancellationToken);
        var standardBuilds = await _db.PluginVersions.CountAsync(v => !v.Validated, cancellationToken);
        var validatedBuilds = await _db.PluginVersions.CountAsync(v => v.Validated, cancellationToken);

        var aiRequests = await _db.AiUsageEvents.CountAsync(cancellationToken);
        var aiTotalTokens = await _db.AiUsageEvents.SumAsync(e => (long?)e.TotalTokens, cancellationToken) ?? 0;
        var aiCost = await _db.AiUsageEvents.SumAsync(e => (long?)e.EstimatedCostUsdMicros, cancellationToken);

        var now = DateTime.UtcNow;
        var promotions = await _db.Promotions.AsNoTracking().Select(p => new { p.IsEnabled, p.StartsAtUtc, p.EndsAtUtc }).ToListAsync(cancellationToken);
        var activePromotions = promotions.Count(p =>
            PromotionStateResolver.Resolve(p.IsEnabled, p.StartsAtUtc, p.EndsAtUtc, now) == PromotionState.Active);
        var promotionalCreditsGranted = await _db.CreditTransactions
            .Where(t => t.Type == CreditTransactionType.PromotionBonus)
            .SumAsync(t => (int?)t.Amount, cancellationToken) ?? 0;
        var freeBuildsRedeemed = await _db.BuildEntitlementTransactions
            .CountAsync(t => t.Type == BuildEntitlementTransactionType.FreeBuildConsumed, cancellationToken);

        return Ok(new AdminOverviewResponse
        {
            TotalUsers = totalUsers,
            UsersRegisteredLast7Days = 0,
            TotalCreditBalance = totalCreditBalance,
            GrossCreditsConsumed = grossConsumed,
            CreditsRefunded = refunded,
            TotalPluginProjects = totalProjects,
            TotalVersions = totalVersions,
            StandardBuilds = standardBuilds,
            ValidatedBuilds = validatedBuilds,
            AiRequests = aiRequests,
            AiTotalTokens = aiTotalTokens,
            AiEstimatedCostUsdMicros = aiCost,
            ActivePromotions = activePromotions,
            PromotionalCreditsGranted = promotionalCreditsGranted,
            FreeBuildsRedeemed = freeBuildsRedeemed,
        });
    }

    [HttpGet("users")]
    [ProducesResponseType(typeof(IReadOnlyList<AdminUserSummaryResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Users([FromQuery] string? email, CancellationToken cancellationToken)
    {
        var query = _db.Users.AsQueryable();
        if (!string.IsNullOrWhiteSpace(email))
        {
            query = query.Where(u => u.Email != null && u.Email.Contains(email));
        }

        var users = await query.OrderBy(u => u.Email).Take(200).ToListAsync(cancellationToken);
        var userIds = users.Select(u => u.Id).ToList();

        var balances = await _db.CreditAccounts
            .Where(a => userIds.Contains(a.UserId))
            .ToDictionaryAsync(a => a.UserId, a => a.Balance, cancellationToken);
        var projectCounts = await _db.PluginProjects
            .Where(p => userIds.Contains(p.UserId))
            .GroupBy(p => p.UserId)
            .Select(g => new { UserId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.UserId, g => g.Count, cancellationToken);
        var versionCounts = await _db.PluginVersions
            .Where(v => v.PluginProject != null && userIds.Contains(v.PluginProject.UserId))
            .GroupBy(v => v.PluginProject!.UserId)
            .Select(g => new { UserId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.UserId, g => g.Count, cancellationToken);

        var response = users.Select(u => new AdminUserSummaryResponse
        {
            Id = u.Id,
            Email = u.Email,
            LockedOut = u.LockoutEnd is not null && u.LockoutEnd > DateTimeOffset.UtcNow,
            CreditBalance = balances.GetValueOrDefault(u.Id, 0),
            PluginCount = projectCounts.GetValueOrDefault(u.Id, 0),
            VersionCount = versionCounts.GetValueOrDefault(u.Id, 0),
        }).ToList();

        return Ok(response);
    }

    [HttpGet("users/{id}")]
    [ProducesResponseType(typeof(AdminUserDetailResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UserDetail(string id, CancellationToken cancellationToken)
    {
        var user = await _userManager.FindByIdAsync(id);
        if (user is null)
        {
            return NotFound();
        }

        var balance = await _db.CreditAccounts.AsNoTracking()
            .Where(a => a.UserId == id).Select(a => (int?)a.Balance).SingleOrDefaultAsync(cancellationToken) ?? 0;
        var pluginCount = await _db.PluginProjects.CountAsync(p => p.UserId == id, cancellationToken);
        var versionCount = await _db.PluginVersions.CountAsync(v => v.PluginProject != null && v.PluginProject.UserId == id, cancellationToken);
        var isAdmin = await _userManager.IsInRoleAsync(user, AdminAuthorization.AdminRole);

        var aiEvents = _db.AiUsageEvents.Where(e => e.UserId == id);
        var aiRequestCount = await aiEvents.CountAsync(cancellationToken);
        var aiInputTokens = await aiEvents.SumAsync(e => (long?)e.InputTokens, cancellationToken) ?? 0;
        var aiOutputTokens = await aiEvents.SumAsync(e => (long?)e.OutputTokens, cancellationToken) ?? 0;
        var aiTotalTokens = await aiEvents.SumAsync(e => (long?)e.TotalTokens, cancellationToken) ?? 0;
        var aiCost = await aiEvents.SumAsync(e => (long?)e.EstimatedCostUsdMicros, cancellationToken);

        var recentActivity = await aiEvents
            .OrderByDescending(e => e.CreatedAtUtc)
            .Take(20)
            .Select(e => ToResponse(e, user.Email))
            .ToListAsync(cancellationToken);

        var recentCreditActivity = await _db.CreditTransactions
            .Where(t => t.UserId == id)
            .OrderByDescending(t => t.CreatedAtUtc)
            .Take(20)
            .Select(t => new AdminCreditLedgerEntryResponse { Type = t.Type, Amount = t.Amount, CreatedAtUtc = t.CreatedAtUtc })
            .ToListAsync(cancellationToken);

        var completedPurchases = _db.Purchases.Where(p => p.UserId == id && p.Status == PurchaseStatus.Completed);
        var lifetimePurchases = await completedPurchases.CountAsync(cancellationToken);
        var lifetimeRevenue = await completedPurchases.SumAsync(p => (int?)p.AmountMinor, cancellationToken) ?? 0;
        var lifetimeRefunded = await completedPurchases.SumAsync(p => (int?)p.RefundedAmountMinor, cancellationToken) ?? 0;
        var purchaseCurrency = await completedPurchases.Select(p => p.Currency).FirstOrDefaultAsync(cancellationToken) ?? "GBP";
        var creditsPurchased = await completedPurchases.SumAsync(p => (int?)p.CreditsPurchased, cancellationToken) ?? 0;
        var creditsConsumed = await _db.CreditTransactions
            .Where(t => t.UserId == id && (t.Type == CreditTransactionType.PluginBuild || t.Type == CreditTransactionType.ValidatedBuild))
            .SumAsync(t => (int?)-t.Amount, cancellationToken) ?? 0;

        return Ok(new AdminUserDetailResponse
        {
            Id = user.Id,
            Email = user.Email,
            LockedOut = user.LockoutEnd is not null && user.LockoutEnd > DateTimeOffset.UtcNow,
            IsAdmin = isAdmin,
            CreditBalance = balance,
            PluginCount = pluginCount,
            VersionCount = versionCount,
            AiRequestCount = aiRequestCount,
            AiInputTokens = aiInputTokens,
            AiOutputTokens = aiOutputTokens,
            AiTotalTokens = aiTotalTokens,
            AiEstimatedCostUsdMicros = aiCost,
            RecentAiActivity = recentActivity,
            RecentCreditActivity = recentCreditActivity,
            LifetimePurchases = lifetimePurchases,
            LifetimeRevenueMinor = lifetimeRevenue,
            LifetimeRefundedMinor = lifetimeRefunded,
            PurchaseCurrency = purchaseCurrency,
            CreditsPurchased = creditsPurchased,
            CreditsConsumed = creditsConsumed,
            ApproximateContributionMinor = lifetimeRevenue > 0 ? lifetimeRevenue - lifetimeRefunded : null,
        });
    }

    /// <summary>
    /// Signed credit adjustment via the existing ledger-backed CreditService -
    /// never a direct balance write. Reason is mandatory and stored only in
    /// the audit log (never in the ledger reference). IdempotencyKey lets a
    /// retried/duplicated submission return the original outcome instead of
    /// adjusting twice.
    /// </summary>
    [ValidateAntiForgeryToken]
    [HttpPost("users/{id}/credits/adjust")]
    [ProducesResponseType(typeof(AdminCreditAdjustmentResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> AdjustCredits(string id, [FromBody] AdminCreditAdjustmentRequest request, CancellationToken cancellationToken)
    {
        var user = await _userManager.FindByIdAsync(id);
        if (user is null)
        {
            return NotFound();
        }

        if (request.Amount == 0)
        {
            return BadRequest(new { error = "Adjustment amount must be non-zero." });
        }

        if (Math.Abs(request.Amount) > _creditOptions.MaxAdminAdjustmentMagnitude)
        {
            return BadRequest(new { error = $"Adjustment magnitude must not exceed {_creditOptions.MaxAdminAdjustmentMagnitude}." });
        }

        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            return BadRequest(new { error = "A reason is required." });
        }

        if (request.IdempotencyKey == Guid.Empty)
        {
            return BadRequest(new { error = "IdempotencyKey is required." });
        }

        var (success, balance, alreadyApplied) = await _creditService.AdjustCreditsAsync(
            id, request.Amount, request.IdempotencyKey, cancellationToken);

        if (!success)
        {
            return Conflict(new { error = "Adjustment would reduce the balance below zero.", balance });
        }

        if (!alreadyApplied)
        {
            await _auditService.RecordAsync(
                CurrentAdminId, AdminAuditAction.CreditAdjustment, AdminAuditTargetType.User, id,
                $"{(request.Amount > 0 ? "+" : string.Empty)}{request.Amount}: {request.Reason.Trim()}", cancellationToken);
        }

        return Ok(new AdminCreditAdjustmentResponse { Amount = request.Amount, Balance = balance, AlreadyApplied = alreadyApplied });
    }

    /// <summary>Locks the account (ASP.NET Core Identity lockout) - never exposes or resets the password.</summary>
    [ValidateAntiForgeryToken]
    [HttpPost("users/{id}/lock")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> LockUser(string id, CancellationToken cancellationToken)
    {
        var user = await _userManager.FindByIdAsync(id);
        if (user is null)
        {
            return NotFound();
        }

        if (!user.LockoutEnabled)
        {
            await _userManager.SetLockoutEnabledAsync(user, true);
        }
        await _userManager.SetLockoutEndDateAsync(user, DateTimeOffset.MaxValue);
        await _auditService.RecordAsync(CurrentAdminId, AdminAuditAction.AccountLocked, AdminAuditTargetType.User, id, null, cancellationToken);

        return Ok();
    }

    /// <summary>Unlocks the account - never exposes or resets the password.</summary>
    [ValidateAntiForgeryToken]
    [HttpPost("users/{id}/unlock")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UnlockUser(string id, CancellationToken cancellationToken)
    {
        var user = await _userManager.FindByIdAsync(id);
        if (user is null)
        {
            return NotFound();
        }

        await _userManager.SetLockoutEndDateAsync(user, null);
        await _auditService.RecordAsync(CurrentAdminId, AdminAuditAction.AccountUnlocked, AdminAuditTargetType.User, id, null, cancellationToken);

        return Ok();
    }

    [HttpGet("credits")]
    [ProducesResponseType(typeof(AdminCreditAnalyticsResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> CreditAnalytics(CancellationToken cancellationToken)
    {
        var totalHeld = await _db.CreditAccounts.SumAsync(a => (int?)a.Balance, cancellationToken) ?? 0;

        var signupGranted = await _db.CreditTransactions
            .Where(t => t.Type == CreditTransactionType.SignupGrant)
            .SumAsync(t => (int?)t.Amount, cancellationToken) ?? 0;
        var adjustmentsGranted = await _db.CreditTransactions
            .Where(t => t.Type == CreditTransactionType.AdminAdjustment && t.Amount > 0)
            .SumAsync(t => (int?)t.Amount, cancellationToken) ?? 0;
        var adjustmentsDeducted = await _db.CreditTransactions
            .Where(t => t.Type == CreditTransactionType.AdminAdjustment && t.Amount < 0)
            .SumAsync(t => (int?)-t.Amount, cancellationToken) ?? 0;
        var standardUsage = await _db.CreditTransactions
            .Where(t => t.Type == CreditTransactionType.PluginBuild)
            .SumAsync(t => (int?)-t.Amount, cancellationToken) ?? 0;
        var validatedUsage = await _db.CreditTransactions
            .Where(t => t.Type == CreditTransactionType.ValidatedBuild)
            .SumAsync(t => (int?)-t.Amount, cancellationToken) ?? 0;
        var refunded = await _db.CreditTransactions
            .Where(t => t.Type == CreditTransactionType.Refund)
            .SumAsync(t => (int?)t.Amount, cancellationToken) ?? 0;

        var purchasedCredits = await _db.CreditTransactions
            .Where(t => t.Type == CreditTransactionType.CreditPurchase)
            .SumAsync(t => (int?)t.Amount, cancellationToken) ?? 0;

        var grossConsumed = standardUsage + validatedUsage;

        return Ok(new AdminCreditAnalyticsResponse
        {
            TotalCreditsHeld = totalHeld,
            SignupCreditsGranted = signupGranted,
            AdminAdjustmentsNet = adjustmentsGranted - adjustmentsDeducted,
            AdminAdjustmentsGranted = adjustmentsGranted,
            AdminAdjustmentsDeducted = adjustmentsDeducted,
            GrossCreditsConsumed = grossConsumed,
            CreditsRefunded = refunded,
            NetCreditsConsumed = grossConsumed - refunded,
            StandardBuildUsage = standardUsage,
            ValidatedBuildUsage = validatedUsage,
            PurchasedCredits = purchasedCredits,
        });
    }

    [HttpGet("audit-log")]
    [ProducesResponseType(typeof(IReadOnlyList<AdminAuditLogEntryResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> AuditLog(
        [FromQuery] string? admin, [FromQuery] string? action, [FromQuery] string? targetId,
        [FromQuery] DateTime? from, [FromQuery] DateTime? to, CancellationToken cancellationToken)
    {
        var query = _db.AdminAuditLogs.AsQueryable();
        if (!string.IsNullOrWhiteSpace(admin)) query = query.Where(a => a.AdminUserId == admin);
        if (!string.IsNullOrWhiteSpace(action)) query = query.Where(a => a.Action == action);
        if (!string.IsNullOrWhiteSpace(targetId)) query = query.Where(a => a.TargetId == targetId);
        if (from.HasValue) query = query.Where(a => a.CreatedAtUtc >= from.Value);
        if (to.HasValue) query = query.Where(a => a.CreatedAtUtc <= to.Value);

        var entries = await query.OrderByDescending(a => a.CreatedAtUtc).Take(200).ToListAsync(cancellationToken);

        var userIds = entries.Select(e => e.AdminUserId).Concat(entries.Select(e => e.TargetId)).Distinct().ToList();
        var emails = await _db.Users.Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Email, cancellationToken);

        var response = entries.Select(e => new AdminAuditLogEntryResponse
        {
            Id = e.Id,
            AdminUserId = e.AdminUserId,
            AdminEmail = emails.GetValueOrDefault(e.AdminUserId),
            Action = e.Action,
            TargetType = e.TargetType,
            TargetId = e.TargetId,
            TargetEmail = e.TargetType == AdminAuditTargetType.User ? emails.GetValueOrDefault(e.TargetId) : null,
            Description = e.Description,
            CreatedAtUtc = e.CreatedAtUtc,
        }).ToList();

        return Ok(response);
    }

    [HttpGet("plugins")]
    [ProducesResponseType(typeof(IReadOnlyList<AdminPluginProjectResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Plugins(
        [FromQuery] string? userId, [FromQuery] bool? validated, CancellationToken cancellationToken)
    {
        var query = _db.PluginProjects.Include(p => p.Versions).AsQueryable();
        if (!string.IsNullOrWhiteSpace(userId))
        {
            query = query.Where(p => p.UserId == userId);
        }

        var projects = await query.OrderByDescending(p => p.UpdatedAtUtc).Take(200).ToListAsync(cancellationToken);
        if (validated.HasValue)
        {
            projects = projects.Where(p => p.Versions.Count > 0
                && p.Versions.OrderByDescending(v => v.RevisionNumber).First().Validated == validated.Value).ToList();
        }

        var userIds = projects.Select(p => p.UserId).Distinct().ToList();
        var emails = await _db.Users.Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.Email, cancellationToken);

        var response = projects.Select(p =>
        {
            var latest = p.Versions.OrderByDescending(v => v.RevisionNumber).FirstOrDefault();
            return new AdminPluginProjectResponse
            {
                Id = p.Id,
                UserId = p.UserId,
                UserEmail = emails.GetValueOrDefault(p.UserId),
                Name = p.Name,
                Slug = p.Slug,
                CreatedAtUtc = p.CreatedAtUtc,
                UpdatedAtUtc = p.UpdatedAtUtc,
                VersionCount = p.Versions.Count,
                LatestValidated = latest?.Validated ?? false,
            };
        }).ToList();

        return Ok(response);
    }

    [HttpGet("ai-usage")]
    [ProducesResponseType(typeof(AdminAiUsageAggregateResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> AiUsageReport(
        [FromQuery] DateTime? from, [FromQuery] DateTime? to, [FromQuery] string? userId,
        [FromQuery] string? model, [FromQuery] string? provider, [FromQuery] bool? succeeded,
        CancellationToken cancellationToken)
    {
        var query = _db.AiUsageEvents.AsQueryable();
        if (from.HasValue) query = query.Where(e => e.CreatedAtUtc >= from.Value);
        if (to.HasValue) query = query.Where(e => e.CreatedAtUtc <= to.Value);
        if (!string.IsNullOrWhiteSpace(userId)) query = query.Where(e => e.UserId == userId);
        if (!string.IsNullOrWhiteSpace(model)) query = query.Where(e => e.Model == model);
        if (!string.IsNullOrWhiteSpace(provider)) query = query.Where(e => e.Provider == provider);
        if (succeeded.HasValue) query = query.Where(e => e.Succeeded == succeeded.Value);

        var requests = await query.CountAsync(cancellationToken);
        var successfulRequests = await query.CountAsync(e => e.Succeeded, cancellationToken);
        var inputTokens = await query.SumAsync(e => (long?)e.InputTokens, cancellationToken) ?? 0;
        var outputTokens = await query.SumAsync(e => (long?)e.OutputTokens, cancellationToken) ?? 0;
        var totalTokens = await query.SumAsync(e => (long?)e.TotalTokens, cancellationToken) ?? 0;
        var cost = await query.SumAsync(e => (long?)e.EstimatedCostUsdMicros, cancellationToken);

        var byProvider = await query.GroupBy(e => e.Provider)
            .Select(g => new AdminAiUsageBreakdownRow
            {
                Key = g.Key,
                Requests = g.Count(),
                TotalTokens = g.Sum(e => (long?)e.TotalTokens) ?? 0,
                EstimatedCostUsdMicros = g.Sum(e => (long?)e.EstimatedCostUsdMicros),
            })
            .ToListAsync(cancellationToken);

        var byModel = await query.Where(e => e.Model != null).GroupBy(e => e.Model!)
            .Select(g => new AdminAiUsageBreakdownRow
            {
                Key = g.Key,
                Requests = g.Count(),
                TotalTokens = g.Sum(e => (long?)e.TotalTokens) ?? 0,
                EstimatedCostUsdMicros = g.Sum(e => (long?)e.EstimatedCostUsdMicros),
            })
            .ToListAsync(cancellationToken);

        var rawByDay = await query
            .Select(e => new { e.CreatedAtUtc, e.TotalTokens, e.EstimatedCostUsdMicros })
            .ToListAsync(cancellationToken);
        var byDay = rawByDay
            .GroupBy(e => DateOnly.FromDateTime(e.CreatedAtUtc))
            .OrderBy(g => g.Key)
            .Select(g => new AdminAiUsageDailyRow
            {
                Date = g.Key,
                Requests = g.Count(),
                TotalTokens = g.Sum(e => (long?)e.TotalTokens) ?? 0,
                EstimatedCostUsdMicros = g.Sum(e => (long?)e.EstimatedCostUsdMicros),
            })
            .ToList();

        var recentEvents = await query.OrderByDescending(e => e.CreatedAtUtc).Take(50).ToListAsync(cancellationToken);
        var recentUserIds = recentEvents.Where(e => e.UserId != null).Select(e => e.UserId!).Distinct().ToList();
        var recentEmails = await _db.Users.Where(u => recentUserIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.Email, cancellationToken);

        return Ok(new AdminAiUsageAggregateResponse
        {
            Requests = requests,
            SuccessfulRequests = successfulRequests,
            FailedRequests = requests - successfulRequests,
            InputTokens = inputTokens,
            OutputTokens = outputTokens,
            TotalTokens = totalTokens,
            EstimatedCostUsdMicros = cost,
            AverageCostUsdMicrosPerRequest = requests > 0 && cost.HasValue ? (decimal)cost.Value / requests : null,
            ByProvider = byProvider,
            ByModel = byModel,
            ByDay = byDay,
            RecentEvents = recentEvents.Select(e => ToResponse(e, recentEmails.GetValueOrDefault(e.UserId ?? string.Empty))).ToList(),
        });
    }

    [HttpGet("system")]
    [ProducesResponseType(typeof(AdminSystemStatusResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> System(CancellationToken cancellationToken)
    {
        var planning = _planningOptions.Value;
        var providerKey = planning.DefaultProvider.Equals("anthropic", StringComparison.OrdinalIgnoreCase)
            ? planning.Anthropic.ApiKey : planning.OpenAI.ApiKey;

        bool databaseHealthy;
        try
        {
            databaseHealthy = await _db.Database.CanConnectAsync(cancellationToken);
        }
        catch
        {
            databaseHealthy = false;
        }

        var artifactRoot = Path.IsPathRooted(_artifactOptions.RootPath)
            ? _artifactOptions.RootPath
            : Path.Combine(_environment.ContentRootPath, _artifactOptions.RootPath);
        bool artifactStorageWritable;
        try
        {
            Directory.CreateDirectory(artifactRoot);
            artifactStorageWritable = true;
        }
        catch
        {
            artifactStorageWritable = false;
        }

        return Ok(new AdminSystemStatusResponse
        {
            ApplicationVersion = typeof(AdminController).Assembly.GetName().Version?.ToString(),
            Environment = _hostEnvironment.EnvironmentName,
            DatabaseHealthy = databaseHealthy,
            PlanningProvider = planning.DefaultProvider,
            PlanningProviderConfigured = !string.IsNullOrWhiteSpace(providerKey),
            DockerValidationAvailable = _validationOptions.Enabled,
            ArtifactStorageWritable = artifactStorageWritable,
            StripeConfigured = !string.IsNullOrWhiteSpace(_stripeOptions.SecretKey)
                && !string.IsNullOrWhiteSpace(_stripeOptions.WebhookSecret)
                && !string.IsNullOrWhiteSpace(_stripeOptions.PublicBaseUrl),
            TransactionalEmailConfigured = _transactionalEmailConfigured,
        });
    }

    private static AdminAiUsageEventResponse ToResponse(AiUsageEvent e, string? userEmail) => new()
    {
        Id = e.Id,
        UserId = e.UserId,
        UserEmail = userEmail,
        OperationType = e.OperationType,
        Provider = e.Provider,
        Model = e.Model,
        InputTokens = e.InputTokens,
        OutputTokens = e.OutputTokens,
        TotalTokens = e.TotalTokens,
        EstimatedCostUsdMicros = e.EstimatedCostUsdMicros,
        DurationMs = e.DurationMs,
        Succeeded = e.Succeeded,
        FailureCategory = e.FailureCategory,
        CreatedAtUtc = e.CreatedAtUtc,
    };
}
