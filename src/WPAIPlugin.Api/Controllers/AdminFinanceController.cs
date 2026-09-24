using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WPAIPlugin.Api.Data;
using WPAIPlugin.Api.Security;

namespace WPAIPlugin.Api.Controllers;

/// <summary>
/// Admin-only revenue and combined AI-cost/revenue/operational reporting
/// (Milestone 16). Revenue is always sourced from Purchase records - never
/// derived from CreditTransaction/credit balances, which are a distinct
/// accounting concern (a purchased credit and a consumed credit are not the
/// same thing as cash). AdminOnly-gated exactly like AdminController.
/// </summary>
[ApiController]
[Route("api/admin/finance")]
[Authorize(Policy = AdminAuthorization.AdminOnlyPolicy)]
public sealed class AdminFinanceController : ControllerBase
{
    private readonly AppDbContext _db;

    public AdminFinanceController(AppDbContext db)
    {
        _db = db;
    }

    [HttpGet("revenue")]
    [ProducesResponseType(typeof(AdminRevenueResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Revenue([FromQuery] DateTime? from, [FromQuery] DateTime? to, CancellationToken cancellationToken)
    {
        var query = _db.Purchases.AsQueryable();
        if (from.HasValue) query = query.Where(p => p.CreatedAtUtc >= from.Value);
        if (to.HasValue) query = query.Where(p => p.CreatedAtUtc <= to.Value);

        var completed = query.Where(p => p.Status == PurchaseStatus.Completed);
        var grossRevenue = await completed.SumAsync(p => (int?)p.AmountMinor, cancellationToken) ?? 0;
        var refunded = await completed.SumAsync(p => (int?)p.RefundedAmountMinor, cancellationToken) ?? 0;
        var successfulPurchases = await completed.CountAsync(cancellationToken);
        var failedOrCancelled = await query.CountAsync(
            p => p.Status == PurchaseStatus.Failed || p.Status == PurchaseStatus.Cancelled, cancellationToken);
        var creditsSold = await completed.SumAsync(p => (int?)p.CreditsPurchased, cancellationToken) ?? 0;
        var purchasingCustomers = await completed.Select(p => p.UserId).Distinct().CountAsync(cancellationToken);

        var rawForDay = await completed
            .Select(p => new { p.CreatedAtUtc, p.AmountMinor, p.CreditsPurchased })
            .ToListAsync(cancellationToken);
        var byPeriod = rawForDay
            .GroupBy(p => DateOnly.FromDateTime(p.CreatedAtUtc))
            .OrderBy(g => g.Key)
            .Select(g => new AdminRevenueByPeriodRow
            {
                Date = g.Key,
                GrossRevenueMinor = g.Sum(p => p.AmountMinor),
                SuccessfulPurchases = g.Count(),
                CreditsSold = g.Sum(p => p.CreditsPurchased),
            })
            .ToList();

        var recentPurchases = await query.OrderByDescending(p => p.CreatedAtUtc).Take(50).ToListAsync(cancellationToken);
        var userIds = recentPurchases.Select(p => p.UserId).Distinct().ToList();
        var emails = await _db.Users.Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Email, cancellationToken);

        // GBP-only currently (see CreditPackOptions) - a genuinely multi-currency
        // catalog would need per-currency totals instead of one combined figure.
        var currency = await completed.Select(p => p.Currency).FirstOrDefaultAsync(cancellationToken) ?? "GBP";

        return Ok(new AdminRevenueResponse
        {
            GrossRevenueMinor = grossRevenue,
            RefundedMinor = refunded,
            NetRevenueMinor = grossRevenue - refunded,
            Currency = currency,
            SuccessfulPurchases = successfulPurchases,
            FailedOrCancelledPurchases = failedOrCancelled,
            CreditsSold = creditsSold,
            PurchasingCustomers = purchasingCustomers,
            AveragePurchaseMinor = successfulPurchases > 0 ? (decimal)grossRevenue / successfulPurchases : null,
            ByPeriod = byPeriod,
            RecentPurchases = recentPurchases.Select(p => new AdminPurchaseRow
            {
                Id = p.Id, UserId = p.UserId, UserEmail = emails.GetValueOrDefault(p.UserId),
                PackId = p.PackId, AmountMinor = p.AmountMinor, Currency = p.Currency,
                CreditsPurchased = p.CreditsPurchased, Status = p.Status,
                RefundedAmountMinor = p.RefundedAmountMinor, CreatedAtUtc = p.CreatedAtUtc, CompletedAtUtc = p.CompletedAtUtc,
            }).ToList(),
        });
    }

    [HttpGet("contribution")]
    [ProducesResponseType(typeof(AdminContributionResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Contribution(CancellationToken cancellationToken)
    {
        var completed = _db.Purchases.Where(p => p.Status == PurchaseStatus.Completed);
        var revenue = await completed.SumAsync(p => (int?)p.AmountMinor, cancellationToken) ?? 0;
        var refunded = await completed.SumAsync(p => (int?)p.RefundedAmountMinor, cancellationToken) ?? 0;
        var purchasingCustomers = await completed.Select(p => p.UserId).Distinct().CountAsync(cancellationToken);

        var aiCost = await _db.AiUsageEvents.SumAsync(e => (long?)e.EstimatedCostUsdMicros, cancellationToken);
        var aiCustomers = await _db.AiUsageEvents.Where(e => e.UserId != null).Select(e => e.UserId).Distinct().CountAsync(cancellationToken);

        var totalBuilds = await _db.PluginVersions.CountAsync(cancellationToken);

        return Ok(new AdminContributionResponse
        {
            RevenueMinor = revenue,
            RefundedRevenueMinor = refunded,
            EstimatedAiSpendUsdMicros = aiCost,
            RevenuePerCustomerMinor = purchasingCustomers > 0 ? (decimal)revenue / purchasingCustomers : null,
            AiCostPerCustomerUsdMicros = aiCustomers > 0 && aiCost.HasValue ? (decimal)aiCost.Value / aiCustomers : null,
            RevenuePerBuildMinor = totalBuilds > 0 ? (decimal)revenue / totalBuilds : null,
            AiCostPerBuildUsdMicros = totalBuilds > 0 && aiCost.HasValue ? (decimal)aiCost.Value / totalBuilds : null,
            Note = "Approximate contribution estimate: revenue in GBP pence vs. estimated AI spend in USD micro-dollars, " +
                   "shown separately and never combined into one figure or converted at an assumed exchange rate. " +
                   "Excludes Stripe fees (not currently available), hosting, and all other business costs - never call this profit.",
        });
    }

    [HttpGet("operational")]
    [ProducesResponseType(typeof(AdminOperationalAnalyticsResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Operational([FromQuery] DateTime? from, [FromQuery] DateTime? to, CancellationToken cancellationToken)
    {
        // ASP.NET Core Identity's own user table has no CreatedAtUtc column,
        // so "registrations in range" cannot be reported here - see Overview
        // for the same, already-documented limitation.
        var registrations = 0;

        var aiEventsQuery = _db.AiUsageEvents.AsQueryable();
        if (from.HasValue) aiEventsQuery = aiEventsQuery.Where(e => e.CreatedAtUtc >= from.Value);
        if (to.HasValue) aiEventsQuery = aiEventsQuery.Where(e => e.CreatedAtUtc <= to.Value);
        var planGenerations = await aiEventsQuery.CountAsync(e => e.OperationType == AiUsageOperationType.Plan, cancellationToken);
        var planFailures = await aiEventsQuery.CountAsync(e => e.OperationType == AiUsageOperationType.Plan && !e.Succeeded, cancellationToken);
        var aiRequests = await aiEventsQuery.CountAsync(cancellationToken);
        var aiFailures = await aiEventsQuery.CountAsync(e => !e.Succeeded, cancellationToken);

        var versionsQuery = _db.PluginVersions.AsQueryable();
        if (from.HasValue) versionsQuery = versionsQuery.Where(v => v.CreatedAtUtc >= from.Value);
        if (to.HasValue) versionsQuery = versionsQuery.Where(v => v.CreatedAtUtc <= to.Value);
        var totalBuilds = await versionsQuery.CountAsync(cancellationToken);
        var standardBuilds = await versionsQuery.CountAsync(v => !v.Validated, cancellationToken);
        var validatedBuilds = await versionsQuery.CountAsync(v => v.Validated, cancellationToken);

        var transactionsQuery = _db.CreditTransactions.AsQueryable();
        if (from.HasValue) transactionsQuery = transactionsQuery.Where(t => t.CreatedAtUtc >= from.Value);
        if (to.HasValue) transactionsQuery = transactionsQuery.Where(t => t.CreatedAtUtc <= to.Value);
        var creditsConsumed = await transactionsQuery
            .Where(t => t.Type == CreditTransactionType.PluginBuild || t.Type == CreditTransactionType.ValidatedBuild)
            .SumAsync(t => (int?)-t.Amount, cancellationToken) ?? 0;
        var refundedBuilds = await transactionsQuery.CountAsync(t => t.Type == CreditTransactionType.Refund, cancellationToken);

        var purchasesQuery = _db.Purchases.AsQueryable();
        if (from.HasValue) purchasesQuery = purchasesQuery.Where(p => p.CreatedAtUtc >= from.Value);
        if (to.HasValue) purchasesQuery = purchasesQuery.Where(p => p.CreatedAtUtc <= to.Value);
        var purchases = await purchasesQuery.CountAsync(p => p.Status == PurchaseStatus.Completed, cancellationToken);
        var revenue = await purchasesQuery.Where(p => p.Status == PurchaseStatus.Completed).SumAsync(p => (int?)p.AmountMinor, cancellationToken) ?? 0;

        return Ok(new AdminOperationalAnalyticsResponse
        {
            From = from,
            To = to,
            Registrations = registrations,
            PlanGenerations = planGenerations,
            PlanGenerationFailures = planFailures,
            TotalBuilds = totalBuilds,
            StandardBuilds = standardBuilds,
            ValidatedBuilds = validatedBuilds,
            RefundedBuilds = refundedBuilds,
            CreditsConsumed = creditsConsumed,
            AiRequests = aiRequests,
            AiFailures = aiFailures,
            Purchases = purchases,
            RevenueMinor = revenue,
            PaymentWebhookFailures = 0,
        });
    }
}
