using WPAIPlugin.Generator.Models;
using WPAIPlugin.Planning;
using Xunit;

namespace WPAIPlugin.Generator.Tests.Planning;

public class PluginPlannerTests
{
    private static PluginPlanner CreatePlanner(FakePlanningProvider provider, string? defaultProvider = null)
    {
        var resolver = new PlanningProviderResolver(new[] { provider }, defaultProvider ?? provider.Name);
        return new PluginPlanner(resolver);
    }

    [Fact]
    public async Task PlanAsync_ValidDescription_ReturnsValidatedPluginSpec()
    {
        var planner = CreatePlanner(new FakePlanningProvider());

        var result = await planner.PlanAsync("Create a simple staff directory plugin with a shortcode.");

        Assert.Equal("Staff Directory", result.Spec.Name);
        Assert.Equal("staff-directory", result.Spec.Slug);
        Assert.Equal("1.0.0", result.Spec.Version);
        Assert.Equal("WPAI Plugin Builder", result.Spec.Author);
        Assert.Contains("shortcode", result.Spec.Features);
        Assert.Empty(result.UnsupportedRequirements);
    }

    [Fact]
    public async Task PlanAsync_ProviderReturnsCustomPostType_MapsThroughToValidatedSpec()
    {
        var provider = new FakePlanningProvider
        {
            Handler = (_, _) => Task.FromResult(new PlanningResult
            {
                Name = "Staff Directory",
                Slug = "staff-directory",
                Description = "A staff directory plugin.",
                Version = "1.0.0",
                Author = "WPAI Plugin Builder",
                Features = new[] { "custom-post-type" },
                UnsupportedRequirements = Array.Empty<string>(),
                CustomPostType = new CustomPostTypeSpec
                {
                    SingularName = "Staff Member",
                    PluralName = "Staff Members",
                    Slug = "staff-member",
                    Public = true,
                    HasArchive = false,
                },
            }),
        };
        var planner = CreatePlanner(provider);

        var result = await planner.PlanAsync("Create a staff directory with a staff member custom post type.");

        Assert.Contains("custom-post-type", result.Spec.Features);
        Assert.NotNull(result.Spec.CustomPostType);
        Assert.Equal("staff-member", result.Spec.CustomPostType!.Slug);
        Assert.Empty(result.UnsupportedRequirements);
    }

    [Fact]
    public async Task PlanAsync_ProviderReturnsSettingsPage_MapsThroughToValidatedSpec()
    {
        var provider = new FakePlanningProvider
        {
            Handler = (_, _) => Task.FromResult(new PlanningResult
            {
                Name = "Staff Directory",
                Slug = "staff-directory",
                Description = "A staff directory plugin.",
                Version = "1.0.0",
                Author = "WPAI Plugin Builder",
                Features = new[] { "settings-page" },
                UnsupportedRequirements = Array.Empty<string>(),
                SettingsPage = new SettingsPageSpec
                {
                    PageTitle = "Staff Directory Settings",
                    MenuTitle = "Staff Directory",
                    Fields = new List<SettingsFieldSpec>
                    {
                        new() { Key = "api_key", Label = "API Key", Type = "text" },
                    },
                },
            }),
        };
        var planner = CreatePlanner(provider);

        var result = await planner.PlanAsync("Create a staff directory with a settings page for an API key.");

        Assert.Contains("settings-page", result.Spec.Features);
        Assert.NotNull(result.Spec.SettingsPage);
        Assert.Single(result.Spec.SettingsPage!.Fields);
        Assert.Empty(result.UnsupportedRequirements);
    }

    [Fact]
    public async Task PlanAsync_ProviderReturnsCustomFields_MapsThroughToValidatedSpec()
    {
        var provider = new FakePlanningProvider
        {
            Handler = (_, _) => Task.FromResult(new PlanningResult
            {
                Name = "Staff Directory",
                Slug = "staff-directory",
                Description = "A staff directory plugin.",
                Version = "1.0.0",
                Author = "WPAI Plugin Builder",
                Features = new[] { "custom-post-type", "custom-fields" },
                UnsupportedRequirements = Array.Empty<string>(),
                CustomPostType = new CustomPostTypeSpec
                {
                    SingularName = "Staff Member",
                    PluralName = "Staff Members",
                    Slug = "staff-member",
                },
                CustomFields = new CustomFieldsSpec
                {
                    PostType = "staff-member",
                    Fields = new List<CustomFieldSpec>
                    {
                        new() { Key = "job_title", Label = "Job Title", Type = "text" },
                    },
                },
            }),
        };
        var planner = CreatePlanner(provider);

        var result = await planner.PlanAsync("Create a staff directory with a staff member post type and a job title custom field.");

        Assert.Contains("custom-fields", result.Spec.Features);
        Assert.NotNull(result.Spec.CustomFields);
        Assert.Equal("staff-member", result.Spec.CustomFields!.PostType);
        Assert.Single(result.Spec.CustomFields.Fields);
        Assert.Empty(result.UnsupportedRequirements);
    }

    [Fact]
    public async Task PlanAsync_ProviderReturnsScheduledTask_MapsThroughToValidatedSpec()
    {
        var provider = new FakePlanningProvider
        {
            Handler = (_, _) => Task.FromResult(new PlanningResult
            {
                Name = "Staff Directory",
                Slug = "staff-directory",
                Description = "A staff directory plugin.",
                Version = "1.0.0",
                Author = "WPAI Plugin Builder",
                Features = new[] { "scheduled-task" },
                UnsupportedRequirements = Array.Empty<string>(),
                ScheduledTask = new ScheduledTaskSpec
                {
                    TaskName = "Cleanup Old Entries",
                    Schedule = "daily",
                    HookName = "staff_directory_cleanup",
                },
            }),
        };
        var planner = CreatePlanner(provider);

        var result = await planner.PlanAsync("Create a staff directory with a daily cleanup task.");

        Assert.Contains("scheduled-task", result.Spec.Features);
        Assert.NotNull(result.Spec.ScheduledTask);
        Assert.Equal("daily", result.Spec.ScheduledTask!.Schedule);
        Assert.Empty(result.UnsupportedRequirements);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task PlanAsync_BlankDescription_ThrowsInvalidInput(string? description)
    {
        var planner = CreatePlanner(new FakePlanningProvider());

        var ex = await Assert.ThrowsAsync<PluginPlanException>(() => planner.PlanAsync(description!));

        Assert.Equal(PluginPlanFailureReason.InvalidInput, ex.Reason);
    }

    [Fact]
    public async Task PlanAsync_OversizedDescription_ThrowsInvalidInput()
    {
        var planner = CreatePlanner(new FakePlanningProvider());
        var oversized = new string('a', PlanningConstants.MaxDescriptionLength + 1);

        var ex = await Assert.ThrowsAsync<PluginPlanException>(() => planner.PlanAsync(oversized));

        Assert.Equal(PluginPlanFailureReason.InvalidInput, ex.Reason);
    }

    [Fact]
    public async Task PlanAsync_MaxLengthDescription_IsAccepted()
    {
        var planner = CreatePlanner(new FakePlanningProvider());
        var atLimit = new string('a', PlanningConstants.MaxDescriptionLength);

        var result = await planner.PlanAsync(atLimit);

        Assert.NotNull(result.Spec);
    }

    [Fact]
    public async Task PlanAsync_ProviderReturnsInvalidSlug_ThrowsGeneratedSpecInvalid()
    {
        var provider = new FakePlanningProvider
        {
            Handler = (_, _) => Task.FromResult(new PlanningResult
            {
                Name = "Staff Directory",
                Slug = "Staff_Directory!", // invalid: uppercase, underscore, punctuation
                Description = "desc",
                Version = "1.0.0",
                Author = "WPAI Plugin Builder",
                Features = Array.Empty<string>(),
                UnsupportedRequirements = Array.Empty<string>(),
            }),
        };
        var planner = CreatePlanner(provider);

        var ex = await Assert.ThrowsAsync<PluginPlanException>(() => planner.PlanAsync("anything"));

        Assert.Equal(PluginPlanFailureReason.GeneratedSpecInvalid, ex.Reason);
        Assert.NotEmpty(ex.ValidationErrors);
    }

    [Fact]
    public async Task PlanAsync_ProviderReturnsUnsupportedFeature_ThrowsGeneratedSpecInvalid()
    {
        var provider = new FakePlanningProvider
        {
            Handler = (_, _) => Task.FromResult(new PlanningResult
            {
                Name = "Booking Calendar",
                Slug = "booking-calendar",
                Description = "desc",
                Version = "1.0.0",
                Author = "WPAI Plugin Builder",
                Features = new[] { "booking-calendar" }, // not a supported PluginFeature
                UnsupportedRequirements = Array.Empty<string>(),
            }),
        };
        var planner = CreatePlanner(provider);

        var ex = await Assert.ThrowsAsync<PluginPlanException>(() => planner.PlanAsync("anything"));

        Assert.Equal(PluginPlanFailureReason.GeneratedSpecInvalid, ex.Reason);
    }

    [Fact]
    public async Task PlanAsync_UnsupportedRequirements_ReturnedSeparatelyAndNotInFeatures()
    {
        var provider = new FakePlanningProvider
        {
            Handler = (_, _) => Task.FromResult(new PlanningResult
            {
                Name = "Booking Calendar",
                Slug = "booking-calendar",
                Description = "A booking calendar plugin.",
                Version = "1.0.0",
                Author = "WPAI Plugin Builder",
                Features = Array.Empty<string>(),
                UnsupportedRequirements = new[] { "booking calendar", "email notifications", "admin booking management" },
            }),
        };
        var planner = CreatePlanner(provider);

        var result = await planner.PlanAsync("Create a booking calendar plugin with email notifications.");

        Assert.Empty(result.Spec.Features);
        Assert.Equal(
            new[] { "booking calendar", "email notifications", "admin booking management" },
            result.UnsupportedRequirements);
    }

    [Fact]
    public async Task PlanAsync_ProviderThrows_ThrowsProviderFailureWithoutLeakingInnerDetails()
    {
        var provider = new FakePlanningProvider
        {
            Handler = (_, _) => throw new InvalidOperationException("raw provider payload with secret=abc123"),
        };
        var planner = CreatePlanner(provider);

        var ex = await Assert.ThrowsAsync<PluginPlanException>(() => planner.PlanAsync("anything"));

        Assert.Equal(PluginPlanFailureReason.ProviderFailure, ex.Reason);
        Assert.DoesNotContain("secret=abc123", ex.Message);
    }

    [Fact]
    public async Task PlanAsync_MalformedProviderOutput_PropagatesAsPluginPlanException()
    {
        var provider = new FakePlanningProvider
        {
            Handler = (_, _) => throw new PluginPlanException(PluginPlanFailureReason.MalformedProviderOutput, "malformed"),
        };
        var planner = CreatePlanner(provider);

        var ex = await Assert.ThrowsAsync<PluginPlanException>(() => planner.PlanAsync("anything"));

        Assert.Equal(PluginPlanFailureReason.MalformedProviderOutput, ex.Reason);
    }

    [Fact]
    public async Task PlanAsync_CancellationRequested_ThrowsCancelled()
    {
        using var cts = new CancellationTokenSource();
        var provider = new FakePlanningProvider
        {
            Handler = (_, ct) =>
            {
                cts.Cancel();
                ct.ThrowIfCancellationRequested();
                return Task.FromResult<PlanningResult>(null!);
            },
        };
        var planner = CreatePlanner(provider);

        var ex = await Assert.ThrowsAsync<PluginPlanException>(() => planner.PlanAsync("anything", cancellationToken: cts.Token));

        Assert.Equal(PluginPlanFailureReason.Cancelled, ex.Reason);
    }

    [Fact]
    public async Task PlanAsync_UnknownProviderName_ThrowsProviderNotFound()
    {
        var planner = CreatePlanner(new FakePlanningProvider { Name = "fake" });

        var ex = await Assert.ThrowsAsync<PluginPlanException>(() => planner.PlanAsync("anything", provider: "does-not-exist"));

        Assert.Equal(PluginPlanFailureReason.ProviderNotFound, ex.Reason);
    }

    [Fact]
    public async Task PlanAsync_NoProviderSpecified_UsesConfiguredDefault()
    {
        var provider = new FakePlanningProvider { Name = "anthropic" };
        var planner = CreatePlanner(provider, defaultProvider: "anthropic");

        var result = await planner.PlanAsync("anything", provider: null);

        Assert.NotNull(result.Spec);
    }
}
