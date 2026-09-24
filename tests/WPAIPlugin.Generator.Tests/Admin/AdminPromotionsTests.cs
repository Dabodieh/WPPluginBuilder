using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WPAIPlugin.Api.Data;
using WPAIPlugin.Api.Security;
using WPAIPlugin.Generator.Tests.Projects;
using Xunit;

namespace WPAIPlugin.Generator.Tests.Admin;

/// <summary>
/// Admin promotion management authorization/CRUD/audit (Promotions + Free
/// Builds milestone). Reuses ProjectsTestFactory - no live database required.
/// </summary>
public class AdminPromotionsTests
{
    private const string Password = "Str0ng!Passw0rd";

    private static async Task<HttpClient> Register(ProjectsTestFactory factory, string email)
    {
        var client = factory.CreateClient();
        (await client.PostJsonWithCsrfAsync("/api/account/register", new { email, password = Password })).EnsureSuccessStatusCode();
        return client;
    }

    private static async Task PromoteToAdminAndReauthenticateAsync(ProjectsTestFactory factory, HttpClient client, string email)
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
        (await client.PostWithCsrfAsync("/api/account/logout", null)).EnsureSuccessStatusCode();
        (await client.PostJsonWithCsrfAsync("/api/account/login", new { email, password = Password })).EnsureSuccessStatusCode();
    }

    private static object ValidPromotionBody(string name, string? code = "TESTCODE") => new
    {
        name, code, type = "BonusCredits", startsAtUtc = DateTime.UtcNow.AddDays(-1).ToString("O"),
        endsAtUtc = (string?)null, isEnabled = true, requiresCode = code != null,
        appliesToPackId = (string?)null, value = 10, maxRedemptions = (int?)null,
        maxRedemptionsPerUser = (int?)null, eligibility = "Everyone", priority = 0,
    };

    [Fact]
    public async Task NormalUser_CannotCreateOrListPromotions()
    {
        using var factory = new ProjectsTestFactory();
        using var client = await Register(factory, $"normal-{Guid.NewGuid()}@example.com");

        var list = await client.GetAsync("/api/admin/promotions");
        var create = await client.PostJsonWithCsrfAsync("/api/admin/promotions", ValidPromotionBody("Test"));

        Assert.Equal(HttpStatusCode.Forbidden, list.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, create.StatusCode);
    }

    [Fact]
    public async Task Anonymous_DeniedAdminPromotions()
    {
        using var factory = new ProjectsTestFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/admin/promotions");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Admin_CanCreateEditEnableDisableDuplicatePromotion()
    {
        using var factory = new ProjectsTestFactory();
        var email = $"admin-{Guid.NewGuid()}@example.com";
        using var client = await Register(factory, email);
        await PromoteToAdminAndReauthenticateAsync(factory, client, email);

        var createResponse = await client.PostJsonWithCsrfAsync("/api/admin/promotions", ValidPromotionBody("Launch Bonus"));
        createResponse.EnsureSuccessStatusCode();
        var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var id = created.GetProperty("id").GetString();
        Assert.Equal("Active", created.GetProperty("state").GetString());

        var enableResponse = await client.PostWithCsrfAsync($"/api/admin/promotions/{id}/enable", null);
        enableResponse.EnsureSuccessStatusCode();

        var list = await client.GetFromJsonAsync<JsonElement[]>("/api/admin/promotions");
        Assert.Contains(list!, p => p.GetProperty("id").GetString() == id && p.GetProperty("state").GetString() == "Active");

        var updateResponse = await client.PutAsJsonWithCsrfAsync($"/api/admin/promotions/{id}", ValidPromotionBody("Launch Bonus Updated", "TESTCODE"));
        updateResponse.EnsureSuccessStatusCode();

        var disableResponse = await client.PostWithCsrfAsync($"/api/admin/promotions/{id}/disable", null);
        disableResponse.EnsureSuccessStatusCode();

        var duplicateResponse = await client.PostWithCsrfAsync($"/api/admin/promotions/{id}/duplicate", null);
        duplicateResponse.EnsureSuccessStatusCode();
        var duplicate = await duplicateResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Null(duplicate.GetProperty("code").GetString());
        Assert.False(duplicate.GetProperty("isEnabled").GetBoolean());

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var auditActions = await db.AdminAuditLogs.Select(a => a.Action).ToListAsync();
        Assert.Contains(AdminAuditAction.PromotionCreated, auditActions);
        Assert.Contains(AdminAuditAction.PromotionEnabled, auditActions);
        Assert.Contains(AdminAuditAction.PromotionUpdated, auditActions);
        Assert.Contains(AdminAuditAction.PromotionDisabled, auditActions);
        Assert.Equal(2, auditActions.Count(a => a == AdminAuditAction.PromotionCreated)); // original + duplicate
    }

    [Fact]
    public async Task Admin_CannotCreateInvalidPromotion()
    {
        using var factory = new ProjectsTestFactory();
        var email = $"admin-{Guid.NewGuid()}@example.com";
        using var client = await Register(factory, email);
        await PromoteToAdminAndReauthenticateAsync(factory, client, email);

        var negativeValue = await client.PostJsonWithCsrfAsync("/api/admin/promotions", new
        {
            name = "Bad", code = "BAD1", type = "PackPriceDiscount", startsAtUtc = DateTime.UtcNow.ToString("O"),
            isEnabled = true, requiresCode = true, value = 150, eligibility = "Everyone",
        });
        Assert.Equal(HttpStatusCode.BadRequest, negativeValue.StatusCode);

        var endBeforeStart = await client.PostJsonWithCsrfAsync("/api/admin/promotions", new
        {
            name = "Bad2", code = "BAD2", type = "BonusCredits",
            startsAtUtc = DateTime.UtcNow.ToString("O"), endsAtUtc = DateTime.UtcNow.AddDays(-1).ToString("O"),
            isEnabled = true, requiresCode = true, value = 10, eligibility = "Everyone",
        });
        Assert.Equal(HttpStatusCode.BadRequest, endBeforeStart.StatusCode);

        var unknownPack = await client.PostJsonWithCsrfAsync("/api/admin/promotions", new
        {
            name = "Bad3", code = "BAD3", type = "BonusCredits", appliesToPackId = "not-a-real-pack",
            startsAtUtc = DateTime.UtcNow.ToString("O"), isEnabled = true, requiresCode = true, value = 10, eligibility = "Everyone",
        });
        Assert.Equal(HttpStatusCode.BadRequest, unknownPack.StatusCode);
    }

    [Fact]
    public async Task Admin_DuplicateActiveCode_Rejected()
    {
        using var factory = new ProjectsTestFactory();
        var email = $"admin-{Guid.NewGuid()}@example.com";
        using var client = await Register(factory, email);
        await PromoteToAdminAndReauthenticateAsync(factory, client, email);

        (await client.PostJsonWithCsrfAsync("/api/admin/promotions", ValidPromotionBody("First"))).EnsureSuccessStatusCode();
        var second = await client.PostJsonWithCsrfAsync("/api/admin/promotions", ValidPromotionBody("Second"));

        Assert.Equal(HttpStatusCode.BadRequest, second.StatusCode);
    }
}
