using WPAIPlugin.Generator.Models;
using WPAIPlugin.Generator.Validation;
using Xunit;

namespace WPAIPlugin.Generator.Tests;

public class PluginSpecValidatorTests
{
    private static PluginSpec ValidSpec() => new()
    {
        Name = "Staff Directory",
        Slug = "staff-directory",
        Description = "Simple staff directory plugin",
        Version = "1.0.0",
        Author = "AI Plugin Builder",
        Features = new List<string> { "shortcode" },
    };

    [Fact]
    public void Validate_ValidSpec_ReturnsSuccess()
    {
        var result = PluginSpecValidator.Validate(ValidSpec());

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Theory]
    [InlineData("Staff_Directory")]   // uppercase / underscore not allowed
    [InlineData("staff directory")]   // spaces
    [InlineData("-staff-directory")]  // leading hyphen
    [InlineData("staff-directory-")]  // trailing hyphen
    [InlineData("staff--directory")]  // double hyphen not matched by pattern (adjacent groups) - still unsafe/invalid
    [InlineData("")]
    [InlineData("staff.directory")]
    public void Validate_InvalidSlug_ReturnsError(string slug)
    {
        var spec = ValidSpec();
        spec.Slug = slug;

        var result = PluginSpecValidator.Validate(spec);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("Slug", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("../../etc/passwd")]
    [InlineData("..\\..\\windows\\system32")]
    [InlineData("staff/../../directory")]
    [InlineData("staff/directory")]
    [InlineData("staff\\directory")]
    [InlineData("/etc/passwd")]
    [InlineData("C:\\Windows")]
    public void Validate_DirectoryTraversalSlug_ReturnsError(string maliciousSlug)
    {
        var spec = ValidSpec();
        spec.Slug = maliciousSlug;

        var result = PluginSpecValidator.Validate(spec);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void Validate_MissingName_ReturnsError()
    {
        var spec = ValidSpec();
        spec.Name = "";

        var result = PluginSpecValidator.Validate(spec);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("Name"));
    }

    [Fact]
    public void Validate_MissingVersion_ReturnsError()
    {
        var spec = ValidSpec();
        spec.Version = "";

        var result = PluginSpecValidator.Validate(spec);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void Validate_InvalidVersionFormat_ReturnsError()
    {
        var spec = ValidSpec();
        spec.Version = "v1.0";

        var result = PluginSpecValidator.Validate(spec);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void Validate_UnknownFeature_ReturnsError()
    {
        var spec = ValidSpec();
        spec.Features = new List<string> { "not-a-real-feature" };

        var result = PluginSpecValidator.Validate(spec);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void Validate_NullSpec_ReturnsError()
    {
        var result = PluginSpecValidator.Validate(null);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void Validate_ValidCustomPostType_ReturnsSuccess()
    {
        var spec = ValidSpec();
        spec.Features = new List<string> { "custom-post-type" };
        spec.CustomPostType = new CustomPostTypeSpec
        {
            SingularName = "Staff Member",
            PluralName = "Staff Members",
            Slug = "staff-member",
            Public = true,
            HasArchive = false,
        };

        var result = PluginSpecValidator.Validate(spec);

        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData("Staff_Member")]
    [InlineData("../../etc/passwd")]
    [InlineData("staff/member")]
    [InlineData("")]
    public void Validate_InvalidCustomPostTypeSlug_ReturnsError(string invalidSlug)
    {
        var spec = ValidSpec();
        spec.Features = new List<string> { "custom-post-type" };
        spec.CustomPostType = new CustomPostTypeSpec
        {
            SingularName = "Staff Member",
            PluralName = "Staff Members",
            Slug = invalidSlug,
        };

        var result = PluginSpecValidator.Validate(spec);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("Custom post type slug", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_CustomPostTypeFeatureWithoutDetails_ReturnsError()
    {
        var spec = ValidSpec();
        spec.Features = new List<string> { "custom-post-type" };
        spec.CustomPostType = null;

        var result = PluginSpecValidator.Validate(spec);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void Validate_CustomPostTypeMissingSingularOrPluralName_ReturnsError()
    {
        var spec = ValidSpec();
        spec.Features = new List<string> { "custom-post-type" };
        spec.CustomPostType = new CustomPostTypeSpec { SingularName = "", PluralName = "", Slug = "staff-member" };

        var result = PluginSpecValidator.Validate(spec);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("singular name", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Errors, e => e.Contains("plural name", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_ValidSettingsPage_ReturnsSuccess()
    {
        var spec = ValidSpec();
        spec.Features = new List<string> { "settings-page" };
        spec.SettingsPage = new SettingsPageSpec
        {
            PageTitle = "Staff Directory Settings",
            MenuTitle = "Staff Directory",
            Fields = new List<SettingsFieldSpec>
            {
                new() { Key = "api_key", Label = "API Key", Type = "text" },
                new() { Key = "notes", Label = "Notes", Type = "textarea" },
                new() { Key = "enabled", Label = "Enabled", Type = "checkbox" },
            },
        };

        var result = PluginSpecValidator.Validate(spec);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_SettingsPageFeatureWithoutDetails_ReturnsError()
    {
        var spec = ValidSpec();
        spec.Features = new List<string> { "settings-page" };
        spec.SettingsPage = null;

        var result = PluginSpecValidator.Validate(spec);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void Validate_SettingsPageWithNoFields_ReturnsError()
    {
        var spec = ValidSpec();
        spec.Features = new List<string> { "settings-page" };
        spec.SettingsPage = new SettingsPageSpec { PageTitle = "Settings", MenuTitle = "Settings", Fields = new List<SettingsFieldSpec>() };

        var result = PluginSpecValidator.Validate(spec);

        Assert.False(result.IsValid);
    }

    [Theory]
    [InlineData("select")]
    [InlineData("radio")]
    [InlineData("color")]
    [InlineData("")]
    public void Validate_SettingsFieldUnsupportedType_ReturnsError(string invalidType)
    {
        var spec = ValidSpec();
        spec.Features = new List<string> { "settings-page" };
        spec.SettingsPage = new SettingsPageSpec
        {
            PageTitle = "Settings",
            MenuTitle = "Settings",
            Fields = new List<SettingsFieldSpec> { new() { Key = "field_one", Label = "Field One", Type = invalidType } },
        };

        var result = PluginSpecValidator.Validate(spec);

        Assert.False(result.IsValid);
    }

    [Theory]
    [InlineData("Api-Key")]
    [InlineData("1key")]
    [InlineData("api key")]
    [InlineData("../etc")]
    public void Validate_SettingsFieldInvalidKey_ReturnsError(string invalidKey)
    {
        var spec = ValidSpec();
        spec.Features = new List<string> { "settings-page" };
        spec.SettingsPage = new SettingsPageSpec
        {
            PageTitle = "Settings",
            MenuTitle = "Settings",
            Fields = new List<SettingsFieldSpec> { new() { Key = invalidKey, Label = "Label", Type = "text" } },
        };

        var result = PluginSpecValidator.Validate(spec);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void Validate_SettingsPageDuplicateFieldKeys_ReturnsError()
    {
        var spec = ValidSpec();
        spec.Features = new List<string> { "settings-page" };
        spec.SettingsPage = new SettingsPageSpec
        {
            PageTitle = "Settings",
            MenuTitle = "Settings",
            Fields = new List<SettingsFieldSpec>
            {
                new() { Key = "api_key", Label = "One", Type = "text" },
                new() { Key = "api_key", Label = "Two", Type = "text" },
            },
        };

        var result = PluginSpecValidator.Validate(spec);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("Duplicate", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_ValidCustomFields_ReturnsSuccess()
    {
        var spec = ValidSpec();
        spec.Features = new List<string> { "custom-post-type", "custom-fields" };
        spec.CustomPostType = new CustomPostTypeSpec { SingularName = "Staff Member", PluralName = "Staff Members", Slug = "staff-member" };
        spec.CustomFields = new CustomFieldsSpec
        {
            PostType = "staff-member",
            Fields = new List<CustomFieldSpec> { new() { Key = "job_title", Label = "Job Title", Type = "text" } },
        };

        var result = PluginSpecValidator.Validate(spec);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_CustomFieldsWithoutMatchingCustomPostType_ReturnsError()
    {
        var spec = ValidSpec();
        spec.Features = new List<string> { "custom-fields" };
        spec.CustomPostType = null;
        spec.CustomFields = new CustomFieldsSpec
        {
            PostType = "staff-member",
            Fields = new List<CustomFieldSpec> { new() { Key = "job_title", Label = "Job Title", Type = "text" } },
        };

        var result = PluginSpecValidator.Validate(spec);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("no matching custom post type", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_CustomFieldsReferencingDifferentPostType_ReturnsError()
    {
        var spec = ValidSpec();
        spec.Features = new List<string> { "custom-post-type", "custom-fields" };
        spec.CustomPostType = new CustomPostTypeSpec { SingularName = "Staff Member", PluralName = "Staff Members", Slug = "staff-member" };
        spec.CustomFields = new CustomFieldsSpec
        {
            PostType = "some-other-type",
            Fields = new List<CustomFieldSpec> { new() { Key = "job_title", Label = "Job Title", Type = "text" } },
        };

        var result = PluginSpecValidator.Validate(spec);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void Validate_CustomFieldsWithNoFields_ReturnsError()
    {
        var spec = ValidSpec();
        spec.Features = new List<string> { "custom-post-type", "custom-fields" };
        spec.CustomPostType = new CustomPostTypeSpec { SingularName = "Staff Member", PluralName = "Staff Members", Slug = "staff-member" };
        spec.CustomFields = new CustomFieldsSpec { PostType = "staff-member", Fields = new List<CustomFieldSpec>() };

        var result = PluginSpecValidator.Validate(spec);

        Assert.False(result.IsValid);
    }

    [Theory]
    [InlineData("date")]
    [InlineData("select")]
    [InlineData("media")]
    [InlineData("")]
    public void Validate_CustomFieldUnsupportedType_ReturnsError(string invalidType)
    {
        var spec = ValidSpec();
        spec.Features = new List<string> { "custom-post-type", "custom-fields" };
        spec.CustomPostType = new CustomPostTypeSpec { SingularName = "Staff Member", PluralName = "Staff Members", Slug = "staff-member" };
        spec.CustomFields = new CustomFieldsSpec
        {
            PostType = "staff-member",
            Fields = new List<CustomFieldSpec> { new() { Key = "field_one", Label = "Field One", Type = invalidType } },
        };

        var result = PluginSpecValidator.Validate(spec);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void Validate_ValidScheduledTask_ReturnsSuccess()
    {
        var spec = ValidSpec();
        spec.Features = new List<string> { "scheduled-task" };
        spec.ScheduledTask = new ScheduledTaskSpec { TaskName = "Cleanup", Schedule = "daily", HookName = "staff_directory_cleanup" };

        var result = PluginSpecValidator.Validate(spec);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_ScheduledTaskFeatureWithoutDetails_ReturnsError()
    {
        var spec = ValidSpec();
        spec.Features = new List<string> { "scheduled-task" };
        spec.ScheduledTask = null;

        var result = PluginSpecValidator.Validate(spec);

        Assert.False(result.IsValid);
    }

    [Theory]
    [InlineData("weekly")]
    [InlineData("every5minutes")]
    [InlineData("")]
    public void Validate_ScheduledTaskUnsupportedSchedule_ReturnsError(string invalidSchedule)
    {
        var spec = ValidSpec();
        spec.Features = new List<string> { "scheduled-task" };
        spec.ScheduledTask = new ScheduledTaskSpec { TaskName = "Cleanup", Schedule = invalidSchedule, HookName = "staff_directory_cleanup" };

        var result = PluginSpecValidator.Validate(spec);

        Assert.False(result.IsValid);
    }

    [Theory]
    [InlineData("Staff-Cleanup")]
    [InlineData("1cleanup")]
    [InlineData("staff cleanup")]
    [InlineData("../etc")]
    [InlineData("")]
    public void Validate_ScheduledTaskInvalidHookName_ReturnsError(string invalidHookName)
    {
        var spec = ValidSpec();
        spec.Features = new List<string> { "scheduled-task" };
        spec.ScheduledTask = new ScheduledTaskSpec { TaskName = "Cleanup", Schedule = "daily", HookName = invalidHookName };

        var result = PluginSpecValidator.Validate(spec);

        Assert.False(result.IsValid);
    }
}
