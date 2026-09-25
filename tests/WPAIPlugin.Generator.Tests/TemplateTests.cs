using WPAIPlugin.Templates;
using Xunit;

namespace WPAIPlugin.Generator.Tests;

public class TemplateTests
{
    [Fact]
    public void MainPluginFileTemplate_Render_ProducesValidHeaderAndAbspathGuard()
    {
        var php = MainPluginFileTemplate.Render(
            "Staff Directory",
            "staff-directory",
            "Simple staff directory plugin",
            "1.0.0",
            "AI Plugin Builder");

        Assert.StartsWith("<?php", php);
        Assert.Contains("Plugin Name: Staff Directory", php);
        Assert.Contains("Text Domain: staff-directory", php);
        Assert.Contains("if ( ! defined( 'ABSPATH' ) ) {\n\texit;\n}", php);
    }

    [Fact]
    public void ShortcodeTemplate_Render_StaffDirectory_ProducesExpectedOutput()
    {
        var php = ShortcodeTemplate.Render("staff-directory", "Staff Directory");

        Assert.Contains("[staff_directory]", php);
        Assert.Contains("function staff_directory_shortcode(", php);
        Assert.Contains("add_shortcode( 'staff_directory', 'staff_directory_shortcode' );", php);
        Assert.Contains("<div class=\"staff-directory\">'", php);
        Assert.Contains("esc_html( 'Staff Directory' )", php);
    }

    // --- Security regression: header docblock breakout (C-1) ------------------

    [Theory]
    [InlineData("Test */ echo 'HEADER-INJECTION'; /*")]
    [InlineData("*/ phpinfo(); /*")]
    [InlineData("*/ die('x'); /*")]
    public void MainPluginFileTemplate_Render_HostileValueCannotTerminateHeaderComment(string hostileValue)
    {
        var php = MainPluginFileTemplate.Render(hostileValue, "staff-directory", hostileValue, "1.0.0", hostileValue);

        // The security boundary, not an implementation detail: everything
        // between the opening "/**" and the ABSPATH guard must remain one
        // unbroken comment. If a hostile value could close it early, the
        // literal ABSPATH guard line - which always follows immediately -
        // would no longer appear preceded by an unbroken "*/\n\n" from the
        // *intended* comment close, and an un-neutralized "*/" from the
        // hostile value would appear inside the header block.
        var headerStart = php.IndexOf("/**", StringComparison.Ordinal);
        var guardStart = php.IndexOf("// Prevent direct file access.", StringComparison.Ordinal);
        Assert.True(headerStart >= 0 && guardStart > headerStart);
        var headerBlock = php[headerStart..guardStart];

        // Exactly one comment-close: the real one immediately before the guard.
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(headerBlock, System.Text.RegularExpressions.Regex.Escape("*/")));
        Assert.EndsWith("*/\n\n", headerBlock);
    }

    // --- Security regression: shortcode stored XSS (H-1) -----------------------

    [Fact]
    public void ShortcodeTemplate_Render_HostileNameIsHtmlEscapedNotRawOutput()
    {
        const string hostileName = "<img src=x onerror=alert('XSS')>";

        var php = ShortcodeTemplate.Render("staff-directory", hostileName);

        // The untrusted value must reach the browser only through esc_html(),
        // never concatenated directly into the returned HTML string.
        Assert.Contains("esc_html( '<img src=x onerror=alert(\\'XSS\\')>' )", php);
        Assert.DoesNotMatch(new System.Text.RegularExpressions.Regex(
            @"return\s+'[^']*<img src=x onerror=alert"), php);
    }
}
