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
        Assert.Contains("<div class=\"staff-directory\">\n    Staff Directory\n</div>", php);
    }
}
