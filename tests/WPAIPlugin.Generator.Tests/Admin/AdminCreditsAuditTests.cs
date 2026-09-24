using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WPAIPlugin.Api.Credits;
using WPAIPlugin.Api.Data;
using WPAIPlugin.Api.Security;
using WPAIPlugin.Generator.Tests.Projects;
using Xunit;

namespace WPAIPlugin.Generator.Tests.Admin;

/// <summary>
/// Admin credit adjustment, credit analytics, audit log, and account
/// lock/unlock (Milestone 15). Reuses ProjectsTestFactory (isolated InMemory
/// database, real Identity/CSRF pipeline) - no live database, Docker, or AI
/// provider required.
/// </summary>
public class AdminCreditsAuditTests
{
    private const string Password = "Str0ng!Passw0rd";

    private static async Task<HttpClient> Register(ProjectsTestFactory factory, string email)
    {
        var client = factory.CreateClient();
        (await client.PostJsonWithCsrfAsync("/api/account/register", new { email, password = Password })).EnsureSuccessStatusCode();
        return client;
    }

    private static async Task PromoteToAdminAsync(ProjectsTestFactory factory, string email)
    {
        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        if (!await roleManager.RoleExistsAsync(AdminAuthorization.AdminRole))
        {
            await roleManager.CreateAsync(new IdentityRole(AdminAuthorization.AdminRole));
        }
        var user = await userManager.FindByEmailAsync(email);
        await userManager.AddToRoleAsync(user!, AdminAuthorization.AdminRole);
    }

    private static async Task<HttpClient> RegisterAdmin(ProjectsTestFactory factory, string email)
    {
        var client = await Register(factory, email);
        await PromoteToAdminAsync(factory, email);
        (await client.PostWithCsrfAsync("/api/account/logout", null)).EnsureSuccessStatusCode();
        (await client.PostJsonWithCsrfAsync("/api/account/login", new { email, password = Password })).EnsureSuccessStatusCode();
        return client;
    }

    private static async Task<string> GetUserIdAsync(ProjectsTestFactory factory, string email)
    {
        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
        var user = await userManager.FindByEmailAsync(email);
        return user!.Id;
    }

    private static async Task<int> GetBalanceAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<JsonElement>("/api/credits")).GetProperty("balance").GetInt32();

    // --- Credit adjustment: positive/negative/zero/over-deduct ---------------

    [Fact]
    public async Task PositiveAdjustment_IncreasesBalance_WritesLedgerAndAudit()
    {
        using var factory = new ProjectsTestFactory();
        var customerEmail = $"customer-{Guid.NewGuid()}@example.com";
        using var customer = await Register(factory, customerEmail);
        var customerId = await GetUserIdAsync(factory, customerEmail);
        var startingBalance = await GetBalanceAsync(customer);

        var adminEmail = $"admin-{Guid.NewGuid()}@example.com";
        using var admin = await RegisterAdmin(factory, adminEmail);

        var response = await admin.PostJsonWithCsrfAsync($"/api/admin/users/{customerId}/credits/adjust", new
        {
            amount = 50, reason = "Customer support compensation", idempotencyKey = Guid.NewGuid(),
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(startingBalance + 50, body.GetProperty("balance").GetInt32());
        Assert.False(body.GetProperty("alreadyApplied").GetBoolean());

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var ledgerEntry = await db.CreditTransactions.SingleAsync(t => t.UserId == customerId && t.Type == CreditTransactionType.AdminAdjustment);
        Assert.Equal(50, ledgerEntry.Amount);
        Assert.StartsWith("admin:", ledgerEntry.Reference);
        Assert.DoesNotContain(adminEmail, ledgerEntry.Reference);
        Assert.DoesNotContain(customerEmail, ledgerEntry.Reference);

        var auditEntry = await db.AdminAuditLogs.SingleAsync(a => a.TargetId == customerId && a.Action == AdminAuditAction.CreditAdjustment);
        Assert.Contains("Customer support compensation", auditEntry.Description);
        var adminId = await GetUserIdAsync(factory, adminEmail);
        Assert.Equal(adminId, auditEntry.AdminUserId);
    }

    [Fact]
    public async Task NegativeAdjustment_DecreasesBalance()
    {
        using var factory = new ProjectsTestFactory();
        var customerEmail = $"customer-{Guid.NewGuid()}@example.com";
        using var customer = await Register(factory, customerEmail);
        var customerId = await GetUserIdAsync(factory, customerEmail);
        var startingBalance = await GetBalanceAsync(customer);

        using var admin = await RegisterAdmin(factory, $"admin-{Guid.NewGuid()}@example.com");

        var response = await admin.PostJsonWithCsrfAsync($"/api/admin/users/{customerId}/credits/adjust", new
        {
            amount = -20, reason = "Correcting a duplicate grant", idempotencyKey = Guid.NewGuid(),
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(startingBalance - 20, body.GetProperty("balance").GetInt32());
    }

    [Fact]
    public async Task ZeroAdjustment_Rejected()
    {
        using var factory = new ProjectsTestFactory();
        var customerId = await GetUserIdAsync(factory, await RegisterCustomerEmail(factory));
        using var admin = await RegisterAdmin(factory, $"admin-{Guid.NewGuid()}@example.com");

        var response = await admin.PostJsonWithCsrfAsync($"/api/admin/users/{customerId}/credits/adjust", new
        {
            amount = 0, reason = "no-op", idempotencyKey = Guid.NewGuid(),
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Adjustment_MissingReason_Rejected()
    {
        using var factory = new ProjectsTestFactory();
        var customerId = await GetUserIdAsync(factory, await RegisterCustomerEmail(factory));
        using var admin = await RegisterAdmin(factory, $"admin-{Guid.NewGuid()}@example.com");

        var response = await admin.PostJsonWithCsrfAsync($"/api/admin/users/{customerId}/credits/adjust", new
        {
            amount = 10, reason = "   ", idempotencyKey = Guid.NewGuid(),
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Adjustment_ExceedsMaxMagnitude_Rejected()
    {
        using var factory = new ProjectsTestFactory();
        var customerId = await GetUserIdAsync(factory, await RegisterCustomerEmail(factory));
        using var admin = await RegisterAdmin(factory, $"admin-{Guid.NewGuid()}@example.com");

        var response = await admin.PostJsonWithCsrfAsync($"/api/admin/users/{customerId}/credits/adjust", new
        {
            amount = 999_999, reason = "too much", idempotencyKey = Guid.NewGuid(),
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task OverDeduction_BlockedWithoutReducingBelowZero()
    {
        using var factory = new ProjectsTestFactory();
        var customerEmail = await RegisterCustomerEmail(factory);
        var customerId = await GetUserIdAsync(factory, customerEmail);
        using var admin = await RegisterAdmin(factory, $"admin-{Guid.NewGuid()}@example.com");

        var response = await admin.PostJsonWithCsrfAsync($"/api/admin/users/{customerId}/credits/adjust", new
        {
            amount = -101, reason = "over-deduct attempt", idempotencyKey = Guid.NewGuid(), // default signup grant is 100
        });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var account = await db.CreditAccounts.SingleAsync(a => a.UserId == customerId);
        Assert.True(account.Balance >= 0);
        Assert.False(await db.CreditTransactions.AnyAsync(t => t.UserId == customerId && t.Type == CreditTransactionType.AdminAdjustment));
    }

    private static async Task<string> RegisterCustomerEmail(ProjectsTestFactory factory)
    {
        var email = $"customer-{Guid.NewGuid()}@example.com";
        using var client = await Register(factory, email);
        return email;
    }

    // --- Server-owned reference / browser cannot set transaction type -------

    [Fact]
    public async Task ServerGeneratesReference_BrowserCannotSetTransactionType()
    {
        using var factory = new ProjectsTestFactory();
        var customerId = await GetUserIdAsync(factory, await RegisterCustomerEmail(factory));
        using var admin = await RegisterAdmin(factory, $"admin-{Guid.NewGuid()}@example.com");

        (await admin.PostJsonWithCsrfAsync($"/api/admin/users/{customerId}/credits/adjust", new
        {
            amount = 5, reason = "test", idempotencyKey = Guid.NewGuid(),
            type = "SignupGrant", reference = "attacker-controlled",
        })).EnsureSuccessStatusCode();

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var entry = await db.CreditTransactions.SingleAsync(t => t.UserId == customerId && t.Type == CreditTransactionType.AdminAdjustment);
        Assert.Equal(CreditTransactionType.AdminAdjustment, entry.Type);
        Assert.NotEqual("attacker-controlled", entry.Reference);
    }

    // --- Authorization ---------------------------------------------------

    [Fact]
    public async Task NormalUser_ForbiddenFromAdjustingCredits()
    {
        using var factory = new ProjectsTestFactory();
        var customerId = await GetUserIdAsync(factory, await RegisterCustomerEmail(factory));
        using var normal = await Register(factory, $"normal-{Guid.NewGuid()}@example.com");

        var response = await normal.PostJsonWithCsrfAsync($"/api/admin/users/{customerId}/credits/adjust", new
        {
            amount = 10, reason = "test", idempotencyKey = Guid.NewGuid(),
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task AnonymousUser_ForbiddenFromAdjustingCredits()
    {
        using var factory = new ProjectsTestFactory();
        var customerId = await GetUserIdAsync(factory, await RegisterCustomerEmail(factory));
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync($"/api/admin/users/{customerId}/credits/adjust", new
        {
            amount = 10, reason = "test", idempotencyKey = Guid.NewGuid(),
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // --- Concurrency / duplicate submission ---------------------------------

    // Real, forced-overlap concurrency coverage for AdjustCreditsAsync lives in
    // CreditServiceTests.OverlappingAdjustments_ApplyExactlyOnce, using the same
    // SaveChangesInterceptor-based barrier already proven for TryChargeAsync/
    // RefundAsync - EF Core InMemory has no row locking or rollback, so forcing
    // genuine overlap (not just parallel dispatch) requires that adapter rather
    // than Task.WhenAll over real HTTP, which cannot guarantee true overlap and
    // would otherwise test InMemory's contention tolerance, not CreditService's
    // correctness. This admin-API-level test instead confirms sequential admin
    // adjustments over HTTP simply keep accumulating correctly.
    [Fact]
    public async Task SequentialAdjustments_AccumulateCorrectly()
    {
        using var factory = new ProjectsTestFactory();
        var customerEmail = await RegisterCustomerEmail(factory);
        var customerId = await GetUserIdAsync(factory, customerEmail);
        using var admin = await RegisterAdmin(factory, $"admin-{Guid.NewGuid()}@example.com");

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var beforeBalance = await db.CreditAccounts.AsNoTracking().SingleAsync(a => a.UserId == customerId);

        for (var i = 0; i < 2; i++)
        {
            (await admin.PostJsonWithCsrfAsync($"/api/admin/users/{customerId}/credits/adjust",
                new { amount = 10, reason = "sequential test", idempotencyKey = Guid.NewGuid() })).EnsureSuccessStatusCode();
        }

        using var verifyScope = factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var account = await verifyDb.CreditAccounts.AsNoTracking().SingleAsync(a => a.UserId == customerId);
        Assert.Equal(beforeBalance.Balance + 20, account.Balance);
        var adjustmentCount = await verifyDb.CreditTransactions.CountAsync(t => t.UserId == customerId && t.Type == CreditTransactionType.AdminAdjustment);
        Assert.Equal(2, adjustmentCount);
    }

    [Fact]
    public async Task DuplicateSubmission_SameIdempotencyKey_AppliesOnlyOnce()
    {
        using var factory = new ProjectsTestFactory();
        var customerEmail = await RegisterCustomerEmail(factory);
        var customerId = await GetUserIdAsync(factory, customerEmail);
        using var admin = await RegisterAdmin(factory, $"admin-{Guid.NewGuid()}@example.com");
        var key = Guid.NewGuid();

        var first = await admin.PostJsonWithCsrfAsync($"/api/admin/users/{customerId}/credits/adjust",
            new { amount = 30, reason = "double-submit test", idempotencyKey = key });
        var second = await admin.PostJsonWithCsrfAsync($"/api/admin/users/{customerId}/credits/adjust",
            new { amount = 30, reason = "double-submit test", idempotencyKey = key });

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        var secondBody = await second.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(secondBody.GetProperty("alreadyApplied").GetBoolean());

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var count = await db.CreditTransactions.CountAsync(t => t.UserId == customerId && t.Type == CreditTransactionType.AdminAdjustment);
        Assert.Equal(1, count);
    }

    // --- Credit analytics ------------------------------------------------

    [Fact]
    public async Task CreditAnalytics_ReflectsAdminAdjustments()
    {
        using var factory = new ProjectsTestFactory();
        var customerId = await GetUserIdAsync(factory, await RegisterCustomerEmail(factory));
        using var admin = await RegisterAdmin(factory, $"admin-{Guid.NewGuid()}@example.com");

        (await admin.PostJsonWithCsrfAsync($"/api/admin/users/{customerId}/credits/adjust",
            new { amount = 40, reason = "grant", idempotencyKey = Guid.NewGuid() })).EnsureSuccessStatusCode();
        (await admin.PostJsonWithCsrfAsync($"/api/admin/users/{customerId}/credits/adjust",
            new { amount = -15, reason = "deduct", idempotencyKey = Guid.NewGuid() })).EnsureSuccessStatusCode();

        var response = await admin.GetFromJsonAsync<JsonElement>("/api/admin/credits");

        Assert.Equal(40, response.GetProperty("adminAdjustmentsGranted").GetInt32());
        Assert.Equal(15, response.GetProperty("adminAdjustmentsDeducted").GetInt32());
        Assert.Equal(25, response.GetProperty("adminAdjustmentsNet").GetInt32());
        Assert.Equal(0, response.GetProperty("purchasedCredits").GetInt32());
    }

    // --- Account lock/unlock ------------------------------------------------

    [Fact]
    public async Task LockAccount_PreventsLogin_AndIsAudited()
    {
        using var factory = new ProjectsTestFactory();
        var customerEmail = await RegisterCustomerEmail(factory);
        var customerId = await GetUserIdAsync(factory, customerEmail);
        using var admin = await RegisterAdmin(factory, $"admin-{Guid.NewGuid()}@example.com");

        var response = await admin.PostWithCsrfAsync($"/api/admin/users/{customerId}/lock", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var auditEntry = await db.AdminAuditLogs.SingleAsync(a => a.TargetId == customerId && a.Action == AdminAuditAction.AccountLocked);
        Assert.NotNull(auditEntry);

        using var freshClient = factory.CreateClient();
        var loginResponse = await freshClient.PostJsonWithCsrfAsync("/api/account/login", new { email = customerEmail, password = Password });
        Assert.Equal(HttpStatusCode.Unauthorized, loginResponse.StatusCode);
    }

    [Fact]
    public async Task UnlockAccount_RestoresLogin_AndIsAudited()
    {
        using var factory = new ProjectsTestFactory();
        var customerEmail = await RegisterCustomerEmail(factory);
        var customerId = await GetUserIdAsync(factory, customerEmail);
        using var admin = await RegisterAdmin(factory, $"admin-{Guid.NewGuid()}@example.com");

        (await admin.PostWithCsrfAsync($"/api/admin/users/{customerId}/lock", null)).EnsureSuccessStatusCode();
        var unlockResponse = await admin.PostWithCsrfAsync($"/api/admin/users/{customerId}/unlock", null);
        Assert.Equal(HttpStatusCode.OK, unlockResponse.StatusCode);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.True(await db.AdminAuditLogs.AnyAsync(a => a.TargetId == customerId && a.Action == AdminAuditAction.AccountUnlocked));

        using var freshClient = factory.CreateClient();
        var loginResponse = await freshClient.PostJsonWithCsrfAsync("/api/account/login", new { email = customerEmail, password = Password });
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);
    }

    [Fact]
    public async Task NormalUser_ForbiddenFromLockingAccounts()
    {
        using var factory = new ProjectsTestFactory();
        var customerId = await GetUserIdAsync(factory, await RegisterCustomerEmail(factory));
        using var normal = await Register(factory, $"normal-{Guid.NewGuid()}@example.com");

        var response = await normal.PostWithCsrfAsync($"/api/admin/users/{customerId}/lock", null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // --- Audit log API -----------------------------------------------------

    [Fact]
    public async Task AuditLog_FiltersByTargetId()
    {
        using var factory = new ProjectsTestFactory();
        var customerId = await GetUserIdAsync(factory, await RegisterCustomerEmail(factory));
        var otherCustomerId = await GetUserIdAsync(factory, await RegisterCustomerEmail(factory));
        using var admin = await RegisterAdmin(factory, $"admin-{Guid.NewGuid()}@example.com");

        (await admin.PostJsonWithCsrfAsync($"/api/admin/users/{customerId}/credits/adjust",
            new { amount = 5, reason = "a", idempotencyKey = Guid.NewGuid() })).EnsureSuccessStatusCode();
        (await admin.PostJsonWithCsrfAsync($"/api/admin/users/{otherCustomerId}/credits/adjust",
            new { amount = 5, reason = "b", idempotencyKey = Guid.NewGuid() })).EnsureSuccessStatusCode();

        var response = await admin.GetFromJsonAsync<JsonElement[]>($"/api/admin/audit-log?targetId={customerId}") ?? [];

        Assert.All(response, e => Assert.Equal(customerId, e.GetProperty("targetId").GetString()));
        Assert.Contains(response, e => e.GetProperty("action").GetString() == AdminAuditAction.CreditAdjustment);
    }

    [Fact]
    public async Task NormalUser_ForbiddenFromAuditLog()
    {
        using var factory = new ProjectsTestFactory();
        using var normal = await Register(factory, $"normal-{Guid.NewGuid()}@example.com");

        var response = await normal.GetAsync("/api/admin/audit-log");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
