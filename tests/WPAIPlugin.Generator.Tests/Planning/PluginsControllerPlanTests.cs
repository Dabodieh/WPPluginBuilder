using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WPAIPlugin.Api.AiUsage;
using WPAIPlugin.Api.Controllers;
using WPAIPlugin.Api.Data;
using WPAIPlugin.Api.Security;
using WPAIPlugin.Api.Validation;
using WPAIPlugin.Generator.Tests.Validation;
using WPAIPlugin.Planning;
using Xunit;

namespace WPAIPlugin.Generator.Tests.Planning;

public class PluginsControllerPlanTests
{
    private static PluginsController CreateController(
        FakePluginPlanner planner,
        AppDbContext? db = null,
        PartitionedRateLimiter<string>? planningHourlyLimiter = null,
        PartitionedRateLimiter<string>? planningDailyLimiter = null,
        AbuseOptions? abuseOptions = null)
    {
        db ??= new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        return new(
            new global::WPAIPlugin.Generator.PluginBuilder(),
            planner,
            new FakeDockerPluginValidator(),
            Options.Create(new ValidationOptions()),
            new AiUsageRecorder(db, Options.Create(new AiPricingOptions())),
            db,
            planningHourlyLimiter ?? TestPlanningRateLimiters.Generous(),
            planningDailyLimiter ?? TestPlanningRateLimiters.Generous(),
            Options.Create(abuseOptions ?? new AbuseOptions()),
            TestUserManagerFactory.Create(db),
            NullLogger<PluginsController>.Instance);
    }

    private static ClaimsPrincipal UserPrincipal(string userId) =>
        new(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, userId) }, "test"));

    /// <summary>These rate-limit/cost-ceiling tests exercise checks that run after the email-verification gate, so the userId they use must resolve to a verified user.</summary>
    private static async Task SeedVerifiedUserAsync(AppDbContext db, string userId)
    {
        db.Users.Add(new IdentityUser { Id = userId, UserName = userId, EmailConfirmed = true });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Plan_ValidDescription_Returns200WithSpecAndUnsupportedRequirements()
    {
        var controller = CreateController(new FakePluginPlanner());

        var result = await controller.Plan(new PlanPluginRequest { Description = "Create a simple staff directory plugin with a shortcode." }, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<PlanPluginResponse>(ok.Value);
        Assert.Equal("staff-directory", response.Spec.Slug);
        Assert.Empty(response.UnsupportedRequirements);
    }

    [Fact]
    public async Task Plan_InvalidInput_Returns400()
    {
        var planner = new FakePluginPlanner
        {
            Handler = (_, _, _, _) => throw new PluginPlanException(PluginPlanFailureReason.InvalidInput, "Description cannot be blank."),
        };
        var controller = CreateController(planner);

        var result = await controller.Plan(new PlanPluginRequest { Description = "" }, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task Plan_GeneratedSpecInvalid_Returns400WithValidationErrors()
    {
        var planner = new FakePluginPlanner
        {
            Handler = (_, _, _, _) => throw new PluginPlanException(
                PluginPlanFailureReason.GeneratedSpecInvalid,
                "invalid",
                new[] { "Slug must contain only lowercase letters, digits, and hyphens." }),
        };
        var controller = CreateController(planner);

        var result = await controller.Plan(new PlanPluginRequest { Description = "anything" }, CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.NotNull(badRequest.Value);
    }

    [Fact]
    public async Task Plan_ProviderFailure_Returns502()
    {
        var planner = new FakePluginPlanner
        {
            Handler = (_, _, _, _) => throw new PluginPlanException(PluginPlanFailureReason.ProviderFailure, "The AI planning provider failed."),
        };
        var controller = CreateController(planner);

        var result = await controller.Plan(new PlanPluginRequest { Description = "anything" }, CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status502BadGateway, objectResult.StatusCode);
    }

    [Fact]
    public async Task Plan_Timeout_Returns504()
    {
        var planner = new FakePluginPlanner
        {
            Handler = (_, _, _, _) => throw new PluginPlanException(PluginPlanFailureReason.Timeout, "The AI planning provider timed out."),
        };
        var controller = CreateController(planner);

        var result = await controller.Plan(new PlanPluginRequest { Description = "anything" }, CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status504GatewayTimeout, objectResult.StatusCode);
    }

    [Fact]
    public async Task Plan_MalformedProviderOutput_Returns502()
    {
        var planner = new FakePluginPlanner
        {
            Handler = (_, _, _, _) => throw new PluginPlanException(PluginPlanFailureReason.MalformedProviderOutput, "malformed"),
        };
        var controller = CreateController(planner);

        var result = await controller.Plan(new PlanPluginRequest { Description = "anything" }, CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status502BadGateway, objectResult.StatusCode);
    }

    [Fact]
    public async Task Plan_OutOfScope_Returns400WithFixedSafeMessageOnly()
    {
        var planner = new FakePluginPlanner
        {
            Handler = (_, _, _, _) => throw new PluginPlanException(
                PluginPlanFailureReason.OutOfScope,
                "ModuleMint can only process requests related to creating or modifying WordPress plugins.",
                detail: "not_wordpress_plugin_request"),
        };
        var controller = CreateController(planner);

        var result = await controller.Plan(new PlanPluginRequest { Description = "What's the weather today?" }, CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        var json = System.Text.Json.JsonSerializer.Serialize(badRequest.Value);
        Assert.Contains("ModuleMint can only process requests related to creating or modifying WordPress plugins.", json);
        Assert.DoesNotContain("What's the weather", json);
        Assert.DoesNotContain("not_wordpress_plugin_request", json);
    }

    [Fact]
    public async Task Plan_OutOfScope_RecordsFailureUsageEventWithRealUsage()
    {
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var planner = new FakePluginPlanner
        {
            Handler = (_, _, _, _) => throw new PluginPlanException(
                PluginPlanFailureReason.OutOfScope,
                "ModuleMint can only process requests related to creating or modifying WordPress plugins.",
                usage: new PlanningUsage { InputTokens = 50, OutputTokens = 10, TotalTokens = 60 },
                detail: "not_wordpress_plugin_request"),
        };
        var controller = CreateController(planner, db: db);

        await controller.Plan(new PlanPluginRequest { Description = "anything" }, CancellationToken.None);

        var events = await db.AiUsageEvents.ToListAsync();
        var recorded = Assert.Single(events);
        Assert.False(recorded.Succeeded);
        Assert.Equal("OutOfScope", recorded.FailureCategory);
        Assert.Equal(50, recorded.InputTokens);
        Assert.Equal(10, recorded.OutputTokens);
    }

    [Fact]
    public async Task Plan_UnverifiedUser_Returns403WithoutCallingPlannerOrRecordingUsage()
    {
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        db.Users.Add(new IdentityUser { Id = "unverified-user", UserName = "unverified-user", EmailConfirmed = false });
        await db.SaveChangesAsync();
        var planner = new FakePluginPlanner
        {
            Handler = (_, _, _, _) => throw new Xunit.Sdk.XunitException("Planner must not be called for an unverified user."),
        };
        var controller = CreateController(planner, db: db);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = UserPrincipal("unverified-user") },
        };

        var result = await controller.Plan(new PlanPluginRequest { Description = "anything" }, CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, objectResult.StatusCode);
        Assert.Empty(await db.AiUsageEvents.ToListAsync());
    }

    [Fact]
    public async Task Plan_HourlyLimitExhausted_Returns429WithoutCallingPlanner()
    {
        var planner = new FakePluginPlanner
        {
            Handler = (_, _, _, _) => throw new Xunit.Sdk.XunitException("Planner must not be called once the pre-flight limit is exhausted."),
        };
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        await SeedVerifiedUserAsync(db, "user-1");
        var hourlyLimiter = TestPlanningRateLimiters.Create(1, TimeSpan.FromHours(1));
        hourlyLimiter.AttemptAcquire("user-1").Dispose(); // consume the single permit before the request under test
        var controller = CreateController(planner, db: db, planningHourlyLimiter: hourlyLimiter);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = UserPrincipal("user-1") },
        };

        var result = await controller.Plan(new PlanPluginRequest { Description = "anything" }, CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status429TooManyRequests, objectResult.StatusCode);
    }

    [Fact]
    public async Task Plan_DailyCostBudgetExceeded_Returns429WithoutCallingPlanner()
    {
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        db.AiUsageEvents.Add(new AiUsageEvent
        {
            Id = Guid.NewGuid(),
            UserId = "user-2",
            OperationType = AiUsageOperationType.Plan,
            Provider = "openai",
            Succeeded = true,
            EstimatedCostUsdMicros = 10_000_000,
            DurationMs = 100,
            CreatedAtUtc = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
        await SeedVerifiedUserAsync(db, "user-2");

        var planner = new FakePluginPlanner
        {
            Handler = (_, _, _, _) => throw new Xunit.Sdk.XunitException("Planner must not be called once the daily cost ceiling is exceeded."),
        };
        var controller = CreateController(planner, db: db, abuseOptions: new AbuseOptions { MaxAiCostUsdMicrosPerUserPerDay = 1_000_000 });
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = UserPrincipal("user-2") },
        };

        var result = await controller.Plan(new PlanPluginRequest { Description = "anything" }, CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status429TooManyRequests, objectResult.StatusCode);
    }

    [Fact]
    public async Task Plan_DoesNotExposeExceptionDetails()
    {
        var planner = new FakePluginPlanner
        {
            Handler = (_, _, _, _) => throw new PluginPlanException(PluginPlanFailureReason.ProviderFailure, "The AI planning provider failed to produce a plugin plan."),
        };
        var controller = CreateController(planner);

        var result = await controller.Plan(new PlanPluginRequest { Description = "anything" }, CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result);
        var json = System.Text.Json.JsonSerializer.Serialize(objectResult.Value);
        Assert.DoesNotContain("StackTrace", json);
        Assert.DoesNotContain("api_key", json, StringComparison.OrdinalIgnoreCase);
    }
}
