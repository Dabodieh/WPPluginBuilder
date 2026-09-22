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
}
